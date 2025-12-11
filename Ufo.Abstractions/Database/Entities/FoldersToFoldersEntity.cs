using Cysharp.Serialization.Json;
//using SQLite;
//using SQLiteNetExtensions.Attributes;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

//[Table("FoldersToFolders")]
public class FoldersToFoldersEntity
{
    [JsonConverter(typeof(UlidJsonConverter))]
    //[ForeignKey(typeof(SnapshotEntity))]
    public Ulid SnapshotId { get; set; }

    [JsonConverter(typeof(UlidJsonConverter))]
    //[ForeignKey(typeof(FsFolderEntity))]
    public Ulid? ParentFolderId { get; set; }

    [JsonConverter(typeof(UlidJsonConverter))]
    //[ForeignKey(typeof(FsFolderEntity))]
    public Ulid ChildFolderId { get; set; }

    // EF Core navigation properties
    [JsonIgnore]
    public SnapshotEntity? Snapshot { get; set; }

    [JsonIgnore]
    public FsFolderEntity? ParentFolder { get; set; }

    [JsonIgnore]
    public FsFolderEntity? ChildFolder { get; set; }
}
