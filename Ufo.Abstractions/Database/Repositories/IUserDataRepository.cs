namespace Ufo.Abstractions.Database.Repositories;

/// <summary>How many rows of each kind one Delete all data removed.</summary>
/// <param name="Snapshots">Snapshots, each with its tree and machine identity.</param>
/// <param name="FileSystemItems">Flags, ratings and tags together.</param>
/// <param name="Labels">Labels, the vocabulary snapshots are filed under.</param>
/// <param name="Settings">Theme, rebound shortcuts and locked folder tabs together.</param>
public record UserDataDeletionCounts(int Snapshots, int FileSystemItems, int Labels, int Settings);

/// <summary>
/// Wholesale deletion of one user's data behind the Settings page's danger
/// zone: one kind at a time, or all of it at once.
/// </summary>
/// <remarks>
/// Separate kinds because they answer different needs: starting the index over
/// is not the same as forgetting what was flagged on disk, and neither is
/// putting the application back to how it came. Each is scoped to the calling
/// user and touches nobody else's rows. Each is one transaction, and so is
/// deleting everything: the tables reference one another, and a failure
/// part-way would otherwise leave a snapshot with no tree, or half an account.
/// The account itself is never deleted here, so the user stays signed in.
/// </remarks>
public interface IUserDataRepository
{
    /// <summary>
    /// Deletes every snapshot the user has, with the trees and the machine
    /// identity rows nothing else binds. The labels stay, with nothing filed
    /// under them. Answers with how many snapshots went.
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

    /// <summary>
    /// Deletes all of the user's data in one transaction: snapshots, file system
    /// data, labels and settings. The account remains. Either everything goes
    /// or, on a failure, nothing does.
    /// </summary>
    Task<UserDataDeletionCounts> DeleteAllAsync(Ulid userId, CancellationToken cancellationToken = default);
}
