using Cysharp.Serialization.Json;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Requests
{
    public class LabelRequest
    {
        [JsonConverter(typeof(UlidJsonConverter))]
        [JsonPropertyOrder(0)]
        public Ulid Id { get; set; } = Ulid.NewUlid();

        [JsonPropertyOrder(1)]
        [MaxLength(256)]
        [Required]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyOrder(14)]
        [MaxLength(32)]
        public string ColorHex { get; set; } = string.Empty;

        [JsonConverter(typeof(UlidJsonConverter))]
        [JsonPropertyOrder(22)]
        public IList<Ulid> SnapshotIds { get; set; } = [];
    }
}
