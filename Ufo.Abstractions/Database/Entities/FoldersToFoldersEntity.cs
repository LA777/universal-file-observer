using SQLite;
using SQLiteNetExtensions.Attributes;

namespace Ufo.Abstractions.Database.Entities
{
    [Table("FoldersToFolders")]
    public class FoldersToFoldersEntity
    {
        [ForeignKey(typeof(SnapshotEntity))]
        public Guid SnapshotGuid { get; set; }

        [ForeignKey(typeof(FsFolderEntity))]
        public Guid? ParentFolderGuid { get; set; }

        [ForeignKey(typeof(FsFolderEntity))]
        public Guid ChildFolderGuid { get; set; }
    }
}
