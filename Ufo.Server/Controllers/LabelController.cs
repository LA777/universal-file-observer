using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.Requests;
using Ufo.Extensions;
using Ufo.Server.Attributes;

namespace Ufo.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[JwtClaimsRequired]
public class LabelController : ControllerBase
{
    private readonly ILogger<LabelController> _logger;
    private readonly ILabelsRepository _labelsRepository;

    public LabelController(ILabelsRepository labelsRepository, ILogger<LabelController> logger)
    {
        _labelsRepository = labelsRepository ?? throw new ArgumentNullException(nameof(labelsRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpPost]
    public async Task<IActionResult> AddLabelAsync([FromBody] LabelRequest label, CancellationToken cancellationToken)
    {
        _logger.LogInformation("AddLabelAsync");
        var userId = HttpContext.GetUserIdAsUlid();

        var serverResults = await _labelsRepository.AddLabelAsync(label, userId, cancellationToken);
        if (serverResults.Any(x=>x.Priority == ActionPriority.Highest && x.Result == Result.Error))
        {
            return BadRequest(serverResults);
        }

        return Ok(serverResults);
    }

    [HttpGet]
    public async Task<IActionResult> GetAllLabelsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetAllLabelsAsync");
        var userId = HttpContext.GetUserIdAsUlid();

        var result = await _labelsRepository.GetAllLabelsAsync(userId, cancellationToken);
        if (result is { Count: > 0 })
        {
            return Ok(result);
        }

        return NotFound();
    }

    [HttpGet("snapshot/{snapshotId}")]
    public async Task<IActionResult> GetLabelsBySnapshotIdAsync(Ulid snapshotId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetLabelsBySnapshotIdAsync");
        var userId = HttpContext.GetUserIdAsUlid();

        var result = await _labelsRepository.GetLabelsBySnapshotIdAsync(snapshotId, userId, cancellationToken);
        if (result is { Count: > 0 })
        {
            return Ok(result);
        }

        return NotFound();
    }

    [HttpPut]
    public async Task<IActionResult> UpdateLabelAsync([FromBody] LabelRequest label, CancellationToken cancellationToken)
    {
        _logger.LogInformation("UpdateLabelAsync");
        var userId = HttpContext.GetUserIdAsUlid();

        var serverResult = await _labelsRepository.UpdateLabelAsync(label, userId, cancellationToken);
        if (serverResult.Result == Result.Success)
        {
            return Ok(serverResult);
        }

        return BadRequest(serverResult);
    }

    [HttpPost("{labelId}/snapshot/{snapshotId}")]
    public async Task<IActionResult> AddLabelToSnapshotAsync(Ulid labelId, Ulid snapshotId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("AddLabelToSnapshotAsync");
        var userId = HttpContext.GetUserIdAsUlid();

        var serverResult = await _labelsRepository.AddLabelToSnapshotAsync(labelId, snapshotId, userId, cancellationToken);
        if (serverResult.Result == Result.Success)
        {
            return Ok(serverResult);
        }

        return BadRequest(serverResult);
    }

    [HttpDelete("{labelId}/snapshot/{snapshotId}")]
    public async Task<IActionResult> RemoveLabelFromSnapshotAsync(Ulid labelId, Ulid snapshotId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("RemoveLabelFromSnapshotAsync");
        var userId = HttpContext.GetUserIdAsUlid();

        var serverResult = await _labelsRepository.RemoveLabelFromSnapshotAsync(labelId, snapshotId, userId, cancellationToken);
        if (serverResult.Result == Result.Success)
        {
            return Ok(serverResult);
        }

        return BadRequest(serverResult);
    }

    [HttpDelete("{labelId}")]
    public async Task<IActionResult> DeleteLabelByIdAsync(Ulid labelId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("DeleteLabelByIdAsync");
        var userId = HttpContext.GetUserIdAsUlid();

        var serverResult = await _labelsRepository.DeleteLabelByIdAsync(labelId, userId, cancellationToken);

        if (serverResult.Result == Result.NotFound)
        {
            _logger.LogInformation("Label {0} was not found.", labelId);
            return NotFound($"Label with Id: {labelId} was not found.");
        }
        else if (serverResult.Result == Result.Success)
        {
            _logger.LogInformation("Label {0} deleted from DB.", labelId);
            return Ok($"Label with Id: {labelId} was sucessfully deleted.");
        }

        return BadRequest("Failed to delete Label.");
    }
}
