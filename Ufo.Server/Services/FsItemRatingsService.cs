using Ufo.Abstractions;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.Requests;

namespace Ufo.Server.Services;

public interface IFsItemRatingsService
{
    /// <summary>
    /// The ratings this user has set, by path, with anything the server may no
    /// longer read left out.
    /// </summary>
    Task<IReadOnlyDictionary<string, int>> GetRatingsAsync(Ulid userId, CancellationToken cancellationToken);

    /// <summary>Sets or clears the rating for a set of files and folders.</summary>
    Task<ServerResult> SetRatingsAsync(FsItemRatingsRequest request, Ulid userId, CancellationToken cancellationToken);
}

public class FsItemRatingsService : IFsItemRatingsService
{
    /// <summary>The top of the scale. Zero is unrated rather than the bottom of it.</summary>
    public const int MaximumRating = 10;

    /// <summary>
    /// As many paths as one request may name. Generous enough for selecting a
    /// whole listing, and there so one call cannot fill the table.
    /// </summary>
    private const int MaximumPathsPerRequest = 1000;

    /// <summary>
    /// Longest path a row will hold. Enforced here rather than left to the
    /// entity's <c>[MaxLength]</c>, which is a sqlite-net attribute the raw
    /// Dapper SQL never consults - the column is plain TEXT.
    /// </summary>
    private const int MaximumPathLength = 4096;

    private readonly IFsItemRatingsRepository _fsItemRatingsRepository;
    private readonly IPathGuard _pathGuard;
    private readonly ILogger<FsItemRatingsService> _logger;

    public FsItemRatingsService(
        IFsItemRatingsRepository fsItemRatingsRepository,
        IPathGuard pathGuard,
        ILogger<FsItemRatingsService> logger)
    {
        _fsItemRatingsRepository = fsItemRatingsRepository
            ?? throw new ArgumentNullException(nameof(fsItemRatingsRepository));
        _pathGuard = pathGuard ?? throw new ArgumentNullException(nameof(pathGuard));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyDictionary<string, int>> GetRatingsAsync(
        Ulid userId,
        CancellationToken cancellationToken)
    {
        var ratings = await _fsItemRatingsRepository.GetFsItemRatingsAsync(userId, cancellationToken);

        var byPath = new Dictionary<string, int>(PathComparer);

        foreach (var rating in ratings)
        {
            // Re-checked on the way out as well as the way in: the allow-list is
            // configuration and can be tightened between sessions, and a rating
            // set while the server was unrestricted must not name a path it may
            // no longer open.
            if (_pathGuard.TryResolveQuietly(rating.FullPath, out _))
            {
                byPath[rating.FullPath] = rating.Rating;
            }
        }

        return byPath;
    }

    public async Task<ServerResult> SetRatingsAsync(
        FsItemRatingsRequest request,
        Ulid userId,
        CancellationToken cancellationToken)
    {
        if (request?.FullPaths is not { Count: > 0 })
        {
            return Rejected("No files or folders were given.");
        }

        if (request.FullPaths.Count > MaximumPathsPerRequest)
        {
            return Rejected($"At most {MaximumPathsPerRequest} items can be rated in one go.");
        }

        if (request.Rating is < 0 or > MaximumRating)
        {
            return Rejected($"A rating must be between 0 and {MaximumRating}.");
        }

        var isClearing = request.Rating == 0;
        var pathsToWrite = new List<string>();

        foreach (var requestedPath in request.FullPaths)
        {
            if (string.IsNullOrWhiteSpace(requestedPath) || requestedPath.Length > MaximumPathLength)
            {
                return Rejected("A path was empty or longer than this server will store.");
            }

            if (isClearing)
            {
                // Clearing skips the guard on purpose, as clearing a flag does: a
                // path that has left the allow-list still has a row, and refusing
                // to remove it would leave the user a rating they can neither see
                // nor change. The resolved spelling goes too, so a row written
                // under one can still be let go of.
                pathsToWrite.Add(requestedPath);

                if (_pathGuard.TryResolveQuietly(requestedPath, out var resolvedForClear)
                    && resolvedForClear != requestedPath)
                {
                    pathsToWrite.Add(resolvedForClear);
                }

                continue;
            }

            // The guard authorises; it does not decide the key. Its answer is
            // trimmed and symbolic-link resolved, and nothing else in the
            // application resolves anything - so a row keyed on it would be one
            // the listings and the snapshot walk could never find again.
            if (!_pathGuard.TryResolve(requestedPath, out _))
            {
                return Rejected($"'{requestedPath}' is not something this server is allowed to open.");
            }

            pathsToWrite.Add(requestedPath);
        }

        _logger.LogInformation(
            "SetRatingsAsync - UserId: {UserId}, Count: {Count}, Rating: {Rating}",
            userId,
            pathsToWrite.Count,
            request.Rating);

        return await _fsItemRatingsRepository.SetFsItemRatingsAsync(
            userId,
            pathsToWrite,
            request.Rating,
            cancellationToken);
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    private static ServerResult Rejected(string message) =>
        new()
        {
            ActionName = "Setting Ratings.",
            Result = Result.Error,
            Priority = ActionPriority.Highest,
            Message = message
        };
}
