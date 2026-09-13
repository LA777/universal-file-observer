using Dapper;
using Microsoft.Extensions.Logging;
using Ufo.Abstractions;
using Ufo.Abstractions.Database;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;

namespace Ufo.Database.Repositories;

public class TagsRepository : ITagsRepository
{
    private readonly ILogger<TagsRepository> _logger;
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public TagsRepository(IDbConnectionFactory dbConnectionFactory, ILogger<TagsRepository>? logger)
    {
        _dbConnectionFactory = dbConnectionFactory ?? throw new ArgumentNullException(nameof(dbConnectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<TagEntity>> GetTagsAsync(
        Ulid userId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetTagsAsync - UserId: {UserId}", userId);

        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);

            var tags = await sqLiteConnection.QueryAsync<TagEntity>(SqlScripts.SelectTagsSql, new { UserId = userId });

            return tags.ToList();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - GetTagsAsync");
            throw;
        }
    }

    public async Task<TagEntity> GetOrCreateTagAsync(TagEntity tag, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tag);

        _logger.LogInformation("GetOrCreateTagAsync - UserId: {UserId}, Name: {Name}", tag.UserId, tag.Name);

        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);

            // Insert-then-read rather than read-then-insert: two clients naming
            // the same new tag at once both end up with the one row, instead of
            // one of them failing on the unique constraint.
            await sqLiteConnection.ExecuteAsync(
                SqlScripts.InsertTagSql,
                new { tag.Id, tag.Name, tag.ColorHex, tag.UserId });

            // Read back, because the insert does nothing when the name was taken
            // and the caller needs the id of whichever row now holds that name.
            var storedTag = await sqLiteConnection.QueryFirstAsync<TagEntity>(
                SqlScripts.SelectTagByNameSql,
                new { tag.UserId, tag.Name });

            return storedTag;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - GetOrCreateTagAsync");
            throw;
        }
    }

    public async Task<IReadOnlyList<FsItemTagEntity>> GetFsItemTagsAsync(
        Ulid userId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetFsItemTagsAsync - UserId: {UserId}", userId);

        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);

            var assignments = await sqLiteConnection.QueryAsync<FsItemTagEntity>(
                SqlScripts.SelectFsItemTagsSql,
                new { UserId = userId });

            return assignments.ToList();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - GetFsItemTagsAsync");
            throw;
        }
    }

    public async Task<ServerResult> SetFsItemTagAsync(
        Ulid tagId,
        IReadOnlyList<string> fullPaths,
        bool isApplied,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fullPaths);

        _logger.LogInformation(
            "SetFsItemTagAsync - TagId: {TagId}, Count: {Count}, Applied: {IsApplied}",
            tagId,
            fullPaths.Count,
            isApplied);

        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);

            // One transaction: the user acted on a selection, and half of it
            // taking the tag is a listing nobody can read.
            using var transaction = sqLiteConnection.BeginTransaction();

            await sqLiteConnection.ExecuteAsync(
                isApplied ? SqlScripts.InsertFsItemTagSql : SqlScripts.DeleteFsItemTagSql,
                fullPaths.Select(fullPath => new { TagId = tagId, FullPath = fullPath }),
                transaction);

            transaction.Commit();

            return new ServerResult
            {
                ActionName = "Setting Tags.",
                Result = Result.Success,
                Priority = ActionPriority.Highest
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - SetFsItemTagAsync");
            throw;
        }
    }
}
