using Cysharp.Serialization.Json;
using SQLite;
using SQLiteNetExtensions.Attributes;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

/// <summary>
/// The per-user preferences row. Exactly one exists per user, created lazily on
/// the first save rather than at registration, so a user who never opens the
/// Settings page has no row and simply reads <see cref="UiThemes.Default"/>.
/// </summary>
[Table("UserSettings")]
public class UserSettingsEntity : EntityBase
{
    [JsonPropertyOrder(1)]
    [NotNull]
    [MaxLength(32)]
    public string Theme { get; set; } = UiThemes.Default;

    [JsonConverter(typeof(UlidJsonConverter))]
    [JsonPropertyOrder(7)]
    [ForeignKey(typeof(UserEntity))]
    public Ulid UserId { get; set; }
}
