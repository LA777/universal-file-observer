using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ufo.Server.Services;

[ApiController]
[Route("api/user")]
public class UserController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ILogger<UserController> _logger;

    public UserController(IUserService userService, ILogger<UserController> logger)
    {
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [AllowAnonymous]
    [HttpGet("is-created")]
    public async Task<IActionResult> UserIsCreatedAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("UserIsCreatedAsync");

        try
        {
            var anyUserExists = await _userService.AnyUserExistsAsync(cancellationToken);
            return Ok(new { isCreated = anyUserExists });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error checking if user is created");
            return StatusCode(500, "Internal server error.");
        }
    }
}