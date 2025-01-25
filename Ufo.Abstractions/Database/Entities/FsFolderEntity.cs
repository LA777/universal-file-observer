using Newtonsoft.Json;
using SQLite;
using SQLiteNetExtensions.Attributes;

namespace Ufo.Abstractions.Database.Entities
{
    [Table("Folders")]
    public class FsFolderEntity : FsItemEntity
    {
        [JsonIgnore]
        [ManyToMany(typeof(FoldersToFoldersEntity))]
        public IList<SnapshotEntity> Snapshots { get; set; } = new List<SnapshotEntity>();

        [ManyToMany(typeof(FilesToFoldersEntity))]
        public  IList<FsFileEntity> Files { get; set; } = new List<FsFileEntity>();

        [ManyToMany(typeof(FoldersToFoldersEntity))]
        public  IList<FsFolderEntity> ChildFolders { get; set; } = new List<FsFolderEntity>();

        [JsonIgnore]
        [ManyToMany(typeof(FoldersToFoldersEntity))]
        public IList<FsFolderEntity> ParentFolders { get; set; } = new List<FsFolderEntity>();
    }
}
