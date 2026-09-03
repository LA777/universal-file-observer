using System.ComponentModel.DataAnnotations;

namespace Ufo.Abstractions.Requests;

/// <summary>Renames one file or folder in place, without moving it.</summary>
public record FileSystemRenameRequest
{
    /// <summary>The entry to rename.</summary>
    [Required]
    public required string Path { get; set; }

    /// <summary>A single name segment; the entry keeps its parent folder.</summary>
    [Required]
    public required string NewName { get; set; }
}
