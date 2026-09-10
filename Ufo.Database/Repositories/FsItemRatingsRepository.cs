using Dapper;
using Microsoft.Extensions.Logging;
using Ufo.Abstractions;
using Ufo.Abstractions.Database;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;

namespace Ufo.Database.Repositories;

public class FsItemRatingsRepository : IFsItemRatingsRepository
{
    private readonly ILogger<FsItemRatingsRepository> _logger;
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public FsItemRatingsRepository(IDbConnectionFactory dbConnectionFactory, ILogger<FsItemRatingsRepository>? logger)
    {
        _dbConnectionFactory = dbConnectionFactory ?? throw new ArgumentNullException(nameof(dbConnectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<FsItemRatingEntity>> GetFsItemRatingsAsync(
        Ulid userId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetFsItemRatingsAsync - UserId: {UserId}", userId);

        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);

            var ratings = await sqLiteConnection.QueryAsync<FsItemRatingEntity>(
                SqlScripts.SelectFsItemRatingsSql,
                new { UserId = userId });

            return ratings.ToList();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - GetFsItemRatingsAsync");
            throw;
        }
    }

    public async Task<ServerResult> SetFsItemRatingsAsync(
        Ulid userId,
        IReadOnlyList<string> fullPaths,
        int rating,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fullPaths);

        _logger.LogInformation(
            "SetFsItemRatingsAsync - UserId: {UserId}, Count: {Count}, Rating: {Rating}",
            userId,
            fullPaths.Count,
            rating);

        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);

            // One transaction because the user rated a selection, not a sequence:
            // half of a multi-select taking the new rating is a listing where
            // nobody can tell which half.
            using var transaction = sqLiteConnection.BeginTransaction();

            if (rating > 0)
            {
                await sqLiteConnection.ExecuteAsync(
                    SqlScripts.UpsertFsItemRatingSql,
                    fullPaths.Select(fullPath => new
                    {
                        Id = Ulid.NewUlid(),
                        FullPath = fullPath,
                        Rating = rating,
                        UserId = userId
                    }),
                    transaction);
            }
            else
            {
                // Zero is unrated, and unrated is the absence of a row.
                await sqLiteConnection.ExecuteAsync(
                    SqlScripts.DeleteFsItemRatingSql,
                    fullPaths.Select(fullPath => new { UserId = userId, FullPath = fullPath }),
                    transaction);
            }

            transaction.Commit();

            return new ServerResult
            {
                ActionName = "Setting Ratings.",
                Result = Result.Success,
                Priority = ActionPriority.Highest
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - SetFsItemRatingsAsync");
            throw;
        }
    }
}
