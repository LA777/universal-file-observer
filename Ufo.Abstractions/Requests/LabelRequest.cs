using Cysharp.Serialization.Json;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Requests
{
    public record LabelRequest
    {
        // TODO LA - Consider merging with LabelResponse and using it for both request and response, as they are very similar. If we do that, we should rename it to LabelDto or something like that.
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

        [JsonPropertyOrder(22)]
        public List<Ulid> SnapshotIds { get; set; } = [];
    }
}
