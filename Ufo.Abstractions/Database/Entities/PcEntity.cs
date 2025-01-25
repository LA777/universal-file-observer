using Newtonsoft.Json;
using SQLite;
using SQLiteNetExtensions.Attributes;

namespace Ufo.Abstractions.Database.Entities
{
    [Table("Pcs")]
    public class PcEntity : EntityBase
    {
        [JsonIgnore]
        [ManyToMany(typeof(PcsToStorageDrivesEntity))]
        public IList<StorageDriveEntity> StorageDrives { get; set; } = new List<StorageDriveEntity>();

        [JsonIgnore]
        [ManyToMany(typeof(PcsToStorageDrivesEntity))]
        public IList<SnapshotEntity> Snapshots { get; set; } = new List<SnapshotEntity>();
    }
}
