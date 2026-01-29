using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;

namespace Ufo.Database.Repositories;

public class UserRepository : IUserRepository
{
    private readonly string _connectionString;
    private readonly ILogger<UserRepository> _logger;

    public UserRepository(string connectionString, ILogger<UserRepository>? logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<UserEntity?> GetUserByUsernameAsync(string username)
    {
        using var sqLiteConnection = new SqliteConnection(_connectionString);
        const string sql = @"SELECT * FROM Users WHERE Name = @Username"; // TODO LA - Move to SqlScript class

        try
        {
            var userEntity = await sqLiteConnection.QueryFirstOrDefaultAsync<UserEntity>(sql, new { Username = username });
            _logger.LogInformation("Retrieved user: {Username}", userEntity?.Name);
            return userEntity;
        }
        catch (Exception)
        {
            throw;
        }
    }

    public async Task<bool> UserExistsAsync(string username)
    {
        using var sqLiteConnection = new SqliteConnection(_connectionString);
        const string sql = "SELECT COUNT(1) FROM Users WHERE Name = @Username"; // TODO LA - Move to SqlScript class
        _logger.LogInformation("Checking if user exists: {Username}", username);

        try
        {
            var usersWithSameName = await sqLiteConnection.ExecuteScalarAsync<int>(sql, new { Username = username });
            return usersWithSameName > 0;
        }
        catch (Exception)
        {
            throw;
        }
    }

    public async Task<bool> CreateUserAsync(UserEntity user)
    {
        using var sqLiteConnection = new SqliteConnection(_connectionString);
        await sqLiteConnection.OpenAsync();
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

    public async Task<int> GetUserCountAsync()
    {
        using var sqLiteConnection = new SqliteConnection(_connectionString);
        const string sql = "SELECT COUNT(1) FROM Users"; // TODO LA - Move to SqlScript class

        try
        {
            var userCount = await sqLiteConnection.ExecuteScalarAsync<int>(sql);
            _logger.LogInformation("Users in Databse: {userCount}", userCount);

            return userCount;
        }
        catch (Exception)
        {
            throw;
        }        
    }
}
