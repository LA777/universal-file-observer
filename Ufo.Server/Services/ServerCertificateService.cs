using Microsoft.Extensions.Options;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Ufo.Abstractions;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.DataTransferObjects;
using Ufo.Abstractions.Options;
using Ufo.Abstractions.Requests;
using Ufo.Server.Security;

namespace Ufo.Server.Services;

public interface IServerCertificateService
{
    /// <summary>
    /// Describes the certificate the server is presenting, for the Settings page.
    /// </summary>
    Task<ServerCertificateDto> GetCertificateAsync(Ulid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the certificate with one an administrator uploaded. Takes effect
    /// on the next connection, without a restart.
    /// </summary>
    Task<ServerResult> ReplaceCertificateAsync(ServerCertificateRequest request, Ulid userId, CancellationToken cancellationToken);

    /// <summary>Generates and stores a fresh self-signed certificate.</summary>
    Task<ServerResult> GenerateSelfSignedCertificateAsync(Ulid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Startup bootstrap: publishes the stored certificate, generating and
    /// storing a self-signed one when there is nothing usable to publish.
    /// </summary>
    Task EnsureCertificateAsync(CancellationToken cancellationToken);
}

public class ServerCertificateService : IServerCertificateService
{
    /// <summary>
    /// Ceiling on an uploaded archive. A PKCS#12 with a full chain is a few
    /// kilobytes; anything approaching this is not a certificate, and the limit
    /// stops a large body being base64-decoded and parsed before that is noticed.
    /// </summary>
    private const int MaximumPfxSizeInBytes = 256 * 1024;

    private readonly IServerSettingsRepository _serverSettingsRepository;
    private readonly ICertificateProtector _certificateProtector;
    private readonly ISelfSignedCertificateFactory _selfSignedCertificateFactory;
    private readonly IServerCertificateProvider _serverCertificateProvider;
    private readonly IUserService _userService;
    private readonly IOptions<UfoHostOptions> _hostOptions;
    private readonly ILogger<ServerCertificateService> _logger;

    public ServerCertificateService(
        IServerSettingsRepository serverSettingsRepository,
        ICertificateProtector certificateProtector,
        ISelfSignedCertificateFactory selfSignedCertificateFactory,
        IServerCertificateProvider serverCertificateProvider,
        IUserService userService,
        IOptions<UfoHostOptions> hostOptions,
        ILogger<ServerCertificateService> logger)
    {
        _serverSettingsRepository = serverSettingsRepository ?? throw new ArgumentNullException(nameof(serverSettingsRepository));
        _certificateProtector = certificateProtector ?? throw new ArgumentNullException(nameof(certificateProtector));
        _selfSignedCertificateFactory = selfSignedCertificateFactory ?? throw new ArgumentNullException(nameof(selfSignedCertificateFactory));
        _serverCertificateProvider = serverCertificateProvider ?? throw new ArgumentNullException(nameof(serverCertificateProvider));
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        _hostOptions = hostOptions ?? throw new ArgumentNullException(nameof(hostOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ServerCertificateDto> GetCertificateAsync(Ulid userId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetCertificateAsync - UserId: {UserId}", userId);

        var canManage = await IsAdministratorAsync(userId, cancellationToken);
        var serverSettings = await _serverSettingsRepository.GetServerSettingsAsync(cancellationToken);

        // A host that does not serve TLS may still carry a row from an earlier
        // run, so the flag has to come from the host's configuration rather than
        // from the presence of a stored certificate. Reported as "not configured"
        // rather than as an error: terminating TLS upstream is a valid
        // deployment, not a fault.
        if (!_hostOptions.Value.EnableHttps
            || serverSettings == null
            || string.IsNullOrEmpty(serverSettings.CertificateThumbprint))
        {
            return new ServerCertificateDto { IsConfigured = false, CanManage = canManage };
        }

        var notAfter = ParseTimestamp(serverSettings.CertificateNotAfter);

        return new ServerCertificateDto
        {
            IsConfigured = true,
            Subject = serverSettings.CertificateSubject,
            Thumbprint = serverSettings.CertificateThumbprint,
            NotBefore = serverSettings.CertificateNotBefore,
            NotAfter = serverSettings.CertificateNotAfter,
            Source = serverSettings.CertificateSource,
            IsExpired = notAfter.HasValue && notAfter.Value < DateTimeOffset.UtcNow,
            UpdatedAt = serverSettings.UpdatedAt,
            CanManage = canManage
        };
    }

    public async Task<ServerResult> ReplaceCertificateAsync(ServerCertificateRequest request, Ulid userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogInformation("ReplaceCertificateAsync - UserId: {UserId}", userId);

        if (!await IsAdministratorAsync(userId, cancellationToken))
        {
            return Failure("Only an administrator can change the server certificate.");
        }

        if (!_hostOptions.Value.EnableHttps)
        {
            // Storing it would report success for a change that can never take
            // effect, because nothing on this host will ever present it.
            return Failure("This server does not serve HTTPS, so it has no certificate to replace. "
                + "TLS is terminated upstream, or Ufo__EnableHttps is switched off.");
        }

        if (string.IsNullOrWhiteSpace(request.PfxBase64))
        {
            return Failure("No certificate was supplied.");
        }

        byte[] pfxBytes;
        try
        {
            pfxBytes = Convert.FromBase64String(request.PfxBase64);
        }
        catch (FormatException)
        {
            return Failure("The certificate could not be decoded. Upload a PKCS#12 (.pfx or .p12) file.");
        }

        if (pfxBytes.Length == 0)
        {
            return Failure("The certificate file is empty.");
        }

        if (pfxBytes.Length > MaximumPfxSizeInBytes)
        {
            return Failure($"The certificate file is larger than {MaximumPfxSizeInBytes / 1024} KB, so it is not a PKCS#12 archive.");
        }

        X509Certificate2Collection collection;
        try
        {
            collection = CertificateSerializer.CollectionFromPkcs12(pfxBytes, request.Passphrase);
        }
        catch (CryptographicException exception)
        {
            // Covers both "not a PKCS#12" and "wrong passphrase"; the platform
            // does not reliably distinguish them, and saying which it was would
            // tell an attacker whether a guessed passphrase was close.
            _logger.LogWarning(exception, "Rejected an unreadable certificate upload from user: {UserId}", userId);

            return Failure("The certificate could not be opened. Check that it is a PKCS#12 (.pfx or .p12) file and that the passphrase is correct.");
        }

        // Every certificate loaded here holds an unmanaged key handle. On Windows
        // an Exportable, non-ephemeral key is written to a per-user key container
        // that only disposal releases, so a rejected upload would otherwise leak
        // one per attempt.
        try
        {
            var leafCertificate = collection.FirstOrDefault(candidate => candidate.HasPrivateKey);
            if (leafCertificate == null)
            {
                return Failure("The archive contains no private key. Export the certificate again including its key.");
            }

            var validationFailure = ValidateForServerUse(leafCertificate);
            if (validationFailure != null)
            {
                return Failure(validationFailure);
            }

            // Re-exported without a passphrase and then sealed by the protector.
            // The uploaded passphrase is therefore never stored: the blob at rest
            // is protected by the server's own key instead.
            var normalisedPfx = collection.Export(X509ContentType.Pkcs12)
                ?? throw new InvalidOperationException("Re-exporting the certificate produced no bytes.");

            return await StoreAndPublishAsync(
                leafCertificate,
                normalisedPfx,
                CertificateSources.UserSupplied,
                userId,
                cancellationToken);
        }
        finally
        {
            foreach (var loadedCertificate in collection)
            {
                loadedCertificate.Dispose();
            }
        }
    }

    public async Task<ServerResult> GenerateSelfSignedCertificateAsync(Ulid userId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("GenerateSelfSignedCertificateAsync - UserId: {UserId}", userId);

        if (!await IsAdministratorAsync(userId, cancellationToken))
        {
            return Failure("Only an administrator can change the server certificate.");
        }

        if (!_hostOptions.Value.EnableHttps)
        {
            return Failure("This server does not serve HTTPS, so there is no certificate to generate. "
                + "TLS is terminated upstream, or Ufo__EnableHttps is switched off.");
        }

        using var certificate = _selfSignedCertificateFactory.Create();
        var pfxBytes = certificate.Export(X509ContentType.Pkcs12)
            ?? throw new InvalidOperationException("Exporting the generated certificate produced no bytes.");

        return await StoreAndPublishAsync(
            certificate,
            pfxBytes,
            CertificateSources.SelfSigned,
            userId,
            cancellationToken);
    }

    public async Task EnsureCertificateAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("EnsureCertificateAsync");

        var serverSettings = await _serverSettingsRepository.GetServerSettingsAsync(cancellationToken);

        if (serverSettings?.CertificatePfx is { Length: > 0 } storedBlob)
        {
            var restored = TryRestore(storedBlob, serverSettings.CertificateSource);
            if (restored != null)
            {
                _serverCertificateProvider.Set(restored);

                _logger.LogInformation(
                    "Serving the stored {Source} certificate {Thumbprint}, valid until {NotAfter}.",
                    serverSettings.CertificateSource,
                    serverSettings.CertificateThumbprint,
                    serverSettings.CertificateNotAfter);

                return;
            }
        }

        // Nothing usable stored: first run, a protection key that no longer
        // matches, or a self-signed certificate that has expired.
        _logger.LogInformation("Generating a self-signed certificate for this installation.");

        using var certificate = _selfSignedCertificateFactory.Create();
        var pfxBytes = certificate.Export(X509ContentType.Pkcs12)
            ?? throw new InvalidOperationException("Exporting the generated certificate produced no bytes.");

        // No user id: the server generated this for itself, nobody asked for it.
        await StoreAndPublishAsync(certificate, pfxBytes, CertificateSources.SelfSigned, updatedByUserId: null, cancellationToken);
    }

    /// <summary>
    /// Decrypts and loads a stored certificate, deciding whether it is still fit
    /// to serve.
    /// </summary>
    /// <returns>The certificate, or <c>null</c> when it must be replaced.</returns>
    private X509Certificate2? TryRestore(byte[] storedBlob, string source)
    {
        byte[] pfxBytes;
        try
        {
            pfxBytes = _certificateProtector.Unprotect(storedBlob);
        }
        catch (CryptographicException exception)
        {
            // The protection key is gone or does not match - typically a data
            // directory restored without its key file. The blob is unrecoverable,
            // so the only way back to a working listener is a new certificate.
            _logger.LogError(
                exception,
                "The stored certificate could not be decrypted; the protection key does not match. Generating a replacement.");

            return null;
        }

        X509Certificate2 certificate;
        try
        {
            certificate = CertificateSerializer.FromPkcs12(pfxBytes);
        }
        catch (CryptographicException exception)
        {
            _logger.LogError(exception, "The stored certificate could not be loaded. Generating a replacement.");

            return null;
        }

        var isSelfSigned = !string.Equals(source, CertificateSources.UserSupplied, StringComparison.Ordinal);

        if (isSelfSigned && FindNameMissingFromCertificate(certificate) is { } missingName)
        {
            // The deployment has been told to serve a name the stored certificate
            // does not carry - typically UFO_CERTIFICATE_HOST added after first
            // run. Clients would get a host-name mismatch, which trusting the
            // certificate does not fix, so it is reissued rather than served.
            _logger.LogInformation(
                "The stored self-signed certificate does not name {MissingName}. Generating a replacement that does.",
                missingName);
            certificate.Dispose();

            return null;
        }

        if (certificate.NotAfter.ToUniversalTime() >= DateTime.UtcNow)
        {
            return certificate;
        }

        if (!isSelfSigned)
        {
            // Deliberately kept rather than replaced. Silently swapping an
            // administrator's certificate for a self-signed one would change the
            // identity clients pin against without anyone asking; an expired
            // certificate is at least the one they configured, and the Settings
            // page flags it as expired.
            _logger.LogWarning(
                "The uploaded server certificate expired on {NotAfter:u}. Serving it anyway - upload a replacement on the Settings page.",
                certificate.NotAfter);

            return certificate;
        }

        _logger.LogInformation("The self-signed certificate expired on {NotAfter:u}. Generating a replacement.", certificate.NotAfter);
        certificate.Dispose();

        return null;
    }

    private async Task<ServerResult> StoreAndPublishAsync(
        X509Certificate2 certificate,
        byte[] pfxBytes,
        string source,
        Ulid? updatedByUserId,
        CancellationToken cancellationToken)
    {
        var entity = new ServerSettingsEntity
        {
            CertificatePfx = _certificateProtector.Protect(pfxBytes),
            CertificateThumbprint = certificate.Thumbprint,
            CertificateSubject = certificate.Subject,
            CertificateNotBefore = certificate.NotBefore.ToUniversalTime().ToString("o"),
            CertificateNotAfter = certificate.NotAfter.ToUniversalTime().ToString("o"),
            CertificateSource = source,
            UpdatedAt = DateTimeOffset.UtcNow.ToString("o"),
            UpdatedByUserId = updatedByUserId
        };

        var saveResult = await _serverSettingsRepository.SaveCertificateAsync(entity, cancellationToken);
        if (saveResult.Result != Result.Success)
        {
            return saveResult;
        }

        // Published only after the write succeeded, so a failed save cannot leave
        // the listener serving a certificate that will be gone on restart.
        // Re-loaded from the exported bytes because the caller disposes its copy.
        //
        // KNOWN LIMITATION: only the leaf is presented. The stored archive keeps
        // the whole collection, so any intermediates an administrator uploaded are
        // preserved and a future change needs no migration - but Kestrel's
        // ServerCertificateSelector hands back a single certificate, so clients
        // that cannot fetch intermediates themselves (no AIA chasing) will see an
        // incomplete chain. Serving the chain needs the TLS handshake callback
        // rather than the selector. Self-signed certificates, which have no
        // intermediates, are unaffected.
        _serverCertificateProvider.Set(CertificateSerializer.FromPkcs12(pfxBytes));

        _logger.LogInformation(
            "Now serving the {Source} certificate {Thumbprint} for {Subject}.",
            source,
            certificate.Thumbprint,
            certificate.Subject);

        return new ServerResult
        {
            ActionName = "Saving Server Certificate.",
            Result = Result.Success,
            Priority = ActionPriority.Highest,
            Message = $"The server is now presenting the certificate for {certificate.Subject}. "
                + "Existing connections keep the previous one until they reconnect."
        };
    }

    /// <summary>
    /// Rejects certificates a browser would refuse anyway, while the
    /// administrator is still looking at the page and can do something about it.
    /// </summary>
    private static string? ValidateForServerUse(X509Certificate2 certificate)
    {
        var utcNow = DateTime.UtcNow;

        if (certificate.NotAfter.ToUniversalTime() < utcNow)
        {
            return $"The certificate expired on {certificate.NotAfter.ToUniversalTime():u}.";
        }

        if (certificate.NotBefore.ToUniversalTime() > utcNow)
        {
            return $"The certificate is not valid until {certificate.NotBefore.ToUniversalTime():u}.";
        }

        // An absent EKU means "any purpose" and is fine. A present one that omits
        // serverAuth is not: every browser would reject the handshake.
        var enhancedKeyUsage = certificate.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .FirstOrDefault();

        if (enhancedKeyUsage != null
            && enhancedKeyUsage.EnhancedKeyUsages.Count > 0
            && !enhancedKeyUsage.EnhancedKeyUsages
                .Cast<Oid>()
                .Any(usage => usage.Value == "1.3.6.1.5.5.7.3.1"))
        {
            return "The certificate is not valid for server authentication (it has no serverAuth extended key usage).";
        }

        return null;
    }

    /// <summary>
    /// The first configured subject alternative name the certificate does not
    /// carry, or <c>null</c> when it covers all of them.
    /// </summary>
    /// <remarks>
    /// Only the configured names are checked, never the ones discovered from the
    /// machine: those change whenever an interface comes or goes, and reissuing
    /// the certificate every time a VPN connected would be worse than the gap it
    /// closed.
    /// </remarks>
    private string? FindNameMissingFromCertificate(X509Certificate2 certificate)
    {
        var configuredNames = _hostOptions.Value.CertificateSubjectAlternativeNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToList();

        if (configuredNames.Count == 0)
        {
            return null;
        }

        var extension = certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>().FirstOrDefault();
        if (extension == null)
        {
            return configuredNames[0];
        }

        var dnsNames = extension.EnumerateDnsNames().ToList();
        var ipAddresses = extension.EnumerateIPAddresses().ToList();

        foreach (var configuredName in configuredNames)
        {
            var isCovered = IPAddress.TryParse(configuredName, out var configuredAddress)
                ? ipAddresses.Any(address => address.Equals(configuredAddress))
                : dnsNames.Any(name => string.Equals(name, configuredName, StringComparison.OrdinalIgnoreCase));

            if (!isCovered)
            {
                return configuredName;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the caller administers this installation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Re-read from the database rather than taken from the caller's JWT: a token
    /// outlives a demotion by up to its seven-day expiry, and this is the one
    /// permission in the application worth that round trip.
    /// </para>
    /// <para>
    /// Fails closed. The lookup throws rather than returning null when the id is
    /// not there, which is exactly what a token outliving its user looks like - a
    /// signed token stays valid until it expires whether or not the account still
    /// exists. Anything that stops us establishing the caller is an administrator
    /// has to read as "not one", rather than escaping as a 500.
    /// </para>
    /// </remarks>
    private async Task<bool> IsAdministratorAsync(Ulid userId, CancellationToken cancellationToken)
    {
        try
        {
            var user = await _userService.GetUserByIdAsync(userId, cancellationToken);

            return user?.IsAdmin == true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Could not establish whether {UserId} administers this installation; treating them as not an administrator.",
                userId);

            return false;
        }
    }

    private static DateTimeOffset? ParseTimestamp(string timestamp) =>
        DateTimeOffset.TryParse(timestamp, out var parsed) ? parsed : null;

    private static ServerResult Failure(string message) =>
        new()
        {
            ActionName = "Saving Server Certificate.",
            Result = Result.Error,
            Priority = ActionPriority.Highest,
            Message = message
        };
}
