using Cysharp.Serialization.Json;
//using SQLite;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

public abstract class EntityBase
{
    [JsonConverter(typeof(UlidJsonConverter))]
    [JsonPropertyOrder(0)]
    //[PrimaryKey]
    public Ulid Id { get; set; } = Ulid.NewUlid();

    [JsonPropertyOrder(1)]
    //[NotNull]
    //[MaxLength(256)]
    public string Name { get; set; } = string.Empty;
}
