using Ufo.Abstractions.Database.Entities;

namespace Ufo.Abstractions.Database.Repositories;

public interface IFolderTabsRepository
{
    /// <summary>
    /// Every locked tab this user has, across both panels, in display order.
    /// Empty when they have locked none, which is the normal case.
    /// </summary>
    Task<IReadOnlyList<FolderTabEntity>> GetFolderTabsAsync(
        Ulid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks one folder in one panel, or does nothing if it is locked already.
    /// </summary>
    /// <remarks>
    /// One row, never a panel's whole set. A replace would be driven by what the
    /// caller believes the other tabs to be, and a caller that failed to read
    /// them believes there are none - so the next lock would delete every tab
    /// the user had kept. This cannot, however wrong the caller is.
    /// </remarks>
    Task<ServerResult> LockFolderTabAsync(
        FolderTabEntity folderTab,
        CancellationToken cancellationToken = default);

    /// <summary>Unlocks one folder. Absent is the desired end state, so it is not an error.</summary>
    Task<ServerResult> UnlockFolderTabAsync(
        Ulid userId,
        string panelId,
        string folderPath,
        CancellationToken cancellationToken = default);
}
