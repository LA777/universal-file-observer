using System.ComponentModel.DataAnnotations;

namespace Ufo.Abstractions.Options
{
    public class ApplicationSettings
    {

        [Required(ErrorMessage = "ERROR: Value for {0} should contain some data.")]
        public string SqliteDbConnectionStrings { get; set; } = string.Empty;
    }
}