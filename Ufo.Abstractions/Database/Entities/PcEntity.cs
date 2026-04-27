using SQLite;
using SQLiteNetExtensions.Attributes;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

[Table("Pcs")]
public class PcEntity : EntityWithUserAndNameAndIdBase
{
    public string MachineId { get; set; } = string.Empty;
    public string HardwareUuid { get; set; } = string.Empty;
    public string HardwareSerialNumber { get; set; } = string.Empty;

    [JsonPropertyOrder(80)]
    [JsonIgnore]
    [ManyToMany(typeof(PcsToStorageDrivesEntity))]
    public IList<StorageDriveEntity> StorageDrives { get; set; } = [];

    [JsonPropertyOrder(90)]
    [JsonIgnore]
    [ManyToMany(typeof(PcsToStorageDrivesEntity))]
    public IList<SnapshotEntity> Snapshots { get; set; } = [];
}
