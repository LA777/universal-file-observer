using Ufo.Abstractions.Database.Entities;

namespace Ufo.Abstractions.Database.Repositories;

public interface IFsItemRatingsRepository
{
    /// <summary>
    /// Every rating this user has set. Empty when they have rated nothing, which
    /// is the default and not an error.
    /// </summary>
    Task<IReadOnlyList<FsItemRatingEntity>> GetFsItemRatingsAsync(
        Ulid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the rating for each of <paramref name="fullPaths"/>, and touches
    /// nothing else. A rating of zero clears them instead.
    /// </summary>
    /// <remarks>
    /// Named paths only, never a replace over the user's whole set - the same
    /// reasoning as flags and folder tabs: a caller that failed to read the
    /// others believes there are none, and would clear the lot.
    /// </remarks>
    Task<ServerResult> SetFsItemRatingsAsync(
        Ulid userId,
        IReadOnlyList<string> fullPaths,
        int rating,
        CancellationToken cancellationToken = default);
}
