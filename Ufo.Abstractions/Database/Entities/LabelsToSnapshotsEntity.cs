using Cysharp.Serialization.Json;
using SQLite;
using SQLiteNetExtensions.Attributes;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities
{
    [Table("LabelsToSnapshots")]
    public class LabelsToSnapshotsEntity
    {
        [JsonConverter(typeof(UlidJsonConverter))]
        [ForeignKey(typeof(SnapshotEntity))]
        public Ulid SnapshotId { get; set; }

        [JsonConverter(typeof(UlidJsonConverter))]
        [ForeignKey(typeof(SnapshotEntity))]
        public Ulid LabelId { get; set; }
    }
}
