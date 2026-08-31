using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Cryptography;
using System.Text;
using Ufo.Abstractions.Options;
using Ufo.Server.Security;

namespace Ufo.UnitTests.Server.Security;

public class CertificateProtectorTests : BaseTest, IDisposable
{
    private readonly Mock<ILogger<CertificateProtector>> _loggerMock = new();
    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), $"ufo-protector-tests-{Ulid.NewUlid()}");

    public CertificateProtectorTests()
    {
        Directory.CreateDirectory(_dataDirectory);
    }

    private CertificateProtector CreateSut(string? dataDirectory = null) =>
        new(
            Options.Create(new UfoHostOptions { DataDirectory = dataDirectory ?? _dataDirectory }),
            _loggerMock.Object);

    private string ProtectionKeyFilePath =>
        Path.Combine(_dataDirectory, CertificateProtector.ProtectionKeyFileName);

    [Fact]
    public void Protect_ThenUnprotect_ReturnsTheOriginalBytes()
    {
        var protector = CreateSut();
        var plaintext = Encoding.UTF8.GetBytes("a pretend PKCS#12 archive");

        var roundTripped = protector.Unprotect(protector.Protect(plaintext));

        roundTripped.Should().Equal(plaintext);
    }

    [Fact]
    public void Protect_DoesNotLeaveThePlaintextInTheSealedBlob()
    {
        var protector = CreateSut();
        var plaintext = Encoding.UTF8.GetBytes("private key material");

        var sealedBlob = protector.Protect(plaintext);

        // The whole point of the class: the database must not end up holding
        // anything recognisable from the archive.
        Encoding.UTF8.GetString(sealedBlob).Should().NotContain("private key material");
        sealedBlob.Should().NotEqual(plaintext);
    }

    [Fact]
    public void Protect_UsesAFreshNonceEachTime()
    {
        var protector = CreateSut();
        var plaintext = Encoding.UTF8.GetBytes("the same input twice");

        var first = protector.Protect(plaintext);
        var second = protector.Protect(plaintext);

        // Identical ciphertexts would mean a reused nonce, which is what breaks
        // AES-GCM outright rather than merely weakening it.
        first.Should().NotEqual(second);
    }

    [Fact]
    public void Protect_PersistsTheKeySoASecondInstanceCanUnprotect()
    {
        var plaintext = Encoding.UTF8.GetBytes("survives a restart");

        var sealedBlob = CreateSut().Protect(plaintext);

        // A second instance stands in for the next run of the application.
        CreateSut().Unprotect(sealedBlob).Should().Equal(plaintext);
    }

    [Fact]
    public void Unprotect_WithADifferentProtectionKey_Throws()
    {
        var sealedBlob = CreateSut().Protect(Encoding.UTF8.GetBytes("sealed under key A"));

        var otherDataDirectory = Path.Combine(Path.GetTempPath(), $"ufo-protector-tests-{Ulid.NewUlid()}");
        Directory.CreateDirectory(otherDataDirectory);

        try
        {
            var act = () => CreateSut(otherDataDirectory).Unprotect(sealedBlob);

            // This is the "data directory restored without its key file" case,
            // which the certificate service recovers from by regenerating.
            act.Should().Throw<CryptographicException>();
        }
        finally
        {
            Directory.Delete(otherDataDirectory, true);
        }
    }

    [Fact]
    public void Unprotect_WithATamperedCiphertext_Throws()
    {
        var protector = CreateSut();
        var sealedBlob = protector.Protect(Encoding.UTF8.GetBytes("do not modify me"));

        // Flip a bit in the ciphertext, past the version, nonce and tag.
        sealedBlob[^1] ^= 0xFF;

        var act = () => protector.Unprotect(sealedBlob);

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Unprotect_WithAnUnknownFormatVersion_Throws()
    {
        var protector = CreateSut();
        var sealedBlob = protector.Protect(Encoding.UTF8.GetBytes("version one"));

        sealedBlob[0] = 99;

        var act = () => protector.Unprotect(sealedBlob);

        act.Should().Throw<CryptographicException>()
            .WithMessage("*format version*");
    }

    [Fact]
    public void Unprotect_WithATruncatedBlob_Throws()
    {
        var act = () => CreateSut().Unprotect([1, 2, 3]);

        act.Should().Throw<CryptographicException>()
            .WithMessage("*truncated*");
    }

    [Fact]
    public void Protect_WithAnUnusableKeyFile_ReplacesItRatherThanFailing()
    {
        // A key that cannot be parsed cannot have sealed anything readable, so
        // nothing is lost by regenerating it.
        File.WriteAllText(ProtectionKeyFilePath, "not hex at all");

        var protector = CreateSut();
        var plaintext = Encoding.UTF8.GetBytes("still works");

        protector.Unprotect(protector.Protect(plaintext)).Should().Equal(plaintext);
        File.ReadAllText(ProtectionKeyFilePath).Should().NotBe("not hex at all");
    }

    [Fact]
    public void Protect_DoesNotCreateAKeyFileUntilItIsUsed()
    {
        _ = CreateSut();

        // Constructed but never asked to protect anything: a host that does not
        // serve TLS should not litter its data directory with a key.
        File.Exists(ProtectionKeyFilePath).Should().BeFalse();
    }

    [Fact]
    public void ProtectionKeyFile_IsNotReadableByOtherUsers()
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows inherits the data directory's ACL instead.
            return;
        }

        CreateSut().Protect(Encoding.UTF8.GetBytes("anything"));

        var mode = File.GetUnixFileMode(ProtectionKeyFilePath);

        mode.Should().NotHaveFlag(UnixFileMode.GroupRead);
        mode.Should().NotHaveFlag(UnixFileMode.OtherRead);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, true);
        }

        GC.SuppressFinalize(this);
    }
}
