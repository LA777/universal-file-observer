using System.Security.Claims;

namespace Ufo.Server.Services;

/// <summary>
/// Service to extract user identity information from JWT claims.
/// </summary>
public interface IJwtClaimsService
{
    /// <summary>
    /// Extracts the user ID from the current HTTP context's JWT claims.
    /// </summary>
    /// <returns>The user ID (ULID) as a string, or null if not found.</returns>
    string? GetUserId();

    /// <summary>
    /// Extracts the username from the current HTTP context's JWT claims.
    /// </summary>
    /// <returns>The username, or null if not found.</returns>
    string? GetUsername();
}

public class JwtClaimsService : IJwtClaimsService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<JwtClaimsService> _logger;

    public JwtClaimsService(IHttpContextAccessor httpContextAccessor, ILogger<JwtClaimsService> logger)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string? GetUserId()
    {
        var userId = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("UserId claim not found in JWT token");
            return null;
        }
        return userId;
    }

    public string? GetUsername()
    {
        var username = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(username))
        {
            _logger.LogWarning("Username claim not found in JWT token");
            return null;
        }
        return username;
    }
}
