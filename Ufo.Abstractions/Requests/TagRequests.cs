using Cysharp.Serialization.Json;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Requests;

/// <summary>
/// Creates a tag, or names one that already exists.
/// </summary>
/// <remarks>
/// Asking for a tag whose name is taken is asking for the one that exists: a
/// tag is known to the user by its name, and two tags called "Important" in
/// different colours would be a puzzle rather than a feature.
/// </remarks>
public record CreateTagRequest
{
    [JsonPropertyOrder(1)]
    [Required]
    [MaxLength(256)]
    public required string Name { get; set; }

    /// <summary>A CSS hex colour, "#rrggbb". Checked, because it is written into a stylesheet.</summary>
    [JsonPropertyOrder(2)]
    [Required]
    [MaxLength(32)]
    public required string ColorHex { get; set; }
}

/// <summary>Puts one tag on, or takes it off, a set of files and folders.</summary>
public record SetFsItemTagRequest
{
    [JsonConverter(typeof(UlidJsonConverter))]
    [JsonPropertyOrder(1)]
    public Ulid TagId { get; set; }

    [JsonPropertyOrder(2)]
    [Required]
    public required IList<string> FullPaths { get; set; }

    /// <summary>True to apply the tag, false to take it off.</summary>
    [JsonPropertyOrder(3)]
    public bool IsApplied { get; set; }
}
