using System.Data.Common;
using Dapper;
using Microsoft.Extensions.Logging;
using Ufo.Abstractions.Database;
using Ufo.Abstractions.Database.Repositories;

namespace Ufo.Database.Repositories;

public class UserDataRepository : IUserDataRepository
{
    private readonly ILogger<UserDataRepository> _logger;
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public UserDataRepository(IDbConnectionFactory dbConnectionFactory, ILogger<UserDataRepository> logger)
    {
        _dbConnectionFactory = dbConnectionFactory ?? throw new ArgumentNullException(nameof(dbConnectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<int> DeleteSnapshotsAsync(Ulid userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("DeleteSnapshotsAsync - UserId: {UserId}", userId);

        var deletedSnapshots = await RunInTransactionAsync(
            userId, nameof(DeleteSnapshotsAsync), DeleteSnapshotsStepAsync, cancellationToken);

        _logger.LogInformation("Deleted {Count} snapshots for user: {UserId}", deletedSnapshots, userId);

        return deletedSnapshots;
    }

    public async Task<int> DeleteFileSystemDataAsync(Ulid userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("DeleteFileSystemDataAsync - UserId: {UserId}", userId);

        var deletedRows = await RunInTransactionAsync(
            userId, nameof(DeleteFileSystemDataAsync), DeleteFileSystemDataStepAsync, cancellationToken);

        _logger.LogInformation("Deleted {Count} flags, ratings and tags for user: {UserId}", deletedRows, userId);

        return deletedRows;
    }

    public async Task<int> DeleteSettingsAsync(Ulid userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("DeleteSettingsAsync - UserId: {UserId}", userId);

        var deletedRows = await RunInTransactionAsync(
            userId, nameof(DeleteSettingsAsync), DeleteSettingsStepAsync, cancellationToken);

        _logger.LogInformation("Deleted {Count} settings rows for user: {UserId}", deletedRows, userId);

        return deletedRows;
    }

    public async Task<UserDataDeletionCounts> DeleteAllAsync(Ulid userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("DeleteAllAsync - UserId: {UserId}", userId);

        // The same steps the single-kind deletes run, on one transaction, so a
        // failure in the last leaves the first undone rather than half an
        // account. File system data before snapshots: its tag assignments
        // include the snapshot copies, which then no longer hold up the trees.
        var counts = await RunInTransactionAsync(
            userId,
            nameof(DeleteAllAsync),
            async (connection, transaction, parameters) =>
            {
                var fileSystemItems = await DeleteFileSystemDataStepAsync(connection, transaction, parameters);
                var snapshots = await DeleteSnapshotsStepAsync(connection, transaction, parameters);
                var labels = await DeleteLabelsStepAsync(connection, transaction, parameters);
                var settings = await DeleteSettingsStepAsync(connection, transaction, parameters);

                return new UserDataDeletionCounts(snapshots, fileSystemItems, labels, settings);
            },
            cancellationToken);

        _logger.LogInformation("Deleted all data for user: {UserId} - {Counts}", userId, counts);

        return counts;
    }

    /// <summary>
    /// Bindings before the rows they bind, shared rows only once unbound, the
    /// snapshots themselves last: the order DeleteSnapshotByIdAsync uses, run
    /// once over the user's whole set. Labels are left in place; only their
    /// assignments to these snapshots go. Answers with the snapshots deleted.
    /// </summary>
    private static async Task<int> DeleteSnapshotsStepAsync(DbConnection connection, DbTransaction transaction, object parameters)
    {
        string[] statements =
        [
            SqlScripts.DeleteTagsToSnapshotFilesByUserSql,
            SqlScripts.DeleteTagsToSnapshotFoldersByUserSql,
            SqlScripts.DeleteLabelsToSnapshotsByUserSql,
            SqlScripts.DeleteUsersToSnapshotsByUserSql,
            SqlScripts.DeleteFilesToFoldersByUserSql,
            SqlScripts.DeleteUnboundFilesByUserSql,
            SqlScripts.DeleteFoldersToFoldersByUserSql,
            SqlScripts.DeleteUnboundFoldersByUserSql,
            SqlScripts.DeletePcsToStorageDrivesByUserSql,
            SqlScripts.DeleteUnboundPcsByUserSql,
            SqlScripts.DeleteVolumeInfosByUserSql,
            SqlScripts.DeleteUnboundVolumesByUserSql,
            SqlScripts.DeleteUnboundStorageDrivesByUserSql,
        ];

        foreach (var statement in statements)
        {
            await connection.ExecuteAsync(statement, parameters, transaction);
        }

        return await connection.ExecuteAsync(SqlScripts.DeleteSnapshotsByUserSql, parameters, transaction);
    }

    /// <summary>Flags, ratings, then tags after their assignments. Answers with flags, ratings and tags deleted.</summary>
    private static async Task<int> DeleteFileSystemDataStepAsync(DbConnection connection, DbTransaction transaction, object parameters)
    {
        var deletedFlags = await connection.ExecuteAsync(SqlScripts.DeleteFsItemFlagsByUserSql, parameters, transaction);
        var deletedRatings = await connection.ExecuteAsync(SqlScripts.DeleteFsItemRatingsByUserSql, parameters, transaction);

        // Assignments before the tags they name, so nothing is briefly
        // pointing at a tag that has gone.
        await connection.ExecuteAsync(SqlScripts.DeleteFsItemTagsByUserSql, parameters, transaction);
        await connection.ExecuteAsync(SqlScripts.DeleteTagsToSnapshotFilesByTagOwnerSql, parameters, transaction);
        await connection.ExecuteAsync(SqlScripts.DeleteTagsToSnapshotFoldersByTagOwnerSql, parameters, transaction);
        var deletedTags = await connection.ExecuteAsync(SqlScripts.DeleteTagsByUserSql, parameters, transaction);

        return deletedFlags + deletedRatings + deletedTags;
    }

    /// <summary>Labels, after any assignment still naming them. Answers with the labels deleted.</summary>
    private static async Task<int> DeleteLabelsStepAsync(DbConnection connection, DbTransaction transaction, object parameters)
    {
        await connection.ExecuteAsync(SqlScripts.DeleteLabelsToSnapshotsByLabelOwnerSql, parameters, transaction);

        return await connection.ExecuteAsync(SqlScripts.DeleteLabelsByUserSql, parameters, transaction);
    }

    /// <summary>Shortcuts, folder tabs and the settings row. Answers with the rows deleted.</summary>
    private static async Task<int> DeleteSettingsStepAsync(DbConnection connection, DbTransaction transaction, object parameters)
    {
        var deletedRows = 0;
        deletedRows += await connection.ExecuteAsync(SqlScripts.DeleteAllUserKeyBindingsSql, parameters, transaction);
        deletedRows += await connection.ExecuteAsync(SqlScripts.DeleteFolderTabsByUserSql, parameters, transaction);
        deletedRows += await connection.ExecuteAsync(SqlScripts.DeleteUserSettingsByUserSql, parameters, transaction);

        return deletedRows;
    }

    /// <summary>
    /// Runs <paramref name="work"/> on one transaction scoped to the user,
    /// committing only if all of it succeeds.
    /// </summary>
    private async Task<TResult> RunInTransactionAsync<TResult>(
        Ulid userId,
        string operationName,
        Func<DbConnection, DbTransaction, object, Task<TResult>> work,
        CancellationToken cancellationToken)
    {
        var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);
        await using var transaction = await sqLiteConnection.BeginTransactionAsync(cancellationToken);

        try
        {
            var result = await work(sqLiteConnection, transaction, new { UserId = userId });

            await transaction.CommitAsync(cancellationToken);

            return result;
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "ERROR - {Operation} rolled back", operationName);
            throw;
        }
    }
}
