using Cysharp.Serialization.Json;
using SQLite;
using SQLiteNetExtensions.Attributes;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

/// <summary>
/// One rated file or folder on disk.
/// </summary>
/// <remarks>
/// <para>
/// Keyed by path, for the same reason <see cref="FsItemFlagEntity"/> is: the
/// <c>Files</c> and <c>Folders</c> tables are deduplicated by content - one row
/// shared by every byte-identical item in every snapshot, carrying no path - so a
/// rating column there would rate every copy of a file at once.
/// </para>
/// <para>
/// A row exists only for something actually rated. Zero is "unrated", the
/// default, so it is stored as the absence of a row rather than as a value.
/// </para>
/// </remarks>
[Table("FsItemRatings")]
public class FsItemRatingEntity : EntityBase
{
    /// <summary>The path of the rated file or folder, as the listings produce it.</summary>
    [JsonPropertyOrder(1)]
    [NotNull]
    [MaxLength(4096)]
    public string FullPath { get; set; } = string.Empty;

    /// <summary>Between 1 and 10. Zero never reaches a row - it deletes one.</summary>
    [JsonPropertyOrder(2)]
    public int Rating { get; set; }

    [JsonConverter(typeof(UlidJsonConverter))]
    [JsonPropertyOrder(7)]
    [ForeignKey(typeof(UserEntity))]
    public Ulid UserId { get; set; }
}
