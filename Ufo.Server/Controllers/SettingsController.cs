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

    public SettingsController(
        IUserSettingsService userSettingsService,
        IServerCertificateService serverCertificateService,
        ILogger<SettingsController> logger)
    {
        _userSettingsService = userSettingsService ?? throw new ArgumentNullException(nameof(userSettingsService));
        _serverCertificateService = serverCertificateService ?? throw new ArgumentNullException(nameof(serverCertificateService));
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

    /// <summary>
    /// The TLS certificate the server is currently presenting.
    /// </summary>
    /// <remarks>
    /// Readable by any signed-in user, because the response describes only what
    /// the certificate already tells every client that connects. The response
    /// carries <c>canManage</c> so the client knows whether to offer the upload
    /// control; the write endpoints below enforce that independently.
    /// </remarks>
    [HttpGet("certificate")]
    public async Task<IActionResult> GetServerCertificateAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetServerCertificateAsync");
        var userId = HttpContext.GetUserIdAsUlid();

        var result = await _serverCertificateService.GetCertificateAsync(userId, cancellationToken);

        return Ok(result);
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
