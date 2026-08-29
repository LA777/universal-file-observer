using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Options;
using Ufo.Server.Services;

namespace Ufo.UnitTests.Server.Services;

public class JwtTokenServiceTests : BaseTest
{
    private const string SigningKey = "this-is-a-very-secret-key-that-must-be-at-least-32-characters-long";

    private readonly Mock<IOptionsMonitor<JwtOptions>> _optionsMonitorMock;
    private readonly JwtOptions _validJwtOptions;
    private readonly JwtTokenService _sut;

    public JwtTokenServiceTests()
    {
        _optionsMonitorMock = new Mock<IOptionsMonitor<JwtOptions>>();
        _validJwtOptions = new JwtOptions
        {
            Key = SigningKey,
            Issuer = "TestIssuer",
            Audience = "TestAudience"
        };

        _optionsMonitorMock.Setup(x => x.CurrentValue).Returns(_validJwtOptions);
        _sut = new JwtTokenService(_optionsMonitorMock.Object);
    }

    private static UserEntity CreateUser(string name = "testuser") => new()
    {
        Id = Ulid.NewUlid(),
        Name = name,
        PasswordHash = "hashedpassword"
    };

    private static JwtSecurityToken Read(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token);

    private ClaimsPrincipal Validate(string token, string signingKey) =>
        new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _validJwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = _validJwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        }, out _);

    #region CreateToken Tests

    [Fact]
    public void CreateToken_WithValidUser_ReturnsWellFormedJwt()
    {
        // Act
        var token = _sut.CreateToken(CreateUser());

        // Assert - a JWT is three base64url segments and must be readable by the handler.
        token.Should().NotBeNullOrEmpty();
        token.Split('.').Should().HaveCount(3);
        new JwtSecurityTokenHandler().CanReadToken(token).Should().BeTrue();
    }

    [Fact]
    public void CreateToken_WithValidUser_ContainsCorrectNameIdentifierClaim()
    {
        // Arrange
        var user = CreateUser();

        // Act
        var jwtToken = Read(_sut.CreateToken(user));

        // Assert - JWT uses short claim type name "nameid"
        var userIdClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "nameid");
        userIdClaim.Should().NotBeNull();
        userIdClaim!.Value.Should().Be(user.Id.ToString());
    }

    [Fact]
    public void CreateToken_WithValidUser_ContainsCorrectNameClaim()
    {
        // Arrange
        var user = CreateUser("uniqueusername");

        // Act
        var jwtToken = Read(_sut.CreateToken(user));

        // Assert - JWT uses short claim type name "unique_name"
        var nameClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "unique_name");
        nameClaim.Should().NotBeNull();
        nameClaim!.Value.Should().Be(user.Name);
    }

    [Fact]
    public void CreateToken_WithValidUser_TokenHasCorrectIssuer()
    {
        // Act
        var jwtToken = Read(_sut.CreateToken(CreateUser()));

        // Assert
        jwtToken.Issuer.Should().Be(_validJwtOptions.Issuer);
    }

    [Fact]
    public void CreateToken_WithValidUser_TokenHasCorrectAudience()
    {
        // Act
        var jwtToken = Read(_sut.CreateToken(CreateUser()));

        // Assert
        jwtToken.Audiences.Should().Contain(_validJwtOptions.Audience);
    }

    [Fact]
    public void CreateToken_WithValidUser_TokenExpiresIn7Days()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var jwtToken = Read(_sut.CreateToken(CreateUser()));

        // Assert - allow a small time window for test execution.
        jwtToken.ValidTo.Should().BeCloseTo(beforeCreation.AddDays(7), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void CreateToken_WithDifferentUsers_GeneratesDifferentTokens()
    {
        // Act
        var token1 = _sut.CreateToken(CreateUser("user1"));
        var token2 = _sut.CreateToken(CreateUser("user2"));

        // Assert
        token1.Should().NotBe(token2);
    }

    #endregion

    #region Signature Validation Tests

    [Fact]
    public void CreateToken_SignatureValidatesWithConfiguredKey()
    {
        // Arrange
        var user = CreateUser();

        // Act
        var token = _sut.CreateToken(user);
        var principal = Validate(token, SigningKey);

        // Assert - full validation (issuer, audience, lifetime, signature) succeeds
        // and the identity round-trips.
        principal.Should().NotBeNull();
        principal.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be(user.Id.ToString());
    }

    [Fact]
    public void CreateToken_SignatureFailsValidationWithDifferentKey()
    {
        // Arrange
        var token = _sut.CreateToken(CreateUser());

        // Act
        var validateWithWrongKey = () => Validate(token, "a-completely-different-signing-key-32-characters!!");

        // Assert - a token signed with one key must never validate against another.
        validateWithWrongKey.Should().Throw<SecurityTokenException>();
    }

    #endregion

    #region Edge Cases and Error Handling

    [Fact]
    public void Constructor_WithNullOptionsMonitor_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new JwtTokenService(null!));
    }

    [Fact]
    public void Constructor_WithNullCurrentValue_ThrowsArgumentNullException()
    {
        // Arrange
        var nullOptionsMonitorMock = new Mock<IOptionsMonitor<JwtOptions>>();
        nullOptionsMonitorMock.Setup(x => x.CurrentValue).Returns((JwtOptions)null!);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new JwtTokenService(nullOptionsMonitorMock.Object));
    }

    [Fact]
    public void CreateToken_WithUserHavingEmptyName_GeneratesTokenWithUserIdClaim()
    {
        // Arrange
        var user = CreateUser(string.Empty);

        // Act
        var token = _sut.CreateToken(user);
        var jwtToken = Read(token);

        // Assert
        token.Should().NotBeNullOrEmpty();

        // The nameid claim must still be present.
        var userIdClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "nameid");
        userIdClaim.Should().NotBeNull();
        userIdClaim!.Value.Should().Be(user.Id.ToString());

        // The unique_name claim may be dropped or empty; both are acceptable for empty input.
        var nameClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "unique_name");
        if (nameClaim != null)
        {
            nameClaim.Value.Should().Be(string.Empty);
        }
    }

    [Fact]
    public void CreateToken_WithUserHavingSpecialCharactersInName_PreservesName()
    {
        // Arrange
        var specialName = "user@example.com!#$%";
        var user = CreateUser(specialName);

        // Act
        var jwtToken = Read(_sut.CreateToken(user));

        // Assert
        var nameClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "unique_name");
        nameClaim.Should().NotBeNull();
        nameClaim!.Value.Should().Be(specialName);
    }

    [Fact]
    public void CreateToken_WithDifferentJwtOptionsIssuer_UsesCorrectIssuer()
    {
        // Arrange
        var customIssuer = "CustomIssuerName";
        var customService = CreateServiceWithOptions(issuer: customIssuer);

        // Act
        var jwtToken = Read(customService.CreateToken(CreateUser()));

        // Assert
        jwtToken.Issuer.Should().Be(customIssuer);
    }

    [Fact]
    public void CreateToken_WithDifferentJwtOptionsAudience_UsesCorrectAudience()
    {
        // Arrange
        var customAudience = "CustomAudienceName";
        var customService = CreateServiceWithOptions(audience: customAudience);

        // Act
        var jwtToken = Read(customService.CreateToken(CreateUser()));

        // Assert
        jwtToken.Audiences.Should().Contain(customAudience);
    }

    private static JwtTokenService CreateServiceWithOptions(string issuer = "TestIssuer", string audience = "TestAudience")
    {
        var options = new JwtOptions { Key = SigningKey, Issuer = issuer, Audience = audience };
        var monitorMock = new Mock<IOptionsMonitor<JwtOptions>>();
        monitorMock.Setup(x => x.CurrentValue).Returns(options);
        return new JwtTokenService(monitorMock.Object);
    }

    #endregion

    #region Token Validation Tests

    [Fact]
    public void CreateToken_TokenIsValidNowAndNotExpired()
    {
        // Act
        var jwtToken = Read(_sut.CreateToken(CreateUser()));

        // Assert
        jwtToken.ValidFrom.Should().BeOnOrBefore(DateTime.UtcNow);
        jwtToken.ValidTo.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public void CreateToken_TokenContainsNoEmptyClaims()
    {
        // Act
        var jwtToken = Read(_sut.CreateToken(CreateUser()));

        // Assert
        foreach (var claim in jwtToken.Claims)
        {
            claim.Type.Should().NotBeNullOrEmpty();
            claim.Value.Should().NotBeNull(); // Value can be empty string but not null
        }
    }

    #endregion
}
