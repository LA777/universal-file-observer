using Cysharp.Serialization.Json;
using SQLite;
using SQLiteNetExtensions.Attributes;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

[Table("Volumes")]
public class VolumeEntity : EntityWithUserAndIdBase
{
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
