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
        const string sql = @"SELECT * FROM Users WHERE Username = @Username";

        var userEntity = await sqLiteConnection.QueryFirstOrDefaultAsync<UserEntity>(sql, new { username });
        _logger.LogInformation("Retrieved user: {Username}", userEntity?.Name);

        return userEntity;
    }

    public async Task<bool> UserExistsAsync(string username)
    {
        using var sqLiteConnection = new SqliteConnection(_connectionString);
        const string sql = "SELECT COUNT(1) FROM Users WHERE Name = @Username";
        _logger.LogInformation("Checking if user exists: {Username}", username);

        return await sqLiteConnection.ExecuteScalarAsync<int>(sql, new { username }) > 0;
    }

    public async Task<bool> CreateUserAsync(UserEntity user, Ulid defaultRoleId)
    {
        using var sqLiteConnection = new SqliteConnection(_connectionString);
        await sqLiteConnection.OpenAsync();
        using var transaction = sqLiteConnection.BeginTransaction();

        try
        {
            const string userSql = @"
                INSERT INTO Users (Id, Name, PasswordHash) 
                VALUES (@Id, @Name, @PasswordHash)";

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
}
