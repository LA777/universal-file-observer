using System.Globalization;
using System.Reflection;

namespace Ufo.Server.Services;

/// <summary>
/// The version of the running build, as three segments - major.minor.patch.
/// </summary>
public interface IApplicationVersionService
{
    string Version { get; }
}

/// <summary>
/// Reads the version back out of the assembly metadata, which the build stamps
/// from the <c>&lt;Version&gt;</c> element in <c>Ufo.Server.csproj</c>.
/// </summary>
/// <remarks>
/// Reading it rather than declaring it is what keeps the number in one place: a
/// constant here would be a second thing to remember on a release, and the two
/// would eventually disagree. See <c>_docs/AI_VERSIONING.md</c>.
/// </remarks>
public sealed class ApplicationVersionService : IApplicationVersionService
{
    /// <summary>
    /// Reported when the assembly carries no usable version - a build whose
    /// metadata was stripped. No release is ever numbered 0.0.0, so it cannot be
    /// mistaken for one.
    /// </summary>
    public const string UnknownVersion = "0.0.0";

    /// <summary>
    /// The running build's version. Static because the entry points log it before
    /// a service container exists, and they must report what the API reports.
    /// </summary>
    public static string Current { get; } = ReadVersion(typeof(ApplicationVersionService).Assembly);

    public string Version => Current;

    internal static string ReadVersion(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        // AssemblyInformationalVersion is the one stamped verbatim from
        // <Version>; the others are normalised to four segments by the SDK.
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return ToThreeSegments(informationalVersion)
            ?? ToThreeSegments(assembly.GetName().Version?.ToString())
            ?? UnknownVersion;
    }

    /// <summary>
    /// Reduces whatever the build stamped to major.minor.patch, or answers
    /// <see langword="null"/> when it is not a version at all.
    /// </summary>
    /// <remarks>
    /// Trims the "+commit" build metadata and "-beta" prerelease suffix a
    /// NuGet-style version may carry, and the fourth segment an AssemblyVersion
    /// always does, so that every caller sees the three segments the versioning
    /// scheme promises.
    /// </remarks>
    internal static string? ToThreeSegments(string? rawVersion)
    {
        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            return null;
        }

        var withoutSuffixes = rawVersion.Split('+', 2)[0].Split('-', 2)[0].Trim();
        var segments = withoutSuffixes.Split('.');
        if (segments.Length < 3)
        {
            return null;
        }

        var significantSegments = segments.Take(3).ToArray();
        foreach (var segment in significantSegments)
        {
            if (!int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                return null;
            }
        }

        return string.Join('.', significantSegments);
    }
}
