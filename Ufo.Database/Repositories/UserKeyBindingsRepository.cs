using Dapper;
using Microsoft.Extensions.Logging;
using Ufo.Abstractions;
using Ufo.Abstractions.Database;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;

namespace Ufo.Database.Repositories;

public class UserKeyBindingsRepository : IUserKeyBindingsRepository
{
    private readonly ILogger<UserKeyBindingsRepository> _logger;
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public UserKeyBindingsRepository(
        IDbConnectionFactory dbConnectionFactory,
        ILogger<UserKeyBindingsRepository>? logger)
    {
        _dbConnectionFactory = dbConnectionFactory ?? throw new ArgumentNullException(nameof(dbConnectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<UserKeyBindingEntity>> GetUserKeyBindingsAsync(
        Ulid userId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetUserKeyBindingsAsync - UserId: {UserId}", userId);

        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);

            var keyBindings = await sqLiteConnection.QueryAsync<UserKeyBindingEntity>(
                SqlScripts.SelectUserKeyBindingsSql,
                new { UserId = userId });

            return keyBindings.ToList();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - GetUserKeyBindingsAsync");
            throw;
        }
    }

    public async Task<ServerResult> SaveUserKeyBindingsAsync(
        IReadOnlyList<UserKeyBindingEntity> keyBindings,
        Ulid userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keyBindings);

        _logger.LogInformation(
            "SaveUserKeyBindingsAsync - UserId: {UserId}, Count: {Count}",
            userId,
            keyBindings.Count);

        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);

            // One transaction for the whole table. A shortcut page saved halfway
            // is a keyboard where some actions answer to the new keys and some to
            // the old, and the user has no way to tell which is which.
            using var transaction = sqLiteConnection.BeginTransaction();

            var actionIds = keyBindings.Select(keyBinding => keyBinding.ActionId).ToList();

            if (actionIds.Count == 0)
            {
                await sqLiteConnection.ExecuteAsync(
                    SqlScripts.DeleteAllUserKeyBindingsSql,
                    new { UserId = userId },
                    transaction);
            }
            else
            {
                // Anything the caller did not send is an action put back to its
                // default, and a default is stored as the absence of a row.
                await sqLiteConnection.ExecuteAsync(
                    SqlScripts.DeleteUserKeyBindingsNotInSql,
                    new { UserId = userId, ActionIds = actionIds },
                    transaction);

                await sqLiteConnection.ExecuteAsync(
                    SqlScripts.UpsertUserKeyBindingSql,
                    keyBindings.Select(keyBinding => new
                    {
                        keyBinding.Id,
                        keyBinding.ActionId,
                        keyBinding.PrimaryKey,
                        keyBinding.SecondaryKey,
                        UserId = userId
                    }),
                    transaction);
            }

            transaction.Commit();

            _logger.LogInformation("Saved {Count} key bindings for user: {UserId}", keyBindings.Count, userId);

            return new ServerResult
            {
                ActionName = "Saving Key Bindings.",
                Result = Result.Success,
                Priority = ActionPriority.Highest
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - SaveUserKeyBindingsAsync");
            throw;
        }
    }
}
