namespace Ufo.Abstractions.Responses;

/// <summary>
/// Answer to <c>GET /api/version</c>.
/// </summary>
/// <remarks>
/// An object rather than a bare string so the About tab keeps parsing the same
/// shape once the response grows a second field.
/// </remarks>
public class VersionResponse
{
    /// <summary>Three segments - major.minor.patch.</summary>
    public string Version { get; set; } = string.Empty;
}
