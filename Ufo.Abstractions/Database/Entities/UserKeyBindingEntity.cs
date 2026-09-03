using Cysharp.Serialization.Json;
using SQLite;
using SQLiteNetExtensions.Attributes;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

/// <summary>
/// One user's keys for one action.
/// </summary>
/// <remarks>
/// A row exists only for an action the user has actually changed. Everything else
/// is answered from <see cref="KeyBindingActions.All"/>, so the defaults can be
/// reworded or re-keyed in a later build and every account that never touched
/// them picks the new value up, instead of being frozen at whatever was current
/// the day they first opened the page.
/// </remarks>
[Table("UserKeyBindings")]
public class UserKeyBindingEntity : EntityBase
{
    /// <summary>One of the ids in <see cref="KeyBindingActions"/>.</summary>
    [JsonPropertyOrder(1)]
    [NotNull]
    [MaxLength(64)]
    public string ActionId { get; set; } = string.Empty;

    /// <summary>
    /// The first chord, or empty for none. Empty is a real answer here: it is how
    /// a user says an action should have no key at all, which is different from
    /// never having expressed an opinion (no row).
    /// </summary>
    [JsonPropertyOrder(2)]
    [NotNull]
    [MaxLength(64)]
    public string PrimaryKey { get; set; } = string.Empty;

    /// <summary>The second chord, or empty for none.</summary>
    [JsonPropertyOrder(3)]
    [NotNull]
    [MaxLength(64)]
    public string SecondaryKey { get; set; } = string.Empty;

    [JsonConverter(typeof(UlidJsonConverter))]
    [JsonPropertyOrder(7)]
    [ForeignKey(typeof(UserEntity))]
    public Ulid UserId { get; set; }
}
