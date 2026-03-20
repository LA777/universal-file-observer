using System.ComponentModel.DataAnnotations;

namespace Ufo.Abstractions.Requests;

public record LoginRequest
{
    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}
