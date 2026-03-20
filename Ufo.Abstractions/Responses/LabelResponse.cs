using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Responses
{
    public record LabelResponse
    {
        [JsonPropertyOrder(0)]
        public Ulid Id { get; set; }

        [JsonPropertyOrder(1)]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyOrder(2)]
        public string ColorHex { get; set; } = string.Empty;

        [JsonPropertyOrder(5)]
        public string UserId { get; set; } = string.Empty;

        [JsonPropertyOrder(10)]
        public List<Ulid> SnapshotIds { get; set; } = [];
    }
}
