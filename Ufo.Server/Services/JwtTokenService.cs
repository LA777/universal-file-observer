using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Options;
using Ufo.Server.Authorization;

namespace Ufo.Server.Services;

public interface IJwtTokenService
{
    public string CreateToken(UserEntity user);
}

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions jwtOptions;

    public JwtTokenService(IOptionsMonitor<JwtOptions> optionsMonitor)
    {
        if (optionsMonitor == null)
        {
            throw new ArgumentNullException(nameof(optionsMonitor));
        }

        jwtOptions = optionsMonitor.CurrentValue ?? throw new ArgumentNullException(nameof(optionsMonitor));

        // Backstop to the startup check in UfoHost.ValidateJwtTokenLifetime. This
        // service is transient and takes the monitor's value as it is now, so a
        // configuration reload can bring a lifetime past that check. Refusing here
        // makes such a value a failed sign-in that says why, instead of tokens
        // handed out already expired - which reaches the user as being signed out
        // the instant they sign in, with nothing in the logs to explain it.
        if (jwtOptions.TokenLifetimeMinutes <= 0)
        {
            throw new InvalidOperationException(
                $"JWT:TokenLifetimeMinutes is {jwtOptions.TokenLifetimeMinutes}; it must be greater than zero.");
        }
    }


    public string CreateToken(UserEntity user)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Name),

            // Carried so the client can decide whether to render the
            // server-scoped parts of the Settings page. Never the basis for
            // authorising a write: a token issued before a demotion stays valid
            // until it expires, so ServerCertificateService re-reads the flag
            // from the database instead.
            new Claim(UfoClaimTypes.IsAdmin, user.IsAdmin ? "true" : "false")
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.Add(jwtOptions.TokenLifetime),
            SigningCredentials = creds,
            Issuer = jwtOptions.Issuer,
            Audience = jwtOptions.Audience
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);

        return tokenHandler.WriteToken(token);
    }
}
