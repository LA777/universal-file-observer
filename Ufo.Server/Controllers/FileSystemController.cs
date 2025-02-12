using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Ufo.Server.Models;

namespace Ufo.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FileSystemController : ControllerBase
    {
        private readonly ILogger<FileSystemController> _logger;
        //private readonly BlockingCollection<DirectoryInfo> dirCollection = new BlockingCollection<DirectoryInfo>();
        //private readonly BlockingCollection<FileInfo> fileCollection = new BlockingCollection<FileInfo>();

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

        //private void CollectFolders(DirectoryInfo directoryInfo)
        //{
        //    try
        //    {
        //        foreach (var subDirectoryInfo in directoryInfo.EnumerateDirectories("*", SearchOption.AllDirectories))
        //        {
        //            dirCollection.Add(subDirectoryInfo);
        //        }
        //    }
        //    finally
        //    {
        //        dirCollection.CompleteAdding();
        //    }
        //}

        //private void CollectFiles(DirectoryInfo directoryInfo)
        //{
        //    try
        //    {
        //        foreach (var fileInfo in directoryInfo.EnumerateFiles("*", SearchOption.AllDirectories))
        //        {
        //            fileCollection.Add(fileInfo);
        //        }
        //    }
        //    finally
        //    {
        //        fileCollection.CompleteAdding();
        //    }
        //}

        [HttpPost("folder")]
        public async Task<FsFolder> GetFolderInfoAsync([FromBody] PathModel folderPath, CancellationToken cancellationToken)
        {
            // Get subfolders and files
            var folderEntity = new FsFolder();
            var dirInfo = new DirectoryInfo(folderPath.Path);

            //var crawlDirectoriesTask = Task.Factory.StartNew(() => CollectFolders(dirInfo), cancellationToken);
            //var crawlFilesTask = Task.Factory.StartNew(() => CollectFiles(dirInfo), cancellationToken);
            //await Task.WhenAll(crawlDirectoriesTask, crawlFilesTask);

            //foreach (var directoryInfo in dirCollection.GetConsumingEnumerable())
            //{
            //    var subfolderEntity = new FsFolder
            //    {
            //        Name = directoryInfo.Name,
            //        FullPath = directoryInfo.FullName,
            //        HasParent = directoryInfo.Parent != null,
            //        IsHidden = directoryInfo.Attributes.HasFlag(FileAttributes.Hidden),
            //        Size = null
            //    };

            //    folderEntity.ChildFolders.Add(subfolderEntity);
            //}

            //foreach (var fileInfo in fileCollection.GetConsumingEnumerable())
            //{
            //    var file = new FsFile
            //    {
            //        Name = Path.GetFileNameWithoutExtension(fileInfo.Name),
            //        Size = fileInfo.Length,
            //        FileExtension = fileInfo.Extension,
            //        FullPath = fileInfo.FullName,
            //        HasParent = true,
            //        IsHidden = fileInfo.Attributes.HasFlag(FileAttributes.Hidden)
            //    };

            //    folderEntity.Files.Add(file);
            //}


            foreach (var subfolderPath in Directory.EnumerateDirectories(folderPath.Path))
            {
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
        public async Task<FsFolder> GetParentFolderInfoAsync([FromBody] PathModel folderPath, CancellationToken cancellationToken)
        {
            // TODO - Remove it
            // Get subfolders and files
            string parent = new DirectoryInfo(folderPath?.Path)?.Parent?.FullName;
            var pathModel = new PathModel { Path = parent };

            return await GetFolderInfoAsync(pathModel, cancellationToken);
        }
    }
}
