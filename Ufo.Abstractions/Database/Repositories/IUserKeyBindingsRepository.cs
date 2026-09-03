using Ufo.Abstractions.Database.Entities;

namespace Ufo.Abstractions.Database.Repositories;

public interface IUserKeyBindingsRepository
{
    /// <summary>
    /// The rows this user has saved. Empty when they have never changed a
    /// shortcut, which is the normal case and not an error.
    /// </summary>
    Task<IReadOnlyList<UserKeyBindingEntity>> GetUserKeyBindingsAsync(
        Ulid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces this user's shortcuts with exactly <paramref name="keyBindings"/>.
    /// </summary>
    /// <remarks>
    /// Replace rather than merge, and in one transaction. Any action not named
    /// here loses its row and goes back to following the build's default, so
    /// resetting a shortcut is the same operation as changing one - there is no
    /// second code path that could disagree with this one about what "default"
    /// means.
    /// </remarks>
    Task<ServerResult> SaveUserKeyBindingsAsync(
        IReadOnlyList<UserKeyBindingEntity> keyBindings,
        Ulid userId,
        CancellationToken cancellationToken = default);
}
