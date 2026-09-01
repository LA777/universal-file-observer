using Dapper;
using Microsoft.Extensions.Logging;
using Ufo.Abstractions.Database;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;

namespace Ufo.Database.Repositories;

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly ILogger<RefreshTokenRepository> _logger;
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public RefreshTokenRepository(IDbConnectionFactory dbConnectionFactory, ILogger<RefreshTokenRepository>? logger)
    {
        _dbConnectionFactory = dbConnectionFactory ?? throw new ArgumentNullException(nameof(dbConnectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InsertAsync(RefreshTokenEntity refreshToken, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(refreshToken);

        // The row id and the user identify the session; the hash is the closest
        // thing here to a secret and is never logged, not even truncated.
        _logger.LogInformation(
            "InsertAsync - RefreshTokenId: {RefreshTokenId}, UserId: {UserId}", refreshToken.Id, refreshToken.UserId);
        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);

            await sqLiteConnection.ExecuteAsync(
                SqlScripts.InsertRefreshTokenSql,
                new
                {
                    refreshToken.Id,
                    refreshToken.UserId,
                    refreshToken.TokenHash,
                    refreshToken.CreatedAt,
                    refreshToken.ExpiresAt,
                    refreshToken.AbsoluteExpiresAt
                });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - InsertAsync");
            throw;
        }
    }

    public async Task<RefreshTokenEntity?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);

            return await sqLiteConnection.QueryFirstOrDefaultAsync<RefreshTokenEntity>(
                SqlScripts.SelectRefreshTokenByHashSql,
                new { TokenHash = tokenHash });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - GetByHashAsync");
            throw;
        }
    }

    public async Task<RefreshTokenEntity?> GetByIdAsync(Ulid refreshTokenId, CancellationToken cancellationToken = default)
    {
        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);

            return await sqLiteConnection.QueryFirstOrDefaultAsync<RefreshTokenEntity>(
                SqlScripts.SelectRefreshTokenByIdSql,
                new { Id = refreshTokenId });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - GetByIdAsync");
            throw;
        }
    }

    public async Task<bool> TryRotateAsync(Ulid refreshTokenId, Ulid replacedByTokenId, string revokedAt, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "TryRotateAsync - RefreshTokenId: {RefreshTokenId}, ReplacedByTokenId: {ReplacedByTokenId}",
            refreshTokenId,
            replacedByTokenId);
        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);

            var rowsAffected = await sqLiteConnection.ExecuteAsync(
                SqlScripts.RotateRefreshTokenSql,
                new { Id = refreshTokenId, ReplacedByTokenId = replacedByTokenId, RevokedAt = revokedAt });

            return rowsAffected == 1;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - TryRotateAsync");
            throw;
        }
    }

    public async Task<bool> TryRevokeAsync(Ulid refreshTokenId, string revokedAt, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("TryRevokeAsync - RefreshTokenId: {RefreshTokenId}", refreshTokenId);
        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);

            var rowsAffected = await sqLiteConnection.ExecuteAsync(
                SqlScripts.RevokeRefreshTokenSql,
                new { Id = refreshTokenId, RevokedAt = revokedAt });

            return rowsAffected == 1;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - TryRevokeAsync");
            throw;
        }
    }

    public async Task<int> RevokeAllForUserAsync(Ulid userId, string revokedAt, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("RevokeAllForUserAsync - UserId: {UserId}", userId);
        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);

            return await sqLiteConnection.ExecuteAsync(
                SqlScripts.RevokeAllRefreshTokensForUserSql,
                new { UserId = userId, RevokedAt = revokedAt });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - RevokeAllForUserAsync");
            throw;
        }
    }

    public async Task<int> DeleteExpiredAsync(string utcNow, CancellationToken cancellationToken = default)
    {
        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);

            return await sqLiteConnection.ExecuteAsync(
                SqlScripts.DeleteExpiredRefreshTokensSql,
                new { UtcNow = utcNow });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - DeleteExpiredAsync");
            throw;
        }
    }
}
