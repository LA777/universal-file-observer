namespace Ufo.Abstractions.Options;

public class JwtOptions
{
    /// <summary>
    /// The lifetime applied when configuration names none. Deliberately short:
    /// an access token is a stateless JWT, so nothing can withdraw one early and
    /// it is authority over that account until it expires, wherever it has been
    /// copied to. Sessions outlast it through refresh tokens, which are written
    /// down and therefore revocable - see <see cref="RefreshTokenLifetimeDays"/>.
    /// </summary>
    public const int DefaultTokenLifetimeMinutes = 30;

    /// <summary>
    /// The default sliding refresh window. Long enough that everyday use never
    /// meets it, short enough that an abandoned session dies on its own.
    /// </summary>
    public const int DefaultRefreshTokenLifetimeDays = 14;

    /// <summary>
    /// The default cap on a single sign-in, however actively it is used.
    /// </summary>
    public const int DefaultRefreshTokenAbsoluteLifetimeDays = 30;

    public string Key { get; set; } = string.Empty;

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// How long an issued token stays valid, in minutes
    /// (<c>JWT:TokenLifetimeMinutes</c>, or <c>JWT__TokenLifetimeMinutes</c> for a
    /// deployment).
    ///
    /// Raising this extends how long a leaked or stale access token keeps working,
    /// and there is no way to end one sooner. Lowering it costs a round trip
    /// rather than a session: the client refreshes on the first refused request
    /// and replays it. Must be greater than zero - the host refuses to start
    /// otherwise, rather than issuing tokens that are already expired.
    /// </summary>
    public int TokenLifetimeMinutes { get; set; } = DefaultTokenLifetimeMinutes;

    /// <summary>The configured lifetime, ready to add to an issuing time.</summary>
    public TimeSpan TokenLifetime => TimeSpan.FromMinutes(TokenLifetimeMinutes);

    /// <summary>
    /// How long a refresh token stays usable without being used, in days
    /// (<c>JWT:RefreshTokenLifetimeDays</c>). This deadline slides: every rotation
    /// issues a successor with a fresh window, so it measures idleness rather
    /// than age - stop using the app for this long and the session is over.
    /// </summary>
    public int RefreshTokenLifetimeDays { get; set; } = DefaultRefreshTokenLifetimeDays;

    /// <summary>
    /// The longest a single sign-in may last however actively it is used, in days
    /// (<c>JWT:RefreshTokenAbsoluteLifetimeDays</c>). Rotation carries this
    /// deadline forward unchanged, so without it a session used daily would never
    /// ask for the password again. Must be at least
    /// <see cref="RefreshTokenLifetimeDays"/>, or the sliding window could never
    /// be reached.
    /// </summary>
    public int RefreshTokenAbsoluteLifetimeDays { get; set; } = DefaultRefreshTokenAbsoluteLifetimeDays;

    /// <summary>The sliding refresh window, ready to add to an issuing time.</summary>
    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(RefreshTokenLifetimeDays);

    /// <summary>The absolute cap on one sign-in, ready to add to an issuing time.</summary>
    public TimeSpan RefreshTokenAbsoluteLifetime => TimeSpan.FromDays(RefreshTokenAbsoluteLifetimeDays);
}
