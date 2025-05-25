using SQLite;
using SQLiteNetExtensions.Attributes;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

[Table("Pcs")]
public class PcEntity : EntityBase
{
    [JsonPropertyOrder(80)]
    [JsonIgnore]
    [ManyToMany(typeof(PcsToStorageDrivesEntity))]
    public IList<StorageDriveEntity> StorageDrives { get; set; } = [];

    [JsonPropertyOrder(90)]
    [JsonIgnore]
    [ManyToMany(typeof(PcsToStorageDrivesEntity))]
    public IList<SnapshotEntity> Snapshots { get; set; } = [];
}
