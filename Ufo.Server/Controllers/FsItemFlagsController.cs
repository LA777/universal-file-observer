using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ufo.Abstractions;
using Ufo.Abstractions.Requests;
using Ufo.Extensions;
using Ufo.Server.Attributes;
using Ufo.Server.Services;

namespace Ufo.Server.Controllers;

/// <summary>
/// The files and folders a user has flagged.
/// </summary>
/// <remarks>
/// Flags are a property of a path, not of a snapshot. What a snapshot holds is a
/// copy of the flag as it stood when the snapshot was taken, written by
/// <see cref="SnapshotController"/> at capture time and never changed afterwards.
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[JwtClaimsRequired]
public class FsItemFlagsController : ControllerBase
{
    private readonly ILogger<FsItemFlagsController> _logger;
    private readonly IFsItemFlagsService _fsItemFlagsService;

    public FsItemFlagsController(IFsItemFlagsService fsItemFlagsService, ILogger<FsItemFlagsController> logger)
    {
        _fsItemFlagsService = fsItemFlagsService ?? throw new ArgumentNullException(nameof(fsItemFlagsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Every path this user has flagged.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFlaggedPathsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetFlaggedPathsAsync");
        var userId = HttpContext.GetUserIdAsUlid();

        return Ok(await _fsItemFlagsService.GetFlaggedPathsAsync(userId, cancellationToken));
    }

    /// <summary>
    /// Flags or unflags a set of files and folders. Only the paths named change.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ServerResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetFlagsAsync(
        [FromBody] FsItemFlagsRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("SetFlagsAsync");
        var userId = HttpContext.GetUserIdAsUlid();

        var serverResult = await _fsItemFlagsService.SetFlagsAsync(request!, userId, cancellationToken);

        return serverResult.Result == Result.Success ? Ok(serverResult) : BadRequest(serverResult);
    }
}
