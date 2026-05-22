using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using Ufo.Abstractions.Database;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Options;
using Ufo.Abstractions.Requests;
using Ufo.Database.Contexts;
using Ufo.FunctionalTests.Extensions;
using Ufo.Server.Extensions;

namespace Ufo.FunctionalTests.SnapshotController;

#region Test WebApplication factory

/// <summary>
/// Boots a real in-process ASP.NET Core host for Snapshot Controller tests.
/// Uses an in-memory SQLite database for complete isolation.
/// </summary>
public class SnapshotApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"test-snapshot-{Guid.NewGuid():N}";
    private SqliteConnection? _sqLiteConnection;

    public string ConnectionString => $"Data Source={_dbName};Mode=Memory;Cache=Shared";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(HostEnvironmentExtensions.FunctionalTesting);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = ConnectionString,
                ["JWT:Key"] = SnapshotTestConstants.JwtKey,
                ["JWT:Issuer"] = SnapshotTestConstants.JwtIssuer,
                ["JWT:Audience"] = SnapshotTestConstants.JwtAudience,
                ["Kestrel:Endpoints:App:Url"] = "http://localhost:0"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDbConnectionFactory>();
            services.AddScoped<IDbConnectionFactory>(sp =>
                new SqliteConnectionFactory(
                    new DatabaseOptions { ConnectionString = ConnectionString }.ToOptionsMonitor(),
                    sp.GetRequiredService<ILogger<SqliteConnectionFactory>>()));

            services.AddLogging(lb => lb.SetMinimumLevel(LogLevel.Warning));

            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(SnapshotTestConstants.JwtKey)),
                    ValidateIssuer = true,
                    ValidIssuer = SnapshotTestConstants.JwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = SnapshotTestConstants.JwtAudience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            });
        });
    }

    public async Task<HttpClient> CreateClientAsync()
    {
        _sqLiteConnection = new SqliteConnection(ConnectionString);
        await _sqLiteConnection.OpenAsync();
        await DapperDataContext.InitiateDatabaseAsync(_sqLiteConnection);

        return CreateClient();
    }

    public override ValueTask DisposeAsync()
    {
        _sqLiteConnection?.Dispose();
        return base.DisposeAsync();
    }
}

#endregion

#region Test Constants

public static class SnapshotTestConstants
{
    public const string JwtKey = "78D97475131A633F6975CA554DCEDDCBDC0EBE456EA270F5A7EA1C604787258A";
    public const string JwtIssuer = "UFO";
    public const string JwtAudience = "UFO";

    public static readonly Ulid TestUserId = Ulid.NewUlid();
    public static readonly string TestUserName = "testuser";
    public static readonly string TestUserPasswordHash = BCrypt.Net.BCrypt.HashPassword("TestPassword123!");

    public static readonly Ulid TestUserId2 = Ulid.NewUlid();
    public static readonly string TestUserName2 = "testuser2";
    public static readonly string TestUserPasswordHash2 = BCrypt.Net.BCrypt.HashPassword("TestPassword456!");
}

#endregion

#region Test Helpers

public static class SnapshotRequestFactory
{
    public static PathRequest CreatePathRequest(string path)
    {
        return new PathRequest { Path = path };
    }
}

public static class SnapshotTestDataBuilder
{
    public static SnapshotEntity CreateSnapshotEntity(Ulid userId, UserEntity? user = null)
    {
        return new SnapshotEntity
        {
            Id = Ulid.NewUlid(),
            Timestamp = DateTimeOffset.UtcNow,
            Description = "Test Snapshot",
            UserId = userId,
            User = user ?? new UserEntity { Id = userId, Name = "Test User" }
        };
    }

    public static FolderEntity CreateRootFolderEntity(Ulid userId, UserEntity? user = null)
    {
        user ??= new UserEntity { Id = userId, Name = "Test User" };
        return new FolderEntity
        {
            Id = Ulid.NewUlid(),
            Name = "Root",
            Size = 0,
            Sha256Hash = "root-hash",
            CreatedAt = DateTime.UtcNow.ToString("o"),
            UpdatedAt = DateTime.UtcNow.ToString("o"),
            IsHidden = false,
            UserId = userId,
            User = user
        };
    }

    public static FileEntity CreateFileEntity(Ulid userId, string fileName = "test.txt", long size = 1024, UserEntity? user = null)
    {
        user ??= new UserEntity { Id = userId, Name = "Test User" };
        return new FileEntity
        {
            Id = Ulid.NewUlid(),
            Name = Path.GetFileNameWithoutExtension(fileName),
            Size = size,
            Sha256Hash = "test-file-hash",
            FileExtension = Path.GetExtension(fileName),
            CreatedAt = DateTime.UtcNow.ToString("o"),
            UpdatedAt = DateTime.UtcNow.ToString("o"),
            IsHidden = false,
            UserId = userId,
            User = user
        };
    }
}

#endregion

#region Functional Tests

public class SnapshotControllerFunctionalTests : IAsyncLifetime
{
    private SnapshotApiFactory _factory = null!;
    private HttpClient _client = null!;
    private SqliteConnection _connection = null!;

    public async Task InitializeAsync()
    {
        _factory = new SnapshotApiFactory();
        _client = await _factory.CreateClientAsync();
        _connection = new SqliteConnection(_factory.ConnectionString);
        await _connection.OpenAsync();

        // Register test users
        await RegisterTestUser(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName, SnapshotTestConstants.TestUserPasswordHash);
        await RegisterTestUser(SnapshotTestConstants.TestUserId2, SnapshotTestConstants.TestUserName2, SnapshotTestConstants.TestUserPasswordHash2);
    }

    public async Task DisposeAsync()
    {
        _connection?.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task RegisterTestUser(Ulid userId, string userName, string passwordHash)
    {
        // TODO LA - Move to a shared class for all tests that need users.
        const string sql = @"
            INSERT INTO Users (Id, Name, PasswordHash, CreatedAt) 
            VALUES (@Id, @Name, @PasswordHash, @CreatedAt)";

        using var connection = new SqliteConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@Id", userId.ToString());
        command.Parameters.AddWithValue("@Name", userName);
        command.Parameters.AddWithValue("@PasswordHash", passwordHash);
        command.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow.ToString("o"));
        await command.ExecuteNonQueryAsync();
    }

    private string GenerateToken(Ulid userId, string userName)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SnapshotTestConstants.JwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.NameId, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, userName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: SnapshotTestConstants.JwtIssuer,
            audience: SnapshotTestConstants.JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    #region GetLatestSnapshot Tests

    [Fact]
    public async Task GetLatestSnapshot_WithNoSnapshots_ReturnsNoContent()
    {
        // Arrange
        var token = GenerateToken(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        // Act
        var response = await _client.GetAsync("/api/snapshot/latest");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task GetLatestSnapshot_WithSnapshot_ReturnsOkWithSnapshot()
    {
        // Arrange
        var token = GenerateToken(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var user = new UserEntity { Id = SnapshotTestConstants.TestUserId, Name = SnapshotTestConstants.TestUserName };
        var snapshot = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId, user);

        await InsertSnapshot(snapshot);

        // Act
        var response = await _client.GetAsync("/api/snapshot/latest");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(content);
        Assert.Contains(snapshot.Id.ToString(), content);

        // TODO LA - Add more assertions to validate the returned snapshot content is correct (description, timestamp, etc.).
        // TODO LA - Add assertion for root folder and ensure it is returned correctly.
    }

    [Fact]
    public async Task GetLatestSnapshot_WithMultipleSnapshots_ReturnsLatestSnapshot()
    {
        // Arrange
        var token = GenerateToken(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var user = new UserEntity { Id = SnapshotTestConstants.TestUserId, Name = SnapshotTestConstants.TestUserName };

        var snapshot1 = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId, user);
        snapshot1.Timestamp = DateTimeOffset.UtcNow.AddHours(-2);

        await InsertSnapshot(snapshot1);

        var snapshot2 = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId, user);
        snapshot2.Timestamp = DateTimeOffset.UtcNow;

        await InsertSnapshot(snapshot2);

        // Act
        var response = await _client.GetAsync("/api/snapshot/latest");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(snapshot2.Id.ToString(), content);
    }

    [Fact]
    public async Task GetLatestSnapshot_WithoutAuth_ReturnUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/snapshot/latest");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetLatestSnapshot_WithMultipleUsers_ReturnsOnlyCurrentUserSnapshot()
    {
        // Arrange - Create and insert snapshots for both test users
        var user1 = new UserEntity { Id = SnapshotTestConstants.TestUserId, Name = SnapshotTestConstants.TestUserName };
        var user2 = new UserEntity { Id = SnapshotTestConstants.TestUserId2, Name = SnapshotTestConstants.TestUserName2 };

        // User 1 snapshots
        var user1Snapshot1 = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId, user1);
        user1Snapshot1.Timestamp = DateTimeOffset.UtcNow.AddHours(-3);
        user1Snapshot1.Description = "User 1 Old Snapshot";

        var user1Snapshot2 = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId, user1);
        user1Snapshot2.Timestamp = DateTimeOffset.UtcNow.AddHours(-1);
        user1Snapshot2.Description = "User 1 Latest Snapshot";

        // User 2 snapshots
        var user2Snapshot1 = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId2, user2);
        user2Snapshot1.Timestamp = DateTimeOffset.UtcNow;
        user2Snapshot1.Description = "User 2 Latest Snapshot";

        await InsertSnapshot(user1Snapshot1);
        await InsertSnapshot(user1Snapshot2);
        await InsertSnapshot(user2Snapshot1);

        // Act - Request as User 1
        var token1 = GenerateToken(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token1}");

        var response1 = await _client.GetAsync("/api/snapshot/latest");
        var content1 = await response1.Content.ReadAsStringAsync();

        // Assert - User 1 should only get their latest snapshot
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Contains(user1Snapshot2.Id.ToString(), content1);
        Assert.DoesNotContain(user1Snapshot1.Id.ToString(), content1);
        Assert.DoesNotContain(user2Snapshot1.Id.ToString(), content1);

        // Act - Request as User 2
        var token2 = GenerateToken(SnapshotTestConstants.TestUserId2, SnapshotTestConstants.TestUserName2);
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token2}");

        var response2 = await _client.GetAsync("/api/snapshot/latest");
        var content2 = await response2.Content.ReadAsStringAsync();

        // Assert - User 2 should only get their latest snapshot
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.Contains(user2Snapshot1.Id.ToString(), content2);
        Assert.DoesNotContain(user1Snapshot1.Id.ToString(), content2);
        Assert.DoesNotContain(user1Snapshot2.Id.ToString(), content2);
    }

    #endregion

    #region GetSnapshotById Tests

    [Fact]
    public async Task GetSnapshotById_WithValidSnapshot_ReturnsOk()
    {
        // Arrange
        var token = GenerateToken(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var user = new UserEntity { Id = SnapshotTestConstants.TestUserId, Name = SnapshotTestConstants.TestUserName };
        var snapshot = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId, user);

        await InsertSnapshot(snapshot);

        // Act
        var response = await _client.GetAsync($"/api/snapshot/{snapshot.Id}");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(snapshot.Id.ToString(), content);
    }

    [Fact]
    public async Task GetSnapshotById_WithNonExistentSnapshotId_ReturnsNotFound()
    {
        // Arrange
        var token = GenerateToken(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var nonExistentSnapshotId = Ulid.NewUlid();

        // Act
        var response = await _client.GetAsync($"/api/snapshot/{nonExistentSnapshotId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetSnapshotById_WithOtherUserSnapshot_ReturnsNotFound()
    {
        // Arrange
        var token1 = GenerateToken(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName);
        var user1 = new UserEntity { Id = SnapshotTestConstants.TestUserId, Name = SnapshotTestConstants.TestUserName };
        var snapshot = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId, user1);

        await InsertSnapshot(snapshot);

        // Act - Try to access with different user
        var token2 = GenerateToken(SnapshotTestConstants.TestUserId2, SnapshotTestConstants.TestUserName2);
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token2}");
        var response = await _client.GetAsync($"/api/snapshot/{snapshot.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetSnapshotById_WithoutAuth_ReturnUnauthorized()
    {
        // Act
        var response = await _client.GetAsync($"/api/snapshot/{Ulid.NewUlid()}");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region GetAllSnapshotsSummary Tests

    [Fact]
    public async Task GetAllSnapshotsSummary_WithNoSnapshots_ReturnsEmptyList()
    {
        // Arrange
        var token = GenerateToken(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        // Act
        var response = await _client.GetAsync("/api/snapshot/all/summary");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("[]", content);
    }

    [Fact]
    public async Task GetAllSnapshotsSummary_WithMultipleSnapshots_ReturnsAllUserSnapshots()
    {
        // Arrange
        var token = GenerateToken(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var user = new UserEntity { Id = SnapshotTestConstants.TestUserId, Name = SnapshotTestConstants.TestUserName };

        var snapshot1 = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId, user);

        await InsertSnapshot(snapshot1);

        var snapshot2 = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId, user);

        await InsertSnapshot(snapshot2);

        // Act
        var response = await _client.GetAsync("/api/snapshot/all/summary");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(snapshot1.Id.ToString(), content);
        Assert.Contains(snapshot2.Id.ToString(), content);
    }

    [Fact]
    public async Task GetAllSnapshotsSummary_WithUserIsolation_ReturnsOnlyUserSnapshots()
    {
        // Arrange
        var user1 = new UserEntity { Id = SnapshotTestConstants.TestUserId, Name = SnapshotTestConstants.TestUserName };
        var user2 = new UserEntity { Id = SnapshotTestConstants.TestUserId2, Name = SnapshotTestConstants.TestUserName2 };

        // Create snapshots for both users
        var snapshot1 = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId, user1);

        await InsertSnapshot(snapshot1);

        var snapshot2 = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId2, user2);

        await InsertSnapshot(snapshot2);

        // Act - Query with user 1 token
        var token = GenerateToken(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        var response = await _client.GetAsync("/api/snapshot/all/summary");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(snapshot1.Id.ToString(), content);
        Assert.DoesNotContain(snapshot2.Id.ToString(), content);
    }

    [Fact]
    public async Task GetAllSnapshotsSummary_WithoutAuth_ReturnUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/snapshot/all/summary");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region DeleteSnapshotByIdAsync Tests

    [Fact]
    public async Task DeleteSnapshotByIdAsync_WithValidSnapshot_ReturnsOkAndDeletes()
    {
        // Arrange
        var token = GenerateToken(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var user = new UserEntity { Id = SnapshotTestConstants.TestUserId, Name = SnapshotTestConstants.TestUserName };
        var snapshot = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId, user);

        await InsertSnapshot(snapshot);

        // Act
        var response = await _client.DeleteAsync($"/api/snapshot/delete/{snapshot.Id}");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("sucessfully deleted", content);
    }

    [Fact]
    public async Task DeleteSnapshotByIdAsync_WithInvalidSnapshot_ReturnsNotFound()
    {
        // Arrange
        var token = GenerateToken(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var nonExistentSnapshotId = Ulid.NewUlid();

        // Act
        var response = await _client.DeleteAsync($"/api/snapshot/delete/{nonExistentSnapshotId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteSnapshotByIdAsync_WithOtherUserSnapshot_ReturnsNotFound()
    {
        // Arrange
        var user1 = new UserEntity { Id = SnapshotTestConstants.TestUserId, Name = SnapshotTestConstants.TestUserName };
        var snapshot = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId, user1);

        await InsertSnapshot(snapshot);

        // Act - Try to delete with different user
        var token2 = GenerateToken(SnapshotTestConstants.TestUserId2, SnapshotTestConstants.TestUserName2);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token2}");
        var response = await _client.DeleteAsync($"/api/snapshot/delete/{snapshot.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteSnapshotByIdAsync_WithoutAuth_ReturnUnauthorized()
    {
        // Act
        var response = await _client.DeleteAsync($"/api/snapshot/delete/{Ulid.NewUlid()}");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteSnapshotByIdAsync_WithUserIsolation_UserCanOnlyDeleteOwnSnapshots()
    {
        // Arrange - Create snapshots for both users
        var user1 = new UserEntity { Id = SnapshotTestConstants.TestUserId, Name = SnapshotTestConstants.TestUserName };
        var user2 = new UserEntity { Id = SnapshotTestConstants.TestUserId2, Name = SnapshotTestConstants.TestUserName2 };

        var user1Snapshot = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId, user1);
        var user2Snapshot = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId2, user2);

        await InsertSnapshot(user1Snapshot);
        await InsertSnapshot(user2Snapshot);

        // Act & Assert - User 1 successfully deletes their own snapshot
        var token1 = GenerateToken(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token1}");

        var deleteOwnResponse = await _client.DeleteAsync($"/api/snapshot/delete/{user1Snapshot.Id}");

        Assert.Equal(HttpStatusCode.OK, deleteOwnResponse.StatusCode);

        // Act & Assert - User 1 cannot delete User 2's snapshot
        var deleteOtherResponse = await _client.DeleteAsync($"/api/snapshot/delete/{user2Snapshot.Id}");

        Assert.Equal(HttpStatusCode.NotFound, deleteOtherResponse.StatusCode);

        // Act & Assert - User 2 can delete their own snapshot
        var token2 = GenerateToken(SnapshotTestConstants.TestUserId2, SnapshotTestConstants.TestUserName2);
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token2}");

        var user2DeleteOwnResponse = await _client.DeleteAsync($"/api/snapshot/delete/{user2Snapshot.Id}");

        Assert.Equal(HttpStatusCode.OK, user2DeleteOwnResponse.StatusCode);

        // Act & Assert - User 2 cannot delete already-deleted User 1 snapshot
        var user2DeleteDeletedResponse = await _client.DeleteAsync($"/api/snapshot/delete/{user1Snapshot.Id}");

        Assert.Equal(HttpStatusCode.NotFound, user2DeleteDeletedResponse.StatusCode);
    }

    #endregion

    #region Helper Methods

    private async Task InsertSnapshot(SnapshotEntity snapshot)
    {
        const string snapshotSql = @"
            INSERT INTO Snapshots (Id, Timestamp, Description, UserId)
            VALUES (@Id, @Timestamp, @Description, @UserId)";

        using var connection = new SqliteConnection(_factory.ConnectionString);
        await connection.OpenAsync();

        using var snapshotCommand = connection.CreateCommand();
        snapshotCommand.CommandText = snapshotSql;
        snapshotCommand.Parameters.AddWithValue("@Id", snapshot.Id.ToString());
        snapshotCommand.Parameters.AddWithValue("@Timestamp", snapshot.Timestamp.ToString("o"));
        snapshotCommand.Parameters.AddWithValue("@Description", snapshot.Description ?? "");
        snapshotCommand.Parameters.AddWithValue("@UserId", snapshot.UserId.ToString());
        await snapshotCommand.ExecuteNonQueryAsync();

        // Insert root folder if it exists
        if (snapshot.RootFolder != null)
        {
            const string folderSql = @"
                INSERT INTO Folders (Id, Name, Size, Sha256Hash, CreatedAt, UpdatedAt, IsHidden, UserId)
                VALUES (@Id, @Name, @Size, @Sha256Hash, @CreatedAt, @UpdatedAt, @IsHidden, @UserId)";

            using var folderCommand = connection.CreateCommand();
            folderCommand.CommandText = folderSql;
            folderCommand.Parameters.AddWithValue("@Id", snapshot.RootFolder.Id.ToString());
            folderCommand.Parameters.AddWithValue("@Name", snapshot.RootFolder.Name);
            folderCommand.Parameters.AddWithValue("@Size", snapshot.RootFolder.Size);
            folderCommand.Parameters.AddWithValue("@Sha256Hash", snapshot.RootFolder.Sha256Hash);
            folderCommand.Parameters.AddWithValue("@CreatedAt", snapshot.RootFolder.CreatedAt);
            folderCommand.Parameters.AddWithValue("@UpdatedAt", snapshot.RootFolder.UpdatedAt);
            folderCommand.Parameters.AddWithValue("@IsHidden", snapshot.RootFolder.IsHidden ? 1 : 0);
            folderCommand.Parameters.AddWithValue("@UserId", snapshot.RootFolder.UserId.ToString());
            await folderCommand.ExecuteNonQueryAsync();

            // Link folder to snapshot via FoldersToFolders
            const string folderToFolderSql = @"
                INSERT OR IGNORE INTO FoldersToFolders (SnapshotId, ParentFolderId, ChildFolderId)
                VALUES (@SnapshotId, @ParentFolderId, @ChildFolderId)";

            using var folderToFolderCommand = connection.CreateCommand();
            folderToFolderCommand.CommandText = folderToFolderSql;
            folderToFolderCommand.Parameters.AddWithValue("@SnapshotId", snapshot.Id.ToString());
            folderToFolderCommand.Parameters.AddWithValue("@ParentFolderId", DBNull.Value);
            folderToFolderCommand.Parameters.AddWithValue("@ChildFolderId", snapshot.RootFolder.Id.ToString());
            try
            {
                await folderToFolderCommand.ExecuteNonQueryAsync();
            }
            catch
            {
                // Ignore errors for test data insertion
            }
        }
    }

    private async Task InsertFolder(SnapshotEntity snapshot, FolderEntity folder, FolderEntity? parentFolder, SqliteConnection connection)
    {
        const string folderSql = @"
            INSERT INTO Folders (Id, Name, Size, Sha256Hash, CreatedAt, UpdatedAt, IsHidden, UserId)
            VALUES (@Id, @Name, @Size, @Sha256Hash, @CreatedAt, @UpdatedAt, @IsHidden, @UserId)";

        using var command = connection.CreateCommand();
        command.CommandText = folderSql;
        command.Parameters.AddWithValue("@Id", folder.Id.ToString());
        command.Parameters.AddWithValue("@Name", folder.Name);
        command.Parameters.AddWithValue("@Size", folder.Size);
        command.Parameters.AddWithValue("@Sha256Hash", folder.Sha256Hash);
        command.Parameters.AddWithValue("@CreatedAt", folder.CreatedAt);
        command.Parameters.AddWithValue("@UpdatedAt", folder.UpdatedAt);
        command.Parameters.AddWithValue("@IsHidden", folder.IsHidden ? 1 : 0);
        command.Parameters.AddWithValue("@UserId", folder.UserId.ToString());
        await command.ExecuteNonQueryAsync();

        // Link folder to snapshot
        using var folderSnapshotCommand = connection.CreateCommand();
        folderSnapshotCommand.CommandText = "INSERT OR IGNORE INTO FoldersToFolders (SnapshotId, ParentFolderId, ChildFolderId) VALUES (@SnapshotId, @ParentFolderId, @ChildFolderId)";
        folderSnapshotCommand.Parameters.AddWithValue("@SnapshotId", snapshot.Id.ToString());
        folderSnapshotCommand.Parameters.AddWithValue("@ParentFolderId", parentFolder?.Id.ToString() ?? (object)DBNull.Value);
        folderSnapshotCommand.Parameters.AddWithValue("@ChildFolderId", folder.Id.ToString());
        await folderSnapshotCommand.ExecuteNonQueryAsync();
    }

    #endregion
}

#endregion
