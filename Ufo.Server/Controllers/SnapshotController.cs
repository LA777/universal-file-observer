using Microsoft.AspNetCore.Mvc;
using System.Text;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.DataProviders;
using Ufo.Abstractions.Requests;
using Ufo.Extensions;
using Ufo.Server.Models;

namespace Ufo.Server.Controllers;

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
    [ProducesResponseType(typeof(SnapshotEntity), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLatestSnapshotAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetLatestSnapshotAsync");
        var latestSnapshot = await _repository.GetLatestSnapshotWithAllEntitiesAsync(cancellationToken);

        if (latestSnapshot == null)
        {
            return NoContent();
        }

        _logger.LogInformation("LatestSnapshot retrieved from DB");

        return Ok(latestSnapshot);
    }

    [HttpGet("{snapshotId}")]
    [ProducesResponseType(typeof(SnapshotEntity), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSnapshotByIdAsync(Ulid snapshotId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetSnapshotByIdAsync");

        var snapshot = await _repository.GetSnapshotByIdAsync(snapshotId, cancellationToken);

        if (snapshot == null)
        {
            return NotFound();
        }

        _logger.LogInformation("Snapshot by Id retrieved from DB");

        return Ok(snapshot);
    }

    [HttpGet("all")]
    public async Task<IEnumerable<SnapshotEntity>> GetAllSnapshotsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetAllSnapshotsAsync");
        var snapshots = await _repository.GetAllSnapshotsAsync(cancellationToken);
        _logger.LogInformation("Snapshots retrieved from DB");

        return snapshots;
    }

    [HttpPost("create")]
    public async Task<string> CreateSnapshotAsync([FromBody] PathRequest folderPath, CancellationToken cancellationToken)
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
        await _repository.AddSnapshotAsync(snapshot, cancellationToken);
        _logger.LogInformation("Snapshot saved to DB");

        // TODO LA - Return OK(snapshot)
        return "Snapshot created successfully.";
    }

    [HttpDelete("delete/{id}")]
    public async Task<IActionResult> DeleteSnapshotByIdAsync(Ulid id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("DeleteSnapshotByIdAsync");
        var result = await _repository.DeleteSnapshotByIdAsync(id, cancellationToken);

        switch (result)
        {
            case Abstractions.DeleteResult.Success:
                _logger.LogInformation("Snapshot {0} deleted in DB.", id);
                return Ok($"Snapshot with Id: {id} was sucessfully deleted.");
            case Abstractions.DeleteResult.NotFound:
                _logger.LogInformation("Snapshot {0} was not found.", id);
                return NotFound($"Snapshot with Id: {id} was not found.");
            default:
                return BadRequest();
        }
    }

    private FsFolderEntity CreateFolderTree(string path, SnapshotEntity snapshot, FsFolderEntity? parentFolder)
    {// TODO LA - move to a separate service
        _logger.LogInformation($"Indexing {path}");
        var directoryInfo = new DirectoryInfo(path);

        var folder = new FsFolderEntity
        {
            Name = directoryInfo.Name,
            Sha256Hash = string.Empty
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
}
