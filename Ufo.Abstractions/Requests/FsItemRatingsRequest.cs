using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Requests;

/// <summary>
/// Sets the rating for a set of files and folders at once.
/// </summary>
/// <remarks>
/// A batch because the user acts on a selection - several rows picked in the
/// pane and one choice from the picker. It is still only the named paths that
/// change: nothing here can alter a rating the caller did not mention.
/// </remarks>
public record FsItemRatingsRequest
{
    /// <summary>The files and folders to rate. Each is judged on its own.</summary>
    [JsonPropertyOrder(1)]
    [Required]
    public required IList<string> FullPaths { get; set; }

    /// <summary>
    /// 0 to 10. Zero is not a rating but the absence of one, and clears whatever
    /// was there - which is how the picker's own zero is expressed.
    /// </summary>
    [JsonPropertyOrder(2)]
    [Range(0, 10)]
    public int Rating { get; set; }
}
