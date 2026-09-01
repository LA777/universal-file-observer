using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.Options;
using Ufo.Server.Services;

namespace Ufo.UnitTests.Server.Services;

public class RefreshTokenServiceTests : BaseTest
{
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepositoryMock;
    private readonly JwtOptions _jwtOptions;
    private readonly RefreshTokenService _sut;

    public RefreshTokenServiceTests()
    {
        _refreshTokenRepositoryMock = new Mock<IRefreshTokenRepository>();
        _jwtOptions = new JwtOptions
        {
            Key = "this-is-a-very-secret-key-that-must-be-at-least-32-characters-long",
            Issuer = "TestIssuer",
            Audience = "TestAudience"
        };

        var optionsMonitorMock = new Mock<IOptionsMonitor<JwtOptions>>();
        optionsMonitorMock.Setup(x => x.CurrentValue).Returns(_jwtOptions);

        _sut = new RefreshTokenService(
            _refreshTokenRepositoryMock.Object,
            optionsMonitorMock.Object,
            Mock.Of<ILogger<RefreshTokenService>>());
    }

    private static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static DateTimeOffset Parse(string instant) =>
        DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    /// <summary>A live token, unless the caller says otherwise.</summary>
    private static RefreshTokenEntity StoredToken(
        Ulid userId,
        string tokenHash,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? absoluteExpiresAt = null,
        DateTimeOffset? revokedAt = null) =>
        new()
        {
            Id = Ulid.NewUlid(),
            UserId = userId,
            TokenHash = tokenHash,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1).ToString("o"),
            ExpiresAt = (expiresAt ?? DateTimeOffset.UtcNow.AddDays(13)).ToString("o"),
            AbsoluteExpiresAt = (absoluteExpiresAt ?? DateTimeOffset.UtcNow.AddDays(29)).ToString("o"),
            RevokedAt = revokedAt?.ToString("o")
        };

    #region IssueAsync

    [Fact]
    public async Task IssueAsync_StoresTheHashAndNeverTheToken()
    {
        // Arrange
        RefreshTokenEntity? insertedToken = null;
        _refreshTokenRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshTokenEntity, CancellationToken>((refreshToken, _) => insertedToken = refreshToken)
            .Returns(Task.CompletedTask);

        var userId = Ulid.NewUlid();

        // Act
        var issuedToken = await _sut.IssueAsync(userId);

        // Assert - the row identifies the token by hash; the token itself is
        // returned to the caller and kept nowhere.
        issuedToken.Token.Should().NotBeNullOrEmpty();
        insertedToken.Should().NotBeNull();
        insertedToken!.UserId.Should().Be(userId);
        insertedToken.TokenHash.Should().Be(Hash(issuedToken.Token));
        insertedToken.TokenHash.Should().NotBe(issuedToken.Token);

        // The deadline is reported back, because the cookie has to expire with the
        // row rather than with the configured window.
        Parse(insertedToken.ExpiresAt).Should().BeCloseTo(issuedToken.ExpiresAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task IssueAsync_GeneratesADifferentTokenEveryTime()
    {
        var firstToken = await _sut.IssueAsync(Ulid.NewUlid());
        var secondToken = await _sut.IssueAsync(Ulid.NewUlid());

        firstToken.Token.Should().NotBe(secondToken.Token);
    }

    [Fact]
    public async Task IssueAsync_SetsBothDeadlinesFromConfiguration()
    {
        // Arrange
        _jwtOptions.RefreshTokenLifetimeDays = 3;
        _jwtOptions.RefreshTokenAbsoluteLifetimeDays = 10;

        RefreshTokenEntity? insertedToken = null;
        _refreshTokenRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshTokenEntity, CancellationToken>((refreshToken, _) => insertedToken = refreshToken)
            .Returns(Task.CompletedTask);

        var beforeIssue = DateTimeOffset.UtcNow;

        // Act
        await _sut.IssueAsync(Ulid.NewUlid());

        // Assert
        Parse(insertedToken!.ExpiresAt).Should().BeCloseTo(beforeIssue.AddDays(3), TimeSpan.FromSeconds(5));
        Parse(insertedToken.AbsoluteExpiresAt).Should().BeCloseTo(beforeIssue.AddDays(10), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task IssueAsync_ReportsTheDeadlineItActuallyUsed()
    {
        // Arrange - a sliding window longer than the absolute cap is clamped, and
        // the caller has to be told the clamped value or the cookie it writes
        // outlives the row.
        _jwtOptions.RefreshTokenLifetimeDays = 14;
        _jwtOptions.RefreshTokenAbsoluteLifetimeDays = 14;

        var beforeIssue = DateTimeOffset.UtcNow;

        // Act
        var issuedToken = await _sut.IssueAsync(Ulid.NewUlid());

        // Assert
        issuedToken.ExpiresAt.Should().BeCloseTo(beforeIssue.AddDays(14), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task IssueAsync_DropsRowsThatArePastTheirAbsoluteDeadline()
    {
        // Arrange & Act - this app runs no background service, so housekeeping
        // rides along with the sign-in.
        await _sut.IssueAsync(Ulid.NewUlid());

        // Assert
        _refreshTokenRepositoryMock.Verify(
            x => x.DeleteExpiredAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region RotateAsync - the happy path

    [Fact]
    public async Task RotateAsync_WithLiveToken_RevokesItAndIssuesASuccessor()
    {
        // Arrange
        var userId = Ulid.NewUlid();
        const string presentedToken = "presented-token";
        var storedToken = StoredToken(userId, Hash(presentedToken));

        _refreshTokenRepositoryMock
            .Setup(x => x.GetByHashAsync(Hash(presentedToken), It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedToken);
        _refreshTokenRepositoryMock
            .Setup(x => x.TryRotateAsync(storedToken.Id, It.IsAny<Ulid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        RefreshTokenEntity? successor = null;
        _refreshTokenRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshTokenEntity, CancellationToken>((refreshToken, _) => successor = refreshToken)
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.RotateAsync(presentedToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.UserId.Should().Be(userId);
        result.RefreshToken.Should().NotBeNull();
        result.RefreshToken!.Token.Should().NotBeNullOrEmpty().And.NotBe(presentedToken);

        successor.Should().NotBeNull();
        successor!.TokenHash.Should().Be(Hash(result.RefreshToken.Token));

        // The reported deadline is the successor's own, so the cookie written from
        // it cannot outlive the row.
        Parse(successor.ExpiresAt).Should().BeCloseTo(result.RefreshToken.ExpiresAt, TimeSpan.FromSeconds(1));

        // The row the presented token was revoked with must name the successor
        // that was actually written, so a chain can be followed afterwards.
        _refreshTokenRepositoryMock.Verify(
            x => x.TryRotateAsync(storedToken.Id, successor.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RotateAsync_SuccessorInheritsTheAbsoluteDeadlineUnchanged()
    {
        // Arrange - rotation renews a session's idle window, never its total life.
        var absoluteExpiresAt = DateTimeOffset.UtcNow.AddDays(9);
        const string presentedToken = "presented-token";
        var storedToken = StoredToken(Ulid.NewUlid(), Hash(presentedToken), absoluteExpiresAt: absoluteExpiresAt);

        _refreshTokenRepositoryMock
            .Setup(x => x.GetByHashAsync(Hash(presentedToken), It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedToken);
        _refreshTokenRepositoryMock
            .Setup(x => x.TryRotateAsync(It.IsAny<Ulid>(), It.IsAny<Ulid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        RefreshTokenEntity? successor = null;
        _refreshTokenRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshTokenEntity, CancellationToken>((refreshToken, _) => successor = refreshToken)
            .Returns(Task.CompletedTask);

        // Act
        await _sut.RotateAsync(presentedToken);

        // Assert
        Parse(successor!.AbsoluteExpiresAt).Should().BeCloseTo(absoluteExpiresAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task RotateAsync_SlidingDeadlineIsCappedByTheAbsoluteOne()
    {
        // Arrange - a sign-in with an hour of its absolute life left must not hand
        // out a successor claiming another fortnight.
        var absoluteExpiresAt = DateTimeOffset.UtcNow.AddHours(1);
        const string presentedToken = "presented-token";
        var storedToken = StoredToken(Ulid.NewUlid(), Hash(presentedToken), absoluteExpiresAt: absoluteExpiresAt);

        _refreshTokenRepositoryMock
            .Setup(x => x.GetByHashAsync(Hash(presentedToken), It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedToken);
        _refreshTokenRepositoryMock
            .Setup(x => x.TryRotateAsync(It.IsAny<Ulid>(), It.IsAny<Ulid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        RefreshTokenEntity? successor = null;
        _refreshTokenRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshTokenEntity, CancellationToken>((refreshToken, _) => successor = refreshToken)
            .Returns(Task.CompletedTask);

        // Act
        await _sut.RotateAsync(presentedToken);

        // Assert
        Parse(successor!.ExpiresAt).Should().BeCloseTo(absoluteExpiresAt, TimeSpan.FromSeconds(1));
    }

    #endregion

    #region RotateAsync - refusals

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RotateAsync_WithNoToken_IsRefusedWithoutTouchingTheDatabase(string presentedToken)
    {
        var result = await _sut.RotateAsync(presentedToken);

        result.IsSuccess.Should().BeFalse();
        result.Failure.Should().Be(RefreshTokenFailure.Unknown);
        _refreshTokenRepositoryMock.Verify(
            x => x.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RotateAsync_WithUnknownToken_IsRefused()
    {
        // Arrange
        _refreshTokenRepositoryMock
            .Setup(x => x.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshTokenEntity?)null);

        // Act
        var result = await _sut.RotateAsync("never-issued");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Failure.Should().Be(RefreshTokenFailure.Unknown);
    }

    [Fact]
    public async Task RotateAsync_PastTheSlidingDeadline_IsRefused()
    {
        // Arrange
        const string presentedToken = "idle-too-long";
        _refreshTokenRepositoryMock
            .Setup(x => x.GetByHashAsync(Hash(presentedToken), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoredToken(
                Ulid.NewUlid(), Hash(presentedToken), expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1)));

        // Act
        var result = await _sut.RotateAsync(presentedToken);

        // Assert
        result.Failure.Should().Be(RefreshTokenFailure.Expired);
        _refreshTokenRepositoryMock.Verify(
            x => x.InsertAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RotateAsync_PastTheAbsoluteDeadline_IsRefusedEvenWhileTheSlidingOneHolds()
    {
        // Arrange - the whole point of the second deadline.
        const string presentedToken = "signed-in-too-long-ago";
        _refreshTokenRepositoryMock
            .Setup(x => x.GetByHashAsync(Hash(presentedToken), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoredToken(
                Ulid.NewUlid(),
                Hash(presentedToken),
                expiresAt: DateTimeOffset.UtcNow.AddDays(13),
                absoluteExpiresAt: DateTimeOffset.UtcNow.AddMinutes(-1)));

        // Act
        var result = await _sut.RotateAsync(presentedToken);

        // Assert
        result.Failure.Should().Be(RefreshTokenFailure.Expired);
    }

    [Fact]
    public async Task RotateAsync_WhenAnotherRequestWinsTheRotation_IsRefusedAndIssuesNothing()
    {
        // Arrange - TryRotateAsync is conditional on the row still being live, so
        // the loser of a simultaneous refresh is told no.
        const string presentedToken = "contended-token";
        var storedToken = StoredToken(Ulid.NewUlid(), Hash(presentedToken));

        _refreshTokenRepositoryMock
            .Setup(x => x.GetByHashAsync(Hash(presentedToken), It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedToken);
        _refreshTokenRepositoryMock
            .Setup(x => x.TryRotateAsync(It.IsAny<Ulid>(), It.IsAny<Ulid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _sut.RotateAsync(presentedToken);

        // Assert - and nothing is revoked: losing a race is not evidence of theft.
        result.Failure.Should().Be(RefreshTokenFailure.Raced);
        _refreshTokenRepositoryMock.Verify(
            x => x.InsertAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        _refreshTokenRepositoryMock.Verify(
            x => x.RevokeAllForUserAsync(It.IsAny<Ulid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region RotateAsync - reuse detection

    [Fact]
    public async Task RotateAsync_WithATokenRotatedMomentsAgo_IsRefusedWithoutEndingTheSession()
    {
        // Arrange - a client retrying a request whose response never arrived
        // presents the token it still believes in. That is indistinguishable from
        // a replay, so the benign reading wins inside the grace period.
        const string presentedToken = "retried-token";
        _refreshTokenRepositoryMock
            .Setup(x => x.GetByHashAsync(Hash(presentedToken), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoredToken(
                Ulid.NewUlid(), Hash(presentedToken), revokedAt: DateTimeOffset.UtcNow.AddSeconds(-2)));

        // Act
        var result = await _sut.RotateAsync(presentedToken);

        // Assert
        result.Failure.Should().Be(RefreshTokenFailure.Raced);
        _refreshTokenRepositoryMock.Verify(
            x => x.RevokeAllForUserAsync(It.IsAny<Ulid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RotateAsync_WithALongRevokedToken_EndsEverySessionForThatUser()
    {
        // Arrange - two parties hold one token and nothing here says which is the
        // owner, so every session goes.
        var userId = Ulid.NewUlid();
        const string presentedToken = "copied-token";
        _refreshTokenRepositoryMock
            .Setup(x => x.GetByHashAsync(Hash(presentedToken), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoredToken(userId, Hash(presentedToken), revokedAt: DateTimeOffset.UtcNow.AddMinutes(-5)));

        // Act
        var result = await _sut.RotateAsync(presentedToken);

        // Assert
        result.Failure.Should().Be(RefreshTokenFailure.Reused);
        _refreshTokenRepositoryMock.Verify(
            x => x.RevokeAllForUserAsync(userId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _refreshTokenRepositoryMock.Verify(
            x => x.InsertAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region RevokeAsync

    [Fact]
    public async Task RevokeAsync_EndsTheSessionTheTokenBelongsTo()
    {
        // Arrange
        const string presentedToken = "signing-out";
        var storedToken = StoredToken(Ulid.NewUlid(), Hash(presentedToken));
        _refreshTokenRepositoryMock
            .Setup(x => x.GetByHashAsync(Hash(presentedToken), It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedToken);

        // Act
        await _sut.RevokeAsync(presentedToken);

        // Assert
        _refreshTokenRepositoryMock.Verify(
            x => x.TryRevokeAsync(storedToken.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeAsync_AlsoEndsASuccessorTheTokenWasRotatedInto()
    {
        // Arrange - a refresh that was in flight when the user signed out has
        // already rotated the presented token into a successor the browser was
        // handed. Revoking only what was presented would leave that live, and the
        // sign-out would have ended nothing.
        const string presentedToken = "signed-out-mid-refresh";
        var successor = StoredToken(Ulid.NewUlid(), "successor-hash");
        var presented = StoredToken(successor.UserId, Hash(presentedToken), revokedAt: DateTimeOffset.UtcNow);
        presented.ReplacedByTokenId = successor.Id;

        _refreshTokenRepositoryMock
            .Setup(x => x.GetByHashAsync(Hash(presentedToken), It.IsAny<CancellationToken>()))
            .ReturnsAsync(presented);
        _refreshTokenRepositoryMock
            .Setup(x => x.GetByIdAsync(successor.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(successor);

        // Act
        await _sut.RevokeAsync(presentedToken);

        // Assert
        _refreshTokenRepositoryMock.Verify(
            x => x.TryRevokeAsync(presented.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _refreshTokenRepositoryMock.Verify(
            x => x.TryRevokeAsync(successor.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeAsync_WithUnknownToken_DoesNothingAndSaysNothing()
    {
        // Arrange - signing out is not a place to tell a caller which tokens exist.
        _refreshTokenRepositoryMock
            .Setup(x => x.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshTokenEntity?)null);

        // Act
        var revoke = async () => await _sut.RevokeAsync("never-issued");

        // Assert
        await revoke.Should().NotThrowAsync();
        _refreshTokenRepositoryMock.Verify(
            x => x.TryRevokeAsync(It.IsAny<Ulid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion
}
