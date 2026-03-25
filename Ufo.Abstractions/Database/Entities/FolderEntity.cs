using SQLite;
using SQLiteNetExtensions.Attributes;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

[Table("Folders")]
public class FolderEntity : FsItemEntity
{
    [JsonIgnore]
    [ManyToMany(typeof(FoldersToFoldersEntity))]
    public IList<SnapshotEntity> Snapshots { get; set; } = [];

    [JsonPropertyOrder(100)]
    [ManyToMany(typeof(FilesToFoldersEntity))]
    public IList<FileEntity> Files { get; set; } = [];

    [JsonPropertyOrder(99)]
    [ManyToMany(typeof(FoldersToFoldersEntity))]
    public  IList<FolderEntity> ChildFolders { get; set; } = [];

    [JsonIgnore]
    [ManyToMany(typeof(FoldersToFoldersEntity))]
    public IList<FolderEntity> ParentFolders { get; set; } = [];
}
