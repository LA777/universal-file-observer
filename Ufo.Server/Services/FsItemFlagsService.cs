using Ufo.Abstractions;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.Requests;

namespace Ufo.Server.Services;

public interface IFsItemFlagsService
{
    /// <summary>
    /// The paths this user has flagged, with anything the server may no longer
    /// read left out.
    /// </summary>
    Task<IReadOnlyList<string>> GetFlaggedPathsAsync(Ulid userId, CancellationToken cancellationToken);

    /// <summary>Turns the flag on or off for a set of files and folders.</summary>
    Task<ServerResult> SetFlagsAsync(FsItemFlagsRequest request, Ulid userId, CancellationToken cancellationToken);
}

public class FsItemFlagsService : IFsItemFlagsService
{
    /// <summary>
    /// As many paths as one request may name. Generous enough for selecting a
    /// whole listing, and there so one call cannot fill the table.
    /// </summary>
    private const int MaximumPathsPerRequest = 1000;

    /// <summary>
    /// Longest path a row will hold.
    /// </summary>
    /// <remarks>
    /// Enforced here rather than left to the entity's <c>[MaxLength]</c>, which is
    /// a sqlite-net attribute that the raw Dapper SQL never consults - the column
    /// is plain TEXT and would take a megabyte as happily as a path. Comfortably
    /// above every platform's real limit, so it only ever catches nonsense.
    /// </remarks>
    private const int MaximumPathLength = 4096;

    private readonly IFsItemFlagsRepository _fsItemFlagsRepository;
    private readonly IPathGuard _pathGuard;
    private readonly ILogger<FsItemFlagsService> _logger;

    public FsItemFlagsService(
        IFsItemFlagsRepository fsItemFlagsRepository,
        IPathGuard pathGuard,
        ILogger<FsItemFlagsService> logger)
    {
        _fsItemFlagsRepository = fsItemFlagsRepository ?? throw new ArgumentNullException(nameof(fsItemFlagsRepository));
        _pathGuard = pathGuard ?? throw new ArgumentNullException(nameof(pathGuard));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<string>> GetFlaggedPathsAsync(Ulid userId, CancellationToken cancellationToken)
    {
        var flags = await _fsItemFlagsRepository.GetFsItemFlagsAsync(userId, cancellationToken);

        return flags
            // Re-checked on the way out as well as the way in: the allow-list is
            // configuration and can be tightened between sessions, and a flag set
            // while the server was unrestricted must not name a path it may no
            // longer open.
            .Where(flag => _pathGuard.TryResolveQuietly(flag.FullPath, out _))
            .Select(flag => flag.FullPath)
            .ToList();
    }

    public async Task<ServerResult> SetFlagsAsync(
        FsItemFlagsRequest request,
        Ulid userId,
        CancellationToken cancellationToken)
    {
        if (request?.FullPaths is not { Count: > 0 })
        {
            return Rejected("No files or folders were given.");
        }

        if (request.FullPaths.Count > MaximumPathsPerRequest)
        {
            return Rejected($"At most {MaximumPathsPerRequest} items can be flagged in one go.");
        }

        var pathsToWrite = new List<string>();

        foreach (var requestedPath in request.FullPaths)
        {
            // Clearing a flag skips the guard on purpose - the same reasoning as
            // unlocking a folder tab. A path that has gone out of the allow-list
            // still has a row, and refusing to remove it would leave the user a
            // flag they cannot see and cannot clear.
            if (!request.IsFlagEnabled)
            {
                // Clearing skips the guard on purpose, the same reasoning as
                // unlocking a folder tab: a path that has left the allow-list
                // still has a row, and refusing to remove it would leave the user
                // a flag they can neither see nor clear. The resolved spelling is
                // sent too, so a row written before flags were keyed on the
                // unresolved path can still be let go of.
                pathsToWrite.Add(requestedPath);

                if (_pathGuard.TryResolveQuietly(requestedPath, out var resolvedForClear)
                    && resolvedForClear != requestedPath)
                {
                    pathsToWrite.Add(resolvedForClear);
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(requestedPath) || requestedPath.Length > MaximumPathLength)
            {
                return Rejected("A path was empty or longer than this server will store.");
            }

            // The guard authorises; it does not decide the key. Its answer is
            // trimmed and symbolic-link resolved, and neither the listings nor the
            // snapshot walk resolve anything - they hand back whatever enumeration
            // gave them. Storing the resolved form would key the row on a path the
            // rest of the application never produces, so the marker would never
            // appear and the flag could never be found again. Nothing opens a file
            // from this string, so keeping it unresolved costs no containment: the
            // guard has already refused anything that leaves the allowed roots.
            if (!_pathGuard.TryResolve(requestedPath, out _))
            {
                return Rejected($"'{requestedPath}' is not something this server is allowed to open.");
            }

            pathsToWrite.Add(requestedPath);
        }

        _logger.LogInformation(
            "SetFlagsAsync - UserId: {UserId}, Count: {Count}, Enabled: {IsFlagEnabled}",
            userId,
            pathsToWrite.Count,
            request.IsFlagEnabled);

        return await _fsItemFlagsRepository.SetFsItemFlagsAsync(
            userId,
            pathsToWrite,
            request.IsFlagEnabled,
            cancellationToken);
    }

    private static ServerResult Rejected(string message) =>
        new()
        {
            ActionName = "Setting Flags.",
            Result = Result.Error,
            Priority = ActionPriority.Highest,
            Message = message
        };
}
