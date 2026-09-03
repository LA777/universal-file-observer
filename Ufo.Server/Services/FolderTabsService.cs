using Ufo.Abstractions;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.DataTransferObjects;
using Ufo.Abstractions.Requests;

namespace Ufo.Server.Services;

public interface IFolderTabsService
{
    /// <summary>
    /// The user's locked tabs, in display order, with anything the server may no
    /// longer read left out.
    /// </summary>
    Task<IReadOnlyList<FolderTabDto>> GetFolderTabsAsync(Ulid userId, CancellationToken cancellationToken);

    /// <summary>Replaces one panel's locked tabs.</summary>
    Task<ServerResult> SaveFolderTabsAsync(
        FolderTabsRequest request,
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

    /// <summary>
    /// As many tabs as one pane will keep. Not a limit anybody should reach by
    /// hand; it is here so a scripted caller cannot turn the table into a place
    /// to store arbitrary amounts of text.
    /// </summary>
    private const int MaximumTabsPerPanel = 50;

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
                Position = savedTab.Position
            })
            .ToList();
    }

    public async Task<ServerResult> SaveFolderTabsAsync(
        FolderTabsRequest request,
        Ulid userId,
        CancellationToken cancellationToken)
    {
        if (request?.FolderPaths is null || string.IsNullOrWhiteSpace(request.PanelId))
        {
            return Rejected("No folder tabs were given.");
        }

        if (!KnownPanelIds.Contains(request.PanelId, StringComparer.Ordinal))
        {
            return Rejected($"'{request.PanelId}' is not a panel this version of UFO has.");
        }

        if (request.FolderPaths.Count > MaximumTabsPerPanel)
        {
            return Rejected($"A panel may keep at most {MaximumTabsPerPanel} locked tabs.");
        }

        var folderTabs = new List<FolderTabEntity>();
        var seenPaths = new HashSet<string>(PathComparer);

        foreach (var requestedPath in request.FolderPaths)
        {
            if (!_pathGuard.TryResolve(requestedPath, out var resolvedPath))
            {
                return Rejected($"'{requestedPath}' is not a folder this server is allowed to open.");
            }

            if (!Directory.Exists(resolvedPath))
            {
                return Rejected($"'{requestedPath}' is not a folder that exists.");
            }

            // Two locked tabs on one folder in one pane are the same tab twice.
            // Dropped rather than rejected: it is a duplicate, not a mistake
            // worth stopping the whole save for.
            if (!seenPaths.Add(resolvedPath))
            {
                continue;
            }

            folderTabs.Add(new FolderTabEntity
            {
                PanelId = request.PanelId,
                FolderPath = resolvedPath,
                Position = folderTabs.Count,
                UserId = userId
            });
        }

        _logger.LogInformation(
            "SaveFolderTabsAsync - UserId: {UserId}, Panel: {PanelId}, Count: {Count}",
            userId,
            request.PanelId,
            folderTabs.Count);

        return await _folderTabsRepository.SaveFolderTabsAsync(
            folderTabs,
            userId,
            request.PanelId,
            cancellationToken);
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    private static ServerResult Rejected(string message) =>
        new()
        {
            ActionName = "Saving Folder Tabs.",
            Result = Result.Error,
            Priority = ActionPriority.Highest,
            Message = message
        };
}
