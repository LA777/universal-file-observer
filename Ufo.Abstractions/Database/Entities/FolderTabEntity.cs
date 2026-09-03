using Cysharp.Serialization.Json;
using SQLite;
using SQLiteNetExtensions.Attributes;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

/// <summary>
/// One locked folder tab.
/// </summary>
/// <remarks>
/// Only locked tabs get a row. An ordinary tab is somewhere the user happens to
/// be looking and belongs to the session; locking is how they say this one is
/// worth keeping, and the row is what keeping it means.
/// </remarks>
[Table("FolderTabs")]
public class FolderTabEntity : EntityBase
{
    /// <summary>Which pane the tab belongs to - the panel ids the client uses.</summary>
    [JsonPropertyOrder(1)]
    [NotNull]
    [MaxLength(16)]
    public string PanelId { get; set; } = string.Empty;

    /// <summary>
    /// The folder the tab is pinned to. Stored as the caller gave it, after the
    /// path guard has resolved and approved it.
    /// </summary>
    [JsonPropertyOrder(2)]
    [NotNull]
    [MaxLength(4096)]
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>Left-to-right order within its panel.</summary>
    [JsonPropertyOrder(3)]
    public int Position { get; set; }

    [JsonConverter(typeof(UlidJsonConverter))]
    [JsonPropertyOrder(7)]
    [ForeignKey(typeof(UserEntity))]
    public Ulid UserId { get; set; }
}
