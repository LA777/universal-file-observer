using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ufo.Abstractions;
using Ufo.Extensions;
using Ufo.Server.Attributes;
using Ufo.Server.Services;

namespace Ufo.Server.Controllers;

/// <summary>
/// Wholesale deletion of the calling user's data, one kind at a time - the
/// endpoints behind the Settings page's danger zone.
/// </summary>
/// <remarks>
/// Its own controller rather than more routes on <see cref="SettingsController"/>:
/// two of the three things deleted here are not settings, and a route that
/// deletes every snapshot does not belong under a path that otherwise saves a
/// theme. Everything is scoped to the user in the token; there is no admin
/// variant that reaches into another account.
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[JwtClaimsRequired]
public class UserDataController : ControllerBase
{
    private readonly ILogger<UserDataController> _logger;
    private readonly IUserDataService _userDataService;

    public UserDataController(IUserDataService userDataService, ILogger<UserDataController> logger)
    {
        _userDataService = userDataService ?? throw new ArgumentNullException(nameof(userDataService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Deletes every snapshot the user has, together with the labels, which
    /// exist only to be put on snapshots.
    /// </summary>
    [HttpDelete("snapshots")]
    [ProducesResponseType(typeof(ServerResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteSnapshotsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("DeleteSnapshotsAsync");
        var userId = HttpContext.GetUserIdAsUlid();

        return Ok(await _userDataService.DeleteSnapshotsAsync(userId, cancellationToken));
    }

    /// <summary>
    /// Deletes every flag, rating and tag the user has put on files and folders
    /// on disk. The files and folders themselves are not touched.
    /// </summary>
    [HttpDelete("filesystem")]
    [ProducesResponseType(typeof(ServerResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteFileSystemDataAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("DeleteFileSystemDataAsync");
        var userId = HttpContext.GetUserIdAsUlid();

        return Ok(await _userDataService.DeleteFileSystemDataAsync(userId, cancellationToken));
    }

    /// <summary>
    /// Deletes the user's settings - theme, keyboard shortcuts and locked folder
    /// tabs - so each returns to the build's default.
    /// </summary>
    [HttpDelete("settings")]
    [ProducesResponseType(typeof(ServerResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteSettingsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("DeleteSettingsAsync");
        var userId = HttpContext.GetUserIdAsUlid();

        return Ok(await _userDataService.DeleteSettingsAsync(userId, cancellationToken));
    }
}
