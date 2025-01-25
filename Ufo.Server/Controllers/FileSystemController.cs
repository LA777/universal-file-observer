using Microsoft.AspNetCore.Mvc;
using System.Runtime.InteropServices;
using Ufo.Abstractions.Database.Entities;
using Ufo.Server.Models;

namespace Ufo.Server.Controllers
{
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
        public async Task<FileSystemRoot> GetFileSystemRootAsync(CancellationToken cancellationToken)
        {
            var fileSystemRoot = new FileSystemRoot();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                foreach (DriveInfo driveInfo in DriveInfo.GetDrives())
                {
                    fileSystemRoot.Drives.Add(driveInfo.Name);
                }
            }

            var homeFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var pathModel = new PathModel { Path = homeFolderPath  };
            fileSystemRoot.Folder = await GetFolderInfoAsync(pathModel, cancellationToken);

            return fileSystemRoot;
        }

        [HttpPost("folder")]
        public async Task<FsFolderEntity> GetFolderInfoAsync([FromBody] PathModel folderPath, CancellationToken cancellationToken)
        {
            // Get subfolders and files
            var folderEntity = new FsFolderEntity();

            foreach (var subfolderPath in Directory.EnumerateDirectories(folderPath.Path))
            {
                var subfolderEntity = new FsFolderEntity();
                var directoryInfo = new DirectoryInfo(subfolderPath);
                subfolderEntity.Name = directoryInfo.Name;
                subfolderEntity.FullPath = subfolderPath;
                subfolderEntity.HasParent = directoryInfo.Parent != null;
                subfolderEntity.IsHidden = directoryInfo.Attributes.HasFlag(FileAttributes.Hidden);
                subfolderEntity.Size = null;

                folderEntity.ChildFolders.Add(subfolderEntity);
            }

            foreach (var filePath in Directory.EnumerateFiles(folderPath.Path))
            {
                var fileInfo = new FileInfo(filePath);
                var file = new FsFileEntity
                {
                    Name = Path.GetFileNameWithoutExtension(fileInfo.Name),
                    Size = fileInfo.Length,
                    FileExtension = fileInfo.Extension,
                    FullPath = filePath,
                    HasParent = true,
                    IsHidden = fileInfo.Attributes.HasFlag(FileAttributes.Hidden)
                };

                folderEntity.Files.Add(file);
            }
            var dirInfo = new DirectoryInfo(folderPath.Path);
            folderEntity.Name = dirInfo.Name;
            folderEntity.FullPath = folderPath.Path;
            folderEntity.HasParent = dirInfo.Parent != null;

            return folderEntity;
        }

        [HttpPost("parent")]
        public async Task<FsFolderEntity> GetParentFolderInfoAsync([FromBody] PathModel folderPath, CancellationToken cancellationToken)
        {
            // Get subfolders and files
            string parent = new DirectoryInfo(folderPath?.Path)?.Parent?.FullName;
            var pathModel = new PathModel { Path = parent };

            return await GetFolderInfoAsync(pathModel, cancellationToken);
        }
    }
}
