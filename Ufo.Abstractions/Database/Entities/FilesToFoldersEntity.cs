using SQLite;
using SQLiteNetExtensions.Attributes;

namespace Ufo.Abstractions.Database.Entities
{
    [Table("FilesToFolders")]
    public class FilesToFoldersEntity
    {
        [ForeignKey(typeof(SnapshotEntity))]
        public Guid SnapshotGuid { get; set; }

        [ForeignKey(typeof(FsFolderEntity))]
        public Guid FolderGuid { get; set; }

        [ForeignKey(typeof(FsFileEntity))]
        public Guid FileGuid { get; set; }
    }
}
