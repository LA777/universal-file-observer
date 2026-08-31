using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Requests;

/// <summary>
/// An administrator supplying their own TLS certificate from the Settings page.
/// </summary>
public record ServerCertificateRequest
{
    /// <summary>
    /// The PKCS#12 (.pfx/.p12) archive, base64 encoded. Required: a PEM pair
    /// would need two fields and a private-key parser, whereas PKCS#12 carries
    /// the chain and the key in the one blob that
    /// <c>X509CertificateLoader</c> already understands.
    /// </summary>
    [JsonPropertyOrder(1)]
    [Required]
    public string? PfxBase64 { get; set; }

    /// <summary>
    /// The archive's passphrase, or null/empty when it has none. Used only to
    /// open the archive during validation - it is never stored, because the blob
    /// is re-protected with the server's own key before it reaches the database.
    /// </summary>
    [JsonPropertyOrder(2)]
    public string? Passphrase { get; set; }
}
