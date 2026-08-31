using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Ufo.Abstractions;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.Options;
using Ufo.Abstractions.Requests;
using Ufo.Server.Security;
using Ufo.Server.Services;

namespace Ufo.UnitTests.Server.Services;

public class ServerCertificateServiceTests : BaseTest
{
    private readonly Mock<IServerSettingsRepository> _serverSettingsRepositoryMock = new();
    private readonly Mock<ICertificateProtector> _certificateProtectorMock = new();
    private readonly Mock<ISelfSignedCertificateFactory> _selfSignedCertificateFactoryMock = new();
    private readonly Mock<IServerCertificateProvider> _serverCertificateProviderMock = new();
    private readonly Mock<IUserService> _userServiceMock = new();
    private readonly Mock<ILogger<ServerCertificateService>> _loggerMock = new();

    private readonly Ulid _userId = Ulid.NewUlid();
    private readonly UfoHostOptions _hostOptions = new() { EnableHttps = true };

    public ServerCertificateServiceTests()
    {
        // A pass-through protector: the sealing itself is covered by
        // CertificateProtectorTests, so these tests only care that it is used.
        _certificateProtectorMock
            .Setup(protector => protector.Protect(It.IsAny<byte[]>()))
            .Returns<byte[]>(plaintext => [.. new byte[] { 0xFF }, .. plaintext]);
        _certificateProtectorMock
            .Setup(protector => protector.Unprotect(It.IsAny<byte[]>()))
            .Returns<byte[]>(sealedBlob => sealedBlob[1..]);

        _serverSettingsRepositoryMock
            .Setup(repository => repository.SaveCertificateAsync(
                It.IsAny<ServerSettingsEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServerResult { Result = Result.Success });

        _selfSignedCertificateFactoryMock
            .Setup(factory => factory.Create())
            .Returns(() => CreateRealCertificate());
    }

    private ServerCertificateService CreateSut() =>
        new(
            _serverSettingsRepositoryMock.Object,
            _certificateProtectorMock.Object,
            _selfSignedCertificateFactoryMock.Object,
            _serverCertificateProviderMock.Object,
            _userServiceMock.Object,
            Options.Create(_hostOptions),
            _loggerMock.Object);

    private static X509Certificate2 CreateRealCertificate(params string[] subjectAlternativeNames) =>
        new SelfSignedCertificateFactory(
            Options.Create(new UfoHostOptions { CertificateSubjectAlternativeNames = subjectAlternativeNames }),
            Mock.Of<ILogger<SelfSignedCertificateFactory>>()).Create();

    private void SetupCaller(bool isAdmin) =>
        _userServiceMock
            .Setup(service => service.GetUserByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserEntity { Id = _userId, Name = "tester", IsAdmin = isAdmin });

    private void SetupStoredSettings(ServerSettingsEntity? storedSettings) =>
        _serverSettingsRepositoryMock
            .Setup(repository => repository.GetServerSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedSettings);

    private void VerifyNothingWasStored() =>
        _serverSettingsRepositoryMock.Verify(
            repository => repository.SaveCertificateAsync(
                It.IsAny<ServerSettingsEntity>(), It.IsAny<CancellationToken>()),
            Times.Never);

    #region Authorisation

    [Fact]
    public async Task ReplaceCertificateAsync_WhenTheCallerIsNotAnAdministrator_IsRejected()
    {
        SetupCaller(isAdmin: false);
        var request = new ServerCertificateRequest { PfxBase64 = ValidPfxBase64() };

        var result = await CreateSut().ReplaceCertificateAsync(request, _userId, CancellationToken.None);

        result.Result.Should().Be(Result.Error);
        result.Message.Should().Contain("administrator");
        VerifyNothingWasStored();
        _serverCertificateProviderMock.Verify(provider => provider.Set(It.IsAny<X509Certificate2>()), Times.Never);
    }

    [Fact]
    public async Task GenerateSelfSignedCertificateAsync_WhenTheCallerIsNotAnAdministrator_IsRejected()
    {
        SetupCaller(isAdmin: false);

        var result = await CreateSut().GenerateSelfSignedCertificateAsync(_userId, CancellationToken.None);

        result.Result.Should().Be(Result.Error);
        VerifyNothingWasStored();
    }

    [Fact]
    public async Task ReplaceCertificateAsync_ChecksTheDatabaseRatherThanTrustingTheToken()
    {
        SetupCaller(isAdmin: false);

        await CreateSut().ReplaceCertificateAsync(
            new ServerCertificateRequest { PfxBase64 = ValidPfxBase64() }, _userId, CancellationToken.None);

        // A token issued before a demotion stays valid for up to seven days, so
        // the flag has to be re-read on every write.
        _userServiceMock.Verify(
            service => service.GetUserByIdAsync(_userId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ReplaceCertificateAsync_WhenTheCallerNoLongerExists_IsRefusedRatherThanThrowing()
    {
        // A signed token stays valid until it expires whether or not the account
        // still exists, so this is reachable in normal operation - and the user
        // lookup throws rather than returning null for an unknown id.
        _userServiceMock
            .Setup(service => service.GetUserByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception($"User with ID ({_userId}) was not found."));

        var result = await CreateSut().ReplaceCertificateAsync(
            new ServerCertificateRequest { PfxBase64 = ValidPfxBase64() }, _userId, CancellationToken.None);

        // Fails closed: a refusal, not a 500.
        result.Result.Should().Be(Result.Error);
        result.Message.Should().Contain("administrator");
        VerifyNothingWasStored();
    }

    [Fact]
    public async Task GetCertificateAsync_WhenTheCallerNoLongerExists_IsRefused()
    {
        _userServiceMock
            .Setup(service => service.GetUserByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception($"User with ID ({_userId}) was not found."));
        SetupStoredSettings(null);

        var certificate = await CreateSut().GetCertificateAsync(_userId, CancellationToken.None);

        // Null is what the controller turns into a 403. Failing closed here too,
        // rather than handing back a description of the server's certificate.
        certificate.Should().BeNull();
    }

    #endregion

    #region Upload validation

    [Fact]
    public async Task ReplaceCertificateAsync_WithAnEmptyBody_IsRejected()
    {
        SetupCaller(isAdmin: true);

        var result = await CreateSut().ReplaceCertificateAsync(
            new ServerCertificateRequest { PfxBase64 = null }, _userId, CancellationToken.None);

        result.Result.Should().Be(Result.Error);
        VerifyNothingWasStored();
    }

    [Fact]
    public async Task ReplaceCertificateAsync_WithSomethingThatIsNotBase64_IsRejected()
    {
        SetupCaller(isAdmin: true);

        var result = await CreateSut().ReplaceCertificateAsync(
            new ServerCertificateRequest { PfxBase64 = "not base64 !!!" }, _userId, CancellationToken.None);

        result.Result.Should().Be(Result.Error);
        result.Message.Should().Contain("decoded");
        VerifyNothingWasStored();
    }

    [Fact]
    public async Task ReplaceCertificateAsync_WithAnOversizeBody_IsRejectedBeforeParsing()
    {
        SetupCaller(isAdmin: true);
        var oversize = Convert.ToBase64String(new byte[300 * 1024]);

        var result = await CreateSut().ReplaceCertificateAsync(
            new ServerCertificateRequest { PfxBase64 = oversize }, _userId, CancellationToken.None);

        result.Result.Should().Be(Result.Error);
        result.Message.Should().Contain("larger than");
        VerifyNothingWasStored();
    }

    [Fact]
    public async Task ReplaceCertificateAsync_WithBytesThatAreNotAnArchive_IsRejected()
    {
        SetupCaller(isAdmin: true);
        var notAnArchive = Convert.ToBase64String("this is a text file"u8.ToArray());

        var result = await CreateSut().ReplaceCertificateAsync(
            new ServerCertificateRequest { PfxBase64 = notAnArchive }, _userId, CancellationToken.None);

        result.Result.Should().Be(Result.Error);
        VerifyNothingWasStored();
    }

    [Fact]
    public async Task ReplaceCertificateAsync_WithTheWrongPassphrase_IsRejected()
    {
        SetupCaller(isAdmin: true);
        using var certificate = CreateRealCertificate();
        var protectedArchive = Convert.ToBase64String(certificate.Export(X509ContentType.Pkcs12, "correct-passphrase"));

        var result = await CreateSut().ReplaceCertificateAsync(
            new ServerCertificateRequest { PfxBase64 = protectedArchive, Passphrase = "wrong-passphrase" },
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Error);
        VerifyNothingWasStored();
    }

    [Fact]
    public async Task ReplaceCertificateAsync_WithAPassphraseProtectedArchive_IsAccepted()
    {
        SetupCaller(isAdmin: true);
        using var certificate = CreateRealCertificate();
        var protectedArchive = Convert.ToBase64String(certificate.Export(X509ContentType.Pkcs12, "correct-passphrase"));

        var result = await CreateSut().ReplaceCertificateAsync(
            new ServerCertificateRequest { PfxBase64 = protectedArchive, Passphrase = "correct-passphrase" },
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Success);
    }

    [Fact]
    public async Task ReplaceCertificateAsync_WithACertificateThatHasNoPrivateKey_IsRejected()
    {
        SetupCaller(isAdmin: true);
        using var certificate = CreateRealCertificate();

        // The public certificate on its own, as an "export" that forgot the key.
        using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));
        var withoutKey = Convert.ToBase64String(publicOnly.Export(X509ContentType.Pkcs12));

        var result = await CreateSut().ReplaceCertificateAsync(
            new ServerCertificateRequest { PfxBase64 = withoutKey }, _userId, CancellationToken.None);

        result.Result.Should().Be(Result.Error);
        result.Message.Should().Contain("private key");
        VerifyNothingWasStored();
    }

    #endregion

    #region Storage

    [Fact]
    public async Task ReplaceCertificateAsync_SealsTheArchiveBeforeStoringIt()
    {
        SetupCaller(isAdmin: true);
        ServerSettingsEntity? stored = null;
        _serverSettingsRepositoryMock
            .Setup(repository => repository.SaveCertificateAsync(
                It.IsAny<ServerSettingsEntity>(), It.IsAny<CancellationToken>()))
            .Callback<ServerSettingsEntity, CancellationToken>((entity, _) => stored = entity)
            .ReturnsAsync(new ServerResult { Result = Result.Success });

        await CreateSut().ReplaceCertificateAsync(
            new ServerCertificateRequest { PfxBase64 = ValidPfxBase64() }, _userId, CancellationToken.None);

        // The database must never receive a raw PKCS#12: this asserts the blob
        // went through the protector, whose marker byte is 0xFF here.
        _certificateProtectorMock.Verify(protector => protector.Protect(It.IsAny<byte[]>()), Times.Once);
        stored.Should().NotBeNull();
        stored!.CertificatePfx.Should().NotBeNull();
        stored.CertificatePfx![0].Should().Be(0xFF);
        stored.CertificateSource.Should().Be(CertificateSources.UserSupplied);
        stored.UpdatedByUserId.Should().Be(_userId);
    }

    [Fact]
    public async Task ReplaceCertificateAsync_PublishesTheCertificateOnlyAfterASuccessfulWrite()
    {
        SetupCaller(isAdmin: true);
        _serverSettingsRepositoryMock
            .Setup(repository => repository.SaveCertificateAsync(
                It.IsAny<ServerSettingsEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServerResult { Result = Result.Error });

        var result = await CreateSut().ReplaceCertificateAsync(
            new ServerCertificateRequest { PfxBase64 = ValidPfxBase64() }, _userId, CancellationToken.None);

        // Otherwise the listener would serve a certificate that vanishes on the
        // next restart.
        result.Result.Should().Be(Result.Error);
        _serverCertificateProviderMock.Verify(provider => provider.Set(It.IsAny<X509Certificate2>()), Times.Never);
    }

    [Fact]
    public async Task ReplaceCertificateAsync_PublishesTheCertificateForTheNextConnection()
    {
        SetupCaller(isAdmin: true);

        await CreateSut().ReplaceCertificateAsync(
            new ServerCertificateRequest { PfxBase64 = ValidPfxBase64() }, _userId, CancellationToken.None);

        _serverCertificateProviderMock.Verify(
            provider => provider.Set(It.Is<X509Certificate2>(certificate => certificate.HasPrivateKey)),
            Times.Once);
    }

    #endregion

    #region Reading

    [Fact]
    public async Task GetCertificateAsync_WhenNothingIsStored_ReportsThatTlsIsNotConfigured()
    {
        SetupCaller(isAdmin: true);
        SetupStoredSettings(null);

        var certificate = await CreateSut().GetCertificateAsync(_userId, CancellationToken.None);

        // A host behind a TLS-terminating proxy is a valid deployment, not a fault.
        certificate.Should().NotBeNull();
        certificate!.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public async Task GetCertificateAsync_ForANonAdministrator_IsRefused()
    {
        SetupCaller(isAdmin: false);
        SetupStoredSettings(new ServerSettingsEntity
        {
            CertificateThumbprint = "ABC123",
            CertificateSubject = "CN=secret-host",
            CertificateNotAfter = DateTimeOffset.UtcNow.AddYears(1).ToString("o"),
            CertificateSource = CertificateSources.SelfSigned
        });

        var certificate = await CreateSut().GetCertificateAsync(_userId, CancellationToken.None);

        // Hidden in the UI is not the same as unreachable. This is the half that
        // holds when someone calls the endpoint directly.
        certificate.Should().BeNull();
    }

    [Fact]
    public async Task GetCertificateAsync_FlagsAnExpiredCertificate()
    {
        SetupCaller(isAdmin: true);
        SetupStoredSettings(new ServerSettingsEntity
        {
            CertificateThumbprint = "ABC123",
            CertificateSubject = "CN=old",
            CertificateNotAfter = DateTimeOffset.UtcNow.AddDays(-1).ToString("o"),
            CertificateSource = CertificateSources.UserSupplied
        });

        var certificate = await CreateSut().GetCertificateAsync(_userId, CancellationToken.None);

        // Computed on the server so it does not depend on the browser's clock.
        certificate.Should().NotBeNull();
        certificate!.IsConfigured.Should().BeTrue();
        certificate.IsExpired.Should().BeTrue();
    }

    [Fact]
    public async Task GetCertificateAsync_NeverReturnsTheStoredBlob()
    {
        SetupCaller(isAdmin: true);
        SetupStoredSettings(new ServerSettingsEntity
        {
            CertificatePfx = [1, 2, 3, 4],
            CertificateThumbprint = "ABC123",
            CertificateNotAfter = DateTimeOffset.UtcNow.AddYears(1).ToString("o"),
            CertificateSource = CertificateSources.SelfSigned
        });

        var certificate = await CreateSut().GetCertificateAsync(_userId, CancellationToken.None);

        // The DTO has no field for it, which is the point; this guards against
        // one being added carelessly later.
        certificate.Should().NotBeNull();
        certificate!.GetType().GetProperties()
            .Should().NotContain(property => property.PropertyType == typeof(byte[]));
    }

    [Fact]
    public async Task GetCertificateAsync_OnAHostThatDoesNotServeTls_ReportsItIsNotConfigured()
    {
        SetupCaller(isAdmin: true);
        _hostOptions.EnableHttps = false;
        // A row left over from a run when TLS was still on.
        SetupStoredSettings(new ServerSettingsEntity
        {
            CertificateThumbprint = "ABC123",
            CertificateNotAfter = DateTimeOffset.UtcNow.AddYears(1).ToString("o"),
            CertificateSource = CertificateSources.SelfSigned
        });

        var certificate = await CreateSut().GetCertificateAsync(_userId, CancellationToken.None);

        // What matters is whether this host presents a certificate, not whether a
        // row survives in its database.
        certificate.Should().NotBeNull();
        certificate!.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public async Task ReplaceCertificateAsync_OnAHostThatDoesNotServeTls_IsRefused()
    {
        SetupCaller(isAdmin: true);
        _hostOptions.EnableHttps = false;

        var result = await CreateSut().ReplaceCertificateAsync(
            new ServerCertificateRequest { PfxBase64 = ValidPfxBase64() }, _userId, CancellationToken.None);

        // Storing it would report success for a change nothing can ever apply.
        result.Result.Should().Be(Result.Error);
        result.Message.Should().Contain("does not serve HTTPS");
        VerifyNothingWasStored();
    }

    [Fact]
    public async Task GenerateSelfSignedCertificateAsync_OnAHostThatDoesNotServeTls_IsRefused()
    {
        SetupCaller(isAdmin: true);
        _hostOptions.EnableHttps = false;

        var result = await CreateSut().GenerateSelfSignedCertificateAsync(_userId, CancellationToken.None);

        result.Result.Should().Be(Result.Error);
        VerifyNothingWasStored();
        _selfSignedCertificateFactoryMock.Verify(factory => factory.Create(), Times.Never);
    }

    #endregion

    #region Startup bootstrap

    [Fact]
    public async Task EnsureCertificateAsync_OnAFreshInstallation_GeneratesAndStoresOne()
    {
        SetupStoredSettings(null);

        await CreateSut().EnsureCertificateAsync(CancellationToken.None);

        _selfSignedCertificateFactoryMock.Verify(factory => factory.Create(), Times.Once);
        _serverSettingsRepositoryMock.Verify(
            repository => repository.SaveCertificateAsync(
                It.Is<ServerSettingsEntity>(entity =>
                    entity.CertificateSource == CertificateSources.SelfSigned
                    && entity.UpdatedByUserId == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _serverCertificateProviderMock.Verify(provider => provider.Set(It.IsAny<X509Certificate2>()), Times.Once);
    }

    [Fact]
    public async Task EnsureCertificateAsync_WithAUsableStoredCertificate_ServesItWithoutRegenerating()
    {
        using var certificate = CreateRealCertificate();
        SetupStoredSettings(new ServerSettingsEntity
        {
            CertificatePfx = [0xFF, .. certificate.Export(X509ContentType.Pkcs12)],
            CertificateThumbprint = certificate.Thumbprint,
            CertificateSource = CertificateSources.UserSupplied
        });

        await CreateSut().EnsureCertificateAsync(CancellationToken.None);

        _selfSignedCertificateFactoryMock.Verify(factory => factory.Create(), Times.Never);
        _serverCertificateProviderMock.Verify(
            provider => provider.Set(It.Is<X509Certificate2>(served => served.Thumbprint == certificate.Thumbprint)),
            Times.Once);
    }

    [Fact]
    public async Task EnsureCertificateAsync_WhenTheStoredBlobCannotBeDecrypted_GeneratesAReplacement()
    {
        SetupStoredSettings(new ServerSettingsEntity
        {
            CertificatePfx = [9, 9, 9],
            CertificateSource = CertificateSources.SelfSigned
        });
        _certificateProtectorMock
            .Setup(protector => protector.Unprotect(It.IsAny<byte[]>()))
            .Throws(new CryptographicException("protection key does not match"));

        await CreateSut().EnsureCertificateAsync(CancellationToken.None);

        // The "data directory restored without its key file" case: the blob is
        // unrecoverable, so a new certificate is the only way back to a working
        // listener.
        _selfSignedCertificateFactoryMock.Verify(factory => factory.Create(), Times.Once);
        _serverCertificateProviderMock.Verify(provider => provider.Set(It.IsAny<X509Certificate2>()), Times.Once);
    }

    [Fact]
    public async Task EnsureCertificateAsync_KeepsAnExpiredUploadedCertificateRatherThanReplacingIt()
    {
        // Built expired on purpose, to stand in for an uploaded certificate that
        // has run out.
        using var expired = CreateExpiredCertificate();
        SetupStoredSettings(new ServerSettingsEntity
        {
            CertificatePfx = [0xFF, .. expired.Export(X509ContentType.Pkcs12)],
            CertificateThumbprint = expired.Thumbprint,
            CertificateSource = CertificateSources.UserSupplied
        });

        await CreateSut().EnsureCertificateAsync(CancellationToken.None);

        // Silently swapping an administrator's certificate for a self-signed one
        // would change the identity clients pin against without anyone asking.
        _selfSignedCertificateFactoryMock.Verify(factory => factory.Create(), Times.Never);
        _serverCertificateProviderMock.Verify(
            provider => provider.Set(It.Is<X509Certificate2>(served => served.Thumbprint == expired.Thumbprint)),
            Times.Once);
    }

    [Fact]
    public async Task EnsureCertificateAsync_ReissuesASelfSignedCertificateThatDoesNotNameAConfiguredAddress()
    {
        // The certificate was generated before UFO_CERTIFICATE_HOST was set, so
        // it does not name the address people actually browse to. The address is
        // from TEST-NET-3, which is reserved for documentation and so can never
        // be one this machine already holds - otherwise the factory would pick it
        // up unprompted and the test would assert nothing.
        using var certificate = CreateRealCertificate();
        _hostOptions.CertificateSubjectAlternativeNames = ["203.0.113.10"];
        SetupStoredSettings(new ServerSettingsEntity
        {
            CertificatePfx = [0xFF, .. certificate.Export(X509ContentType.Pkcs12)],
            CertificateThumbprint = certificate.Thumbprint,
            CertificateSource = CertificateSources.SelfSigned
        });

        await CreateSut().EnsureCertificateAsync(CancellationToken.None);

        // Serving it would give every LAN client a host-name mismatch, which
        // trusting the certificate does not fix.
        _selfSignedCertificateFactoryMock.Verify(factory => factory.Create(), Times.Once);
    }

    [Fact]
    public async Task EnsureCertificateAsync_KeepsASelfSignedCertificateThatAlreadyNamesTheConfiguredAddress()
    {
        using var certificate = CreateRealCertificate("203.0.113.10");
        _hostOptions.CertificateSubjectAlternativeNames = ["203.0.113.10"];
        SetupStoredSettings(new ServerSettingsEntity
        {
            CertificatePfx = [0xFF, .. certificate.Export(X509ContentType.Pkcs12)],
            CertificateThumbprint = certificate.Thumbprint,
            CertificateSource = CertificateSources.SelfSigned
        });

        await CreateSut().EnsureCertificateAsync(CancellationToken.None);

        // Reissuing on every restart would churn the key for no reason.
        _selfSignedCertificateFactoryMock.Verify(factory => factory.Create(), Times.Never);
        _serverCertificateProviderMock.Verify(
            provider => provider.Set(It.Is<X509Certificate2>(served => served.Thumbprint == certificate.Thumbprint)),
            Times.Once);
    }

    [Fact]
    public async Task EnsureCertificateAsync_DoesNotReissueAnUploadedCertificateForAMissingName()
    {
        using var certificate = CreateRealCertificate();
        _hostOptions.CertificateSubjectAlternativeNames = ["203.0.113.10"];
        SetupStoredSettings(new ServerSettingsEntity
        {
            CertificatePfx = [0xFF, .. certificate.Export(X509ContentType.Pkcs12)],
            CertificateThumbprint = certificate.Thumbprint,
            CertificateSource = CertificateSources.UserSupplied
        });

        await CreateSut().EnsureCertificateAsync(CancellationToken.None);

        // An administrator chose this certificate. Quietly swapping it for a
        // self-signed one would change the identity clients pin against.
        _selfSignedCertificateFactoryMock.Verify(factory => factory.Create(), Times.Never);
        _serverCertificateProviderMock.Verify(
            provider => provider.Set(It.Is<X509Certificate2>(served => served.Thumbprint == certificate.Thumbprint)),
            Times.Once);
    }

    [Fact]
    public async Task EnsureCertificateAsync_WithNoConfiguredNames_KeepsWhateverIsStored()
    {
        using var certificate = CreateRealCertificate();
        SetupStoredSettings(new ServerSettingsEntity
        {
            CertificatePfx = [0xFF, .. certificate.Export(X509ContentType.Pkcs12)],
            CertificateThumbprint = certificate.Thumbprint,
            CertificateSource = CertificateSources.SelfSigned
        });

        await CreateSut().EnsureCertificateAsync(CancellationToken.None);

        // Only configured names drive a reissue; addresses discovered from the
        // machine come and go and would churn the certificate.
        _selfSignedCertificateFactoryMock.Verify(factory => factory.Create(), Times.Never);
    }

    [Fact]
    public async Task EnsureCertificateAsync_ReplacesAnExpiredSelfSignedCertificate()
    {
        using var expired = CreateExpiredCertificate();
        SetupStoredSettings(new ServerSettingsEntity
        {
            CertificatePfx = [0xFF, .. expired.Export(X509ContentType.Pkcs12)],
            CertificateThumbprint = expired.Thumbprint,
            CertificateSource = CertificateSources.SelfSigned
        });

        await CreateSut().EnsureCertificateAsync(CancellationToken.None);

        // Nobody chose this one, so renewing it needs no permission.
        _selfSignedCertificateFactoryMock.Verify(factory => factory.Create(), Times.Once);
    }

    #endregion

    private static string ValidPfxBase64()
    {
        using var certificate = CreateRealCertificate();

        return Convert.ToBase64String(certificate.Export(X509ContentType.Pkcs12));
    }

    private static X509Certificate2 CreateExpiredCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=expired", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-30),
            DateTimeOffset.UtcNow.AddDays(-1));

        return CertificateSerializer.FromPkcs12(certificate.Export(X509ContentType.Pkcs12));
    }
}
