using SQLite;
using SQLiteNetExtensions.Attributes;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

[Table("Snapshots")]
public class SnapshotEntity : EntityWithUserAndIdBase
{
    [JsonPropertyOrder(5)]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    [JsonPropertyOrder(6)]
    [MaxLength(1024)]
    public string? Description { get; set; }    

    [JsonPropertyOrder(15)]
    [OneToOne(nameof(FolderEntity))]
    public FolderEntity? RootFolder { get; set; }

    [JsonPropertyOrder(20)]
    [OneToOne(nameof(VolumeInfoEntity))]
    public VolumeInfoEntity? VolumeInfo { get; set; }

    [JsonPropertyOrder(99)]
    [ManyToMany(typeof(FoldersToFoldersEntity))]
    public List<LabelEntity> Labels { get; set; } = [];
}
