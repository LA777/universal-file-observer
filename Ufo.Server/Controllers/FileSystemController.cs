using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Runtime.InteropServices;
using Ufo.Abstractions.Requests;
using Ufo.Server.Models;

namespace Ufo.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FileSystemController : ControllerBase
{
    // TODO LA - Cover with Functional tests
    private readonly ILogger<FileSystemController> _logger;

    public FileSystemController(ILogger<FileSystemController> logger)
    {
        _logger = logger;
    }

    [HttpGet("root")]
    [ProducesResponseType(typeof(FileSystemRoot), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult GetFileSystemRoot(CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetFileSystemRoot");
        var fileSystemRoot = new FileSystemRoot();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            foreach (DriveInfo driveInfo in DriveInfo.GetDrives())
            {
                fileSystemRoot.Drives.Add(driveInfo.Name);
            }
        }

        var homeFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var pathModel = new PathRequest { Path = homeFolderPath };

        var folderEntity = GetFolder(pathModel, cancellationToken);
        if (folderEntity == null)
        {
            return NoContent();
        }

        fileSystemRoot.Folder = folderEntity;

        return Ok(fileSystemRoot);
    }

    [HttpPost("folder")]
    [ProducesResponseType(typeof(FsFolder), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult GetFolderInfo([FromBody] PathRequest folderPath, CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetFolderInfo");
        var folderEntity = GetFolder(folderPath, cancellationToken);
        if (folderEntity == null)
        {
            return NoContent();
        }

        return Ok(folderEntity);
    }

    private FsFolder? GetFolder(PathRequest folderPath, CancellationToken cancellationToken)
    {
        var folderEntity = new FsFolder();
        var dirInfo = new DirectoryInfo(folderPath.Path);

        foreach (var subfolderPath in Directory.EnumerateDirectories(folderPath.Path))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            var subfolderEntity = new FsFolder();
            var directoryInfo = new DirectoryInfo(subfolderPath);
            subfolderEntity.Name = directoryInfo.Name;
            subfolderEntity.FullPath = subfolderPath;
            subfolderEntity.IsHidden = directoryInfo.Attributes.HasFlag(FileAttributes.Hidden);
            subfolderEntity.Size = null;

            folderEntity.ChildFolders.Add(subfolderEntity);
        }

        foreach (var filePath in Directory.EnumerateFiles(folderPath.Path))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
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
        folderEntity.FullPath = folderPath.Path;
        folderEntity.ParentFolder = dirInfo.Parent == null ? null : new FsFolder() { FullPath = dirInfo.Parent?.FullName };

        return folderEntity;
    }

    [HttpPost("search")]
    [ProducesResponseType(typeof(List<FsSearchResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult SearchFileSystem([FromBody] FileSystemSearchRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("SearchFileSystem - Path: {Path}, Query: {Query}", request.Path, request.Query);

        if (string.IsNullOrWhiteSpace(request.Path) || !Directory.Exists(request.Path))
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
        pending.Push(request.Path);

        while (pending.Count > 0 && results.Count < maxResults && !cancellationToken.IsCancellationRequested)
        {
            var currentDir = pending.Pop();

            try
            {
                foreach (var subDir in Directory.EnumerateDirectories(currentDir))
                {
                    pending.Push(subDir);

                    if (!request.IncludeFolders || results.Count >= maxResults)
                    {
                        continue;
                    }

                    var dirInfo = new DirectoryInfo(subDir);
                    if (MatchesName(dirInfo.Name) && MatchesDate(dirInfo.LastWriteTime))
                    {
                        results.Add(new FsSearchResult
                        {
                            Name = dirInfo.Name,
                            FullPath = subDir,
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
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetParentFolderInfo([FromBody] PathRequest folderPath, CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetParentFolderInfo");
        string? parent = new DirectoryInfo(folderPath.Path).Parent?.FullName;

        if (parent is null)
        {
            _logger.LogWarning("Parent folder was not found.");
            return NotFound();
        }

        var pathModel = new PathRequest { Path = parent };

        return GetFolderInfo(pathModel, cancellationToken);
    }
}
