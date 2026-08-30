using Microsoft.Extensions.Options;
using Ufo.Abstractions.Options;

namespace Ufo.Server.Services;

/// <summary>
/// Canonicalises a caller-supplied file-system path and decides whether the
/// application is permitted to read it.
/// </summary>
/// <remarks>
/// The desktop application deliberately browses the whole machine, so an empty
/// <see cref="UfoHostOptions.AllowedRoots"/> means "allow everything" and
/// preserves that behaviour exactly. A network-reachable container sets
/// <c>Ufo__AllowedRoots</c>, without which the browse, search and video
/// endpoints amount to an arbitrary file read API.
/// </remarks>
public interface IPathGuard
{
    /// <summary>Configured roots, or empty when access is unrestricted.</summary>
    IReadOnlyList<string> AllowedRoots { get; }

    /// <summary>True when a non-empty allow-list is in force.</summary>
    bool IsRestricted { get; }

    /// <summary>
    /// Canonicalises <paramref name="path"/> (resolving relative segments and
    /// symbolic links) and reports whether it is readable. A rejection is logged
    /// as a warning, so this is the check for a path a caller asked for by name.
    /// </summary>
    /// <param name="resolvedPath">
    /// The canonical path to use for all subsequent file-system access. Callers
    /// must use this rather than the original input.
    /// </param>
    bool TryResolve(string? path, out string resolvedPath);

    /// <summary>
    /// As <see cref="TryResolve"/>, but a rejection is an expected outcome rather
    /// than a warning.
    /// </summary>
    /// <remarks>
    /// For paths the application reached by itself - entries returned by
    /// enumeration, or a parent folder being tested before it is offered as
    /// somewhere to navigate to. Logging those at warning level turns one ordinary
    /// listing into a burst of alarming lines, which is how a log stops being
    /// read.
    /// </remarks>
    bool TryResolveQuietly(string? path, out string resolvedPath);

    /// <summary>
    /// Whether an entry just enumerated from a directory that has <b>already</b>
    /// been resolved and allowed may itself be read.
    /// </summary>
    /// <remarks>
    /// The cheap form of the same question. Because the containing directory is
    /// known to resolve inside an allowed root, an entry that is not itself a
    /// symbolic link cannot lead out of one, and the ancestor chain does not need
    /// walking again - which matters, because the callers run this over every
    /// entry of a tree.
    /// </remarks>
    bool IsAllowedChild(string childPath);
}

public class PathGuard : IPathGuard
{
    /// <summary>
    /// Upper bound on symbolic links followed while canonicalising one path.
    /// Exceeding it is how a link cycle is detected. Matches the traditional
    /// SYMLOOP_MAX of 40.
    /// </summary>
    private const int MaximumSymbolicLinkHops = 40;

    private readonly ILogger<PathGuard> _logger;
    private readonly string[] _allowedRoots;

    public PathGuard(ILogger<PathGuard> logger, IOptions<UfoHostOptions> hostOptions)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(hostOptions);

        _allowedRoots = hostOptions.Value.AllowedRoots
            .Where(configuredRoot => !string.IsNullOrWhiteSpace(configuredRoot))
            .Select(NormaliseRoot)
            .Where(normalisedRoot => normalisedRoot.Length > 0)
            .Distinct(PathComparer)
            .ToArray();

        if (_allowedRoots.Length > 0)
        {
            _logger.LogInformation("File-system access is restricted to: {AllowedRoots}", string.Join(", ", _allowedRoots));
        }
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    private static StringComparison PathComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    public IReadOnlyList<string> AllowedRoots => _allowedRoots;

    public bool IsRestricted => _allowedRoots.Length > 0;

    public bool TryResolve(string? path, out string resolvedPath) =>
        TryResolve(path, out resolvedPath, logRejectionAsWarning: true);

    public bool TryResolveQuietly(string? path, out string resolvedPath) =>
        TryResolve(path, out resolvedPath, logRejectionAsWarning: false);

    public bool IsAllowedChild(string childPath)
    {
        if (!IsRestricted)
        {
            return true;
        }

        // One directory-entry read, against the whole ancestor walk TryResolve does.
        if (ReadLinkTarget(childPath) is null)
        {
            return true;
        }

        return TryResolveQuietly(childPath, out _);
    }

    private bool TryResolve(string? path, out string resolvedPath, bool logRejectionAsWarning)
    {
        resolvedPath = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string canonicalPath;
        try
        {
            canonicalPath = Path.GetFullPath(path.Trim());
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            _logger.LogWarning(exception, "Rejected malformed path {Path}.", path);
            return false;
        }

        if (!TryResolveRealPath(canonicalPath, out var realPath))
        {
            LogRejection(logRejectionAsWarning, "Rejected path {Path} - symbolic links could not be resolved.", canonicalPath);
            return false;
        }

        if (!IsRestricted)
        {
            resolvedPath = realPath;
            return true;
        }

        var isAllowed = _allowedRoots.Any(allowedRoot => IsWithin(realPath, allowedRoot));
        if (!isAllowed)
        {
            LogRejection(logRejectionAsWarning, "Rejected path {Path} - outside the configured allowed roots.", realPath);
            return false;
        }

        resolvedPath = realPath;
        return true;
    }

    private void LogRejection(bool logRejectionAsWarning, string messageTemplate, string path)
    {
        if (logRejectionAsWarning)
        {
            _logger.LogWarning(messageTemplate, path);
        }
        else
        {
            _logger.LogDebug(messageTemplate, path);
        }
    }

    /// <summary>
    /// Resolves symbolic links at <b>every</b> component of the path, not just the
    /// last one, producing the true physical path.
    /// </summary>
    /// <remarks>
    /// Resolving only the final component is not enough, and the difference is a
    /// read of any file on the machine. Given "/library/link/passwd" where
    /// "/library/link" is a symlink to "/etc", <see cref="Path.GetFullPath(string)"/>
    /// leaves the path untouched (it is purely lexical) and
    /// <see cref="File.ResolveLinkTarget(string, bool)"/> returns null, because
    /// "passwd" itself is not a link. The path then compares as being inside
    /// "/library" and the file is served.
    /// </remarks>
    /// <returns>
    /// <c>false</c> when the link chain is longer than
    /// <see cref="MaximumSymbolicLinkHops"/>, which is how a link cycle ends.
    /// </returns>
    private bool TryResolveRealPath(string canonicalPath, out string realPath)
    {
        realPath = string.Empty;

        var pathRoot = Path.GetPathRoot(canonicalPath);
        if (string.IsNullOrEmpty(pathRoot))
        {
            return false;
        }

        var pendingSegments = new Queue<string>(SplitSegments(canonicalPath[pathRoot.Length..]));
        var resolvedSoFar = pathRoot;
        var hops = 0;

        while (pendingSegments.Count > 0)
        {
            var segment = pendingSegments.Dequeue();

            switch (segment)
            {
                case ".":
                    continue;
                case "..":
                    // A link target can reintroduce these after GetFullPath ran.
                    resolvedSoFar = Path.GetDirectoryName(resolvedSoFar) is { Length: > 0 } parent ? parent : pathRoot;
                    continue;
            }

            var candidatePath = Path.Combine(resolvedSoFar, segment);
            var linkTarget = ReadLinkTarget(candidatePath);

            if (linkTarget == null)
            {
                resolvedSoFar = candidatePath;
                continue;
            }

            if (++hops > MaximumSymbolicLinkHops)
            {
                return false;
            }

            // The target's own ancestors may be links too, so its segments go back
            // through the same walk rather than being trusted as resolved.
            var targetRoot = Path.GetPathRoot(linkTarget);
            var isAbsoluteTarget = !string.IsNullOrEmpty(targetRoot);

            var targetSegments = isAbsoluteTarget
                ? SplitSegments(linkTarget[targetRoot!.Length..])
                : SplitSegments(linkTarget);

            pendingSegments = new Queue<string>(targetSegments.Concat(pendingSegments));
            resolvedSoFar = isAbsoluteTarget ? targetRoot! : resolvedSoFar;
        }

        realPath = resolvedSoFar;
        return true;
    }

    /// <summary>
    /// The immediate symlink target of <paramref name="path"/>, or <c>null</c>
    /// when it is not a link (or cannot be inspected).
    /// </summary>
    private string? ReadLinkTarget(string path)
    {
        try
        {
            // LinkTarget is read from the directory entry, so it is populated for a
            // dangling link too - which must still be followed, since the target
            // can come into existence between this check and the read.
            FileSystemInfo entry = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);

            return entry.LinkTarget;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogDebug(exception, "Could not inspect {Path} for a link target.", path);
            return null;
        }
    }

    private static IEnumerable<string> SplitSegments(string relativePath) =>
        relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Puts a configured root through the same link resolution as the paths it is
    /// compared against.
    /// </summary>
    /// <remarks>
    /// A root that is itself a symlink would otherwise never match: configure
    /// "/tmp" on macOS, where it links to "/private/tmp", and every resolved path
    /// under it compares as being outside the allow-list.
    /// </remarks>
    private string NormaliseRoot(string configuredRoot)
    {
        try
        {
            var absoluteRoot = Path.GetFullPath(configuredRoot.Trim());

            return Path.TrimEndingDirectorySeparator(
                TryResolveRealPath(absoluteRoot, out var realRoot) ? realRoot : absoluteRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            _logger.LogWarning(exception, "Ignoring malformed allowed root {ConfiguredRoot}.", configuredRoot);
            return string.Empty;
        }
    }

    private static bool IsWithin(string candidatePath, string root)
    {
        if (candidatePath.Equals(root, PathComparison))
        {
            return true;
        }

        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return candidatePath.StartsWith(rootWithSeparator, PathComparison);
    }
}
