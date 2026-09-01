using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.Options;

namespace Ufo.Server.Services;

/// <summary>Why a refresh was refused. <see cref="None"/> means it was not.</summary>
public enum RefreshTokenFailure
{
    None,

    /// <summary>No such token: never issued here, or already deleted as expired.</summary>
    Unknown,

    /// <summary>Past its sliding window or its absolute cap.</summary>
    Expired,

    /// <summary>
    /// Presented after it had already been rotated away, long enough after that a
    /// lost response cannot explain it. Two parties hold the same token, so every
    /// session for that user is ended.
    /// </summary>
    Reused,

    /// <summary>
    /// Lost a race with another refresh of the same token, or arrived just after
    /// one - a double submit, or a client retrying a request whose response never
    /// came back. Refused, but nothing is revoked: the successor is still live and
    /// signing in again is all it costs.
    /// </summary>
    Raced
}

/// <summary>
/// An issued token and the moment it stops working. The deadline is returned
/// rather than recomputed by the caller because it is not always the configured
/// sliding window: a session close to its absolute cap gets a shorter one, and a
/// cookie outliving the row behind it would promise a session the server has
/// already ended.
/// </summary>
public sealed class IssuedRefreshToken
{
    public required string Token { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }
}

public sealed class RefreshTokenRotationResult
{
    public required bool IsSuccess { get; init; }

    public RefreshTokenFailure Failure { get; init; }

    /// <summary>The session's owner. Only meaningful when <see cref="IsSuccess"/>.</summary>
    public Ulid UserId { get; init; }

    /// <summary>The successor token, to be handed to the client. Only when <see cref="IsSuccess"/>.</summary>
    public IssuedRefreshToken? RefreshToken { get; init; }
}

public interface IRefreshTokenService
{
    /// <summary>
    /// Starts a session and returns the token to hand to the client. The token is
    /// returned here and nowhere else: only its hash is kept.
    /// </summary>
    Task<IssuedRefreshToken> IssueAsync(Ulid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges a presented token for its successor, ending the presented one.
    /// </summary>
    Task<RefreshTokenRotationResult> RotateAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the session a token belongs to, including any successor it was
    /// rotated into while the sign-out was on its way. Silent when the token is
    /// unknown or already revoked: signing out is not a place to tell a caller
    /// which tokens exist, and the outcome they asked for holds either way.
    /// </summary>
    Task RevokeAsync(string refreshToken, CancellationToken cancellationToken = default);
}

/// <summary>
/// Issues, rotates and revokes refresh tokens - the state that lets a session
/// outlive a short access token and still be endable.
/// </summary>
public class RefreshTokenService : IRefreshTokenService
{
    /// <summary>
    /// 256 bits, the same strength as the signature on the access token it
    /// renews, so the token is not the weak end of the session.
    /// </summary>
    private const int TokenLengthInBytes = 32;

    /// <summary>
    /// How long after a rotation the token it replaced is still treated as a
    /// mishap rather than a theft.
    ///
    /// Reuse detection is the point of rotation, but a client that never received
    /// a response - a dropped connection, a sleeping laptop, a double-submitted
    /// request - retries with the token it still believes in, and that is
    /// indistinguishable from an attacker replaying it. Inside this window the
    /// benign reading wins and the refresh is simply refused; outside it, the
    /// theft reading wins and every session for that user ends. Short, because the
    /// benign cases all happen within seconds of the rotation.
    /// </summary>
    private static readonly TimeSpan ReuseGracePeriod = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How far a sign-out will follow rotations from the token it was given. A
    /// couple of hops covers a refresh that overlapped the sign-out; the bound is
    /// there so a malformed chain cannot loop rather than because a real one gets
    /// long.
    /// </summary>
    private const int MaximumRotationChainDepth = 8;

    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly ILogger<RefreshTokenService> _logger;
    private readonly IOptionsMonitor<JwtOptions> _jwtOptionsMonitor;

    public RefreshTokenService(
        IRefreshTokenRepository refreshTokenRepository,
        IOptionsMonitor<JwtOptions> jwtOptionsMonitor,
        ILogger<RefreshTokenService> logger)
    {
        _refreshTokenRepository = refreshTokenRepository ?? throw new ArgumentNullException(nameof(refreshTokenRepository));
        _jwtOptionsMonitor = jwtOptionsMonitor ?? throw new ArgumentNullException(nameof(jwtOptionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IssuedRefreshToken> IssueAsync(Ulid userId, CancellationToken cancellationToken = default)
    {
        var jwtOptions = _jwtOptionsMonitor.CurrentValue;
        var issuedAt = DateTimeOffset.UtcNow;

        var refreshToken = await IssueAsync(
            userId,
            issuedAt,
            absoluteExpiresAt: issuedAt.Add(jwtOptions.RefreshTokenAbsoluteLifetime),
            cancellationToken);

        // Rows past their absolute deadline can never be rotated again, so they
        // are dropped on the way past rather than by anything scheduled - this app
        // deliberately runs no background service.
        await _refreshTokenRepository.DeleteExpiredAsync(issuedAt.ToString("o"), cancellationToken);

        return refreshToken;
    }

    public async Task<RefreshTokenRotationResult> RotateAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Refused(RefreshTokenFailure.Unknown);
        }

        var storedToken = await _refreshTokenRepository.GetByHashAsync(Hash(refreshToken), cancellationToken);
        if (storedToken == null)
        {
            _logger.LogInformation("RotateAsync - refresh refused: the token is not one this server issued.");

            return Refused(RefreshTokenFailure.Unknown);
        }

        var utcNow = DateTimeOffset.UtcNow;

        if (storedToken.RevokedAt != null)
        {
            return await HandleAlreadyRevokedAsync(storedToken, utcNow, cancellationToken);
        }

        // Either deadline is enough to end the session; the absolute one is the
        // reason a token still inside its sliding window can still be refused.
        if (HasPassed(storedToken.ExpiresAt, utcNow) || HasPassed(storedToken.AbsoluteExpiresAt, utcNow))
        {
            _logger.LogInformation(
                "RotateAsync - refresh refused: token {RefreshTokenId} for user {UserId} has expired.",
                storedToken.Id,
                storedToken.UserId);

            return Refused(RefreshTokenFailure.Expired);
        }

        var successorId = Ulid.NewUlid();

        // Claim the presented token before minting its successor. Two refreshes
        // arriving together both reach here; the UPDATE is conditional on the row
        // still being live, so exactly one of them proceeds and the other is told
        // it raced rather than both walking away with a token.
        var rotated = await _refreshTokenRepository.TryRotateAsync(
            storedToken.Id, successorId, utcNow.ToString("o"), cancellationToken);

        if (!rotated)
        {
            _logger.LogInformation(
                "RotateAsync - refresh refused: token {RefreshTokenId} was rotated by a request that arrived at the same time.",
                storedToken.Id);

            return Refused(RefreshTokenFailure.Raced);
        }

        // The successor inherits the absolute deadline unchanged: rotation renews
        // a session's idleness window, never its overall life.
        var successorToken = await IssueAsync(
            storedToken.UserId,
            utcNow,
            absoluteExpiresAt: ParseOrMinValue(storedToken.AbsoluteExpiresAt),
            cancellationToken,
            successorId);

        return new RefreshTokenRotationResult
        {
            IsSuccess = true,
            UserId = storedToken.UserId,
            RefreshToken = successorToken
        };
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var storedToken = await _refreshTokenRepository.GetByHashAsync(Hash(refreshToken), cancellationToken);
        if (storedToken == null)
        {
            return;
        }

        var revokedAt = DateTimeOffset.UtcNow.ToString("o");

        // Follow the chain, because the token presented here is not always the
        // live one. A refresh that was in flight when the user signed out has
        // already rotated this token into a successor - one the browser was handed
        // and the sign-out request could not have carried. Revoking only what was
        // presented would leave that successor live, and "sign out" would have
        // ended nothing.
        var tokenToRevoke = storedToken;
        for (var depth = 0; depth < MaximumRotationChainDepth; depth++)
        {
            await _refreshTokenRepository.TryRevokeAsync(tokenToRevoke.Id, revokedAt, cancellationToken);

            if (tokenToRevoke.ReplacedByTokenId == null)
            {
                return;
            }

            var successor = await _refreshTokenRepository.GetByIdAsync(tokenToRevoke.ReplacedByTokenId.Value, cancellationToken);
            if (successor == null)
            {
                return;
            }

            tokenToRevoke = successor;
        }

        // Only reachable if the chain were circular, which nothing can write: a
        // successor is a new row and rotation only ever points backwards in time.
        _logger.LogWarning(
            "RevokeAsync - stopped following the rotation chain from {RefreshTokenId} after {Depth} hops.",
            storedToken.Id,
            MaximumRotationChainDepth);
    }

    /// <summary>
    /// A token presented after it stopped being live. Whether that is a theft or a
    /// mishap is decided by how long ago it was revoked: see
    /// <see cref="ReuseGracePeriod"/>.
    /// </summary>
    private async Task<RefreshTokenRotationResult> HandleAlreadyRevokedAsync(
        RefreshTokenEntity storedToken,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        var revokedAt = ParseOrMinValue(storedToken.RevokedAt);

        if (utcNow - revokedAt <= ReuseGracePeriod)
        {
            _logger.LogInformation(
                "RotateAsync - refresh refused: token {RefreshTokenId} was rotated moments ago, which a retried request explains.",
                storedToken.Id);

            return Refused(RefreshTokenFailure.Raced);
        }

        // One token, two holders, and nothing here says which of them is the
        // account's owner. Ending every session is the only answer that is certain
        // to end the wrong one's; the owner signs in again and the copy is dead.
        var revokedCount = await _refreshTokenRepository.RevokeAllForUserAsync(
            storedToken.UserId, utcNow.ToString("o"), cancellationToken);

        _logger.LogWarning(
            "Refresh token {RefreshTokenId} for user {UserId} was presented after it had been rotated away. "
            + "Treating it as a copied token and ending every session for that user ({RevokedCount} revoked).",
            storedToken.Id,
            storedToken.UserId,
            revokedCount);

        return Refused(RefreshTokenFailure.Reused);
    }

    private async Task<IssuedRefreshToken> IssueAsync(
        Ulid userId,
        DateTimeOffset issuedAt,
        DateTimeOffset absoluteExpiresAt,
        CancellationToken cancellationToken,
        Ulid? refreshTokenId = null)
    {
        var jwtOptions = _jwtOptionsMonitor.CurrentValue;
        var refreshToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenLengthInBytes));

        // The sliding window cannot reach past the absolute one: a token that
        // outlived its sign-in would be refused on presentation anyway, and the
        // cookie would claim a life the server does not honour.
        var expiresAt = issuedAt.Add(jwtOptions.RefreshTokenLifetime);
        if (expiresAt > absoluteExpiresAt)
        {
            expiresAt = absoluteExpiresAt;
        }

        await _refreshTokenRepository.InsertAsync(
            new RefreshTokenEntity
            {
                Id = refreshTokenId ?? Ulid.NewUlid(),
                UserId = userId,
                TokenHash = Hash(refreshToken),
                CreatedAt = issuedAt.ToString("o"),
                ExpiresAt = expiresAt.ToString("o"),
                AbsoluteExpiresAt = absoluteExpiresAt.ToString("o")
            },
            cancellationToken);

        return new IssuedRefreshToken { Token = refreshToken, ExpiresAt = expiresAt };
    }

    private static RefreshTokenRotationResult Refused(RefreshTokenFailure failure) =>
        new() { IsSuccess = false, Failure = failure };

    /// <summary>
    /// SHA-256, unsalted and uniterated on purpose: the input is 256 bits of
    /// randomness this server generated, not a password, so there is nothing for a
    /// dictionary or a rainbow table to get hold of and no reason to make
    /// verification slow.
    /// </summary>
    private static string Hash(string refreshToken) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

    /// <summary>
    /// A deadline that cannot be read counts as passed, and an instant that cannot
    /// be read counts as long ago - both readings refuse rather than admit.
    /// </summary>
    private static bool HasPassed(string instant, DateTimeOffset utcNow) => ParseOrMinValue(instant) <= utcNow;

    private static DateTimeOffset ParseOrMinValue(string? instant) =>
        DateTimeOffset.TryParse(instant, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
}
