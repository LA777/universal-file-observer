using System.Security.Cryptography.X509Certificates;

namespace Ufo.Server.Security;

/// <summary>
/// Holds the certificate Kestrel presents, and lets it be replaced while the
/// server is running.
/// </summary>
/// <remarks>
/// Kestrel resolves the certificate through a per-connection selector rather
/// than binding one at startup, so an administrator who uploads a replacement
/// gets it served on the next connection instead of after a restart.
/// </remarks>
public interface IServerCertificateProvider
{
    /// <summary>
    /// The certificate to present, or <c>null</c> on a host that is not serving
    /// HTTPS. Read on the TLS handshake path, so it must never block.
    /// </summary>
    X509Certificate2? Current { get; }

    /// <summary>Publishes a certificate for every subsequent connection.</summary>
    void Set(X509Certificate2 certificate);
}

public class ServerCertificateProvider : IServerCertificateProvider
{
    private volatile X509Certificate2? _current;

    public X509Certificate2? Current => _current;

    /// <remarks>
    /// The certificate being replaced is deliberately not disposed. A handshake
    /// already in flight still holds it, and disposing it underneath that
    /// connection would fail the handshake with an error that looks nothing like
    /// its cause. Replacement is rare and the object is small, so it is left to
    /// the finalizer.
    /// </remarks>
    public void Set(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        _current = certificate;
    }
}
