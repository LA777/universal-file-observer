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
    private readonly IServerCertificateService _serverCertificateService;
    private readonly IKeyBindingsService _keyBindingsService;

    public SettingsController(
        IUserSettingsService userSettingsService,
        IServerCertificateService serverCertificateService,
        IKeyBindingsService keyBindingsService,
        ILogger<SettingsController> logger)
    {
        _userSettingsService = userSettingsService ?? throw new ArgumentNullException(nameof(userSettingsService));
        _serverCertificateService = serverCertificateService ?? throw new ArgumentNullException(nameof(serverCertificateService));
        _keyBindingsService = keyBindingsService ?? throw new ArgumentNullException(nameof(keyBindingsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Every bindable action with the keys in force for this user.
    /// </summary>
    /// <remarks>
    /// Always the whole list, defaults included, so a user who has never opened
    /// the page gets exactly the same shape as one who has rebound everything -
    /// and the Files panes have one thing to read rather than a saved set to
    /// reconcile against a built-in one.
    /// </remarks>
    [HttpGet("shortcuts")]
    public async Task<IActionResult> GetKeyBindingsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetKeyBindingsAsync");
        var userId = HttpContext.GetUserIdAsUlid();

        return Ok(await _keyBindingsService.GetKeyBindingsAsync(userId, cancellationToken));
    }

    /// <summary>
    /// Saves the shortcuts table whole. Sending an action back at its default
    /// stops it being stored, which is how "reset" is expressed.
    /// </summary>
    [HttpPut("shortcuts")]
    public async Task<IActionResult> SaveKeyBindingsAsync(
        [FromBody] KeyBindingsRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("SaveKeyBindingsAsync");
        var userId = HttpContext.GetUserIdAsUlid();

        var serverResult = await _keyBindingsService.SaveKeyBindingsAsync(request, userId, cancellationToken);

        return serverResult.Result == Result.Success ? Ok(serverResult) : BadRequest(serverResult);
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

    /// <summary>
    /// The TLS certificate the server is currently presenting.
    /// </summary>
    /// <remarks>
    /// Administrators only, like the writes below. The certificate is not a
    /// secret - every client that connects is shown it - but it is a
    /// server-scoped setting, and hiding it in the UI while leaving it readable
    /// would make "administrators only" depend on which page you loaded.
    /// </remarks>
    [HttpGet("certificate")]
    public async Task<IActionResult> GetServerCertificateAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetServerCertificateAsync");
        var userId = HttpContext.GetUserIdAsUlid();

        var result = await _serverCertificateService.GetCertificateAsync(userId, cancellationToken);

        return result is null ? Forbid() : Ok(result);
    }

    /// <summary>
    /// Replaces the server's TLS certificate with an uploaded PKCS#12 archive.
    /// Administrators only.
    /// </summary>
    [HttpPut("certificate")]
    public async Task<IActionResult> ReplaceServerCertificateAsync(
        [FromBody] ServerCertificateRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("ReplaceServerCertificateAsync");
        var userId = HttpContext.GetUserIdAsUlid();

        var serverResult = await _serverCertificateService.ReplaceCertificateAsync(request, userId, cancellationToken);

        return serverResult.Result == Result.Success ? Ok(serverResult) : BadRequest(serverResult);
    }

    /// <summary>
    /// Discards the current certificate for a freshly generated self-signed one.
    /// Administrators only.
    /// </summary>
    [HttpPost("certificate/self-signed")]
    public async Task<IActionResult> GenerateSelfSignedCertificateAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("GenerateSelfSignedCertificateAsync");
        var userId = HttpContext.GetUserIdAsUlid();

        var serverResult = await _serverCertificateService.GenerateSelfSignedCertificateAsync(userId, cancellationToken);

        return serverResult.Result == Result.Success ? Ok(serverResult) : BadRequest(serverResult);
    }
}
