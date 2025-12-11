//using SQLite;
//using SQLiteNetExtensions.Attributes;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

//[Table("Files")]
public class FsFileEntity: FsItemEntity
{
    [JsonPropertyOrder(50)]
    //[MaxLength(128)]
    public string FileExtension { get; set; } = string.Empty;

    [JsonIgnore]
    //[ManyToMany(typeof(FilesToFoldersEntity))]
    public IList<SnapshotEntity> Snapshots { get; } = [];

    [JsonIgnore]
    //[ManyToMany(typeof(FilesToFoldersEntity))]
    public IList<FsFolderEntity> ParentFolders { get; } = [];

    // EF Core navigation property for join entity
    [JsonIgnore]
    public IList<FilesToFoldersEntity> ParentFolderLinks { get; set; } = [];
}
