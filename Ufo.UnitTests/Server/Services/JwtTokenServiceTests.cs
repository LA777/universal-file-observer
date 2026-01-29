using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using System.IdentityModel.Tokens.Jwt;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Options;
using Ufo.Server.Services;

namespace Ufo.UnitTests.Server.Services;

public class JwtTokenServiceTests : BaseTest
{
    private readonly Mock<IOptionsMonitor<JwtOptions>> _optionsMonitorMock;
    private readonly JwtOptions _validJwtOptions;
    private readonly JwtTokenService _sut;

    public JwtTokenServiceTests()
    {
        _optionsMonitorMock = new Mock<IOptionsMonitor<JwtOptions>>();
        _validJwtOptions = new JwtOptions
        {
            Key = "this-is-a-very-secret-key-that-must-be-at-least-32-characters-long",
            Issuer = "TestIssuer",
            Audience = "TestAudience"
        };

        _optionsMonitorMock.Setup(x => x.CurrentValue).Returns(_validJwtOptions);
        _sut = new JwtTokenService(_optionsMonitorMock.Object);
    }

    #region CreateToken Tests

    [Fact]
    public void CreateToken_WithValidUser_ReturnsValidJwtToken()
    {
        // Arrange
        var user = new UserEntity
        {
            Id = Ulid.NewUlid(),
            Name = "testuser",
            PasswordHash = "hashedpassword"
        };

        // Act
        var token = _sut.CreateToken(user);

        // Assert
        token.Should().NotBeNullOrEmpty();
        token.Should().BeOfType<string>();
    }

    [Fact]
    public void CreateToken_WithValidUser_TokenCanBeReadByJwtSecurityTokenHandler()
    {
        // Arrange
        var user = new UserEntity
        {
            Id = Ulid.NewUlid(),
            Name = "testuser",
            PasswordHash = "hashedpassword"
        };

        // Act
        var token = _sut.CreateToken(user);
        var handler = new JwtSecurityTokenHandler();
        var canRead = handler.CanReadToken(token);

        // Assert
        canRead.Should().BeTrue();
    }

    [Fact]
    public void CreateToken_WithValidUser_ContainsCorrectNameIdentifierClaim()
    {
        // Arrange
        var userId = Ulid.NewUlid();
        var user = new UserEntity
        {
            Id = userId,
            Name = "testuser",
            PasswordHash = "hashedpassword"
        };

        // Act
        var token = _sut.CreateToken(user);
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        // Assert
        // JWT uses short claim type name "nameid"
        var userIdClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "nameid");
        userIdClaim.Should().NotBeNull();
        userIdClaim!.Value.Should().Be(userId.ToString());
    }

    [Fact]
    public void CreateToken_WithValidUser_ContainsCorrectNameClaim()
    {
        // Arrange
        var username = "uniqueusername";
        var user = new UserEntity
        {
            Id = Ulid.NewUlid(),
            Name = username,
            PasswordHash = "hashedpassword"
        };

        // Act
        var token = _sut.CreateToken(user);
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        // Assert
        // JWT uses short claim type name "unique_name"
        var nameClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "unique_name");
        nameClaim.Should().NotBeNull();
        nameClaim!.Value.Should().Be(username);
    }

    [Fact]
    public void CreateToken_WithValidUser_ContainsCorrectNumberOfClaims()
    {
        // Arrange
        var user = new UserEntity
        {
            Id = Ulid.NewUlid(),
            Name = "testuser",
            PasswordHash = "hashedpassword"
        };

        // Act
        var token = _sut.CreateToken(user);
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        // Assert
        // Should have at least nameid and unique_name claims
        jwtToken.Claims.Should().HaveCountGreaterThanOrEqualTo(2);
        jwtToken.Claims.Should().Contain(c => c.Type == "nameid");
        jwtToken.Claims.Should().Contain(c => c.Type == "unique_name");
    }

    [Fact]
    public void CreateToken_WithValidUser_TokenHasCorrectIssuer()
    {
        // Arrange
        var user = new UserEntity
        {
            Id = Ulid.NewUlid(),
            Name = "testuser",
            PasswordHash = "hashedpassword"
        };

        // Act
        var token = _sut.CreateToken(user);
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        // Assert
        jwtToken.Issuer.Should().Be(_validJwtOptions.Issuer);
    }

    [Fact]
    public void CreateToken_WithValidUser_TokenHasCorrectAudience()
    {
        // Arrange
        var user = new UserEntity
        {
            Id = Ulid.NewUlid(),
            Name = "testuser",
            PasswordHash = "hashedpassword"
        };

        // Act
        var token = _sut.CreateToken(user);
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        // Assert
        jwtToken.Audiences.Should().Contain(_validJwtOptions.Audience);
    }

    [Fact]
    public void CreateToken_WithValidUser_TokenExpiresIn7Days()
    {
        // Arrange
        var user = new UserEntity
        {
            Id = Ulid.NewUlid(),
            Name = "testuser",
            PasswordHash = "hashedpassword"
        };
        var beforeCreation = DateTime.UtcNow;

        // Act
        var token = _sut.CreateToken(user);
        var afterCreation = DateTime.UtcNow;
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        // Assert
        var expirationTime = jwtToken.ValidTo;
        var expectedExpiration = beforeCreation.AddDays(7);
        
        // Allow a small time window (within 5 seconds) for test execution
        expirationTime.Should().BeCloseTo(expectedExpiration, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void CreateToken_WithDifferentUsers_GeneratesDifferentTokens()
    {
        // Arrange
        var user1 = new UserEntity
        {
            Id = Ulid.NewUlid(),
            Name = "user1",
            PasswordHash = "hash1"
        };
        var user2 = new UserEntity
        {
            Id = Ulid.NewUlid(),
            Name = "user2",
            PasswordHash = "hash2"
        };

        // Act
        var token1 = _sut.CreateToken(user1);
        var token2 = _sut.CreateToken(user2);

        // Assert
        token1.Should().NotBe(token2);
    }

    [Fact]
    public void CreateToken_WithSameUser_GeneratesTokenWithValidClaims()
    {
        // Arrange
        var user = new UserEntity
        {
            Id = Ulid.NewUlid(),
            Name = "testuser",
            PasswordHash = "hashedpassword"
        };

        // Act
        var token = _sut.CreateToken(user);
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        // Assert
        // JWT uses short claim type names: "nameid" and "unique_name"
        var userIdClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "nameid");
        var nameClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "unique_name");

        userIdClaim.Should().NotBeNull();
        userIdClaim!.Value.Should().Be(user.Id.ToString());
        nameClaim.Should().NotBeNull();
        nameClaim!.Value.Should().Be(user.Name);
    }    

    #endregion

    #region Edge Cases and Error Handling

    [Fact]
    public void Constructor_WithNullOptionsMonitor_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
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
    public void CreateToken_WithUserHavingEmptyName_GeneratesToken()
    {
        // Arrange
        var user = new UserEntity
        {
            Id = Ulid.NewUlid(),
            Name = string.Empty,
            PasswordHash = "hashedpassword"
        };

        // Act
        var token = _sut.CreateToken(user);
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        // Assert
        token.Should().NotBeNullOrEmpty();
        
        // The nameid claim should still be present
        var userIdClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "nameid");
        userIdClaim.Should().NotBeNull();
        userIdClaim!.Value.Should().Be(user.Id.ToString());
        
        // The unique_name claim may be null or empty, both are acceptable for empty input
        var nameClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "unique_name");
        if (nameClaim != null)
        {
            nameClaim.Value.Should().Be(string.Empty);
        }
    }

    [Fact]
    public void CreateToken_WithUserHavingSpecialCharactersInName_GeneratesToken()
    {
        // Arrange
        var specialName = "user@example.com!#$%";
        var user = new UserEntity
        {
            Id = Ulid.NewUlid(),
            Name = specialName,
            PasswordHash = "hashedpassword"
        };

        // Act
        var token = _sut.CreateToken(user);
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

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
        var customOptions = new JwtOptions
        {
            Key = "this-is-a-very-secret-key-that-must-be-at-least-32-characters-long",
            Issuer = customIssuer,
            Audience = "TestAudience"
        };
        var customOptionsMonitorMock = new Mock<IOptionsMonitor<JwtOptions>>();
        customOptionsMonitorMock.Setup(x => x.CurrentValue).Returns(customOptions);
        var customService = new JwtTokenService(customOptionsMonitorMock.Object);

        var user = new UserEntity
        {
            Id = Ulid.NewUlid(),
            Name = "testuser",
            PasswordHash = "hashedpassword"
        };

        // Act
        var token = customService.CreateToken(user);
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        // Assert
        jwtToken.Issuer.Should().Be(customIssuer);
    }

    [Fact]
    public void CreateToken_WithDifferentJwtOptionsAudience_UsesCorrectAudience()
    {
        // Arrange
        var customAudience = "CustomAudienceName";
        var customOptions = new JwtOptions
        {
            Key = "this-is-a-very-secret-key-that-must-be-at-least-32-characters-long",
            Issuer = "TestIssuer",
            Audience = customAudience
        };
        var customOptionsMonitorMock = new Mock<IOptionsMonitor<JwtOptions>>();
        customOptionsMonitorMock.Setup(x => x.CurrentValue).Returns(customOptions);
        var customService = new JwtTokenService(customOptionsMonitorMock.Object);

        var user = new UserEntity
        {
            Id = Ulid.NewUlid(),
            Name = "testuser",
            PasswordHash = "hashedpassword"
        };

        // Act
        var token = customService.CreateToken(user);
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        // Assert
        jwtToken.Audiences.Should().Contain(customAudience);
    }

    #endregion

    #region Token Validation Tests

    [Fact]
    public void CreateToken_TokenIsValidBefore7Days()
    {
        // Arrange
        var user = new UserEntity
        {
            Id = Ulid.NewUlid(),
            Name = "testuser",
            PasswordHash = "hashedpassword"
        };

        // Act
        var token = _sut.CreateToken(user);
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        // Assert
        (jwtToken.ValidFrom <= DateTime.UtcNow).Should().BeTrue();
        (jwtToken.ValidTo > DateTime.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void CreateToken_TokenContainsNoEmptyClaims()
    {
        // Arrange
        var user = new UserEntity
        {
            Id = Ulid.NewUlid(),
            Name = "testuser",
            PasswordHash = "hashedpassword"
        };

        // Act
        var token = _sut.CreateToken(user);
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        // Assert
        foreach (var claim in jwtToken.Claims)
        {
            claim.Type.Should().NotBeNullOrEmpty();
            claim.Value.Should().NotBeNull(); // Value can be empty string but not null
        }
    }

    #endregion
}

