using Ufo.Abstractions;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.DataTransferObjects;
using Ufo.Abstractions.Requests;

namespace Ufo.Server.Services;

public interface IFolderTabsService
{
    /// <summary>
    /// The user's locked tabs, in display order, each flagged with whether its
    /// folder is there right now.
    /// </summary>
    Task<IReadOnlyList<FolderTabDto>> GetFolderTabsAsync(Ulid userId, CancellationToken cancellationToken);

    /// <summary>Locks one tab. Locking one already locked is not an error.</summary>
    Task<ServerResult> LockFolderTabAsync(
        FolderTabRequest request,
        Ulid userId,
        CancellationToken cancellationToken);

    /// <summary>Unlocks one tab. Unlocking one that is not locked is not an error.</summary>
    Task<ServerResult> UnlockFolderTabAsync(
        FolderTabRequest request,
        Ulid userId,
        CancellationToken cancellationToken);
}

public class FolderTabsService : IFolderTabsService
{
    /// <summary>
    /// The panes a tab can belong to. Two of them, because the Files view is two
    /// panes - an id outside this set is a row nothing would ever restore.
    /// </summary>
    private static readonly string[] KnownPanelIds = ["left", "right"];

    private readonly IFolderTabsRepository _folderTabsRepository;
    private readonly IPathGuard _pathGuard;
    private readonly ILogger<FolderTabsService> _logger;

    public FolderTabsService(
        IFolderTabsRepository folderTabsRepository,
        IPathGuard pathGuard,
        ILogger<FolderTabsService> logger)
    {
        _folderTabsRepository = folderTabsRepository ?? throw new ArgumentNullException(nameof(folderTabsRepository));
        _pathGuard = pathGuard ?? throw new ArgumentNullException(nameof(pathGuard));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<FolderTabDto>> GetFolderTabsAsync(
        Ulid userId,
        CancellationToken cancellationToken)
    {
        var savedTabs = await _folderTabsRepository.GetFolderTabsAsync(userId, cancellationToken);

        return savedTabs
            // Re-checked on the way out, not only on the way in. A tab locked
            // while the server was unrestricted must not come back and hand the
            // user a folder outside the roots it is now confined to - the
            // allow-list is configuration and can be tightened between sessions.
            .Where(savedTab => _pathGuard.TryResolveQuietly(savedTab.FolderPath, out _))
            .Select(savedTab => new FolderTabDto
            {
                PanelId = savedTab.PanelId,
                FolderPath = savedTab.FolderPath,
                Position = savedTab.Position,
                // Reported rather than acted on. A missing folder is usually a
                // drive that is not plugged in, and silently dropping the tab
                // would discard a pin the user set deliberately.
                IsAvailable = Directory.Exists(savedTab.FolderPath)
            })
            .ToList();
    }

    public async Task<ServerResult> LockFolderTabAsync(
        FolderTabRequest request,
        Ulid userId,
        CancellationToken cancellationToken)
    {
        if (DescribeInvalidRequest(request) is { } rejection)
        {
            return Rejected(rejection);
        }

        if (!_pathGuard.TryResolve(request!.FolderPath, out var resolvedPath))
        {
            return Rejected($"'{request.FolderPath}' is not a folder this server is allowed to open.");
        }

        if (!Directory.Exists(resolvedPath))
        {
            return Rejected($"'{request.FolderPath}' is not a folder that exists.");
        }

        _logger.LogInformation("LockFolderTabAsync - UserId: {UserId}, Panel: {PanelId}", userId, request.PanelId);

        return await _folderTabsRepository.LockFolderTabAsync(
            new FolderTabEntity
            {
                PanelId = request.PanelId,
                FolderPath = resolvedPath,
                UserId = userId
            },
            cancellationToken);
    }

    public async Task<ServerResult> UnlockFolderTabAsync(
        FolderTabRequest request,
        Ulid userId,
        CancellationToken cancellationToken)
    {
        if (DescribeInvalidRequest(request) is { } rejection)
        {
            return Rejected(rejection);
        }

        // Unlocking is the way out of a tab whose folder has gone, so neither the
        // guard nor the file system gets a say here. Refusing to remove a row
        // because the folder it names is unreachable would leave the user with a
        // locked tab they cannot close and cannot unlock.
        _pathGuard.TryResolveQuietly(request!.FolderPath, out var resolvedPath);

        _logger.LogInformation("UnlockFolderTabAsync - UserId: {UserId}, Panel: {PanelId}", userId, request.PanelId);

        // Both spellings: the row holds the resolved path, but a caller sending
        // the path as it was given should still be able to let go of it.
        var result = await _folderTabsRepository.UnlockFolderTabAsync(
            userId,
            request.PanelId,
            request.FolderPath,
            cancellationToken);

        if (!string.IsNullOrEmpty(resolvedPath) && resolvedPath != request.FolderPath)
        {
            result = await _folderTabsRepository.UnlockFolderTabAsync(
                userId,
                request.PanelId,
                resolvedPath,
                cancellationToken);
        }

        return result;
    }

    private static string? DescribeInvalidRequest(FolderTabRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PanelId) || string.IsNullOrWhiteSpace(request.FolderPath))
        {
            return "No folder tab was given.";
        }

        return KnownPanelIds.Contains(request.PanelId, StringComparer.Ordinal)
            ? null
            : $"'{request.PanelId}' is not a panel this version of UFO has.";
    }

    private static ServerResult Rejected(string message) =>
        new()
        {
            ActionName = "Saving Folder Tabs.",
            Result = Result.Error,
            Priority = ActionPriority.Highest,
            Message = message
        };
}
