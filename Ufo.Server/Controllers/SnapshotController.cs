using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.DataProviders;
using Ufo.Abstractions.Requests;
using Ufo.Extensions;
using Ufo.Server.Attributes;
using Ufo.Server.Mappers;
using Ufo.Server.Services;

namespace Ufo.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[JwtClaimsRequired]
public class SnapshotController : ControllerBase
{
    // TODO LA - Add pagination for GetAllSnapshotsSummaryAsync method.
    private readonly ILogger<SnapshotController> _logger;
    private readonly ISnapshotRepository _repository;
    private readonly IUserRepository _userRepository;
    private readonly ISystemInfoProvider _systemInfoProvider;
    private readonly IPathGuard _pathGuard;
    private readonly IFolderTreeBuilder _folderTreeBuilder;

    public SnapshotController(
        ILogger<SnapshotController> logger,
        ISnapshotRepository repository,
        ISystemInfoProvider systemInfoProvider,
        IUserRepository userRepository,
        IPathGuard pathGuard,
        IFolderTreeBuilder folderTreeBuilder)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _systemInfoProvider = systemInfoProvider ?? throw new ArgumentNullException(nameof(systemInfoProvider));
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _pathGuard = pathGuard ?? throw new ArgumentNullException(nameof(pathGuard));
        _folderTreeBuilder = folderTreeBuilder ?? throw new ArgumentNullException(nameof(folderTreeBuilder));
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

        if (!_pathGuard.TryResolve(folderPath.Path, out var snapshotRootPath))
        {
            return Forbid();
        }

        if (!Directory.Exists(snapshotRootPath))
        {
            throw new DirectoryNotFoundException(snapshotRootPath);
        }

        var user = await _userRepository.GetUserByIdAsync(userId);
        var snapshot = _systemInfoProvider.GetSystemInformation(snapshotRootPath, user);
        var folderTree = await _folderTreeBuilder.BuildAsync(snapshotRootPath, snapshot, user, cancellationToken);
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
}
