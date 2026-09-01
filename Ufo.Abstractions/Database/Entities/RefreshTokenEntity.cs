using Cysharp.Serialization.Json;
using SQLite;
using SQLiteNetExtensions.Attributes;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

/// <summary>
/// One issued refresh token: the state that makes a session revocable.
/// </summary>
/// <remarks>
/// <para>
/// The access token is a JWT and stays stateless - the server validates the
/// signature and asks nothing else. That is what makes it unrevocable, which is
/// why it is short-lived. This row is the opposite half of the trade: long-lived,
/// but written down, so ending a session is a single UPDATE rather than something
/// nobody can do.
/// </para>
/// <para>
/// <see cref="TokenHash"/> holds SHA-256 of the token, never the token itself.
/// This database is not encrypted, and a stored refresh token would be a
/// ready-made credential for every account in it; a hash is enough to recognise
/// the one the client presents. It is the token's identity as far as lookup is
/// concerned, hence UNIQUE.
/// </para>
/// <para>
/// Two deadlines, because they answer different questions.
/// <see cref="ExpiresAt"/> slides forward on every rotation and asks "is this
/// session still in use?"; <see cref="AbsoluteExpiresAt"/> never moves and asks
/// "how long may one sign-in last at most?" - without it, a session that is used
/// daily would never require the password again.
/// </para>
/// </remarks>
[Table("RefreshTokens")]
public class RefreshTokenEntity : EntityBase
{
    [JsonConverter(typeof(UlidJsonConverter))]
    [ForeignKey(typeof(UserEntity))]
    public Ulid UserId { get; set; }

    /// <summary>Lowercase hex SHA-256 of the issued token. Never the token.</summary>
    [NotNull]
    [MaxLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Round-trip ("o") formatted UTC instant.</summary>
    [MaxLength(64)]
    public string CreatedAt { get; set; } = string.Empty;

    /// <summary>
    /// Round-trip ("o") formatted UTC instant. Slides: each rotation issues a
    /// successor with a fresh window.
    /// </summary>
    [MaxLength(64)]
    public string ExpiresAt { get; set; } = string.Empty;

    /// <summary>
    /// Round-trip ("o") formatted UTC instant, carried unchanged across every
    /// rotation of the same sign-in. A successor is never given a longer life
    /// than this.
    /// </summary>
    [MaxLength(64)]
    public string AbsoluteExpiresAt { get; set; } = string.Empty;

    /// <summary>
    /// When the token stopped being usable - rotated away, signed out, or caught
    /// by reuse detection. Null while the token is live, which is what the
    /// conditional UPDATE in rotation tests to settle a race between two
    /// simultaneous refreshes.
    /// </summary>
    [MaxLength(64)]
    public string? RevokedAt { get; set; }

    /// <summary>
    /// The successor issued when this token was rotated, or null when it was
    /// revoked for any other reason. Kept so a chain can be followed back when
    /// working out what happened to a session.
    /// </summary>
    [JsonConverter(typeof(UlidJsonConverter))]
    public Ulid? ReplacedByTokenId { get; set; }
}
