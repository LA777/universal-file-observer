using System.ComponentModel.DataAnnotations;

namespace Ufo.Abstractions.Requests;

public record RegisterRequest
{
    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    public string Password { get; set; } = string.Empty;
}
