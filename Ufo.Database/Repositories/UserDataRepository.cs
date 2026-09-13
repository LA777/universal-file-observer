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

        // Bindings before the rows they bind, shared rows only once unbound, the
        // snapshots themselves last: the order DeleteSnapshotByIdAsync uses, run
        // once over the user's whole set instead of once per snapshot.
        var deletedSnapshots = await ExecuteInTransactionAsync(
            userId,
            countedStatement: SqlScripts.DeleteSnapshotsByUserSql,
            cancellationToken,
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
            SqlScripts.DeleteLabelsByUserSql);

        _logger.LogInformation("Deleted {Count} snapshots for user: {UserId}", deletedSnapshots, userId);

        return deletedSnapshots;
    }

    public async Task<int> DeleteFileSystemDataAsync(Ulid userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("DeleteFileSystemDataAsync - UserId: {UserId}", userId);

        var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);
        await using var transaction = await sqLiteConnection.BeginTransactionAsync(cancellationToken);

        try
        {
            var parameters = new { UserId = userId };

            var deletedFlags = await sqLiteConnection.ExecuteAsync(SqlScripts.DeleteFsItemFlagsByUserSql, parameters, transaction);
            var deletedRatings = await sqLiteConnection.ExecuteAsync(SqlScripts.DeleteFsItemRatingsByUserSql, parameters, transaction);

            // Assignments before the tags they name, so nothing is briefly
            // pointing at a tag that has gone.
            await sqLiteConnection.ExecuteAsync(SqlScripts.DeleteFsItemTagsByUserSql, parameters, transaction);
            await sqLiteConnection.ExecuteAsync(SqlScripts.DeleteTagsToSnapshotFilesByTagOwnerSql, parameters, transaction);
            await sqLiteConnection.ExecuteAsync(SqlScripts.DeleteTagsToSnapshotFoldersByTagOwnerSql, parameters, transaction);
            var deletedTags = await sqLiteConnection.ExecuteAsync(SqlScripts.DeleteTagsByUserSql, parameters, transaction);

            await transaction.CommitAsync(cancellationToken);

            var deletedRows = deletedFlags + deletedRatings + deletedTags;
            _logger.LogInformation(
                "Deleted {Flags} flags, {Ratings} ratings and {Tags} tags for user: {UserId}",
                deletedFlags, deletedRatings, deletedTags, userId);

            return deletedRows;
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "ERROR - DeleteFileSystemDataAsync");
            throw;
        }
    }

    public async Task<int> DeleteSettingsAsync(Ulid userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("DeleteSettingsAsync - UserId: {UserId}", userId);

        var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);
        await using var transaction = await sqLiteConnection.BeginTransactionAsync(cancellationToken);

        try
        {
            var parameters = new { UserId = userId };

            var deletedRows = 0;
            deletedRows += await sqLiteConnection.ExecuteAsync(SqlScripts.DeleteAllUserKeyBindingsSql, parameters, transaction);
            deletedRows += await sqLiteConnection.ExecuteAsync(SqlScripts.DeleteFolderTabsByUserSql, parameters, transaction);
            deletedRows += await sqLiteConnection.ExecuteAsync(SqlScripts.DeleteUserSettingsByUserSql, parameters, transaction);

            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation("Deleted {Count} settings rows for user: {UserId}", deletedRows, userId);

            return deletedRows;
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "ERROR - DeleteSettingsAsync");
            throw;
        }
    }

    /// <summary>
    /// Runs <paramref name="statements"/> in order, then
    /// <paramref name="countedStatement"/>, all in one transaction, and answers
    /// with the rows the last one affected.
    /// </summary>
    private async Task<int> ExecuteInTransactionAsync(
        Ulid userId,
        string countedStatement,
        CancellationToken cancellationToken,
        params string[] statements)
    {
        var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);
        await using var transaction = await sqLiteConnection.BeginTransactionAsync(cancellationToken);

        try
        {
            var parameters = new { UserId = userId };

            foreach (var statement in statements)
            {
                await sqLiteConnection.ExecuteAsync(statement, parameters, transaction);
            }

            var affectedRows = await sqLiteConnection.ExecuteAsync(countedStatement, parameters, transaction);

            await transaction.CommitAsync(cancellationToken);

            return affectedRows;
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "ERROR - user data deletion rolled back");
            throw;
        }
    }
}
