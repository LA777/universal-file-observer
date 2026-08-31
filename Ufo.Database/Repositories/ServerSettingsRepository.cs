using Dapper;
using Microsoft.Extensions.Logging;
using Ufo.Abstractions;
using Ufo.Abstractions.Database;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;

namespace Ufo.Database.Repositories;

public class ServerSettingsRepository : IServerSettingsRepository
{
    private readonly ILogger<ServerSettingsRepository> _logger;
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public ServerSettingsRepository(IDbConnectionFactory dbConnectionFactory, ILogger<ServerSettingsRepository>? logger)
    {
        _dbConnectionFactory = dbConnectionFactory ?? throw new ArgumentNullException(nameof(dbConnectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ServerSettingsEntity?> GetServerSettingsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetServerSettingsAsync");
        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);
            var serverSettings = await sqLiteConnection.QueryFirstOrDefaultAsync<ServerSettingsEntity>(
                SqlScripts.SelectServerSettingsSql);

            if (serverSettings == null)
            {
                _logger.LogInformation("No server settings row exists yet.");
            }

            return serverSettings;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - GetServerSettingsAsync");
            throw;
        }
    }

    public async Task<ServerResult> SaveCertificateAsync(ServerSettingsEntity serverSettings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serverSettings);

        // The thumbprint identifies the certificate without revealing anything
        // secret, so it is safe to log; the blob obviously is not.
        _logger.LogInformation(
            "SaveCertificateAsync - Thumbprint: {Thumbprint}, Source: {Source}",
            serverSettings.CertificateThumbprint,
            serverSettings.CertificateSource);
        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);

            // Upsert on the SingletonGuard for the same reason UserSettings
            // upserts on UserId: two first-time writes racing each other resolve
            // into an update rather than one failing the UNIQUE constraint. The
            // Id passed here is only used when the row is created.
            var rowsAffected = await sqLiteConnection.ExecuteAsync(
                SqlScripts.UpsertServerSettingsSql,
                new
                {
                    serverSettings.Id,
                    serverSettings.CertificatePfx,
                    serverSettings.CertificateThumbprint,
                    serverSettings.CertificateSubject,
                    serverSettings.CertificateNotBefore,
                    serverSettings.CertificateNotAfter,
                    serverSettings.CertificateSource,
                    serverSettings.UpdatedAt,
                    serverSettings.UpdatedByUserId
                });

            return new ServerResult
            {
                ActionName = "Saving Server Certificate.",
                Result = rowsAffected == 1 ? Result.Success : Result.Error,
                Priority = ActionPriority.Highest
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - SaveCertificateAsync");
            throw;
        }
    }
}
