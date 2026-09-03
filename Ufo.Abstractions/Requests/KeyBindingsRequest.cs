using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Requests;

/// <summary>One action's two slots, as the Settings page has them.</summary>
public record KeyBindingRequest
{
    /// <summary>
    /// One of the ids in <see cref="KeyBindingActions"/>. An id this build does
    /// not know about is rejected rather than stored: a row nothing will ever
    /// read is not a preference, it is litter with a foreign key.
    /// </summary>
    [JsonPropertyOrder(1)]
    [Required]
    [MaxLength(64)]
    public string? ActionId { get; set; }

    /// <summary>
    /// The first chord, or null/empty for none. Empty is meaningful - it is how
    /// the user says this action should have no key - so it is not rejected the
    /// way a missing action id is.
    /// </summary>
    [JsonPropertyOrder(2)]
    [MaxLength(64)]
    public string? PrimaryKey { get; set; }

    [JsonPropertyOrder(3)]
    [MaxLength(64)]
    public string? SecondaryKey { get; set; }
}

/// <summary>
/// The whole shortcuts table, saved in one go.
/// </summary>
/// <remarks>
/// Sent whole rather than one row at a time because the rule being enforced spans
/// rows: no chord may be bound to two actions. Judged against a partial picture,
/// a swap - giving F5 to Move and F6 to Copy - would have to be rejected halfway
/// through, leaving the user unable to express it at all.
/// </remarks>
public record KeyBindingsRequest
{
    [JsonPropertyOrder(1)]
    [Required]
    public required IList<KeyBindingRequest> Bindings { get; set; }
}
