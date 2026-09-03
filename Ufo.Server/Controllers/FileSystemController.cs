using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ufo.Abstractions.Requests;
using Ufo.Server.Models;
using Ufo.Server.Services;

namespace Ufo.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FileSystemController : ControllerBase
{
    // TODO LA - Cover with Functional tests
    private readonly ILogger<FileSystemController> _logger;
    private readonly IPathGuard _pathGuard;
    private readonly IFileSystemOperationService _fileSystemOperationService;
    private readonly IFileNameValidator _fileNameValidator;

    public FileSystemController(
        ILogger<FileSystemController> logger,
        IPathGuard pathGuard,
        IFileSystemOperationService fileSystemOperationService,
        IFileNameValidator fileNameValidator)
    {
        _logger = logger;
        _pathGuard = pathGuard ?? throw new ArgumentNullException(nameof(pathGuard));
        _fileSystemOperationService = fileSystemOperationService
            ?? throw new ArgumentNullException(nameof(fileSystemOperationService));
        _fileNameValidator = fileNameValidator ?? throw new ArgumentNullException(nameof(fileNameValidator));
    }

    [HttpGet("root")]
    [ProducesResponseType(typeof(FileSystemRoot), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult GetFileSystemRoot(CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetFileSystemRoot");
        var fileSystemRoot = new FileSystemRoot();

        foreach (var root in EnumerateRoots())
        {
            fileSystemRoot.Roots.Add(root);
        }

        var folderEntity = ReadFirstReadableFolder(fileSystemRoot.Roots, cancellationToken, out var failureResult);

        if (folderEntity == null)
        {
            if (failureResult is not null)
            {
                return failureResult;
            }

            _logger.LogWarning("No readable starting folder could be resolved.");
            return NoContent();
        }

        fileSystemRoot.Folder = folderEntity;
        fileSystemRoot.NameRules = _fileNameValidator.Rules;

        return Ok(fileSystemRoot);
    }

    /// <summary>
    /// Top-level locations offered to the user.
    /// </summary>
    /// <remarks>
    /// Previously this listed Windows drive letters only, leaving the list empty
    /// on any other platform. A restricted host offers exactly what it allows;
    /// an unrestricted POSIX host offers the file-system root.
    /// </remarks>
    private IEnumerable<string> EnumerateRoots()
    {
        if (_pathGuard.IsRestricted)
        {
            return _pathGuard.AllowedRoots;
        }

        if (OperatingSystem.IsWindows())
        {
            return DriveInfo.GetDrives().Select(driveInfo => driveInfo.Name);
        }

        return [Path.GetPathRoot(Environment.CurrentDirectory) ?? "/"];
    }

    /// <summary>
    /// The folder the UI opens on: the user's home folder when it is readable,
    /// otherwise the first available root.
    /// </summary>
    private IEnumerable<string> EnumerateStartingFolderCandidates(IList<string> roots)
    {
        var homeFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!string.IsNullOrWhiteSpace(homeFolderPath)
            && _pathGuard.TryResolve(homeFolderPath, out var resolvedHomeFolderPath)
            && Directory.Exists(resolvedHomeFolderPath))
        {
            yield return resolvedHomeFolderPath;
        }

        foreach (var root in roots.Where(root => Directory.Exists(root)))
        {
            yield return root;
        }
    }

    /// <summary>
    /// Lists the first starting-folder candidate that can actually be read.
    /// </summary>
    /// <remarks>
    /// A folder that exists can still fail to open - an offline network home folder, a
    /// mount this process may not traverse. Stopping at the first failure would take the
    /// whole response down with it, and the client's error branch never gets to render
    /// the roots, so the panel would be left with no drive buttons to escape through.
    /// </remarks>
    /// <param name="failureResult">
    /// Set only when every candidate failed, and then it carries the last reason.
    /// </param>
    private FsFolder? ReadFirstReadableFolder(
        IList<string> roots,
        CancellationToken cancellationToken,
        out IActionResult? failureResult)
    {
        failureResult = null;

        foreach (var candidatePath in EnumerateStartingFolderCandidates(roots))
        {
            var folderEntity = ReadFolder(candidatePath, candidatePath, cancellationToken, out var candidateFailure);

            if (candidateFailure is null)
            {
                return folderEntity;
            }

            _logger.LogWarning("Starting folder {Path} could not be read; trying the next candidate.", candidatePath);
            failureResult = candidateFailure;
        }

        return null;
    }

    [HttpPost("folder")]
    [ProducesResponseType(typeof(FsFolder), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult GetFolderInfo([FromBody] PathRequest folderPath, CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetFolderInfo");

        var requestedPath = folderPath?.Path;

        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            return BadRequest("No folder path was given.");
        }

        if (!_pathGuard.TryResolve(requestedPath, out var resolvedPath))
        {
            return StatusCode(StatusCodes.Status403Forbidden, DescribeRejectedPath(requestedPath));
        }

        if (!Directory.Exists(resolvedPath))
        {
            return DescribeMissingFolder(requestedPath, resolvedPath);
        }

        return ListFolder(requestedPath, resolvedPath, cancellationToken);
    }

    /// <summary>
    /// Lists an already-resolved folder, turning the ways a read can fail into a
    /// status and a sentence the user can act on.
    /// </summary>
    /// <remarks>
    /// Without this the failures leave the controller as unhandled exceptions - a 500
    /// whose body is a developer exception page or nothing at all, which is what the
    /// error popup used to show for a folder that had been deleted or that the account
    /// running UFO cannot read.
    /// </remarks>
    private IActionResult ListFolder(string requestedPath, string resolvedPath, CancellationToken cancellationToken)
    {
        var folderEntity = ReadFolder(requestedPath, resolvedPath, cancellationToken, out var failureResult);

        if (failureResult is not null)
        {
            return failureResult;
        }

        return folderEntity is null ? NoContent() : Ok(folderEntity);
    }

    /// <summary>
    /// Reads one folder, reporting a failure as the response to send back rather than
    /// as an exception.
    /// </summary>
    /// <param name="failureResult">
    /// Non-null when the read failed, and then the only thing the caller should return.
    /// </param>
    private FsFolder? ReadFolder(
        string requestedPath,
        string resolvedPath,
        CancellationToken cancellationToken,
        out IActionResult? failureResult)
    {
        failureResult = null;

        try
        {
            return GetFolder(resolvedPath, cancellationToken);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(exception, "Access denied while reading {Path}.", resolvedPath);

            failureResult = StatusCode(
                StatusCodes.Status403Forbidden,
                $"'{requestedPath}' cannot be read. The account running UFO does not have permission to list this folder.");
        }
        catch (DirectoryNotFoundException exception)
        {
            _logger.LogWarning(exception, "Folder {Path} disappeared while it was being read.", resolvedPath);

            failureResult = NotFound($"'{requestedPath}' was removed while it was being opened.");
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "I/O failure while reading {Path}.", resolvedPath);

            failureResult = StatusCode(
                StatusCodes.Status500InternalServerError,
                $"'{requestedPath}' could not be read: {exception.Message} "
                + "The drive may be disconnected, offline, or in use by another program.");
        }

        return null;
    }

    /// <summary>
    /// Why a path that resolves to nothing on disk is not there - a file, a folder
    /// that was never there, or a drive that is not mounted.
    /// </summary>
    private IActionResult DescribeMissingFolder(string requestedPath, string resolvedPath)
    {
        // Directory.Exists answers false for a folder that is merely unreachable as well
        // as for one that is not there, so a folder inside a directory this process may
        // not traverse would otherwise be reported as deleted. Reading the attributes is
        // what separates the two: it throws rather than shrugging.
        try
        {
            _ = new DirectoryInfo(resolvedPath).Attributes;
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(exception, "Access denied while reaching {Path}.", resolvedPath);

            return StatusCode(
                StatusCodes.Status403Forbidden,
                $"'{requestedPath}' cannot be opened. The account running UFO does not have permission "
                + "to reach it, or to read one of the folders above it.");
        }
        catch (Exception exception) when (exception is IOException or ArgumentException)
        {
            // Not there, or not a path this machine can express - both are covered below.
            _logger.LogDebug(exception, "Could not read attributes of {Path}.", resolvedPath);
        }

        if (System.IO.File.Exists(resolvedPath))
        {
            _logger.LogWarning("Requested path {Path} is a file, not a folder.", resolvedPath);

            return BadRequest($"'{requestedPath}' is a file, not a folder. Only folders can be opened here.");
        }

        var driveRoot = Path.GetPathRoot(resolvedPath);
        if (!string.IsNullOrEmpty(driveRoot) && !Directory.Exists(driveRoot))
        {
            _logger.LogWarning("Requested path {Path} sits on unavailable drive {Drive}.", resolvedPath, driveRoot);

            return NotFound(
                $"'{requestedPath}' is not available. The drive '{driveRoot}' is not connected or not mounted.");
        }

        _logger.LogWarning("Requested folder {Path} does not exist.", resolvedPath);

        return NotFound(
            $"The folder '{requestedPath}' does not exist. It may have been renamed, moved, or deleted.");
    }

    /// <summary>
    /// Why the path guard turned a path down. The allowed roots are the same list the
    /// caller is already offered as root buttons, so naming them here reveals nothing new.
    /// </summary>
    private string DescribeRejectedPath(string requestedPath)
    {
        if (_pathGuard.IsRestricted)
        {
            return $"'{requestedPath}' is outside the folders this server is allowed to browse. "
                + $"Allowed: {string.Join(", ", _pathGuard.AllowedRoots)}.";
        }

        return $"'{requestedPath}' could not be opened. The path is not a valid file-system path, "
            + "or it leads through a link that cannot be followed.";
    }

    /// <summary>
    /// Lists one folder's immediate contents.
    /// </summary>
    /// <param name="resolvedFolderPath">
    /// A path that has already been through <see cref="IPathGuard.TryResolve"/>. Every
    /// caller must resolve first - this method does not re-check the folder itself, only
    /// the children it lists.
    /// </param>
    private FsFolder? GetFolder(string resolvedFolderPath, CancellationToken cancellationToken)
    {
        var folderEntity = new FsFolder();
        var dirInfo = new DirectoryInfo(resolvedFolderPath);

        foreach (var subfolderPath in Directory.EnumerateDirectories(resolvedFolderPath))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            // Children are re-checked, not assumed to be inside the root because their
            // parent is: enumeration returns symbolic links and junctions, and one of
            // those pointing outside would otherwise be listed here - and then be
            // offered to the user as somewhere to navigate to.
            if (!_pathGuard.IsAllowedChild(subfolderPath))
            {
                continue;
            }

            var subfolderEntity = new FsFolder();
            var directoryInfo = new DirectoryInfo(subfolderPath);
            subfolderEntity.Name = directoryInfo.Name;
            subfolderEntity.FullPath = subfolderPath;
            subfolderEntity.IsHidden = directoryInfo.Attributes.HasFlag(FileAttributes.Hidden);
            subfolderEntity.Size = null;

            folderEntity.ChildFolders.Add(subfolderEntity);
        }

        foreach (var filePath in Directory.EnumerateFiles(resolvedFolderPath))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            if (!_pathGuard.IsAllowedChild(filePath))
            {
                continue;
            }

            var fileInfo = new FileInfo(filePath);
            var file = new FsFile
            {
                Name = Path.GetFileNameWithoutExtension(fileInfo.Name),
                Size = fileInfo.Length,
                FileExtension = fileInfo.Extension,
                FullPath = filePath,
                IsHidden = fileInfo.Attributes.HasFlag(FileAttributes.Hidden)
            };

            folderEntity.Files.Add(file);
        }

        folderEntity.Name = dirInfo.Name;
        folderEntity.FullPath = resolvedFolderPath;

        // Only advertise a parent the caller is actually allowed to open, so that a
        // restricted host does not offer an "up" out of its own allowed root. Checked
        // quietly: standing at an allowed root and having no parent to go to is the
        // normal case, not something to warn about once per listing.
        folderEntity.ParentFolder = dirInfo.Parent is { } parentDirectory
            && _pathGuard.TryResolveQuietly(parentDirectory.FullName, out _)
                ? new FsFolder { FullPath = parentDirectory.FullName }
                : null;

        return folderEntity;
    }

    [HttpPost("search")]
    [ProducesResponseType(typeof(List<FsSearchResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult SearchFileSystem([FromBody] FileSystemSearchRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("SearchFileSystem - Path: {Path}, Query: {Query}", request.Path, request.Query);

        if (string.IsNullOrWhiteSpace(request.Path))
        {
            return BadRequest("A valid root path is required.");
        }

        if (!_pathGuard.TryResolve(request.Path, out var searchRootPath))
        {
            return Forbid();
        }

        if (!Directory.Exists(searchRootPath))
        {
            return BadRequest("A valid root path is required.");
        }

        var maxResults = Math.Clamp(request.MaxResults, 1, 2000);
        var query = request.Query.Trim();
        var extension = string.IsNullOrWhiteSpace(request.Extension)
            ? null
            : (request.Extension.Trim().StartsWith('.') ? request.Extension.Trim() : "." + request.Extension.Trim());

        bool MatchesName(string name) =>
            query.Length == 0 || name.Contains(query, StringComparison.OrdinalIgnoreCase);

        bool MatchesDate(DateTimeOffset modified) =>
            (!request.DateFrom.HasValue || modified >= request.DateFrom.Value)
            && (!request.DateTo.HasValue || modified <= request.DateTo.Value);

        var results = new List<FsSearchResult>();
        var pending = new Stack<string>();

        // Guarding only the starting path is not enough: the walk descends into
        // whatever it finds, and a symbolic link inside an allowed root leads
        // straight out of it. Every directory is re-checked before it is queued,
        // and the set of directories already visited - keyed on the resolved
        // physical path - is what stops a link cycle from looping forever.
        var visitedDirectories = new HashSet<string>(
            OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

        pending.Push(searchRootPath);
        visitedDirectories.Add(searchRootPath);

        while (pending.Count > 0 && results.Count < maxResults && !cancellationToken.IsCancellationRequested)
        {
            var currentDir = pending.Pop();

            try
            {
                foreach (var subDir in Directory.EnumerateDirectories(currentDir))
                {
                    if (!_pathGuard.TryResolveQuietly(subDir, out var resolvedSubDir)
                        || !visitedDirectories.Add(resolvedSubDir))
                    {
                        continue;
                    }

                    pending.Push(resolvedSubDir);

                    if (!request.IncludeFolders || results.Count >= maxResults)
                    {
                        continue;
                    }

                    var dirInfo = new DirectoryInfo(resolvedSubDir);
                    if (MatchesName(dirInfo.Name) && MatchesDate(dirInfo.LastWriteTime))
                    {
                        results.Add(new FsSearchResult
                        {
                            Name = dirInfo.Name,
                            FullPath = resolvedSubDir,
                            IsFile = false,
                            Size = null,
                            ModifiedAt = dirInfo.LastWriteTime,
                            IsHidden = dirInfo.Attributes.HasFlag(FileAttributes.Hidden)
                        });
                    }
                }

                if (!request.IncludeFiles)
                {
                    continue;
                }

                foreach (var filePath in Directory.EnumerateFiles(currentDir))
                {
                    if (results.Count >= maxResults)
                    {
                        break;
                    }

                    // A symbolic link to a file outside the allowed roots would
                    // otherwise be listed here. The containing directory was resolved
                    // before it was queued, so only the entry itself needs checking.
                    if (!_pathGuard.IsAllowedChild(filePath))
                    {
                        continue;
                    }

                    var fileInfo = new FileInfo(filePath);
                    if (!MatchesName(Path.GetFileNameWithoutExtension(fileInfo.Name)))
                    {
                        continue;
                    }

                    if (extension is not null && !string.Equals(fileInfo.Extension, extension, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (request.MinSize.HasValue && fileInfo.Length < request.MinSize.Value)
                    {
                        continue;
                    }

                    if (request.MaxSize.HasValue && fileInfo.Length > request.MaxSize.Value)
                    {
                        continue;
                    }

                    if (!MatchesDate(fileInfo.LastWriteTime))
                    {
                        continue;
                    }

                    results.Add(new FsSearchResult
                    {
                        Name = Path.GetFileNameWithoutExtension(fileInfo.Name),
                        FullPath = filePath,
                        IsFile = true,
                        Size = fileInfo.Length,
                        FileExtension = fileInfo.Extension,
                        ModifiedAt = fileInfo.LastWriteTime,
                        IsHidden = fileInfo.Attributes.HasFlag(FileAttributes.Hidden)
                    });
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip folders we cannot read.
            }
            catch (IOException)
            {
                // Skip folders that disappear or error mid-walk.
            }
        }

        return Ok(results);
    }

    [HttpPost("parent")]
    [ProducesResponseType(typeof(FsFolder), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult GetParentFolderInfo([FromBody] PathRequest folderPath, CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetParentFolderInfo");

        var requestedPath = folderPath?.Path;

        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            return BadRequest("No folder path was given.");
        }

        if (!_pathGuard.TryResolve(requestedPath, out var resolvedPath))
        {
            return StatusCode(StatusCodes.Status403Forbidden, DescribeRejectedPath(requestedPath));
        }

        string? parent = new DirectoryInfo(resolvedPath).Parent?.FullName;

        if (parent is null)
        {
            _logger.LogWarning("Parent folder was not found.");
            return NotFound($"'{requestedPath}' is a top-level location - there is no folder above it.");
        }

        return GetFolderInfo(new PathRequest { Path = parent }, cancellationToken);
    }

    #region Write operations

    /// <summary>
    /// Creates one empty file or folder. The client shows a blank row in the
    /// listing and calls this once a name has been typed into it.
    /// </summary>
    [HttpPost("create")]
    [ProducesResponseType(typeof(FileSystemOperationResult), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult CreateEntry([FromBody] FileSystemCreateRequest request)
    {
        _logger.LogInformation("CreateEntry - IsFile: {IsFile}", request?.IsFile);

        if (request is null)
        {
            return BadRequest("No entry to create was given.");
        }

        var result = _fileSystemOperationService.Create(request.ParentPath, request.Name, request.IsFile);

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result)
            : StatusCode(StatusFor(result.Status), result.Message);
    }

    /// <summary>Renames one entry in place. The name field in the listing calls this.</summary>
    [HttpPost("rename")]
    [ProducesResponseType(typeof(FileSystemOperationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult RenameEntry([FromBody] FileSystemRenameRequest request)
    {
        _logger.LogInformation("RenameEntry");

        if (request is null)
        {
            return BadRequest("No entry to rename was given.");
        }

        var result = _fileSystemOperationService.Rename(request.Path, request.NewName);

        return result.IsSuccess
            ? Ok(result)
            : StatusCode(StatusFor(result.Status), result.Message);
    }

    /// <summary>Copies entries into another folder - in the UI, the other panel's.</summary>
    [HttpPost("copy")]
    [ProducesResponseType(typeof(FileSystemBatchResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult CopyEntries([FromBody] FileSystemTransferRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("CopyEntries - Count: {Count}", request?.Paths?.Count);

        if (DescribeInvalidTransfer(request) is { } rejection)
        {
            return BadRequest(rejection);
        }

        return Ok(_fileSystemOperationService.Copy(
            request!.Paths,
            request.DestinationFolderPath,
            request.Overwrite,
            cancellationToken));
    }

    /// <summary>Moves entries into another folder - in the UI, the other panel's.</summary>
    [HttpPost("move")]
    [ProducesResponseType(typeof(FileSystemBatchResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult MoveEntries([FromBody] FileSystemTransferRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("MoveEntries - Count: {Count}", request?.Paths?.Count);

        if (DescribeInvalidTransfer(request) is { } rejection)
        {
            return BadRequest(rejection);
        }

        return Ok(_fileSystemOperationService.Move(
            request!.Paths,
            request.DestinationFolderPath,
            request.Overwrite,
            cancellationToken));
    }

    /// <summary>
    /// Deletes entries permanently, folders with their contents. There is no
    /// recycle bin behind this - the client confirms before it calls.
    /// </summary>
    [HttpPost("delete")]
    [ProducesResponseType(typeof(FileSystemBatchResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult DeleteEntries([FromBody] FileSystemDeleteRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("DeleteEntries - Count: {Count}", request?.Paths?.Count);

        if (request?.Paths is not { Count: > 0 })
        {
            return BadRequest("No items to delete were given.");
        }

        return Ok(_fileSystemOperationService.Delete(request.Paths, cancellationToken));
    }

    /// <summary>
    /// Why a copy or move request is not worth attempting, or null when it is.
    /// </summary>
    /// <remarks>
    /// Only the shape of the request is judged here. Whether the destination is
    /// reachable, and whether each entry can actually be transferred, are answers
    /// that belong per-entry in the batch result - one locked file is not a bad
    /// request.
    /// </remarks>
    private static string? DescribeInvalidTransfer(FileSystemTransferRequest? request)
    {
        if (request?.Paths is not { Count: > 0 })
        {
            return "No items to transfer were given.";
        }

        return string.IsNullOrWhiteSpace(request.DestinationFolderPath)
            ? "No destination folder was given."
            : null;
    }

    /// <summary>The HTTP status that says the same thing as an operation status.</summary>
    private static int StatusFor(FileSystemOperationStatus status) => status switch
    {
        FileSystemOperationStatus.InvalidName => StatusCodes.Status400BadRequest,
        FileSystemOperationStatus.Forbidden => StatusCodes.Status403Forbidden,
        FileSystemOperationStatus.NotFound => StatusCodes.Status404NotFound,
        FileSystemOperationStatus.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError
    };

    #endregion
}
