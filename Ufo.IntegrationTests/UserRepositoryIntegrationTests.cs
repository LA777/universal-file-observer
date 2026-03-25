using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Moq;
using Ufo.Abstractions.Database;
using Ufo.Abstractions.Database.Entities;
using Ufo.Database.Contexts;
using Ufo.Database.Repositories;

namespace Ufo.IntegrationTests;

public class UserRepositoryIntegrationTests : IAsyncLifetime
{
    private Mock<ILogger<UserRepository>> _loggerMock;
    private Mock<IDbConnectionFactory> _dbConnectionFactoryMock;
    private SqliteConnection _sqLiteConnection;
    private UserRepository? _labelsRepository;

    #region Database Initialization and Cleanup

    public async Task InitializeAsync()
    {
        var dbName = $"testdb-{Guid.NewGuid()}";
        var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared;Foreign Keys=True";

        _dbConnectionFactoryMock = new Mock<IDbConnectionFactory>();
        _sqLiteConnection = new SqliteConnection(connectionString);
        await _sqLiteConnection.OpenAsync();
        _dbConnectionFactoryMock.Setup(f => f.GetSqliteConnectionAsync())
            .ReturnsAsync(() => _sqLiteConnection);

        _loggerMock = new Mock<ILogger<UserRepository>>();

        await DapperDataContext.InitiateDatabaseAsync(_sqLiteConnection);
        _labelsRepository = new UserRepository(_dbConnectionFactoryMock.Object, _loggerMock.Object);          
    }

    public async Task DisposeAsync()
    {
        if (_sqLiteConnection is not null)
        {
            await _sqLiteConnection.DisposeAsync();
        }
    }

    #endregion

    #region CreateUserAsync Tests

    [Fact]
    public async Task CreateUserAsync_WithValidUser_CreatesUserSuccessfully()
    {
        // Arrange
        var user = new UserEntity
        {
            Id = Ulid.NewUlid(),
            Name = "testuser",
            PasswordHash = "hashedpassword123"
        };

        // Act
        var result = await _labelsRepository!.CreateUserAsync(user);

        // Assert
        result.Should().BeTrue();
        var retrievedUser = await _labelsRepository.GetUserByUsernameAsync("testuser");
        retrievedUser.Should().NotBeNull();
        retrievedUser!.Name.Should().Be("testuser");
        retrievedUser.PasswordHash.Should().Be("hashedpassword123");
    }

    [Fact]
    public async Task CreateUserAsync_WithMultipleUsers_CreatesAllUsersSuccessfully()
    {
        // Arrange
        var user1 = new UserEntity
        {
            Id = Ulid.NewUlid(),
            Name = "user1",
            PasswordHash = "hash1"
        };
        var user2 = new UserEntity
        {
            Id = Ulid.NewUlid(),
            Name = "user2",
            PasswordHash = "hash2"
        };
        var user3 = new UserEntity
        {
            Id = Ulid.NewUlid(),
            Name = "user3",
            PasswordHash = "hash3"
        };

        // Act
        var result1 = await _labelsRepository!.CreateUserAsync(user1);
        var result2 = await _labelsRepository.CreateUserAsync(user2);
        var result3 = await _labelsRepository.CreateUserAsync(user3);

        // Assert
        result1.Should().BeTrue();
        result2.Should().BeTrue();
        result3.Should().BeTrue();

        var userCount = await _labelsRepository.GetUserCountAsync();
        userCount.Should().Be(3);
    }

    [Fact]
    public async Task CreateUserAsync_WithDuplicateUsername_ThrowsException()
    {
        // Arrange
        var user1 = new UserEntity
        {
            Id = Ulid.NewUlid(),
            Name = "duplicateuser",
            PasswordHash = "hash1"
        };
        var user2 = new UserEntity
        {
            Id = Ulid.NewUlid(),
            Name = "duplicateuser",
            PasswordHash = "hash2"
        };

        await _labelsRepository!.CreateUserAsync(user1);

        // Act & Assert
        await Assert.ThrowsAsync<SqliteException>(() => _labelsRepository.CreateUserAsync(user2));
    }

    #endregion

    #region GetUserByUsernameAsync Tests

    [Fact]
    public async Task GetUserByUsernameAsync_WithExistingUser_ReturnsUser()
    {
        // Arrange
        var user = new UserEntity
        {
            Id = Ulid.NewUlid(),
            Name = "findme",
            PasswordHash = "secrethash"
        };
        await _labelsRepository!.CreateUserAsync(user);

        // Act
        var retrievedUser = await _labelsRepository.GetUserByUsernameAsync("findme");

        // Assert
        retrievedUser.Should().NotBeNull();
        retrievedUser!.Name.Should().Be("findme");
        retrievedUser.PasswordHash.Should().Be("secrethash");
        retrievedUser.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task GetUserByUsernameAsync_WithNonExistentUser_ReturnsNull()
    {
        // Arrange
        // Act
        var retrievedUser = await _labelsRepository!.GetUserByUsernameAsync("nonexistent");

        // Assert
        retrievedUser.Should().BeNull();
    }

    [Fact]
    public async Task GetUserByUsernameAsync_WithMultipleUsers_ReturnsCorrectUser()
    {
        // Arrange
        var user1 = new UserEntity { Id = Ulid.NewUlid(), Name = "alice", PasswordHash = "hash1" };
        var user2 = new UserEntity { Id = Ulid.NewUlid(), Name = "bob", PasswordHash = "hash2" };
        var user3 = new UserEntity { Id = Ulid.NewUlid(), Name = "charlie", PasswordHash = "hash3" };

        await _labelsRepository!.CreateUserAsync(user1);
        await _labelsRepository.CreateUserAsync(user2);
        await _labelsRepository.CreateUserAsync(user3);

        // Act
        var retrievedUser = await _labelsRepository.GetUserByUsernameAsync("bob");

        // Assert
        retrievedUser.Should().NotBeNull();
        retrievedUser!.Name.Should().Be("bob");
        retrievedUser.PasswordHash.Should().Be("hash2");
    }

    #endregion

    #region UserExistsAsync Tests

    [Fact]
    public async Task UserExistsAsync_WithExistingUser_ReturnsTrue()
    {
        // Arrange
        var user = new UserEntity
        {
            Id = Ulid.NewUlid(),
            Name = "existinguser",
            PasswordHash = "hash"
        };
        await _labelsRepository!.CreateUserAsync(user);

        // Act
        var exists = await _labelsRepository.UserExistsAsync("existinguser");

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task UserExistsAsync_WithNonExistentUser_ReturnsFalse()
    {
        // Arrange
        // Act
        var exists = await _labelsRepository!.UserExistsAsync("nonexistent");

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task UserExistsAsync_WithMultipleUsers_ReturnsTrueOnlyForExisting()
    {
        // Arrange
        var user1 = new UserEntity { Id = Ulid.NewUlid(), Name = "alice", PasswordHash = "hash1" };
        var user2 = new UserEntity { Id = Ulid.NewUlid(), Name = "bob", PasswordHash = "hash2" };

        await _labelsRepository!.CreateUserAsync(user1);
        await _labelsRepository.CreateUserAsync(user2);

        // Act
        var aliceExists = await _labelsRepository.UserExistsAsync("alice");
        var bobExists = await _labelsRepository.UserExistsAsync("bob");
        var charlieExists = await _labelsRepository.UserExistsAsync("charlie");

        // Assert
        aliceExists.Should().BeTrue();
        bobExists.Should().BeTrue();
        charlieExists.Should().BeFalse();
    }

    #endregion

    #region GetUserCountAsync Tests

    [Fact]
    public async Task GetUserCountAsync_WhenNoUsersExist_ReturnsZero()
    {
        // Arrange & Act
        var count = await _labelsRepository!.GetUserCountAsync();

        // Assert
        count.Should().Be(0);
    }

    [Fact]
    public async Task GetUserCountAsync_WithSingleUser_ReturnsOne()
    {
        // Arrange
        var user = new UserEntity
        {
            Id = Ulid.NewUlid(),
            Name = "singleuser",
            PasswordHash = "hash"
        };
        await _labelsRepository!.CreateUserAsync(user);

        // Act
        var count = await _labelsRepository.GetUserCountAsync();

        // Assert
        count.Should().Be(1);
    }

    [Fact]
    public async Task GetUserCountAsync_WithMultipleUsers_ReturnsCorrectCount()
    {
        // Arrange
        var users = new List<UserEntity>
            {
                new() { Id = Ulid.NewUlid(), Name = "user1", PasswordHash = "hash1" },
                new() { Id = Ulid.NewUlid(), Name = "user2", PasswordHash = "hash2" },
                new() { Id = Ulid.NewUlid(), Name = "user3", PasswordHash = "hash3" },
                new() { Id = Ulid.NewUlid(), Name = "user4", PasswordHash = "hash4" },
                new() { Id = Ulid.NewUlid(), Name = "user5", PasswordHash = "hash5" }
            };

        foreach (var user in users)
        {
            await _labelsRepository!.CreateUserAsync(user);
        }

        // Act
        var count = await _labelsRepository!.GetUserCountAsync();

        // Assert
        count.Should().Be(5);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task UserRepository_CompleteWorkflow_CreatesAndRetrievesUserSuccessfully()
    {
        // Arrange
        var newUser = new UserEntity
        {
            Id = Ulid.NewUlid(),
            Name = "workflowuser",
            PasswordHash = "hashed_password_123"
        };

        // Act - Create user
        var createResult = await _labelsRepository!.CreateUserAsync(newUser);
        createResult.Should().BeTrue();

        // Act - Check if user exists
        var exists = await _labelsRepository.UserExistsAsync("workflowuser");
        exists.Should().BeTrue();

        // Act - Get total user count
        var userCount = await _labelsRepository.GetUserCountAsync();
        userCount.Should().Be(1);

        // Act - Retrieve user by username
        var retrievedUser = await _labelsRepository.GetUserByUsernameAsync("workflowuser");

        // Assert
        retrievedUser.Should().NotBeNull();
        retrievedUser!.Name.Should().Be("workflowuser");
        retrievedUser.PasswordHash.Should().Be("hashed_password_123");
        retrievedUser.Id.Should().Be(newUser.Id);
    }

    [Fact]
    public async Task UserRepository_WithCaseSensitiveUsernames_TreatsUsernamesCorrectly()
    {
        // Arrange
        var user1 = new UserEntity { Id = Ulid.NewUlid(), Name = "TestUser", PasswordHash = "hash1" };
        var user2 = new UserEntity { Id = Ulid.NewUlid(), Name = "testuser", PasswordHash = "hash2" };

        // Act
        await _labelsRepository!.CreateUserAsync(user1);
        await _labelsRepository.CreateUserAsync(user2);

        var foundUser1 = await _labelsRepository.GetUserByUsernameAsync("TestUser");
        var foundUser2 = await _labelsRepository.GetUserByUsernameAsync("testuser");
        var count = await _labelsRepository.GetUserCountAsync();

        // Assert - Usernames are case-sensitive in the database
        foundUser1.Should().NotBeNull();
        foundUser2.Should().NotBeNull();
        foundUser1!.Name.Should().Be("TestUser");
        foundUser2!.Name.Should().Be("testuser");
        count.Should().Be(2);
    }

    #endregion
}
