using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Requests;

/// <summary>
/// Turns the flag on or off for a set of files and folders at once.
/// </summary>
/// <remarks>
/// A batch because the user acts on a selection - several rows picked in the
/// pane and one click. It is still only the named paths that change: nothing
/// here can clear a flag the caller did not mention.
/// </remarks>
public record FsItemFlagsRequest
{
    /// <summary>The files and folders to change. Each is judged on its own.</summary>
    [JsonPropertyOrder(1)]
    [Required]
    public required IList<string> FullPaths { get; set; }

    /// <summary>True to flag them, false to clear the flag.</summary>
    [JsonPropertyOrder(2)]
    public bool IsFlagEnabled { get; set; }
}
