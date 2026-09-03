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

    public async Task<ServerResult> LockFolderTabAsync(
        FolderTabEntity folderTab,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folderTab);

        _logger.LogInformation(
            "LockFolderTabAsync - UserId: {UserId}, Panel: {PanelId}",
            folderTab.UserId,
            folderTab.PanelId);

        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);

            // One row and no transaction: there is nothing to be half-way
            // through. The statement is a no-op when the folder is already
            // locked, so clicking a closed padlock twice is not an error.
            await sqLiteConnection.ExecuteAsync(
                SqlScripts.InsertFolderTabSql,
                new
                {
                    folderTab.Id,
                    folderTab.PanelId,
                    folderTab.FolderPath,
                    folderTab.UserId
                });

            return Succeeded("Locking Folder Tab.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - LockFolderTabAsync");
            throw;
        }
    }

    public async Task<ServerResult> UnlockFolderTabAsync(
        Ulid userId,
        string panelId,
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("UnlockFolderTabAsync - UserId: {UserId}, Panel: {PanelId}", userId, panelId);

        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);

            // A row that was not there is the end state the caller asked for, so
            // no rows affected is a success rather than something to report.
            await sqLiteConnection.ExecuteAsync(
                SqlScripts.DeleteFolderTabSql,
                new { UserId = userId, PanelId = panelId, FolderPath = folderPath });

            return Succeeded("Unlocking Folder Tab.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - UnlockFolderTabAsync");
            throw;
        }
    }

    private static ServerResult Succeeded(string actionName) =>
        new()
        {
            ActionName = actionName,
            Result = Result.Success,
            Priority = ActionPriority.Highest
        };
}
