using System.ComponentModel.DataAnnotations;

namespace Ufo.Abstractions.Options;

public class DatabaseOptions
{
    [Required(ErrorMessage = "ERROR: Value for {0} should contain some data.")]
    public string? ConnectionString { get; set; } = string.Empty;
}
