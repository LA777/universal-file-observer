using SQLite;
using SQLiteNetExtensions.Attributes;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities
{
    [Table("Files")]
    public class FsFileEntity: FsItemEntity
    {
        [JsonPropertyOrder(50)]
        [MaxLength(128)]
        public string FileExtension { get; set; }

        [JsonIgnore]
        [ManyToMany(typeof(FilesToFoldersEntity))]
        public IList<SnapshotEntity> Snapshots { get; } = new List<SnapshotEntity>();

        [JsonIgnore]
        [ManyToMany(typeof(FilesToFoldersEntity))]
        public IList<FsFolderEntity> ParentFolders { get; } = new List<FsFolderEntity>();
    }
}
