using System.ComponentModel.DataAnnotations;

namespace Ufo.Abstractions.Requests;

/// <summary>Permanently deletes a set of entries. Folders go with their contents.</summary>
public record FileSystemDeleteRequest
{
    [Required]
    public required IList<string> Paths { get; set; }
}
