using SQLite;
using SQLiteNetExtensions.Attributes;

namespace Ufo.Abstractions.Database.Entities
{
    [Table("Snapshots")]
    public class SnapshotEntity
    {
        [PrimaryKey]
        public Guid Guid { get; set; } = Guid.NewGuid();

        public DateTime Timestamp { get; set; } = DateTime.Now;

        [OneToOne(nameof(FsFolderEntity))]
        public FsFolderEntity RootFolder { get; set; }

        [OneToOne(nameof(VolumeInfoEntity))]
        public VolumeInfoEntity VolumeInfo { get; set; }
    }
}
