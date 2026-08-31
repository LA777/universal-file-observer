using Ufo.Abstractions.Database.Entities;

namespace Ufo.Abstractions.Database.Repositories;

/// <summary>
/// Reads and writes the single server-scoped settings row. Unlike every other
/// repository here it takes no user id: see <see cref="ServerSettingsEntity"/>
/// for why the TLS certificate cannot be per-user.
/// </summary>
public interface IServerSettingsRepository
{
    /// <summary>
    /// The server settings row, or <c>null</c> when nothing has been stored yet
    /// (a fresh installation, before the first certificate is generated).
    /// </summary>
    Task<ServerSettingsEntity?> GetServerSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the stored certificate and its metadata, creating the row on first
    /// save. The blob must already be protected by the caller.
    /// </summary>
    Task<ServerResult> SaveCertificateAsync(ServerSettingsEntity serverSettings, CancellationToken cancellationToken = default);
}
