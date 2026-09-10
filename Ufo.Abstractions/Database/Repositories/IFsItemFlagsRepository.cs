using Ufo.Abstractions.Database.Entities;

namespace Ufo.Abstractions.Database.Repositories;

public interface IFsItemFlagsRepository
{
    /// <summary>
    /// Every path this user has flagged. Empty when they have flagged nothing,
    /// which is the default and not an error.
    /// </summary>
    Task<IReadOnlyList<FsItemFlagEntity>> GetFsItemFlagsAsync(
        Ulid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Turns the flag on or off for each of <paramref name="fullPaths"/>, and
    /// touches nothing else.
    /// </summary>
    /// <remarks>
    /// Named paths only, never a replace over the user's whole set. A replace is
    /// driven by what the caller believes the other flags to be, and a caller
    /// that failed to read them believes there are none - which would clear the
    /// lot on the next click.
    /// </remarks>
    Task<ServerResult> SetFsItemFlagsAsync(
        Ulid userId,
        IReadOnlyList<string> fullPaths,
        bool isFlagEnabled,
        CancellationToken cancellationToken = default);
}
