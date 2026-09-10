using Dapper;
using Microsoft.Extensions.Logging;
using Ufo.Abstractions;
using Ufo.Abstractions.Database;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;

namespace Ufo.Database.Repositories;

public class FsItemFlagsRepository : IFsItemFlagsRepository
{
    private readonly ILogger<FsItemFlagsRepository> _logger;
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public FsItemFlagsRepository(IDbConnectionFactory dbConnectionFactory, ILogger<FsItemFlagsRepository>? logger)
    {
        _dbConnectionFactory = dbConnectionFactory ?? throw new ArgumentNullException(nameof(dbConnectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<FsItemFlagEntity>> GetFsItemFlagsAsync(
        Ulid userId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetFsItemFlagsAsync - UserId: {UserId}", userId);

        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);

            var flags = await sqLiteConnection.QueryAsync<FsItemFlagEntity>(
                SqlScripts.SelectFsItemFlagsSql,
                new { UserId = userId });

            return flags.ToList();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - GetFsItemFlagsAsync");
            throw;
        }
    }

    public async Task<ServerResult> SetFsItemFlagsAsync(
        Ulid userId,
        IReadOnlyList<string> fullPaths,
        bool isFlagEnabled,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fullPaths);

        _logger.LogInformation(
            "SetFsItemFlagsAsync - UserId: {UserId}, Count: {Count}, Enabled: {IsFlagEnabled}",
            userId,
            fullPaths.Count,
            isFlagEnabled);

        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);

            // One transaction because the user flagged a selection, not a
            // sequence: half of a multi-select turning the flag on is a listing
            // where nobody can tell which half.
            using var transaction = sqLiteConnection.BeginTransaction();

            if (isFlagEnabled)
            {
                await sqLiteConnection.ExecuteAsync(
                    SqlScripts.InsertFsItemFlagSql,
                    fullPaths.Select(fullPath => new
                    {
                        Id = Ulid.NewUlid(),
                        FullPath = fullPath,
                        UserId = userId
                    }),
                    transaction);
            }
            else
            {
                await sqLiteConnection.ExecuteAsync(
                    SqlScripts.DeleteFsItemFlagSql,
                    fullPaths.Select(fullPath => new { UserId = userId, FullPath = fullPath }),
                    transaction);
            }

            transaction.Commit();

            return new ServerResult
            {
                ActionName = "Setting Flags.",
                Result = Result.Success,
                Priority = ActionPriority.Highest
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - SetFsItemFlagsAsync");
            throw;
        }
    }
}
