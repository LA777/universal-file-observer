using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ufo.Abstractions;
using Ufo.Abstractions.Requests;
using Ufo.Extensions;
using Ufo.Server.Attributes;
using Ufo.Server.Services;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[JwtClaimsRequired]
public class SettingsController : ControllerBase
{
    private readonly ILogger<SettingsController> _logger;
    private readonly IUserSettingsService _userSettingsService;

    public SettingsController(IUserSettingsService userSettingsService, ILogger<SettingsController> logger)
    {
        _userSettingsService = userSettingsService ?? throw new ArgumentNullException(nameof(userSettingsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The current user's settings. Answers with the defaults rather than 404
    /// when nothing has been saved yet, so the client can apply a theme on its
    /// very first load without special-casing the empty response.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetUserSettingsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetUserSettingsAsync");
        var userId = HttpContext.GetUserIdAsUlid();

        var result = await _userSettingsService.GetUserSettingsAsync(userId, cancellationToken);

        return Ok(result);
    }

    [HttpPut]
    public async Task<IActionResult> SaveUserSettingsAsync([FromBody] UserSettingsRequest settings, CancellationToken cancellationToken)
    {
        _logger.LogInformation("SaveUserSettingsAsync");
        var userId = HttpContext.GetUserIdAsUlid();

        var serverResult = await _userSettingsService.SaveUserSettingsAsync(settings, userId, cancellationToken);
        if (serverResult.Result == Result.Success)
        {
            return Ok(serverResult);
        }

        return BadRequest(serverResult);
    }
}
