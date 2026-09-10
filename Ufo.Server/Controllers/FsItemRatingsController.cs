using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ufo.Abstractions;
using Ufo.Abstractions.Requests;
using Ufo.Extensions;
using Ufo.Server.Attributes;
using Ufo.Server.Services;

namespace Ufo.Server.Controllers;

/// <summary>
/// The ratings a user has given files and folders, 1 to 10.
/// </summary>
/// <remarks>
/// A rating is a property of a path, not of a snapshot - exactly as a flag is.
/// What a snapshot holds is a copy of the rating as it stood when the snapshot
/// was taken, written by <see cref="SnapshotController"/> at capture time and
/// never changed afterwards.
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[JwtClaimsRequired]
public class FsItemRatingsController : ControllerBase
{
    private readonly ILogger<FsItemRatingsController> _logger;
    private readonly IFsItemRatingsService _fsItemRatingsService;

    public FsItemRatingsController(
        IFsItemRatingsService fsItemRatingsService,
        ILogger<FsItemRatingsController> logger)
    {
        _fsItemRatingsService = fsItemRatingsService ?? throw new ArgumentNullException(nameof(fsItemRatingsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Every rating this user has set, keyed by path.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyDictionary<string, int>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRatingsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetRatingsAsync");
        var userId = HttpContext.GetUserIdAsUlid();

        return Ok(await _fsItemRatingsService.GetRatingsAsync(userId, cancellationToken));
    }

    /// <summary>
    /// Rates a set of files and folders, or clears them with a rating of zero.
    /// Only the paths named change.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ServerResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetRatingsAsync(
        [FromBody] FsItemRatingsRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("SetRatingsAsync");
        var userId = HttpContext.GetUserIdAsUlid();

        var serverResult = await _fsItemRatingsService.SetRatingsAsync(request!, userId, cancellationToken);

        return serverResult.Result == Result.Success ? Ok(serverResult) : BadRequest(serverResult);
    }
}
