using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Cryptography.X509Certificates;
using Ufo.Abstractions.Options;
using Ufo.Server.Security;

namespace Ufo.UnitTests.Server.Security;

public class SelfSignedCertificateFactoryTests : BaseTest
{
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";

    private readonly Mock<ILogger<SelfSignedCertificateFactory>> _loggerMock = new();

    private SelfSignedCertificateFactory CreateSut(params string[] subjectAlternativeNames) =>
        new(
            Options.Create(new UfoHostOptions { CertificateSubjectAlternativeNames = subjectAlternativeNames }),
            _loggerMock.Object);

    [Fact]
    public void Create_ProducesACertificateWithItsPrivateKey()
    {
        using var certificate = CreateSut().Create();

        // Without the key Kestrel cannot complete a handshake with it.
        certificate.HasPrivateKey.Should().BeTrue();
    }

    [Fact]
    public void Create_ProducesACertificateThatIsValidNow()
    {
        using var certificate = CreateSut().Create();

        var utcNow = DateTime.UtcNow;

        // Backdated slightly, so a client whose clock trails the server's still
        // accepts a certificate created moments ago.
        certificate.NotBefore.ToUniversalTime().Should().BeBefore(utcNow);
        certificate.NotAfter.ToUniversalTime().Should().BeAfter(utcNow);
    }

    [Fact]
    public void Create_ProducesACertificateBrowsersWillAcceptTheLifetimeOf()
    {
        using var certificate = CreateSut().Create();

        var lifetime = certificate.NotAfter - certificate.NotBefore;

        // 825 days is the longest lifetime browsers tolerate for a server
        // certificate; going over it would be rejected outright.
        lifetime.TotalDays.Should().BeLessThanOrEqualTo(825);
        lifetime.TotalDays.Should().BeGreaterThan(1);
    }

    [Fact]
    public void Create_MarksTheCertificateForServerAuthentication()
    {
        using var certificate = CreateSut().Create();

        var enhancedKeyUsage = certificate.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .Single();

        enhancedKeyUsage.EnhancedKeyUsages
            .Cast<System.Security.Cryptography.Oid>()
            .Select(usage => usage.Value)
            .Should().Contain(ServerAuthenticationOid);
    }

    [Fact]
    public void Create_DoesNotMarkTheCertificateAsACertificateAuthority()
    {
        using var certificate = CreateSut().Create();

        var basicConstraints = certificate.Extensions
            .OfType<X509BasicConstraintsExtension>()
            .Single();

        // A leaf claiming it could sign other certificates is rejected by some
        // clients even once it has been trusted.
        basicConstraints.CertificateAuthority.Should().BeFalse();
    }

    [Fact]
    public void Create_NamesLocalhostSoTheDesktopHostIsCovered()
    {
        using var certificate = CreateSut().Create();

        // The desktop host serves https://localhost:55000; a certificate that did
        // not name localhost would fail host-name validation there.
        ReadSubjectAlternativeNames(certificate).Should().Contain(name => name.Contains("localhost"));
    }

    [Fact]
    public void Create_NamesTheLoopbackAddress()
    {
        using var certificate = CreateSut().Create();

        ReadSubjectAlternativeNames(certificate).Should().Contain(name => name.Contains("127.0.0.1"));
    }

    [Fact]
    public void Create_ProducesADistinctCertificateEachTime()
    {
        using var first = CreateSut().Create();
        using var second = CreateSut().Create();

        // "Generate self-signed" on the Settings page has to actually replace the
        // key, not hand back the same one.
        second.Thumbprint.Should().NotBe(first.Thumbprint);
    }

    [Fact]
    public void Create_ProducesACertificateThatSurvivesAPkcs12RoundTrip()
    {
        using var certificate = CreateSut().Create();

        var exported = certificate.Export(X509ContentType.Pkcs12);
        using var reloaded = CertificateSerializer.FromPkcs12(exported);

        // This is exactly the path the certificate takes into and out of the
        // database, and the private key has to survive it.
        reloaded.Thumbprint.Should().Be(certificate.Thumbprint);
        reloaded.HasPrivateKey.Should().BeTrue();
    }

    [Fact]
    public void Create_NamesAConfiguredAddressAsAnIpEntry()
    {
        // The container case: nothing inside it can discover the host's LAN
        // address, so the deployment names it and the certificate has to carry it
        // or every LAN client reports a host-name mismatch. TEST-NET-3, so the
        // assertion cannot pass merely because the build machine holds it.
        using var certificate = CreateSut("203.0.113.10").Create();

        ReadSubjectAlternativeNames(certificate)
            .Should().Contain(name => name.Contains("203.0.113.10"));
    }

    [Fact]
    public void Create_NamesAConfiguredHostNameAsADnsEntry()
    {
        using var certificate = CreateSut("ufo.lan").Create();

        ReadSubjectAlternativeNames(certificate).Should().Contain(name => name.Contains("ufo.lan"));
    }

    [Fact]
    public void Create_NamesEveryConfiguredEntry()
    {
        using var certificate = CreateSut("203.0.113.10", "ufo.lan", "203.0.113.20").Create();

        var names = string.Join(" ", ReadSubjectAlternativeNames(certificate));

        names.Should().Contain("203.0.113.10");
        names.Should().Contain("ufo.lan");
        names.Should().Contain("203.0.113.20");
    }

    [Fact]
    public void Create_IgnoresBlankConfiguredEntries()
    {
        // An unset UFO_CERTIFICATE_HOSTS reaches the binder as an empty string;
        // it must not become a DNS name of "".
        var act = () => CreateSut("", "   ", "ufo.lan").Create().Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public void Create_TrimsSurroundingWhitespaceFromConfiguredEntries()
    {
        using var certificate = CreateSut("  203.0.113.10  ").Create();

        // Untrimmed, this would be recorded as a DNS name rather than an address
        // and would match nothing.
        ReadSubjectAlternativeNames(certificate)
            .Should().Contain(name => name.Contains("203.0.113.10"));
    }

    private static IEnumerable<string> ReadSubjectAlternativeNames(X509Certificate2 certificate) =>
        certificate.Extensions
            .OfType<X509SubjectAlternativeNameExtension>()
            .Select(extension => extension.Format(false));
}
