using Cysharp.Serialization.Json;
using SQLite;
using SQLiteNetExtensions.Attributes;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

[Table("FilesToFolders")]
public class FilesToFoldersEntity
{
    [JsonConverter(typeof(UlidJsonConverter))]
    [ForeignKey(typeof(SnapshotEntity))]
    public Ulid SnapshotId { get; set; }

    [JsonConverter(typeof(UlidJsonConverter))]
    [ForeignKey(typeof(FolderEntity))]
    public Ulid FolderId { get; set; }

    [JsonConverter(typeof(UlidJsonConverter))]
    [ForeignKey(typeof(FileEntity))]
    public Ulid FileId { get; set; }

    /// <summary>
    /// Whether the item was flagged when this snapshot was taken.
    /// </summary>
    /// <remarks>
    /// Here rather than on the item, because the item's row is shared by every
    /// identical copy of it. This association is the only thing in the schema
    /// that is unique to one item in one snapshot under one parent.
    /// </remarks>
    public bool IsFlagEnabled { get; set; }

    /// <summary>
    /// The rating when this snapshot was taken, 0 for unrated. Here rather than
    /// on the item, whose row is shared by every identical copy of it.
    /// </summary>
    public int Rating { get; set; }
}
