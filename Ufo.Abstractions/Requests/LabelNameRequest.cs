using System.ComponentModel.DataAnnotations;

namespace Ufo.Abstractions.Requests
{
    public record LabelNameRequest
    {
        [Required]
        [MaxLength(256)]
        public string Name { get; set; } = string.Empty;
    }
}
