using Cysharp.Serialization.Json;
using SQLite;
using SQLiteNetExtensions.Attributes;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

[Table("Snapshots")]
public class SnapshotEntity
{
    [JsonConverter(typeof(UlidJsonConverter))]
    [JsonPropertyOrder(0)]
    [PrimaryKey]
    public Ulid Id { get; set; } = Ulid.NewUlid();

    [JsonPropertyOrder(5)]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    [JsonPropertyOrder(6)]
    [MaxLength(1024)]
    public string? Description { get; set; }

    [JsonPropertyOrder(10)]
    [OneToOne(nameof(FsFolderEntity))]
    public FsFolderEntity? RootFolder { get; set; }

    [JsonPropertyOrder(20)]
    [OneToOne(nameof(VolumeInfoEntity))]
    public VolumeInfoEntity? VolumeInfo { get; set; }
}
