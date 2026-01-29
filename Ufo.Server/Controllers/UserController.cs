using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.Options;

namespace Ufo.Server.Controllers;

[ApiController]
[Route("api/user")]
public class UserController : ControllerBase
{
    private readonly IUserRepository _userRepository;
    private readonly ILogger<UserController> _logger;
    private readonly ApplicationSettings _appSettings;

    public UserController(IUserRepository userRepository, IOptionsMonitor<ApplicationSettings> optionsMonitor, ILogger<UserController> logger)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _appSettings = optionsMonitor.CurrentValue ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [AllowAnonymous]
    [HttpGet("is-created")]
    public async Task<IActionResult> UserIsCreatedAsync()
    {
        _logger.LogInformation("UserIsCreatedAsync");

        try
        {
            var anyUserExists = await _userRepository.GetUserCountAsync() > 0;
            return Ok(new { isCreated = anyUserExists });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error checking if user is created");
            return StatusCode(500, "Internal server error.");
        }
    }
}
