using Newtonsoft.Json;
using SQLite;
using SQLiteNetExtensions.Attributes;

namespace Ufo.Abstractions.Database.Entities
{
    [Table("StorageDrives")]
    public class StorageDriveEntity: EntityBase
    {
        [MaxLength(128)]
        public string DeviceId { get; set; } = string.Empty;

        [MaxLength(128)]
        public string SerialNumber { get; set; } = string.Empty;

        public long TotalSize { get; set; }

        [MaxLength(128)]
        public string Description { get; set; } = string.Empty;

        [MaxLength(128)]
        public string MediaType { get; set; } = string.Empty;

        [MaxLength(128)]
        public string InterfaceType { get; set; } = string.Empty;

        [ManyToMany(typeof(PcsToStorageDrivesEntity))]
        public IList<PcEntity> Pcs { get; set; } = new List<PcEntity>();

        [JsonIgnore]
        [ManyToMany(typeof(PcsToStorageDrivesEntity))]
        public IList<SnapshotEntity> Snapshots { get; set; } = new List<SnapshotEntity>();

        [JsonIgnore]
        [OneToMany]
        public IList<VolumeEntity> Volumes { get; set; } = new List<VolumeEntity>();
    }

    [Table("Volumes")]
    public class VolumeEntity
    {
        [PrimaryKey]
        public Guid Guid { get; set; } = Guid.NewGuid();

        [MaxLength(10)]
        public string DriveLetter { get; set; } = string.Empty;

        [MaxLength(128)]
        public string VolumeName { get; set; } = string.Empty;

        [MaxLength(128)]
        public string Description { get; set; } = string.Empty;

        [MaxLength(128)]
        public string VolumeSerialNumber { get; set; } = string.Empty;

        public long VolumeSize { get; set; }

        [JsonIgnore]
        [ForeignKey(typeof(StorageDriveEntity))]
        public Guid StorageDriveGuid { get; set; }

        public StorageDriveEntity StorageDrive { get; set; }

        [JsonIgnore]
        [OneToMany]
        public IList<VolumeInfoEntity> VolumeInfos { get; set; } = new List<VolumeInfoEntity>();
    }

    [Table("VolumeInfos")]
    public class VolumeInfoEntity
    {
        [PrimaryKey]
        public Guid Guid { get; set; } = Guid.NewGuid();

        public long FreeSpace { get; set; }

        [MaxLength(128)]
        public string DriveStatus { get; set; } = string.Empty;

        [ForeignKey(typeof(VolumeEntity))]
        public Guid VolumeGuid { get; set; }

        public VolumeEntity Volume { get; set; }

        [JsonIgnore]
        [ForeignKey(typeof(SnapshotEntity))]
        public Guid SnapshotGuid { get; set; }

        [JsonIgnore]
        public SnapshotEntity Snapshot { get; set; }
    }

    [Table("PcsToStorageDrives")]
    public class PcsToStorageDrivesEntity
    {
        [ForeignKey(typeof(SnapshotEntity))]
        public Guid SnapshotGuid { get; set; }

        [ForeignKey(typeof(PcEntity))]
        public Guid PcGuid { get; set; }

        [ForeignKey(typeof(StorageDriveEntity))]
        public Guid StorageDriveGuid { get; set; }
    }
}
