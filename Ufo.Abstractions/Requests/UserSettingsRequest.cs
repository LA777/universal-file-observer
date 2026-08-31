using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Requests;

public record UserSettingsRequest
{
    /// <summary>
    /// One of <see cref="UiThemes.All"/>. Nullable with no default on purpose:
    /// a body that omits the field has to be rejected by <see cref="RequiredAttribute"/>
    /// rather than quietly reading as the default theme and overwriting whatever
    /// the user had chosen. Which value is acceptable is then checked in the
    /// service, so an unknown one comes back as a <see cref="ServerResult"/>
    /// naming the accepted values, like every other rejected write in this API.
    /// </summary>
    [JsonPropertyOrder(1)]
    [Required]
    [MaxLength(32)]
    public string? Theme { get; set; }
}
