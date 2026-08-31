using Dapper;
using Microsoft.Extensions.Logging;
using Ufo.Abstractions;
using Ufo.Abstractions.Database;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;

namespace Ufo.Database.Repositories;

public class UserSettingsRepository : IUserSettingsRepository
{
    private readonly ILogger<UserSettingsRepository> _logger;
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public UserSettingsRepository(IDbConnectionFactory dbConnectionFactory, ILogger<UserSettingsRepository>? logger)
    {
        _dbConnectionFactory = dbConnectionFactory ?? throw new ArgumentNullException(nameof(dbConnectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<UserSettingsEntity?> GetUserSettingsAsync(Ulid userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetUserSettingsAsync - UserId: {UserId}", userId);
        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);
            var userSettings = await sqLiteConnection.QueryFirstOrDefaultAsync<UserSettingsEntity>(
                SqlScripts.SelectUserSettingsSql,
                new { UserId = userId });

            if (userSettings == null)
            {
                _logger.LogInformation("No settings saved yet for user: {UserId}", userId);
            }

            return userSettings;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - GetUserSettingsAsync");
            throw;
        }
    }

    public async Task<ServerResult> SaveUserSettingsAsync(UserSettingsEntity userSettings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userSettings);

        _logger.LogInformation("SaveUserSettingsAsync - UserId: {UserId}", userSettings.UserId);
        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);

            // Upsert rather than select-then-insert-or-update: UserSettings.UserId
            // is UNIQUE, so two first-time saves racing each other resolve into an
            // update instead of one of them failing on the constraint. The Id we
            // pass is only used when the row is created.
            var rowsAffected = await sqLiteConnection.ExecuteAsync(
                SqlScripts.UpsertUserSettingsSql,
                new { userSettings.Id, userSettings.Theme, userSettings.UserId });

            _logger.LogInformation("Saved settings for user: {UserId}", userSettings.UserId);

            return new ServerResult
            {
                ActionName = "Saving User Settings.",
                Result = rowsAffected == 1 ? Result.Success : Result.Error,
                Priority = ActionPriority.Highest
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - SaveUserSettingsAsync");
            throw;
        }
    }
}
