using SQLite;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

public abstract class FsItemEntity: EntityBase
{
    [JsonPropertyOrder(2)]
    public long? Size { get; set; }

    [JsonPropertyOrder(3)]
    [NotNull]
    [MaxLength(128)]
    public string Sha256Hash { get; set; } = string.Empty;

    [Ignore]
    public string? FullPath { get; set; }

    [JsonPropertyOrder(4)]
    [Ignore]
    public bool HasParent { get; set; }

    [JsonPropertyOrder(5)]
    [Ignore]
    public bool IsHidden { get; set; }
}
