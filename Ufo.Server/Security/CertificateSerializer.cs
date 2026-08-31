using System.Security.Cryptography.X509Certificates;

namespace Ufo.Server.Security;

/// <summary>
/// The one place PKCS#12 archives are turned into certificates.
/// </summary>
/// <remarks>
/// Centralised for the key-storage flags rather than for the single call. The
/// certificate has to survive being handed to Kestrel's TLS handshake and being
/// re-exported when it is stored, and getting those flags wrong produces a
/// certificate that loads cleanly and then fails at handshake time on one
/// platform only.
/// </remarks>
public static class CertificateSerializer
{
    /// <summary>
    /// Loads a PKCS#12 archive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="X509KeyStorageFlags.Exportable"/> is required because the
    /// generated certificate is re-exported on its way into the database.
    /// </para>
    /// <para>
    /// <c>EphemeralKeySet</c> is deliberately not used: on Windows an ephemeral
    /// private key cannot be used by SChannel, so the certificate would load and
    /// then fail every TLS handshake.
    /// </para>
    /// </remarks>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// The bytes are not a PKCS#12 archive, or the password is wrong.
    /// </exception>
    public static X509Certificate2 FromPkcs12(byte[] pkcs12Bytes, string? password = null) =>
        X509CertificateLoader.LoadPkcs12(pkcs12Bytes, password, X509KeyStorageFlags.Exportable);

    /// <summary>
    /// Loads every certificate in a PKCS#12 archive, not just the leaf.
    /// </summary>
    /// <remarks>
    /// A real certificate arrives bundled with its intermediates. Loading only
    /// the leaf would silently discard them on the round trip through the
    /// database, so the whole collection is kept.
    /// </remarks>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// The bytes are not a PKCS#12 archive, or the password is wrong.
    /// </exception>
    public static X509Certificate2Collection CollectionFromPkcs12(byte[] pkcs12Bytes, string? password = null) =>
        X509CertificateLoader.LoadPkcs12Collection(pkcs12Bytes, password, X509KeyStorageFlags.Exportable);
}
