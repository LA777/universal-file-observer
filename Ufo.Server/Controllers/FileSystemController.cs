using Microsoft.AspNetCore.Mvc;
using System.Runtime.InteropServices;
using Ufo.Abstractions.Requests;
using Ufo.Server.Models;

namespace Ufo.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FileSystemController : ControllerBase
{
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
