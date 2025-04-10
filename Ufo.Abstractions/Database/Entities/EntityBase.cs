using Newtonsoft.Json;
using SQLite;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities
{
    public abstract class EntityBase
    {
        [JsonPropertyOrder(0)]
        [PrimaryKey]
        public Guid Guid { get; set; } = Guid.NewGuid();

        [JsonPropertyOrder(1)]
        [NotNull]
        [MaxLength(256)]
        public string Name { get; set; }
    }
}
