using Ufo.Server.Models;

namespace Ufo.Server.Services;

/// <summary>
/// Decides whether a single file or folder name is one this host can store.
/// </summary>
/// <remarks>
/// Only ever a name, never a path: everything that reaches the write endpoints
/// combines the caller's name with a folder that has already been through
/// <see cref="IPathGuard"/>, so a name containing a separator would escape that
/// folder. Rejecting separators here is what keeps the guard's answer true.
/// </remarks>
public interface IFileNameValidator
{
    /// <summary>The same rules, in a form the client can apply as the user types.</summary>
    FileNameRules Rules { get; }

    /// <summary>
    /// Whether <paramref name="name"/> may be used as it stands. Callers trim
    /// first - this does not, so that trailing whitespace is judged, not hidden.
    /// </summary>
    /// <param name="rejectionReason">
    /// Set only on a rejection, and then it is one sentence naming the rule that
    /// was broken - it is shown to the user verbatim.
    /// </param>
    bool TryValidate(string? name, out string rejectionReason);
}

public class FileNameValidator : IFileNameValidator
{
    /// <summary>
    /// The limit every mainstream file system shares for one path component.
    /// NTFS, ext4, APFS and exFAT all stop at 255.
    /// </summary>
    public const int MaximumNameLength = 255;

    /// <summary>
    /// DOS device names. Windows still resolves these ahead of the file system
    /// whatever extension follows, so "NUL.txt" is not a file that can be created.
    /// </summary>
    private static readonly string[] WindowsReservedNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    ];

    private readonly char[] _invalidCharacters;
    private readonly bool _rejectsTrailingDotOrSpace;

    public FileNameValidator()
    {
        // The host's own answer, plus both separators unconditionally. On Windows
        // the framework already lists them; on Linux a backslash is a legal name
        // character, but a name carrying one cannot survive a round trip to a
        // Windows machine, and UFO's whole point is indexing across machines.
        _invalidCharacters = Path.GetInvalidFileNameChars()
            .Concat(['/', '\\'])
            .Distinct()
            .ToArray();

        _rejectsTrailingDotOrSpace = OperatingSystem.IsWindows();

        Rules = new FileNameRules
        {
            // Control characters cannot be typed into the name box and would
            // render as nothing, so listing them would only make the message worse.
            InvalidCharacters = new string(_invalidCharacters.Where(character => !char.IsControl(character)).ToArray()),
            ReservedNames = OperatingSystem.IsWindows() ? WindowsReservedNames : [],
            MaximumLength = MaximumNameLength,
            RejectsTrailingDotOrSpace = _rejectsTrailingDotOrSpace,
            IsCaseSensitive = OperatingSystem.IsLinux()
        };
    }

    public FileNameRules Rules { get; }

    public bool TryValidate(string? name, out string rejectionReason)
    {
        rejectionReason = string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            rejectionReason = "A name is required.";
            return false;
        }

        if (name.Length > MaximumNameLength)
        {
            rejectionReason = $"A name may be at most {MaximumNameLength} characters long.";
            return false;
        }

        if (name is "." or "..")
        {
            rejectionReason = "'.' and '..' are reserved - they mean this folder and the one above it.";
            return false;
        }

        // Refused on every platform, not just the ones whose file system says so.
        // Linux will happily store a bell character in a file name; it cannot be
        // typed, renders as nothing in the listing, and does not survive a trip to
        // a Windows machine - which is the trip UFO exists to index.
        if (name.Any(char.IsControl))
        {
            rejectionReason = "A name may not contain control characters.";
            return false;
        }

        var offendingCharacter = name.FirstOrDefault(character => _invalidCharacters.Contains(character));
        if (offendingCharacter != default)
        {
            rejectionReason = $"A name may not contain the character '{offendingCharacter}'.";
            return false;
        }

        if (_rejectsTrailingDotOrSpace && (name.EndsWith('.') || name.EndsWith(' ')))
        {
            rejectionReason = "A name may not end with a dot or a space.";
            return false;
        }

        if (IsReservedDeviceName(name))
        {
            // Reported with the name as typed rather than the stem, since that is
            // what the user is looking at.
            rejectionReason = $"'{name}' is a name Windows reserves for a device.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Whether the name is a DOS device name. The extension is ignored, because
    /// Windows ignores it too - the check is on everything before the first dot.
    /// </summary>
    private bool IsReservedDeviceName(string name)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var dotIndex = name.IndexOf('.');
        var stem = dotIndex < 0 ? name : name[..dotIndex];

        return WindowsReservedNames.Contains(stem, StringComparer.OrdinalIgnoreCase);
    }
}
