using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ufo.Abstractions.Responses;
using Ufo.Server.Services;

/// <summary>
/// The version of the running build, for the About tab.
/// </summary>
/// <remarks>
/// Authenticated, unlike the other read-only trivia the API exposes: the exact
/// build of a self-hosted server is a hint worth having for anyone probing it,
/// and the only screen that shows it sits behind the login anyway.
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class VersionController : ControllerBase
{
    private readonly IApplicationVersionService _applicationVersionService;
    private readonly ILogger<VersionController> _logger;

    public VersionController(
        IApplicationVersionService applicationVersionService,
        ILogger<VersionController> logger)
    {
        _applicationVersionService = applicationVersionService ?? throw new ArgumentNullException(nameof(applicationVersionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpGet]
    public IActionResult GetVersion()
    {
        _logger.LogInformation("GetVersion");

        return Ok(new VersionResponse { Version = _applicationVersionService.Version });
    }
}
