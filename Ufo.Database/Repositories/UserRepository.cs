using Dapper;
using Microsoft.Extensions.Logging;
using Ufo.Abstractions.Database;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;

namespace Ufo.Database.Repositories;

public class UserRepository : IUserRepository
{
    private readonly ILogger<UserRepository> _logger;
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public UserRepository(IDbConnectionFactory dbConnectionFactory, ILogger<UserRepository>? logger)
    {
        _dbConnectionFactory = dbConnectionFactory ?? throw new ArgumentNullException(nameof(dbConnectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<UserEntity?> GetUserByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        const string sql = @"SELECT * FROM Users WHERE Name = @Username"; // TODO LA - Move to SqlScript class

        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);
            var userEntity = await sqLiteConnection.QueryFirstOrDefaultAsync<UserEntity>(sql, new { Username = username });
            _logger.LogInformation("Retrieved user: {Username}", userEntity?.Name);
            return userEntity;
        }
        catch (Exception)
        {
            throw;
        }
    }

    public async Task<bool> UserExistsAsync(string username, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT COUNT(1) FROM Users WHERE Name = @Username"; // TODO LA - Move to SqlScript class
        _logger.LogInformation("Checking if user exists: {Username}", username);

        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);
            var usersWithSameName = await sqLiteConnection.ExecuteScalarAsync<int>(sql, new { Username = username });
            return usersWithSameName > 0;
        }
        catch (Exception)
        {
            throw;
        }
    }

    public async Task<bool> CreateUserAsync(UserEntity user, CancellationToken cancellationToken = default)
    {
        var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);
        using var transaction = sqLiteConnection.BeginTransaction();
        const string userSql = @"
                INSERT INTO Users (Id, Name, PasswordHash) 
                VALUES (@Id, @Name, @PasswordHash)"; // TODO LA - Move to SqlScript class

        try
        {
            await sqLiteConnection.ExecuteAsync(userSql, new
            {
                Id = user.Id.ToString(),
                user.Name,
                user.PasswordHash
            }, transaction);

            await transaction.CommitAsync();
            _logger.LogInformation("Created user: {Username}", user.Name);

            return true;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<int> GetUserCountAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT COUNT(1) FROM Users"; // TODO LA - Move to SqlScript class

        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);
            var userCount = await sqLiteConnection.ExecuteScalarAsync<int>(sql);
            _logger.LogInformation("Users in Databse: {userCount}", userCount);

            return userCount;
        }
        catch (Exception)
        {
            throw;
        }        
    }

    public async Task<UserEntity> GetUserByIdAsync(Ulid userId, CancellationToken cancellationToken = default)
    {
        const string sql = @"SELECT * FROM Users WHERE Id = @UserId"; // TODO LA - Move to SqlScript class
        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);
            var userEntity = await sqLiteConnection.QueryFirstOrDefaultAsync<UserEntity>(sql, new { UserId = userId.ToString() });
            if (userEntity == null)
            {
                throw new Exception($"User with ID ({userId}) was not found.");
            }

            _logger.LogInformation("Retrieved user by ID: {UserId}", userId);
            return userEntity;
        }
        catch (Exception)
        {
            throw;
        }
    }
}
