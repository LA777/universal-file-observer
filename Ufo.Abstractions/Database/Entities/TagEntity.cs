using Cysharp.Serialization.Json;
using SQLite;
using SQLiteNetExtensions.Attributes;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

/// <summary>
/// One of the user's tags: a name and a colour they can put on any number of
/// files and folders.
/// </summary>
/// <remarks>
/// <para>
/// A vocabulary rather than free text on each item, which is what makes a tag
/// worth having - "Important" is one tag applied in forty places, so its colour
/// means the same thing everywhere. The shape deliberately mirrors
/// <see cref="LabelEntity"/>, which does the same job for snapshots.
/// </para>
/// <para>
/// Extends <see cref="EntityBase"/> with its own UserId rather than
/// <c>EntityWithUserAndNameAndIdBase</c>, whose <c>required User</c> navigation
/// would have to be satisfied at every construction site - and this is built in
/// code, not only materialised by Dapper. The other per-path entities added
/// alongside it are shaped the same way.
/// </para>
/// </remarks>
[Table("Tags")]
public class TagEntity : EntityBase
{
    [JsonPropertyOrder(1)]
    [NotNull]
    [MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyOrder(14)]
    [NotNull]
    [MaxLength(32)]
    public string ColorHex { get; set; } = string.Empty;

    [JsonConverter(typeof(UlidJsonConverter))]
    [JsonPropertyOrder(7)]
    [ForeignKey(typeof(UserEntity))]
    public Ulid UserId { get; set; }
}
