using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Options;

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
    }


    public string CreateToken(UserEntity user)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Name)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddDays(7), // TODO LA - Implement expirity configuration
            SigningCredentials = creds,
            Issuer = jwtOptions.Issuer,
            Audience = jwtOptions.Audience
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);

        return tokenHandler.WriteToken(token);
    }
}
