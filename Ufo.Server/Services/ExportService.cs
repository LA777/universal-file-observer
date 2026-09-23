using System.Globalization;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.DataTransferObjects;
using Ufo.Server.Mappers;

namespace Ufo.Server.Services;

/// <summary>Which button on the Settings page's Export section was pressed.</summary>
public enum ExportScope
{
    /// <summary>The account with its settings, shortcuts and locked folder tabs.</summary>
    User,

    /// <summary>Flags, ratings and tags on files and folders on disk.</summary>
    FileSystem,

    /// <summary>Every snapshot with its whole tree, and the labels.</summary>
    Snapshots,

    /// <summary>All three in one file.</summary>
    All
}

/// <summary>A finished export: the bytes of the zip and the name to save it under.</summary>
public record ExportArchive(string FileName, byte[] Content);

public interface IExportService
{
    /// <summary>
    /// Gathers everything in <paramref name="scope"/> for the user into one JSON
    /// document and zips it. Empty sections are exported as empty, not omitted:
    /// a user with no snapshots still gets a file saying so.
    /// </summary>
    Task<ExportArchive> ExportAsync(ExportScope scope, Ulid userId, CancellationToken cancellationToken);
}

public class ExportService : IExportService
{
    /// <summary>Used in the archive's name and the entry's; both carry the scope and the moment.</summary>
    private const string FileNameTimestampFormat = "yyyyMMdd-HHmmss";

    private readonly IUserRepository _userRepository;
    private readonly IUserSettingsService _userSettingsService;
    private readonly IKeyBindingsService _keyBindingsService;
    private readonly IFolderTabsRepository _folderTabsRepository;
    private readonly IFsItemFlagsService _fsItemFlagsService;
    private readonly IFsItemRatingsService _fsItemRatingsService;
    private readonly ITagsService _tagsService;
    private readonly ILabelsService _labelsService;
    private readonly ISnapshotRepository _snapshotRepository;
    private readonly IApplicationVersionService _applicationVersionService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ExportService> _logger;

    public ExportService(
        IUserRepository userRepository,
        IUserSettingsService userSettingsService,
        IKeyBindingsService keyBindingsService,
        IFolderTabsRepository folderTabsRepository,
        IFsItemFlagsService fsItemFlagsService,
        IFsItemRatingsService fsItemRatingsService,
        ITagsService tagsService,
        ILabelsService labelsService,
        ISnapshotRepository snapshotRepository,
        IApplicationVersionService applicationVersionService,
        TimeProvider timeProvider,
        ILogger<ExportService> logger)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _userSettingsService = userSettingsService ?? throw new ArgumentNullException(nameof(userSettingsService));
        _keyBindingsService = keyBindingsService ?? throw new ArgumentNullException(nameof(keyBindingsService));
        _folderTabsRepository = folderTabsRepository ?? throw new ArgumentNullException(nameof(folderTabsRepository));
        _fsItemFlagsService = fsItemFlagsService ?? throw new ArgumentNullException(nameof(fsItemFlagsService));
        _fsItemRatingsService = fsItemRatingsService ?? throw new ArgumentNullException(nameof(fsItemRatingsService));
        _tagsService = tagsService ?? throw new ArgumentNullException(nameof(tagsService));
        _labelsService = labelsService ?? throw new ArgumentNullException(nameof(labelsService));
        _snapshotRepository = snapshotRepository ?? throw new ArgumentNullException(nameof(snapshotRepository));
        _applicationVersionService = applicationVersionService ?? throw new ArgumentNullException(nameof(applicationVersionService));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The scope as it appears in the file name and the document: lower case, one word.</summary>
    public static string ScopeName(ExportScope scope) => scope switch
    {
        ExportScope.User => "user",
        ExportScope.FileSystem => "filesystem",
        ExportScope.Snapshots => "snapshots",
        ExportScope.All => "all",
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown export scope.")
    };

    public async Task<ExportArchive> ExportAsync(ExportScope scope, Ulid userId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("ExportAsync - Scope: {Scope}, UserId: {UserId}", scope, userId);

        var exportedAt = _timeProvider.GetUtcNow();
        var document = new ExportDocument
        {
            ApplicationVersion = _applicationVersionService.Version,
            ExportedAt = exportedAt,
            Scope = ScopeName(scope)
        };

        if (scope is ExportScope.User or ExportScope.All)
        {
            document.User = await BuildUserExportAsync(userId, cancellationToken);
        }

        if (scope is ExportScope.FileSystem or ExportScope.All)
        {
            document.FileSystem = await BuildFileSystemExportAsync(userId, cancellationToken);
        }

        if (scope is ExportScope.Snapshots or ExportScope.All)
        {
            document.Snapshots = await BuildSnapshotsExportAsync(userId, cancellationToken);
        }

        var baseFileName = $"ufo-export-{ScopeName(scope)}-{exportedAt.ToString(FileNameTimestampFormat, CultureInfo.InvariantCulture)}";
        var content = ExportArchiveWriter.Write(document, baseFileName);

        _logger.LogInformation("Export written - {FileName}, {Bytes} bytes", baseFileName, content.Length);

        return new ExportArchive($"{baseFileName}.zip", content);
    }

    private async Task<UserExport> BuildUserExportAsync(Ulid userId, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetUserByIdAsync(userId, cancellationToken);
        var settings = await _userSettingsService.GetUserSettingsAsync(userId, cancellationToken);
        var keyBindings = await _keyBindingsService.GetKeyBindingsAsync(userId, cancellationToken);
        // The rows as stored rather than as the panes restore them: the file may
        // be read on a machine where the guard and the folders are different.
        var folderTabs = await _folderTabsRepository.GetFolderTabsAsync(userId, cancellationToken);

        return new UserExport
        {
            Id = user.Id,
            Name = user.Name,
            CreatedAt = user.CreatedAt,
            IsAdmin = user.IsAdmin,
            Settings = settings,
            KeyBindings = keyBindings.ToList(),
            FolderTabs = folderTabs
                .Select(tab => new FolderTabExport { PanelId = tab.PanelId, FolderPath = tab.FolderPath, Position = tab.Position })
                .ToList()
        };
    }

    private async Task<FileSystemExport> BuildFileSystemExportAsync(Ulid userId, CancellationToken cancellationToken)
    {
        var flaggedPaths = await _fsItemFlagsService.GetFlaggedPathsAsync(userId, cancellationToken);
        var ratings = await _fsItemRatingsService.GetRatingsAsync(userId, cancellationToken);
        var tags = await _tagsService.GetFsItemTagsAsync(userId, cancellationToken);

        return new FileSystemExport
        {
            FlaggedPaths = flaggedPaths.ToList(),
            Ratings = ratings.ToDictionary(pair => pair.Key, pair => pair.Value),
            Tags = tags.Tags,
            TagIdsByPath = tags.TagIdsByPath
        };
    }

    private async Task<SnapshotsExport> BuildSnapshotsExportAsync(Ulid userId, CancellationToken cancellationToken)
    {
        var labels = await _labelsService.GetAllLabelsAsync(userId, cancellationToken);

        // The list query stops at each root folder; the tree under it comes
        // from the by-id read, which is also where the snapshot's tags are
        // stitched on. One read per snapshot. Every tree is held in memory
        // until the whole document is written, so a very large library costs
        // memory in proportion to its size; streaming would be the next step.
        var snapshots = new List<SnapshotDto>();
        foreach (var summary in await _snapshotRepository.GetAllSnapshotsAsync(userId, cancellationToken))
        {
            var snapshot = await _snapshotRepository.GetSnapshotByIdAsync(summary.Id, userId, cancellationToken);
            if (snapshot is not null)
            {
                snapshots.Add(snapshot.ToDto());
            }
        }

        return new SnapshotsExport
        {
            Labels = labels,
            Snapshots = snapshots
        };
    }
}
