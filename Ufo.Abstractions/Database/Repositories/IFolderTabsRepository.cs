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
    /// Replaces one panel's locked tabs with exactly <paramref name="folderTabs"/>,
    /// leaving the other panel's alone.
    /// </summary>
    /// <remarks>
    /// Replace rather than merge, in one transaction. Locking, unlocking,
    /// closing and reordering are then the same operation, so there is no second
    /// code path that could disagree with this one about what a panel's tabs are.
    /// Scoped to the panel because the two panes are saved independently and a
    /// whole-account replace would have each one deleting the other's tabs.
    /// </remarks>
    Task<ServerResult> SaveFolderTabsAsync(
        IReadOnlyList<FolderTabEntity> folderTabs,
        Ulid userId,
        string panelId,
        CancellationToken cancellationToken = default);
}
