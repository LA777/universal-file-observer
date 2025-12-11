using Cysharp.Serialization.Json;
using SQLite;
using SQLiteNetExtensions.Attributes;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

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
