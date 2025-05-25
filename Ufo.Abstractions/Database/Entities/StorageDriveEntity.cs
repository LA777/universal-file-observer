using Cysharp.Serialization.Json;
using SQLite;
using SQLiteNetExtensions.Attributes;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

[Table("StorageDrives")]
public class StorageDriveEntity: EntityBase
{
    [JsonPropertyOrder(10)]
    [MaxLength(128)]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyOrder(15)]
    [MaxLength(128)]
    public string SerialNumber { get; set; } = string.Empty;

    [JsonPropertyOrder(20)]
    public long TotalSize { get; set; }

    [JsonPropertyOrder(25)]
    [MaxLength(128)]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyOrder(30)]
    [MaxLength(128)]
    public string MediaType { get; set; } = string.Empty;

    [JsonPropertyOrder(35)]
    [MaxLength(128)]
    public string InterfaceType { get; set; } = string.Empty;

    [JsonPropertyOrder(40)]
    [ManyToMany(typeof(PcsToStorageDrivesEntity))]
    public IList<PcEntity> Pcs { get; set; } = [];

    [JsonIgnore]
    [ManyToMany(typeof(PcsToStorageDrivesEntity))]
    public IList<SnapshotEntity> Snapshots { get; set; } = [];

    [JsonIgnore]
    [OneToMany]
    public IList<VolumeEntity> Volumes { get; set; } = [];
}

[Table("Volumes")]
public class VolumeEntity
{
    [JsonConverter(typeof(UlidJsonConverter))]
    [JsonPropertyOrder(0)]
    [PrimaryKey]
    public Ulid Id { get; set; } = Ulid.NewUlid();

    [JsonPropertyOrder(5)]
    [MaxLength(10)]
    public string DriveLetter { get; set; } = string.Empty;

    [JsonPropertyOrder(10)]
    [MaxLength(128)]
    public string VolumeName { get; set; } = string.Empty;

    [JsonPropertyOrder(15)]
    [MaxLength(128)]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyOrder(20)]
    [MaxLength(128)]
    public string VolumeSerialNumber { get; set; } = string.Empty;

    [JsonPropertyOrder(25)]
    public long VolumeSize { get; set; }

    [JsonConverter(typeof(UlidJsonConverter))]
    [JsonIgnore]
    [ForeignKey(typeof(StorageDriveEntity))]
    public Ulid StorageDriveId { get; set; }

    [JsonPropertyOrder(100)]
    public StorageDriveEntity? StorageDrive { get; set; }

    [JsonIgnore]
    [OneToMany]
    public IList<VolumeInfoEntity> VolumeInfos { get; set; } = [];
}

[Table("VolumeInfos")]
public class VolumeInfoEntity
{
    [JsonConverter(typeof(UlidJsonConverter))]
    [JsonPropertyOrder(0)]
    [PrimaryKey]
    public Ulid Id { get; set; } = Ulid.NewUlid();

    [JsonPropertyOrder(5)]
    public long FreeSpace { get; set; }

    [JsonPropertyOrder(10)]
    [MaxLength(128)]
    public string DriveStatus { get; set; } = string.Empty;

    [JsonConverter(typeof(UlidJsonConverter))]
    [JsonPropertyOrder(99)]
    [ForeignKey(typeof(VolumeEntity))]
    public Ulid VolumeId { get; set; }

    [JsonPropertyOrder(100)]
    public VolumeEntity? Volume { get; set; }

    [JsonConverter(typeof(UlidJsonConverter))]
    [JsonIgnore]
    [ForeignKey(typeof(SnapshotEntity))]
    public Ulid SnapshotId { get; set; }

    [JsonIgnore]
    public SnapshotEntity? Snapshot { get; set; }
}

[Table("PcsToStorageDrives")]
public class PcsToStorageDrivesEntity
{
    [JsonConverter(typeof(UlidJsonConverter))]
    [ForeignKey(typeof(SnapshotEntity))]
    public Ulid SnapshotId { get; set; }

    [JsonConverter(typeof(UlidJsonConverter))]
    [ForeignKey(typeof(PcEntity))]
    public Ulid PcId { get; set; }

    [JsonConverter(typeof(UlidJsonConverter))]
    [ForeignKey(typeof(StorageDriveEntity))]
    public Ulid StorageDriveId { get; set; }
}
