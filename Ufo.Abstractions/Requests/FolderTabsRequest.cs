using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Requests;

/// <summary>
/// One panel's locked tabs, saved whole.
/// </summary>
/// <remarks>
/// The whole panel rather than one tab at a time, because locking, unlocking,
/// closing and reordering all end in the same question - which folders does this
/// pane keep, and in what order - and answering it once means the client and the
/// server cannot drift into disagreeing about the answer.
/// </remarks>
public record FolderTabsRequest
{
    /// <summary>
    /// Which pane these belong to. Checked against the known panel ids: a row for
    /// a panel that does not exist is one nothing will ever restore.
    /// </summary>
    [JsonPropertyOrder(1)]
    [Required]
    [MaxLength(16)]
    public required string PanelId { get; set; }

    /// <summary>
    /// The folders to keep, in display order. Empty is a real answer - it is what
    /// unlocking the last tab in a pane looks like.
    /// </summary>
    [JsonPropertyOrder(2)]
    [Required]
    public required IList<string> FolderPaths { get; set; }
}
