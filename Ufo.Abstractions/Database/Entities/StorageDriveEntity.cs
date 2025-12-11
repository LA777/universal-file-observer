//using SQLite;
//using SQLiteNetExtensions.Attributes;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

//[Table("StorageDrives")]
public class StorageDriveEntity: EntityBase
{
    [JsonPropertyOrder(10)]
    //[MaxLength(128)]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyOrder(15)]
    //[MaxLength(128)]
    public string SerialNumber { get; set; } = string.Empty;

    [JsonPropertyOrder(20)]
    public long TotalSize { get; set; }

    [JsonPropertyOrder(25)]
    //[MaxLength(128)]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyOrder(30)]
    //[MaxLength(128)]
    public string MediaType { get; set; } = string.Empty;

    [JsonPropertyOrder(35)]
    //[MaxLength(128)]
    public string InterfaceType { get; set; } = string.Empty;

    [JsonPropertyOrder(40)]
    //[ManyToMany(typeof(PcsToStorageDrivesEntity))]
    public IList<PcEntity> Pcs { get; set; } = [];

    [JsonIgnore]
    //[ManyToMany(typeof(PcsToStorageDrivesEntity))]
    public IList<SnapshotEntity> Snapshots { get; set; } = [];

    [JsonIgnore]
    //[OneToMany]
    public IList<VolumeEntity> Volumes { get; set; } = [];

    // EF Core navigation property for join entity
    [JsonIgnore]
    public IList<PcsToStorageDrivesEntity> PcsLinks { get; set; } = [];
}
