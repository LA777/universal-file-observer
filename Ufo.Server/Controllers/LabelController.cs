using Microsoft.AspNetCore.Mvc;
using Ufo.Abstractions.Database.Entities;
using Ufo.Database.Repositories;

namespace Ufo.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LabelController : ControllerBase
{
    private readonly ILogger<LabelController> _logger;
    private readonly LabelsRepository _labelsRepository;

    public LabelController(LabelsRepository labelsRepository, ILogger<LabelController> logger)
    {
        _labelsRepository = labelsRepository ?? throw new ArgumentNullException(nameof(labelsRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpPost]
    public async Task<IActionResult> AddLabelAsync(LabelEntity label, CancellationToken cancellationToken)
    {
        _logger.LogInformation("AddLabelAsync");

        var result = await _labelsRepository.AddLabelAsync(label, cancellationToken);
        if (result == 1)
        {
            return Ok("Label created successfully.");
        }

        return BadRequest("Failed to create Label.");
    }

    [HttpGet]
    public async Task<IActionResult> GetAllLabelsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetAllLabelsAsync");

        var result = await _labelsRepository.GetAllLabelsAsync(cancellationToken);
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

        var result = await _labelsRepository.GetLabelsBySnapshotIdAsync(snapshotId, cancellationToken);
        if (result is { Count: > 0 })
        {
            return Ok(result);
        }

        return NotFound();
    }

    [HttpPut]
    public async Task<IActionResult> UpdateLabelAsync([FromBody] LabelEntity label, CancellationToken cancellationToken)
    {
        _logger.LogInformation("UpdateLabelAsync");

        var result = await _labelsRepository.UpdateLabelAsync(label, cancellationToken);
        if (result == 1)
        {
            return Ok("Label updated successfully.");
        }

        return BadRequest("Failed to update Label.");
    }

    [HttpPost("{labelId}/snapshot/{snapshotId}")]
    public async Task<IActionResult> AddLabelToSnapshotAsync(Ulid labelId, Ulid snapshotId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("AddLabelToSnapshotAsync");

        var result = await _labelsRepository.AddLabelToSnapshotAsync(labelId, snapshotId, cancellationToken);
        if (result == 1)
        {
            return Ok("Label added to Snapshot successfully.");
        }

        return BadRequest("Failed to add Label to Snapshot.");
    }

    [HttpDelete("{labelId}/snapshot/{snapshotId}")]
    public async Task<IActionResult> RemoveLabelFromSnapshotAsync(Ulid labelId, Ulid snapshotId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("RemoveLabelFromSnapshotAsync");

        var result = await _labelsRepository.RemoveLabelFromSnapshotAsync(labelId, snapshotId, cancellationToken);
        if (result == 1)
        {
            return Ok("Label removed from Snapshot successfully.");
        }

        return BadRequest("Failed to remove Label from Snapshot.");
    }

    [HttpDelete("{labelId}")]
    public async Task<IActionResult> RemoveLabelFromSnapshotAsync(Ulid labelId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("DeleteLabelByIdAsync");

        var result = await _labelsRepository.DeleteLabelByIdAsync(labelId, cancellationToken);
        if (result == 1)
        {
            return Ok("Label deleted successfully.");
        }

        return BadRequest("Failed to delete Label.");
    }
}
