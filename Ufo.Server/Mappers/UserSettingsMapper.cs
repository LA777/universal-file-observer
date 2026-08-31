using Ufo.Abstractions;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.DataTransferObjects;

namespace Ufo.Server.Mappers;

public static class UserSettingsMapper
{
    public static UserSettingsDto ToDto(this UserSettingsEntity entity) =>
        new()
        {
            Id = entity.Id,
            UserId = entity.UserId,
            Theme = entity.Theme
        };

    /// <summary>
    /// The settings a user who has never saved any should see. Handed out with
    /// the caller's own <paramref name="userId"/> and a fresh id so the client
    /// gets the same shape whether or not a row exists yet.
    /// </summary>
    public static UserSettingsDto DefaultsFor(Ulid userId) =>
        new()
        {
            UserId = userId,
            Theme = UiThemes.Default
        };
}
