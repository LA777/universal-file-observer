using System.ComponentModel.DataAnnotations;

namespace Ufo.Abstractions.Requests
{
    public class PathRequest
    {
        [Required]
        public required string Path { get; set; }
    }
}
