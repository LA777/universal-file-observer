using SQLite;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

public abstract class FsItemEntity: EntityWithUserAndNameAndIdBase
{
    [JsonPropertyOrder(2)]
    public long? Size { get; set; }

    [JsonPropertyOrder(3)]
    [NotNull]
    [MaxLength(128)]
    public string Sha256Hash { get; set; } = string.Empty;

    public bool IsHidden { get; set; } = false;

    [MaxLength(64)] // TODO LA - Update tests to cover this field. Verify MaxLength.
    public string CreatedAt { get; set; } = string.Empty;

    [MaxLength(64)] // TODO LA - Update tests to cover this field. Verify MaxLength.
    public string UpdatedAt { get; set; } = string.Empty;

    /// <summary>
    /// Whether this item was flagged, as at the moment of the snapshot.
    /// </summary>
    /// <remarks>
    /// <b>Not a column on Files or Folders</b>, and it must never become one:
    /// those rows are deduplicated by content and shared by every identical item
    /// in every snapshot, so a flag stored there would be a flag on all of them.
    /// It lives on the per-snapshot association (FilesToFolders,
    /// FoldersToFolders) and is carried here only in memory - written across on
    /// the way in, read back across on the way out.
    /// </remarks>
    [Ignore]
    public bool IsFlagEnabled { get; set; }
}
