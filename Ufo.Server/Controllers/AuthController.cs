using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.Options;
using Ufo.Abstractions.Requests;
using Ufo.Server.Authorization;
using Ufo.Server.Services;

namespace Ufo.Server.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IUserRepository _userRepository;
    private readonly ILogger<AuthController> _logger;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly IOptionsMonitor<JwtOptions> _jwtOptionsMonitor;

    public AuthController(
        IUserRepository userRepository,
        ILogger<AuthController> logger,
        IJwtTokenService tokenService,
        IRefreshTokenService refreshTokenService,
        IOptionsMonitor<JwtOptions> jwtOptionsMonitor)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _jwtTokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
        _refreshTokenService = refreshTokenService ?? throw new ArgumentNullException(nameof(refreshTokenService));
        _jwtOptionsMonitor = jwtOptionsMonitor ?? throw new ArgumentNullException(nameof(jwtOptionsMonitor));
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

            // 2. Create Entity and Hash Password.
            // The first account to register administers the installation - it
            // belongs to whoever is standing the server up - and is the only one
            // that may change the TLS certificate. Every later account is a plain
            // user. Two signups racing on a brand-new database could in principle
            // both read a count of zero and both become administrators; that
            // needs the installer to be racing themselves on an empty install, so
            // it is left uncontested rather than serialised behind a lock.
            var isFirstUser = await _userRepository.GetUserCountAsync() == 0;

            var newUser = new UserEntity
            {
                Id = Ulid.NewUlid(),
                Name = request.Username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                IsAdmin = isFirstUser
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
    public async Task<IActionResult> LoginAsync([FromBody] LoginRequest request, CancellationToken cancellationToken)
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

            await IssueRefreshTokenAsync(user.Id, cancellationToken);

            return Ok(new { Token = token, Username = user.Name });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error during Login for {Username}", request.Username);
            return StatusCode(500, "Internal server error.");
        }
    }

    /// <summary>
    /// Exchanges the refresh token in the cookie for a fresh access token, and
    /// replaces the refresh token with a successor.
    /// </summary>
    /// <remarks>
    /// Anonymous because it is the endpoint for a client whose access token has
    /// already expired - requiring a live one would defeat the point. The cookie
    /// is the credential, and <see cref="RefreshTokenCookie"/> explains why that
    /// is safe to say.
    /// </remarks>
    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("RefreshAsync");

        try
        {
            var presentedToken = RefreshTokenCookie.Read(Request);
            if (string.IsNullOrWhiteSpace(presentedToken))
            {
                return Unauthorized("No refresh token was presented.");
            }

            var rotation = await _refreshTokenService.RotateAsync(presentedToken, cancellationToken);
            if (!rotation.IsSuccess)
            {
                // A lost race is the one refusal that leaves the session intact:
                // something else rotated this token moments ago, so the browser is
                // already holding the successor. Clearing the cookie here would
                // delete that live token and end a session that nothing was wrong
                // with - two tabs refreshing together would sign the user out.
                // Every other refusal means the cookie is worthless, and leaving it
                // only buys a repeat of this request on the next page load.
                if (rotation.Failure != RefreshTokenFailure.Raced)
                {
                    RefreshTokenCookie.Clear(Response);
                }

                return Unauthorized("The session has ended. Please sign in again.");
            }

            // Read back rather than trusted from the token: a user deleted, or an
            // administrator demoted, since the session began must not be handed a
            // token that still says otherwise.
            var user = await _userRepository.GetUserByIdAsync(rotation.UserId, cancellationToken);

            var token = _jwtTokenService.CreateToken(user);

            WriteRefreshCookie(rotation.RefreshToken!);

            return Ok(new { Token = token, Username = user.Name });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error during Refresh");
            return StatusCode(500, "Internal server error.");
        }
    }

    /// <summary>
    /// Ends the session server-side: the refresh token is revoked and the cookie
    /// cleared, so signing out means something beyond the browser forgetting.
    /// </summary>
    /// <remarks>
    /// Anonymous, and deliberately silent about what it found. Signing out with an
    /// access token that has already expired is the ordinary case, and refusing it
    /// would leave exactly the sessions a user most wants ended still live.
    /// </remarks>
    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> LogoutAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("LogoutAsync");

        try
        {
            var presentedToken = RefreshTokenCookie.Read(Request);
            if (!string.IsNullOrWhiteSpace(presentedToken))
            {
                await _refreshTokenService.RevokeAsync(presentedToken, cancellationToken);
            }

            RefreshTokenCookie.Clear(Response);

            return NoContent();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error during Logout");
            return StatusCode(500, "Internal server error.");
        }
    }

    private async Task IssueRefreshTokenAsync(Ulid userId, CancellationToken cancellationToken)
    {
        WriteRefreshCookie(await _refreshTokenService.IssueAsync(userId, cancellationToken));
    }

    /// <summary>
    /// Writes the cookie so it expires when the token behind it does.
    /// </summary>
    /// <remarks>
    /// The configured sliding window is not always the answer: a session nearing
    /// its absolute cap is issued a shorter-lived token, and a cookie outliving
    /// its row would leave the browser presenting a token the server has already
    /// stopped honouring - a refusal on the next page load instead of a clean
    /// sign-in prompt. The service therefore reports the deadline it actually
    /// used, and the cookie follows it.
    /// </remarks>
    private void WriteRefreshCookie(IssuedRefreshToken refreshToken)
    {
        RefreshTokenCookie.Write(
            Response, refreshToken.Token, refreshToken.ExpiresAt - DateTimeOffset.UtcNow);
    }
}
