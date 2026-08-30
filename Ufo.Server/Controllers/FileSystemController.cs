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

    public FileSystemController(ILogger<FileSystemController> logger, IPathGuard pathGuard)
    {
        _logger = logger;
        _pathGuard = pathGuard ?? throw new ArgumentNullException(nameof(pathGuard));
    }

    [HttpGet("root")]
    [ProducesResponseType(typeof(FileSystemRoot), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult GetFileSystemRoot(CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetFileSystemRoot");
        var fileSystemRoot = new FileSystemRoot();

        foreach (var root in EnumerateRoots())
        {
            fileSystemRoot.Roots.Add(root);
        }

        var startingFolderPath = ResolveStartingFolderPath(fileSystemRoot.Roots);
        if (startingFolderPath == null)
        {
            _logger.LogWarning("No readable starting folder could be resolved.");
            return NoContent();
        }

        var folderEntity = GetFolder(startingFolderPath, cancellationToken);
        if (folderEntity == null)
        {
            return NoContent();
        }

        fileSystemRoot.Folder = folderEntity;

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
    private string? ResolveStartingFolderPath(IList<string> roots)
    {
        var homeFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!string.IsNullOrWhiteSpace(homeFolderPath)
            && _pathGuard.TryResolve(homeFolderPath, out var resolvedHomeFolderPath)
            && Directory.Exists(resolvedHomeFolderPath))
        {
            return resolvedHomeFolderPath;
        }

        return roots.FirstOrDefault(root => Directory.Exists(root));
    }

    [HttpPost("folder")]
    [ProducesResponseType(typeof(FsFolder), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult GetFolderInfo([FromBody] PathRequest folderPath, CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetFolderInfo");

        if (!_pathGuard.TryResolve(folderPath.Path, out var resolvedPath))
        {
            return Forbid();
        }

        var folderEntity = GetFolder(resolvedPath, cancellationToken);
        if (folderEntity == null)
        {
            return NoContent();
        }

        return Ok(folderEntity);
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
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetParentFolderInfo([FromBody] PathRequest folderPath, CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetParentFolderInfo");

        if (!_pathGuard.TryResolve(folderPath.Path, out var resolvedPath))
        {
            return Forbid();
        }

        string? parent = new DirectoryInfo(resolvedPath).Parent?.FullName;

        if (parent is null)
        {
            _logger.LogWarning("Parent folder was not found.");
            return NotFound();
        }

        return GetFolderInfo(new PathRequest { Path = parent }, cancellationToken);
    }
}
