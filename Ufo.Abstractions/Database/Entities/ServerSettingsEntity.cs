using Cysharp.Serialization.Json;
using SQLite;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

/// <summary>
/// Server-scoped configuration. Exactly one row exists for the whole
/// installation, guarded by <c>SingletonGuard</c> in the DDL.
/// </summary>
/// <remarks>
/// <para>
/// This is the one entity in the schema that deliberately has no
/// <c>UserId</c>, and the exception is load-bearing rather than an oversight.
/// Kestrel binds a single certificate for the whole listener, so "which user's
/// certificate is served" has no answer: a per-user row would mean one user's
/// upload silently changing the identity presented to everyone else. The
/// certificate is therefore a property of the server, and only an administrator
/// (<see cref="UserEntity.IsAdmin"/>) may replace it.
/// </para>
/// <para>
/// <see cref="CertificatePfx"/> holds a PKCS#12 blob that has already been
/// encrypted by the server's certificate protector - the database itself is not
/// encrypted, so the raw private key must never reach a table. Everything else
/// on this row is metadata extracted from the certificate at upload time so the
/// Settings page can describe it without decrypting anything.
/// </para>
/// </remarks>
[Table("ServerSettings")]
public class ServerSettingsEntity : EntityBase
{
    /// <summary>
    /// The protected PKCS#12 bytes, or <c>null</c> when no certificate has been
    /// stored yet. Never the raw archive: see the remarks on the class.
    /// </summary>
    [JsonIgnore] // Never leaves the server, encrypted or not.
    public byte[]? CertificatePfx { get; set; }

    [MaxLength(128)]
    public string CertificateThumbprint { get; set; } = string.Empty;

    [MaxLength(512)]
    public string CertificateSubject { get; set; } = string.Empty;

    /// <summary>Round-trip ("o") formatted UTC instant; empty when no certificate is stored.</summary>
    [MaxLength(64)]
    public string CertificateNotBefore { get; set; } = string.Empty;

    /// <summary>Round-trip ("o") formatted UTC instant; empty when no certificate is stored.</summary>
    [MaxLength(64)]
    public string CertificateNotAfter { get; set; } = string.Empty;

    /// <summary>One of <see cref="CertificateSources.All"/>.</summary>
    [MaxLength(32)]
    public string CertificateSource { get; set; } = string.Empty;

    [MaxLength(64)]
    public string UpdatedAt { get; set; } = string.Empty;

    /// <summary>
    /// The administrator who last replaced the certificate. Null for the
    /// self-signed certificate the server generates for itself on first run,
    /// which no user asked for.
    /// </summary>
    [JsonConverter(typeof(UlidJsonConverter))]
    public Ulid? UpdatedByUserId { get; set; }
}
