using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.Requests;
using Ufo.Server.Services;

namespace Ufo.Server.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IUserRepository _userRepository;
    private readonly ILogger<AuthController> _logger;
    private readonly IJwtTokenService _jwtTokenService;

    public AuthController(IUserRepository userRepository, ILogger<AuthController> logger, IJwtTokenService tokenService)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _jwtTokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
    }

    [AllowAnonymous]
    [HttpPost("signup")]
    public async Task<IActionResult> SignupAsync([FromBody] RegisterRequest request)
    {
        _logger.LogInformation("SignupAsync");

        try
        {
            // 1. Check if user already exists
            if (await _userRepository.UserExistsAsync(request.Username))
            {
                return BadRequest("Username is already taken.");
            }

            // 2. Create Entity and Hash Password
            var newUser = new UserEntity
            {
                Id = Ulid.NewUlid(),
                Name = request.Username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password)
            };

            // 3. Save to Database
            var success = await _userRepository.CreateUserAsync(newUser);

            if (success)
            {
                return Ok(new { message = "User registered successfully." });
            }

            return StatusCode(500, "An error occurred during registration.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error during SignUp for {Username}", request.Username);
            return StatusCode(500, "Internal server error.");
        }
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> LoginAsync([FromBody] LoginRequest request)
    {
        _logger.LogInformation("LoginAsync");

        try
        {
            var user = await _userRepository.GetUserByUsernameAsync(request.Username);

            if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                return Unauthorized("Invalid username or password");
            }

            var token = _jwtTokenService.CreateToken(user);

            // Optional: Also set in response header
            Response.Headers.Append("X-Auth-Token", token); // TODO LA - move headername to constants

            return Ok(new { Token = token, Username = user.Name });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error during Login for {Username}", request.Username);
            return StatusCode(500, "Internal server error.");
        }
    }
}
