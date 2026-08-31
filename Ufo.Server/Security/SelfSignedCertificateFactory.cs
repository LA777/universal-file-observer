using Microsoft.Extensions.Options;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Ufo.Abstractions.Options;

namespace Ufo.Server.Security;

/// <summary>
/// Builds the certificate the server presents when nobody has supplied one.
/// </summary>
public interface ISelfSignedCertificateFactory
{
    X509Certificate2 Create();
}

public class SelfSignedCertificateFactory : ISelfSignedCertificateFactory
{
    private const int KeySizeInBits = 2048;

    /// <summary>
    /// 825 days: the longest lifetime browsers will accept for a server
    /// certificate, so the generated one does not have to be replaced sooner
    /// than any client will tolerate.
    /// </summary>
    private const int ValidityInDays = 825;

    private readonly IOptions<UfoHostOptions> _hostOptions;
    private readonly ILogger<SelfSignedCertificateFactory> _logger;

    public SelfSignedCertificateFactory(
        IOptions<UfoHostOptions> hostOptions,
        ILogger<SelfSignedCertificateFactory> logger)
    {
        _hostOptions = hostOptions ?? throw new ArgumentNullException(nameof(hostOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public X509Certificate2 Create()
    {
        using var rsa = RSA.Create(KeySizeInBits);

        var hostName = ResolveHostName();
        var request = new CertificateRequest(
            $"CN={hostName}, O=UFO, OU=Universal File Observer",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        // Not a CA: a leaf certificate that claimed it could sign others would be
        // rejected outright by some clients even after the user trusts it.
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));

        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            true));

        // Without serverAuth, browsers reject the certificate no matter how it
        // was trusted.
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1")],
            false));

        request.CertificateExtensions.Add(BuildSubjectAlternativeNames(hostName));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        // Backdated by an hour so a client whose clock runs slightly behind the
        // server does not reject a certificate that was valid the moment it was
        // created.
        var notBefore = DateTimeOffset.UtcNow.AddHours(-1);
        var notAfter = notBefore.AddDays(ValidityInDays);

        using var certificate = request.CreateSelfSigned(notBefore, notAfter);

        _logger.LogInformation(
            "Generated a self-signed certificate for {HostName}, valid until {NotAfter:u}.",
            hostName,
            notAfter);

        // Round-tripped through PKCS#12 rather than returned directly: on Windows
        // a certificate straight out of CreateSelfSigned holds an ephemeral key
        // that Kestrel's TLS handshake cannot use.
        return CertificateSerializer.FromPkcs12(certificate.Export(X509ContentType.Pkcs12));
    }

    /// <summary>
    /// Names and addresses this certificate should be valid for: loopback, the
    /// machine's own host name, every non-loopback address it currently holds,
    /// and anything named in
    /// <see cref="UfoHostOptions.CertificateSubjectAlternativeNames"/>.
    /// </summary>
    /// <remarks>
    /// The configured entries are what make this usable from a container. A
    /// container can only enumerate its own loopback, container id and bridge
    /// address, so a LAN user reaching <c>https://192.168.x.y:8443</c> would get
    /// a host-name mismatch - a harder failure than an untrusted issuer, and one
    /// no amount of trusting the certificate fixes. The deployment has to name
    /// that address, because nothing inside the container can discover it.
    /// </remarks>
    private X509Extension BuildSubjectAlternativeNames(string hostName)
    {
        var subjectAlternativeNameBuilder = new SubjectAlternativeNameBuilder();

        subjectAlternativeNameBuilder.AddDnsName("localhost");
        subjectAlternativeNameBuilder.AddIpAddress(IPAddress.Loopback);
        subjectAlternativeNameBuilder.AddIpAddress(IPAddress.IPv6Loopback);

        if (!string.Equals(hostName, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            subjectAlternativeNameBuilder.AddDnsName(hostName);
        }

        foreach (var localAddress in ResolveLocalAddresses())
        {
            subjectAlternativeNameBuilder.AddIpAddress(localAddress);
        }

        foreach (var configuredName in _hostOptions.Value.CertificateSubjectAlternativeNames)
        {
            if (string.IsNullOrWhiteSpace(configuredName))
            {
                continue;
            }

            var trimmedName = configuredName.Trim();

            // An address has to go in as an IP entry: a browser matches a literal
            // address in the URL against iPAddress, never against dNSName.
            if (IPAddress.TryParse(trimmedName, out var configuredAddress))
            {
                subjectAlternativeNameBuilder.AddIpAddress(configuredAddress);
            }
            else
            {
                subjectAlternativeNameBuilder.AddDnsName(trimmedName);
            }

            _logger.LogInformation("Naming {ConfiguredName} in the generated certificate.", trimmedName);
        }

        return subjectAlternativeNameBuilder.Build();
    }

    private string ResolveHostName()
    {
        try
        {
            var hostName = Dns.GetHostName();
            return string.IsNullOrWhiteSpace(hostName) ? "localhost" : hostName;
        }
        catch (Exception exception)
        {
            // A container with no resolvable host name still needs a certificate.
            _logger.LogWarning(exception, "Could not resolve the host name. Falling back to 'localhost'.");
            return "localhost";
        }
    }

    private IEnumerable<IPAddress> ResolveLocalAddresses()
    {
        var addresses = new List<IPAddress>();

        try
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                foreach (var unicastAddress in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    var address = unicastAddress.Address;

                    if (IPAddress.IsLoopback(address))
                    {
                        // Already added explicitly above.
                        continue;
                    }

                    // Link-local IPv6 carries a scope id that does not belong in a
                    // certificate and is never what a browser connects to.
                    if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.IsIPv6LinkLocal)
                    {
                        continue;
                    }

                    if (address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                    {
                        addresses.Add(address);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            // A certificate valid only for localhost still lets the desktop host
            // serve HTTPS, so this is a degradation rather than a failure.
            _logger.LogWarning(exception, "Could not enumerate local addresses for the certificate's subject alternative names.");
        }

        return addresses.Distinct();
    }
}
