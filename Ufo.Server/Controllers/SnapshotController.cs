using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.DataProviders;
using Ufo.Abstractions.Requests;
using Ufo.Extensions;
using Ufo.Server.Attributes;
using Ufo.Server.Mappers;

namespace Ufo.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[JwtClaimsRequired]
public class SnapshotController : ControllerBase
{
    // TODO LA - Cover with Functional tests
    // TODO LA - Add pagination for GetAllSnapshotsSummaryAsync method.
    // TODO LA - Consider adding SnapshotService to handle business logic in SnapshotController and cover it with Unit tests.
    private readonly ILogger<SnapshotController> _logger;
    private readonly ISnapshotRepository _repository;
    private readonly IUserRepository _userRepository;
    private readonly ISystemInfoProvider _systemInfoProvider;

    public SnapshotController(
        ILogger<SnapshotController> logger,
        ISnapshotRepository repository,
        ISystemInfoProvider systemInfoProvider,
        IUserRepository userRepository)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _systemInfoProvider = systemInfoProvider ?? throw new ArgumentNullException(nameof(systemInfoProvider));
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
    }

    [HttpGet("latest")]
    [ProducesResponseType(typeof(SnapshotEntity), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLatestSnapshotAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetLatestSnapshotAsync");
        var userId = HttpContext.GetUserIdAsUlid();
        var latestSnapshot = await _repository.GetLatestSnapshotWithAllEntitiesAsync(userId, cancellationToken);

        if (latestSnapshot == null)
        {
            return NoContent();
        }

        _logger.LogInformation("LatestSnapshot retrieved from DB");

        return Ok(latestSnapshot.ToDto());
    }

    [HttpGet("{snapshotId}")]
    [ProducesResponseType(typeof(SnapshotEntity), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSnapshotByIdAsync(Ulid snapshotId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetSnapshotByIdAsync");
        var userId = HttpContext.GetUserIdAsUlid();

        var snapshot = await _repository.GetSnapshotByIdAsync(snapshotId, userId, cancellationToken);

        if (snapshot == null)
        {
            return NotFound();
        }

        _logger.LogInformation("Snapshot by Id retrieved from DB");

        return Ok(snapshot.ToDto());
    }

    [HttpGet("all/summary")]
    public async Task<IActionResult> GetAllSnapshotsSummaryAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetAllSnapshotsAsync");
        var userId = HttpContext.GetUserIdAsUlid();
        var snapshots = await _repository.GetAllSnapshotsAsync(userId, cancellationToken); // TODO LA - Get summary only from DB, without related entities.
        _logger.LogInformation("Snapshots retrieved from DB");

        return Ok(snapshots.ToSummaryDtoList());
    }

    [HttpPost("create")]
    public async Task<IActionResult> CreateSnapshotAsync([FromBody] PathRequest folderPath, CancellationToken cancellationToken)
    {
        _logger.LogInformation("CreateSnapshotAsync");
        var userId = HttpContext.GetUserIdAsUlid();

        if (!Directory.Exists(folderPath.Path))
        {
            throw new DirectoryNotFoundException(folderPath.Path);
        }

        var user = await _userRepository.GetUserByIdAsync(userId);
        var snapshot = _systemInfoProvider.GetSystemInformation(folderPath.Path, user);
        var folderTree = CreateFolderTree(folderPath.Path, snapshot, null, user);
        snapshot.RootFolder = folderTree;
        _logger.LogInformation("Snapshot created");
        await _repository.AddSnapshotAsync(snapshot, userId, cancellationToken);
        _logger.LogInformation("Snapshot saved to DB");

        return Ok(snapshot.ToSummaryDto()); // TODO LA - Check front end.
    }

    [HttpDelete("delete/{snapshotId}")]
    public async Task<IActionResult> DeleteSnapshotByIdAsync(Ulid snapshotId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("DeleteSnapshotByIdAsync");
        var userId = HttpContext.GetUserIdAsUlid();
        var result = await _repository.DeleteSnapshotByIdAsync(snapshotId, userId, cancellationToken);

        switch (result)
        {
            case Abstractions.DatabaseActionResult.Success:
                _logger.LogInformation("Snapshot {0} deleted in DB.", snapshotId);
                return Ok($"Snapshot with Id: {snapshotId} was sucessfully deleted.");
            case Abstractions.DatabaseActionResult.NotFound:
                _logger.LogInformation("Snapshot {0} was not found.", snapshotId);
                return NotFound($"Snapshot with Id: {snapshotId} was not found.");
            default:
                return BadRequest();
        }
    }

    private FolderEntity CreateFolderTree(string path, SnapshotEntity snapshot, FolderEntity? parentFolder, UserEntity user)
    {// TODO LA - move to a separate service
        _logger.LogInformation($"Indexing {path}");
        var directoryInfo = new DirectoryInfo(path);

        var folder = new FolderEntity
        {
            Name = directoryInfo.Name,
            Sha256Hash = string.Empty,
            User = user,
            UserId = user.Id,
            CreatedAt = directoryInfo.CreationTimeUtc.ToString("o"), // TODO LA - consider using DateTimeOffset instead of string for CreatedAt and UpdatedAt. Cover with Unit tests.
            UpdatedAt = directoryInfo.CreationTimeUtc.ToString("o"),  // TODO LA - consider using DateTimeOffset instead of string for CreatedAt and UpdatedAt. Cover with Unit tests.
            IsHidden = (directoryInfo.Attributes & FileAttributes.Hidden) != 0 // TODO LA - Cover with Unit tests.
        };
        folder.Snapshots.Add(snapshot);
        if (parentFolder is not null)
        {
            folder.ParentFolders.Add(parentFolder);
        }

        foreach (var subFolderPath in Directory.EnumerateDirectories(path))
        {
            var subFolder = CreateFolderTree(subFolderPath, snapshot, folder, user);
            folder.ChildFolders.Add(subFolder);
        }

        foreach (var filePath in Directory.EnumerateFiles(path))
        {
            var fileInfo = new FileInfo(filePath);
            var file = new FileEntity
            {
                Name = Path.GetFileNameWithoutExtension(fileInfo.Name),
                Size = fileInfo.Length,
                Sha256Hash = fileInfo.GetFileHashSha256(),
                FileExtension = fileInfo.Extension,
                User = user,
                UserId = user.Id,
                CreatedAt = fileInfo.CreationTimeUtc.ToString("o"), // TODO LA - consider using DateTimeOffset instead of string for CreatedAt and UpdatedAt. Cover with Unit tests.
                UpdatedAt = fileInfo.LastWriteTimeUtc.ToString("o"),  // TODO LA - consider using DateTimeOffset instead of string for CreatedAt and UpdatedAt. Cover with Unit tests.
                IsHidden = (fileInfo.Attributes & FileAttributes.Hidden) != 0 // TODO LA - Cover with Unit tests.
            };
            file.Snapshots.Add(snapshot);
            file.ParentFolders.Add(folder);
            folder.Files.Add(file);
        }

        folder.Sha256Hash = GetFolderSha256Hash(folder);
        folder.Size = folder.Files.Sum(x => x.Size) + folder.ChildFolders.Sum(y => y.Size);

        return folder;
    }

    private static string GetFolderSha256Hash(FolderEntity folder)
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
