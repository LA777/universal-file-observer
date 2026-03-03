using Cysharp.Serialization.Json;
using SQLite;
using SQLiteNetExtensions.Attributes;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

[Table("VolumeInfos")]
public class VolumeInfoEntity: EntityWithUserAndIdBase
{
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
