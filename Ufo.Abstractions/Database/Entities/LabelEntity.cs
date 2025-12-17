using SQLite;
using SQLiteNetExtensions.Attributes;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities
{
    [Table("Labels")]
    public class LabelEntity: EntityBase
    {
        [JsonPropertyOrder(14)]
        [MaxLength(32)]
        public string ColorHex { get; set; } = string.Empty;

        [JsonIgnore]
        [ManyToMany(typeof(LabelsToSnapshotsEntity))]
        public IList<SnapshotEntity> Snapshots { get; } = [];
    }
}
