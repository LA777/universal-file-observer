using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Text;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.DataProviders;
using Ufo.Extensions;
using Ufo.Server.Models;

namespace Ufo.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SnapshotController : ControllerBase
    {
        private readonly ILogger<SnapshotController> _logger;
        private readonly IFileSystemSqLiteRepository _repository;
        private readonly ISystemInfoProvider _systemInfoProvider;

        public SnapshotController(ILogger<SnapshotController> logger, IFileSystemSqLiteRepository repository, ISystemInfoProvider systemInfoProvider)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _systemInfoProvider = systemInfoProvider ?? throw new ArgumentNullException(nameof(systemInfoProvider));
        }

        [HttpGet("latest")]
        public async Task<SnapshotEntity> GetLatestSnapshotAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("GetLatestSnapshotAsync");
            var latestSnapshot = await _repository.GetLatestSnapshotWithAllEntitiesAsync(cancellationToken);
            _logger.LogInformation("LatestSnapshot retrieved from DB");

            //var json = ConvertToNsJson(latestSnapshot);

            return latestSnapshot;
        }

        [HttpGet("{snapshotGuid}")]
        public async Task<SnapshotEntity> GetSnapshotByGuidAsync(Guid snapshotGuid, CancellationToken cancellationToken)
        {
            _logger.LogInformation("GetSnapshotByGuidAsync");
            var snapshot = await _repository.GetSnapshotByGuidAsync(snapshotGuid, cancellationToken);
            _logger.LogInformation("Snapshot by Guid retrieved from DB");

            //var json = ConvertToNsJson(latestSnapshot);

            return snapshot;
        }

        [HttpGet("all")]
        public async Task<IEnumerable<SnapshotEntity>> GetSnapshotsAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("GetSnapshotsAsync");
            var snapshots = await _repository.GetSnapshotsAsync(cancellationToken);
            _logger.LogInformation("Snapshots retrieved from DB");

            //var json = ConvertToNsJson(snapshots);

            return snapshots;
        }

        [HttpPost("create")]
        public async Task<string> CreateSnapshotAsync([FromBody] PathModel folderPath, CancellationToken cancellationToken)
        {
            _logger.LogInformation("CreateSnapshotAsync");

            if (!Directory.Exists(folderPath.Path))
            {
                throw new DirectoryNotFoundException(folderPath.Path);
            }

            var snapshot = _systemInfoProvider.GetSystemInformation(folderPath.Path);
            var folderTree = CreateFolderTree(folderPath.Path, snapshot, null);
            snapshot.RootFolder = folderTree;
            _logger.LogInformation("Snapshot created");
            await _repository.AddDataAsync(snapshot);
            _logger.LogInformation("Snapshot saved to DB");

            return "Snapshot created successfully.";
        }

        private FsFolderEntity CreateFolderTree(string path, SnapshotEntity snapshot, FsFolderEntity? parentFolder)
        {// TODO LA - move to a separate class
            _logger.LogInformation($"Indexing {path}");
            var directoryInfo = new DirectoryInfo(path);

            var folder = new FsFolderEntity
            {
                Name = directoryInfo.Name
            };
            folder.Snapshots.Add(snapshot);
            if (parentFolder is not null)
            {
                folder.ParentFolders.Add(parentFolder);
            }

            foreach (var subFolderPath in Directory.EnumerateDirectories(path))
            {
                var subFolder = CreateFolderTree(subFolderPath, snapshot, folder);
                folder.ChildFolders.Add(subFolder);
            }

            foreach (var filePath in Directory.EnumerateFiles(path))
            {
                var fileInfo = new FileInfo(filePath);
                var file = new FsFileEntity
                {
                    Name = Path.GetFileNameWithoutExtension(fileInfo.Name),
                    Size = fileInfo.Length,
                    Sha256Hash = fileInfo.GetFileHashSha256(),
                    FileExtension = fileInfo.Extension
                };
                file.Snapshots.Add(snapshot);
                file.ParentFolders.Add(folder);
                folder.Files.Add(file);
            }

            folder.Sha256Hash = GetFolderSha256Hash(folder);
            folder.Size = folder.Files.Sum(x => x.Size) + folder.ChildFolders.Sum(y => y.Size);

            return folder;
        }

        private static string GetFolderSha256Hash(FsFolderEntity folder)
        {
            var sb = new StringBuilder();
            var orderedFiles = folder.Files.OrderBy(x => x.Name);

            foreach (var file in orderedFiles)
            {
                var fileNameWithExtension = $"{file.Name}.{file.FileExtension}"; ;
                if (string.IsNullOrWhiteSpace(fileNameWithExtension))
                {
                    throw new ArgumentException(nameof(fileNameWithExtension));
                }

                if (string.IsNullOrWhiteSpace(file.Sha256Hash))
                {
                    throw new ArgumentException(nameof(file.Sha256Hash));
                }

                sb.AppendLine($"{fileNameWithExtension},{file.Sha256Hash}");
            }

            var orderedSubfolders = folder.ChildFolders.OrderBy(x => x.Name);
            foreach (var subfolder in orderedSubfolders)
            {
                if (string.IsNullOrWhiteSpace(subfolder.Name))
                {
                    throw new ArgumentException(nameof(subfolder.Name));
                }

                if (string.IsNullOrWhiteSpace(subfolder.Sha256Hash))
                {
                    throw new ArgumentException(nameof(subfolder.Sha256Hash));
                }

                sb.AppendLine($"{subfolder.Name},{subfolder.Sha256Hash}");
            }

            var dataString = sb.ToString();
            var hash = dataString.GetHashSha256();

            return hash;
        }


        private static string ConvertToNsJson<T>(T entity)
        {
            var settings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
            };

            var json = JsonConvert.SerializeObject(new { entity }, Formatting.Indented, settings);

            return json;
        }
    }
}
