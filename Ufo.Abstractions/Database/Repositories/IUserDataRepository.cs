namespace Ufo.Abstractions.Database.Repositories;

/// <summary>
/// Wholesale deletion of one user's data, one kind at a time, behind the
/// Settings page's danger zone.
/// </summary>
/// <remarks>
/// Three operations rather than one "delete everything" because they answer
/// different needs: starting the index over is not the same as forgetting what
/// was flagged on disk, and neither is putting the application back to how it
/// came. Each is scoped to the calling user and touches nobody else's rows.
/// Each is one transaction: the tables involved reference one another, and a
/// failure part-way would otherwise leave a snapshot with no tree or a tag
/// with assignments and no name.
/// </remarks>
public interface IUserDataRepository
{
    /// <summary>
    /// Deletes every snapshot the user has, with the trees, the machine identity
    /// rows nothing else binds, and the labels - which exist only to be put on
    /// snapshots. Answers with how many snapshots went.
    /// </summary>
    Task<int> DeleteSnapshotsAsync(Ulid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes everything the user has marked on paths on disk: flags, ratings,
    /// and the tag vocabulary together with every assignment of it, in the
    /// snapshots included. Answers with how many flags, ratings and tags went.
    /// </summary>
    Task<int> DeleteFileSystemDataAsync(Ulid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the user's settings - theme, rebound shortcuts and locked folder
    /// tabs - so each returns to the build's default. Answers with how many rows
    /// went.
    /// </summary>
    Task<int> DeleteSettingsAsync(Ulid userId, CancellationToken cancellationToken = default);
}
