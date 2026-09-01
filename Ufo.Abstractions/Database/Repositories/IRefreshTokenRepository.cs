using Ufo.Abstractions.Database.Entities;

namespace Ufo.Abstractions.Database.Repositories;

/// <summary>
/// The stored half of a session. Every method here works on hashes, never on the
/// token a client holds: see <see cref="RefreshTokenEntity"/>.
/// </summary>
public interface IRefreshTokenRepository
{
    Task InsertAsync(RefreshTokenEntity refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// The row for a presented token, live or not. Revoked rows are returned
    /// deliberately - a token that was already rotated away is the signal reuse
    /// detection exists to catch, so the caller has to see it rather than a null.
    /// </summary>
    Task<RefreshTokenEntity?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// One row by id, used to follow a rotation chain: the token a client
    /// presents may already have been rotated into a successor that is still
    /// live, and signing out has to reach that one too.
    /// </summary>
    Task<RefreshTokenEntity?> GetByIdAsync(Ulid refreshTokenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a live token as rotated into its successor. Returns false when the
    /// row was no longer live, which is how a race between two simultaneous
    /// refreshes is settled: the update is conditional on RevokedAt IS NULL, so
    /// only one caller can win it.
    /// </summary>
    Task<bool> TryRotateAsync(Ulid refreshTokenId, Ulid replacedByTokenId, string revokedAt, CancellationToken cancellationToken = default);

    /// <summary>Revokes one live token. Returns false when it was already revoked.</summary>
    Task<bool> TryRevokeAsync(Ulid refreshTokenId, string revokedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes every live token for a user and returns how many were ended. Used
    /// by reuse detection, where one presented copy proves duplication without
    /// saying which holder is the impostor.
    /// </summary>
    Task<int> RevokeAllForUserAsync(Ulid userId, string revokedAt, CancellationToken cancellationToken = default);

    /// <summary>Drops rows past their absolute deadline, which can never be rotated again.</summary>
    Task<int> DeleteExpiredAsync(string utcNow, CancellationToken cancellationToken = default);
}
