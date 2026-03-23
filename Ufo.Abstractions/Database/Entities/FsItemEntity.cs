using SQLite;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

public abstract class FsItemEntity: EntityWithUserAndNameAndIdBase
{
    [JsonPropertyOrder(2)]
    public long? Size { get; set; }

    [JsonPropertyOrder(3)]
    [NotNull]
    [MaxLength(128)]
    public string Sha256Hash { get; set; } = string.Empty;

    public bool IsHidden { get; set; } = false;

    [MaxLength(64)] // TODO LA - Update tests to cover this field. Verify MaxLength.
    public string CreatedAt { get; set; } = string.Empty;

    [MaxLength(64)] // TODO LA - Update tests to cover this field. Verify MaxLength.
    public string UpdatedAt { get; set; } = string.Empty;

    // TODO LA - remove this code
    //[Ignore]
    //public string? FullPath { get; set; }

    //[JsonPropertyOrder(4)]
    //[Ignore]
    //public bool HasParent { get; set; }

    // [JsonPropertyOrder(5)]
    //[Ignore]
    //public bool IsHidden { get; set; }
}
