namespace Ufo.Server.Models;

/// <summary>
/// What the host will accept as a file or folder name.
/// </summary>
/// <remarks>
/// Sent to the client so it can reject a bad name while the user is still typing
/// it, instead of letting them finish and discover the answer from a failed
/// request. The rules are the host's, not a lowest common denominator: a colon is
/// fine in a Linux file name and impossible in a Windows one, and hard-coding
/// either set in the browser would be wrong on the other platform. The server
/// re-checks everything regardless - this is a courtesy, not the enforcement.
/// </remarks>
public class FileNameRules
{
    /// <summary>
    /// Characters a name may not contain, as one string for the client to scan.
    /// Control characters are left out: they cannot be typed, and printing them
    /// in an error message would produce nothing legible.
    /// </summary>
    public string InvalidCharacters { get; set; } = string.Empty;

    /// <summary>
    /// Names the host reserves whatever the extension - the Windows device names,
    /// and empty everywhere else.
    /// </summary>
    public IList<string> ReservedNames { get; set; } = [];

    /// <summary>Longest single name segment the host accepts.</summary>
    public int MaximumLength { get; set; }

    /// <summary>True where a name may not end in a dot or a space (Windows).</summary>
    public bool RejectsTrailingDotOrSpace { get; set; }

    /// <summary>
    /// Whether two names differing only in case are two different entries. It
    /// decides one thing on the client: whether typing "readme.md" next to an
    /// existing "README.md" is a collision to warn about or a second file.
    /// </summary>
    public bool IsCaseSensitive { get; set; }
}
