using System.ComponentModel.DataAnnotations;

namespace Ufo.Abstractions.Requests;

/// <summary>Copies or moves a set of entries into one destination folder.</summary>
public record FileSystemTransferRequest
{
    /// <summary>The entries to transfer. Each is handled independently.</summary>
    [Required]
    public required IList<string> Paths { get; set; }

    /// <summary>The folder they all land in, keeping their own names.</summary>
    [Required]
    public required string DestinationFolderPath { get; set; }

    /// <summary>
    /// Replace entries that already exist at the destination. Left false, a
    /// collision is reported back as a conflict rather than overwriting - the
    /// caller asks the user first and then re-sends with this set.
    /// </summary>
    public bool Overwrite { get; set; }
}
