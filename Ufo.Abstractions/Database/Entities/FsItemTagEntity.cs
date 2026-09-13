using Cysharp.Serialization.Json;
using SQLite;
using SQLiteNetExtensions.Attributes;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

/// <summary>
/// One tag on one file or folder on disk.
/// </summary>
/// <remarks>
/// Keyed by path, for the reason flags and ratings are: the <c>Files</c> and
/// <c>Folders</c> tables are deduplicated by content and carry no path, so a tag
/// hung off them would tag every identical copy at once.
/// </remarks>
[Table("FsItemTags")]
public class FsItemTagEntity
{
    [JsonConverter(typeof(UlidJsonConverter))]
    [JsonPropertyOrder(1)]
    [ForeignKey(typeof(TagEntity))]
    public Ulid TagId { get; set; }

    [JsonPropertyOrder(2)]
    [NotNull]
    [MaxLength(4096)]
    public string FullPath { get; set; } = string.Empty;
}
