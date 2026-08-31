using Ufo.Abstractions.Database.Entities;

namespace Ufo.Abstractions.Database.Repositories;

public interface IUserSettingsRepository
{
    /// <summary>
    /// The user's saved settings, or <c>null</c> when they have never saved any.
    /// </summary>
    Task<UserSettingsEntity?> GetUserSettingsAsync(Ulid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the user's settings, creating the row on first save.
    /// </summary>
    Task<ServerResult> SaveUserSettingsAsync(UserSettingsEntity userSettings, CancellationToken cancellationToken = default);
}
