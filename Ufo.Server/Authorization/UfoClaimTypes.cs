namespace Ufo.Server.Authorization;

/// <summary>
/// Claim names this application issues itself, alongside the standard
/// <see cref="System.Security.Claims.ClaimTypes"/> ones.
/// </summary>
public static class UfoClaimTypes
{
    /// <summary>
    /// Whether the user administers the installation. Present so the client can
    /// hide controls it would only be refused on; the server re-reads the flag
    /// from the database before allowing a server-scoped write.
    /// </summary>
    public const string IsAdmin = "ufo:is_admin";
}
