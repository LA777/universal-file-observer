using Cysharp.Serialization.Json;
using SQLite;
using SQLiteNetExtensions.Attributes;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

public abstract class EntityBase
{
    [JsonConverter(typeof(UlidJsonConverter))]
    [JsonPropertyOrder(0)]
    [PrimaryKey]
    public Ulid Id { get; set; } = Ulid.NewUlid();
}

public abstract class EntityWithUserAndIdBase : EntityBase
{
    [JsonConverter(typeof(UlidJsonConverter))]
    [JsonPropertyOrder(7)]
    [ForeignKey(typeof(UserEntity))]
    public Ulid UserId { get; set; }

    [JsonPropertyOrder(10)]
    [ManyToOne(nameof(UserEntity))]
    public required UserEntity User { get; set; }
}

public abstract class EntityWithUserAndNameAndIdBase : EntityWithUserAndIdBase
{
    [JsonPropertyOrder(1)]
    [NotNull]
    [MaxLength(256)]
    public string Name { get; set; } = string.Empty;
}
