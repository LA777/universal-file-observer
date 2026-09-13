using Ufo.Abstractions.Database.Entities;

namespace Ufo.Abstractions.Database.Repositories;

public interface ITagsRepository
{
    /// <summary>The user's tags, by name. Their vocabulary, not their assignments.</summary>
    Task<IReadOnlyList<TagEntity>> GetTagsAsync(Ulid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a tag, or returns the existing one when the name is already taken.
    /// </summary>
    /// <remarks>
    /// A tag is known to the user by its name, so asking for one that exists is
    /// asking for the one that exists - not an error, and not a second tag with
    /// the same name in a different colour.
    /// </remarks>
    Task<TagEntity> GetOrCreateTagAsync(TagEntity tag, CancellationToken cancellationToken = default);

    /// <summary>Every tag assignment on disk, tag id and path together.</summary>
    Task<IReadOnlyList<FsItemTagEntity>> GetFsItemTagsAsync(
        Ulid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts one tag on, or takes it off, each of <paramref name="fullPaths"/>,
    /// and touches nothing else.
    /// </summary>
    /// <remarks>
    /// Named paths and one tag, never a replace over everything the user has
    /// tagged - the same reasoning as flags, ratings and folder tabs: a caller
    /// that failed to read the rest believes there is nothing there.
    /// </remarks>
    Task<ServerResult> SetFsItemTagAsync(
        Ulid tagId,
        IReadOnlyList<string> fullPaths,
        bool isApplied,
        CancellationToken cancellationToken = default);
}
