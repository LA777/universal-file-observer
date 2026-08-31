using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using Ufo.Abstractions.Options;

namespace Ufo.Server.Security;

/// <summary>
/// Encrypts the PKCS#12 archive before it is written to the database, and
/// decrypts it on the way back out.
/// </summary>
/// <remarks>
/// The SQLite database is not encrypted, so a certificate's private key stored
/// as-is would sit in plaintext in a file that gets copied around in backups.
/// The blob is therefore sealed with AES-GCM under a key that lives beside the
/// database as a separate file, mirroring how the installation's JWT signing key
/// is persisted. This does not defend against someone who takes the whole data
/// directory - it defends against the database alone leaking, which is the far
/// more likely accident.
/// </remarks>
public interface ICertificateProtector
{
    /// <summary>Seals a PKCS#12 archive for storage.</summary>
    byte[] Protect(byte[] plaintext);

    /// <summary>
    /// Opens a blob produced by <see cref="Protect"/>.
    /// </summary>
    /// <exception cref="CryptographicException">
    /// The blob is corrupt, truncated, or was sealed under a different key.
    /// </exception>
    byte[] Unprotect(byte[] protectedBytes);
}

public class CertificateProtector : ICertificateProtector
{
    /// <summary>
    /// File in <see cref="UfoHostOptions.DataDirectory"/> holding the key this
    /// installation seals its certificate with. Sits beside the database and the
    /// JWT signing key, so one backup of the data directory stays self-consistent.
    /// </summary>
    public const string ProtectionKeyFileName = "cert-protection-key";

    private const int KeySizeInBytes = 32;
    private const int NonceSizeInBytes = 12;
    private const int TagSizeInBytes = 16;

    /// <summary>
    /// Leading byte on every sealed blob. Stored rather than assumed so that a
    /// future change of algorithm can recognise, and reject, the old format
    /// instead of decrypting garbage.
    /// </summary>
    private const byte FormatVersion = 1;

    private readonly Lazy<byte[]> _protectionKey;
    private readonly ILogger<CertificateProtector> _logger;

    public CertificateProtector(IOptions<UfoHostOptions> hostOptions, ILogger<CertificateProtector> logger)
    {
        ArgumentNullException.ThrowIfNull(hostOptions);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var dataDirectory = hostOptions.Value.DataDirectory;

        // Lazy so that a host which never touches a certificate - a functional
        // test, or a run with TLS switched off - does not create a key file it
        // has no use for.
        _protectionKey = new Lazy<byte[]>(() => ResolveProtectionKey(dataDirectory));
    }

    public byte[] Protect(byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var nonce = RandomNumberGenerator.GetBytes(NonceSizeInBytes);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSizeInBytes];

        using (var aesGcm = new AesGcm(_protectionKey.Value, TagSizeInBytes))
        {
            aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        var sealedBlob = new byte[1 + NonceSizeInBytes + TagSizeInBytes + ciphertext.Length];
        sealedBlob[0] = FormatVersion;
        nonce.CopyTo(sealedBlob, 1);
        tag.CopyTo(sealedBlob, 1 + NonceSizeInBytes);
        ciphertext.CopyTo(sealedBlob, 1 + NonceSizeInBytes + TagSizeInBytes);

        return sealedBlob;
    }

    public byte[] Unprotect(byte[] protectedBytes)
    {
        ArgumentNullException.ThrowIfNull(protectedBytes);

        if (protectedBytes.Length < 1 + NonceSizeInBytes + TagSizeInBytes)
        {
            throw new CryptographicException("The stored certificate blob is truncated.");
        }

        if (protectedBytes[0] != FormatVersion)
        {
            throw new CryptographicException(
                $"Unsupported certificate blob format version {protectedBytes[0]}.");
        }

        var nonce = protectedBytes.AsSpan(1, NonceSizeInBytes);
        var tag = protectedBytes.AsSpan(1 + NonceSizeInBytes, TagSizeInBytes);
        var ciphertext = protectedBytes.AsSpan(1 + NonceSizeInBytes + TagSizeInBytes);
        var plaintext = new byte[ciphertext.Length];

        using var aesGcm = new AesGcm(_protectionKey.Value, TagSizeInBytes);

        // Throws CryptographicException when the tag does not verify, which is
        // exactly what a wrong key or a tampered row looks like. Left to
        // propagate: callers report it as "stored certificate cannot be read"
        // rather than silently falling back to an unprotected certificate.
        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
    }

    private byte[] ResolveProtectionKey(string dataDirectory)
    {
        var protectionKeyFilePath = Path.Combine(dataDirectory, ProtectionKeyFileName);

        if (File.Exists(protectionKeyFilePath))
        {
            string persistedKey;

            try
            {
                persistedKey = File.ReadAllText(protectionKeyFilePath).Trim();
            }
            catch (Exception exception)
            {
                // Deliberately not a warning-and-regenerate, for the same reason
                // the JWT signing key is not: a momentary lock from a backup or a
                // scanner would otherwise replace a perfectly good key and leave
                // the stored certificate permanently undecryptable.
                throw new InvalidOperationException(
                    $"The certificate protection key '{protectionKeyFilePath}' exists but could not be read, and "
                    + "replacing it would make the stored certificate unreadable. Start again once whatever holds "
                    + "the file has released it, or correct its permissions.",
                    exception);
            }

            if (IsUsableKey(persistedKey, out var decodedKey))
            {
                return decodedKey;
            }

            // A key that cannot be parsed cannot have sealed anything readable,
            // so nothing is lost by replacing it.
            _logger.LogWarning(
                "Ignoring the certificate protection key in {ProtectionKeyFilePath}: it is not a {KeySize}-byte hex key. Generating a replacement.",
                protectionKeyFilePath,
                KeySizeInBytes);
        }

        var generatedKey = RandomNumberGenerator.GetBytes(KeySizeInBytes);

        try
        {
            Directory.CreateDirectory(dataDirectory);
            File.WriteAllText(protectionKeyFilePath, Convert.ToHexString(generatedKey));
            RestrictToOwner(protectionKeyFilePath);
        }
        catch (Exception exception)
        {
            // Refusing to start would take the whole application down over a
            // feature the user may not be using. Carrying on with an in-memory
            // key keeps TLS working for this process; the cost is that a stored
            // certificate cannot be decrypted after a restart, which the
            // certificate service reports and recovers from by regenerating.
            _logger.LogError(
                exception,
                "Could not persist the certificate protection key to {ProtectionKeyFilePath}. Using an in-memory key: "
                + "a certificate stored now will not be readable after a restart.",
                protectionKeyFilePath);
        }

        return generatedKey;
    }

    private static bool IsUsableKey(string persistedKey, out byte[] decodedKey)
    {
        decodedKey = [];

        try
        {
            var candidate = Convert.FromHexString(persistedKey);
            if (candidate.Length != KeySizeInBytes)
            {
                return false;
            }

            decodedKey = candidate;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Takes the key file down to owner-only on Unix. Windows inherits the
    /// data directory's ACL, which for both hosts is already a per-user or
    /// container-private location.
    /// </summary>
    private static void RestrictToOwner(string filePath)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
