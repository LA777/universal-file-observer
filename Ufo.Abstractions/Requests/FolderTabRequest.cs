using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Requests;

/// <summary>
/// One tab to lock or unlock.
/// </summary>
/// <remarks>
/// One tab, not a panel's whole set. A wholesale replace has to be told what
/// every other tab is, which makes the client's picture of them authoritative -
/// and a client whose load failed pictures none at all, so the next lock would
/// delete every tab the user had kept. Adding and removing one row cannot do
/// that however wrong the caller is about the rest.
/// </remarks>
public record FolderTabRequest
{
    /// <summary>Which pane the tab belongs to.</summary>
    [JsonPropertyOrder(1)]
    [Required]
    [MaxLength(16)]
    public required string PanelId { get; set; }

    [JsonPropertyOrder(2)]
    [Required]
    [MaxLength(4096)]
    public required string FolderPath { get; set; }
}
