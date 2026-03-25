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
}
