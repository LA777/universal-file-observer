using System.ComponentModel.DataAnnotations;

namespace Ufo.Abstractions.Requests;

public record PathRequest
{
    [Required]
    public required string Path { get; set; }
}
