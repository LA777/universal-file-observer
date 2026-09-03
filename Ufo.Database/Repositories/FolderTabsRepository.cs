using Dapper;
using Microsoft.Extensions.Logging;
using Ufo.Abstractions;
using Ufo.Abstractions.Database;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;

namespace Ufo.Database.Repositories;

public class FolderTabsRepository : IFolderTabsRepository
{
    private readonly ILogger<FolderTabsRepository> _logger;
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public FolderTabsRepository(IDbConnectionFactory dbConnectionFactory, ILogger<FolderTabsRepository>? logger)
    {
        _dbConnectionFactory = dbConnectionFactory ?? throw new ArgumentNullException(nameof(dbConnectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<FolderTabEntity>> GetFolderTabsAsync(
        Ulid userId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetFolderTabsAsync - UserId: {UserId}", userId);

        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);

            var folderTabs = await sqLiteConnection.QueryAsync<FolderTabEntity>(
                SqlScripts.SelectFolderTabsSql,
                new { UserId = userId });

            return folderTabs.ToList();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - GetFolderTabsAsync");
            throw;
        }
    }

    public async Task<ServerResult> SaveFolderTabsAsync(
        IReadOnlyList<FolderTabEntity> folderTabs,
        Ulid userId,
        string panelId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folderTabs);

        _logger.LogInformation(
            "SaveFolderTabsAsync - UserId: {UserId}, Panel: {PanelId}, Count: {Count}",
            userId,
            panelId,
            folderTabs.Count);

        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);

            // One transaction for the panel. Deleting first and then failing to
            // insert would lose every locked tab the user had, which is the one
            // thing locking them was supposed to prevent.
            using var transaction = sqLiteConnection.BeginTransaction();

            await sqLiteConnection.ExecuteAsync(
                SqlScripts.DeleteFolderTabsForPanelSql,
                new { UserId = userId, PanelId = panelId },
                transaction);

            if (folderTabs.Count > 0)
            {
                await sqLiteConnection.ExecuteAsync(
                    SqlScripts.InsertFolderTabSql,
                    folderTabs.Select(folderTab => new
                    {
                        folderTab.Id,
                        PanelId = panelId,
                        folderTab.FolderPath,
                        folderTab.Position,
                        UserId = userId
                    }),
                    transaction);
            }

            transaction.Commit();

            return new ServerResult
            {
                ActionName = "Saving Folder Tabs.",
                Result = Result.Success,
                Priority = ActionPriority.Highest
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - SaveFolderTabsAsync");
            throw;
        }
    }
}
