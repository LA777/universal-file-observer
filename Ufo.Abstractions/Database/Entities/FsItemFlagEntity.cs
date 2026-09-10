using Cysharp.Serialization.Json;
using SQLite;
using SQLiteNetExtensions.Attributes;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

/// <summary>
/// One flagged file or folder on disk.
/// </summary>
/// <remarks>
/// <para>
/// Keyed by path, and it has to be. The <c>Files</c> and <c>Folders</c> tables
/// are deduplicated by content - one row is shared by every byte-identical file
/// in every snapshot, and carries no path at all - so a flag column there would
/// flag every copy of a file at once, everywhere it appears.
/// </para>
/// <para>
/// A row exists only for something that is flagged. Flags are off by default, so
/// the absence of a row is the default rather than something to store.
/// </para>
/// </remarks>
[Table("FsItemFlags")]
public class FsItemFlagEntity : EntityBase
{
    /// <summary>The resolved path of the flagged file or folder.</summary>
    [JsonPropertyOrder(1)]
    [NotNull]
    [MaxLength(4096)]
    public string FullPath { get; set; } = string.Empty;

    [JsonConverter(typeof(UlidJsonConverter))]
    [JsonPropertyOrder(7)]
    [ForeignKey(typeof(UserEntity))]
    public Ulid UserId { get; set; }
}
