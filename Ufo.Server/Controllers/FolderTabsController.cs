using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ufo.Abstractions;
using Ufo.Abstractions.DataTransferObjects;
using Ufo.Abstractions.Requests;
using Ufo.Extensions;
using Ufo.Server.Attributes;
using Ufo.Server.Services;

namespace Ufo.Server.Controllers;

/// <summary>
/// The folder tabs a user has locked.
/// </summary>
/// <remarks>
/// Its own controller rather than another corner of Settings: these are where the
/// user is working, not how they want the application configured, and they change
/// as a side effect of browsing rather than because somebody opened a page to
/// change them.
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[JwtClaimsRequired]
public class FolderTabsController : ControllerBase
{
    private readonly ILogger<FolderTabsController> _logger;
    private readonly IFolderTabsService _folderTabsService;

    public FolderTabsController(IFolderTabsService folderTabsService, ILogger<FolderTabsController> logger)
    {
        _folderTabsService = folderTabsService ?? throw new ArgumentNullException(nameof(folderTabsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Every locked tab, both panels at once - the panes load together and this
    /// saves them racing for the same rows on startup.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<FolderTabDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFolderTabsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetFolderTabsAsync");
        var userId = HttpContext.GetUserIdAsUlid();

        return Ok(await _folderTabsService.GetFolderTabsAsync(userId, cancellationToken));
    }

    /// <summary>
    /// Locks one tab. Locking one already locked changes nothing and still succeeds.
    /// </summary>
    /// <remarks>
    /// One tab per call, not a panel's whole set. A wholesale replace has to be
    /// told what every other tab is, which makes the caller's picture of them
    /// authoritative - and a caller whose read failed pictures none, so its next
    /// lock would delete every tab the user had kept.
    /// </remarks>
    [HttpPost("lock")]
    [ProducesResponseType(typeof(ServerResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> LockFolderTabAsync(
        [FromBody] FolderTabRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("LockFolderTabAsync - Panel: {PanelId}", request?.PanelId);
        var userId = HttpContext.GetUserIdAsUlid();

        var serverResult = await _folderTabsService.LockFolderTabAsync(request!, userId, cancellationToken);

        return serverResult.Result == Result.Success ? Ok(serverResult) : BadRequest(serverResult);
    }

    /// <summary>
    /// Unlocks one tab. Unlocking one that is not locked is the end state the
    /// caller asked for, so it succeeds too.
    /// </summary>
    [HttpPost("unlock")]
    [ProducesResponseType(typeof(ServerResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UnlockFolderTabAsync(
        [FromBody] FolderTabRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("UnlockFolderTabAsync - Panel: {PanelId}", request?.PanelId);
        var userId = HttpContext.GetUserIdAsUlid();

        var serverResult = await _folderTabsService.UnlockFolderTabAsync(request!, userId, cancellationToken);

        return serverResult.Result == Result.Success ? Ok(serverResult) : BadRequest(serverResult);
    }
}
