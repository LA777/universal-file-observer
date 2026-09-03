using System.ComponentModel.DataAnnotations;

namespace Ufo.Abstractions.Requests;

/// <summary>Creates one empty file or folder inside an existing folder.</summary>
public record FileSystemCreateRequest
{
    /// <summary>The folder the new entry goes into.</summary>
    [Required]
    public required string ParentPath { get; set; }

    /// <summary>
    /// A single name segment, not a path. Anything the host would read as a
    /// directory separator is rejected rather than silently creating a tree.
    /// </summary>
    [Required]
    public required string Name { get; set; }

    /// <summary>True for an empty file, false for a folder.</summary>
    public bool IsFile { get; set; }
}
