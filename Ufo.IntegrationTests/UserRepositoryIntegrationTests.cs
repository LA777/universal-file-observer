using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Moq;
using System.Diagnostics;
using Ufo.Abstractions.Database.Entities;
using Ufo.Database.Contexts;
using Ufo.Database.Handlers;
using Ufo.Database.Repositories;
using FluentAssertions;

namespace Ufo.IntegrationTests
{
    public class UserRepositoryIntegrationTests : IAsyncDisposable
    {
        private readonly string _connectionString;
        private readonly Mock<ILogger<UserRepository>> _loggerMock;
        private UserRepository? _repository;

        public UserRepositoryIntegrationTests()
        {
            var databaseFileName = $"test-{Guid.NewGuid()}.db";
            _connectionString = $"Data Source={databaseFileName};Foreign Keys=True";
            _loggerMock = new Mock<ILogger<UserRepository>>();
        }

        #region Database Initialization and Cleanup

        async ValueTask IAsyncDisposable.DisposeAsync()
        {
            await Task.Run(CleanupDatabase);
            GC.SuppressFinalize(this);
        }

        private async Task InitializeDatabaseAsync()
        {
            // Register Dapper type handlers for Ulid types
            SqlMapper.AddTypeHandler(new SqlUlidTypeHandler());
            SqlMapper.AddTypeHandler(new SqlNullableUlidTypeHandler());
            SqlMapper.RemoveTypeMap(typeof(Ulid));
            SqlMapper.RemoveTypeMap(typeof(Ulid?));

            await DapperDataContext.InitiateDatabaseAsync(_connectionString);
            _repository = new UserRepository(_connectionString, _loggerMock.Object);
        }

        private void CleanupDatabase()
        {
            var connectionStringBuilder = new SqliteConnectionStringBuilder(_connectionString);
            var databasePath = connectionStringBuilder.DataSource;

            // Ensure repository is disposed to release database lock
            _repository = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();

            // Try to delete with retry logic for locked files
            int maxRetries = 3;
            int retryDelayMs = 100;

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    if (File.Exists(databasePath))
                    {
                        File.Delete(databasePath);
                    }

                    // Also try to delete the WAL (Write-Ahead Logging) file
                    var walFile = $"{databasePath}-wal";
                    if (File.Exists(walFile))
                    {
                        File.Delete(walFile);
                    }

                    // Also try to delete the SHM (Shared Memory) file
                    var shmFile = $"{databasePath}-shm";
                    if (File.Exists(shmFile))
                    {
                        File.Delete(shmFile);
                    }

                    break; // Success, exit retry loop
                }
                catch (IOException) when (attempt < maxRetries - 1)
                {
                    // File is locked, wait and retry
                    Thread.Sleep(retryDelayMs);
                    retryDelayMs *= 2; // Exponential backoff
                }
                catch (Exception ex)
                {
                    // Log the exception for debugging but don't throw
                    Debug.WriteLine($"Failed to delete database file: {ex.Message}");
                }
            }
        }

        #endregion

        #region CreateUserAsync Tests

        [Fact]
        public async Task CreateUserAsync_WithValidUser_CreatesUserSuccessfully()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var user = new UserEntity
                {
                    Id = Ulid.NewUlid(),
                    Name = "testuser",
                    PasswordHash = "hashedpassword123"
                };

                // Act
                var result = await _repository!.CreateUserAsync(user);

                // Assert
                result.Should().BeTrue();
                var retrievedUser = await _repository.GetUserByUsernameAsync("testuser");
                retrievedUser.Should().NotBeNull();
                retrievedUser!.Name.Should().Be("testuser");
                retrievedUser.PasswordHash.Should().Be("hashedpassword123");
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task CreateUserAsync_WithMultipleUsers_CreatesAllUsersSuccessfully()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
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
                var result1 = await _repository!.CreateUserAsync(user1);
                var result2 = await _repository.CreateUserAsync(user2);
                var result3 = await _repository.CreateUserAsync(user3);

                // Assert
                result1.Should().BeTrue();
                result2.Should().BeTrue();
                result3.Should().BeTrue();

                var userCount = await _repository.GetUserCountAsync();
                userCount.Should().Be(3);
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task CreateUserAsync_WithDuplicateUsername_ThrowsException()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
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

                await _repository!.CreateUserAsync(user1);

                // Act & Assert
                await Assert.ThrowsAsync<SqliteException>(() => _repository.CreateUserAsync(user2));
            }
            finally
            {
                CleanupDatabase();
            }
        }

        #endregion

        #region GetUserByUsernameAsync Tests

        [Fact]
        public async Task GetUserByUsernameAsync_WithExistingUser_ReturnsUser()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var user = new UserEntity
                {
                    Id = Ulid.NewUlid(),
                    Name = "findme",
                    PasswordHash = "secrethash"
                };
                await _repository!.CreateUserAsync(user);

                // Act
                var retrievedUser = await _repository.GetUserByUsernameAsync("findme");

                // Assert
                retrievedUser.Should().NotBeNull();
                retrievedUser!.Name.Should().Be("findme");
                retrievedUser.PasswordHash.Should().Be("secrethash");
                retrievedUser.Id.Should().Be(user.Id);
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task GetUserByUsernameAsync_WithNonExistentUser_ReturnsNull()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                // Act
                var retrievedUser = await _repository!.GetUserByUsernameAsync("nonexistent");

                // Assert
                retrievedUser.Should().BeNull();
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task GetUserByUsernameAsync_WithMultipleUsers_ReturnsCorrectUser()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var user1 = new UserEntity { Id = Ulid.NewUlid(), Name = "alice", PasswordHash = "hash1" };
                var user2 = new UserEntity { Id = Ulid.NewUlid(), Name = "bob", PasswordHash = "hash2" };
                var user3 = new UserEntity { Id = Ulid.NewUlid(), Name = "charlie", PasswordHash = "hash3" };

                await _repository!.CreateUserAsync(user1);
                await _repository.CreateUserAsync(user2);
                await _repository.CreateUserAsync(user3);

                // Act
                var retrievedUser = await _repository.GetUserByUsernameAsync("bob");

                // Assert
                retrievedUser.Should().NotBeNull();
                retrievedUser!.Name.Should().Be("bob");
                retrievedUser.PasswordHash.Should().Be("hash2");
            }
            finally
            {
                CleanupDatabase();
            }
        }

        #endregion

        #region UserExistsAsync Tests

        [Fact]
        public async Task UserExistsAsync_WithExistingUser_ReturnsTrue()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var user = new UserEntity
                {
                    Id = Ulid.NewUlid(),
                    Name = "existinguser",
                    PasswordHash = "hash"
                };
                await _repository!.CreateUserAsync(user);

                // Act
                var exists = await _repository.UserExistsAsync("existinguser");

                // Assert
                exists.Should().BeTrue();
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task UserExistsAsync_WithNonExistentUser_ReturnsFalse()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                // Act
                var exists = await _repository!.UserExistsAsync("nonexistent");

                // Assert
                exists.Should().BeFalse();
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task UserExistsAsync_WithMultipleUsers_ReturnsTrueOnlyForExisting()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var user1 = new UserEntity { Id = Ulid.NewUlid(), Name = "alice", PasswordHash = "hash1" };
                var user2 = new UserEntity { Id = Ulid.NewUlid(), Name = "bob", PasswordHash = "hash2" };

                await _repository!.CreateUserAsync(user1);
                await _repository.CreateUserAsync(user2);

                // Act
                var aliceExists = await _repository.UserExistsAsync("alice");
                var bobExists = await _repository.UserExistsAsync("bob");
                var charlieExists = await _repository.UserExistsAsync("charlie");

                // Assert
                aliceExists.Should().BeTrue();
                bobExists.Should().BeTrue();
                charlieExists.Should().BeFalse();
            }
            finally
            {
                CleanupDatabase();
            }
        }

        #endregion

        #region GetUserCountAsync Tests

        [Fact]
        public async Task GetUserCountAsync_WhenNoUsersExist_ReturnsZero()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                // Act
                var count = await _repository!.GetUserCountAsync();

                // Assert
                count.Should().Be(0);
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task GetUserCountAsync_WithSingleUser_ReturnsOne()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var user = new UserEntity
                {
                    Id = Ulid.NewUlid(),
                    Name = "singleuser",
                    PasswordHash = "hash"
                };
                await _repository!.CreateUserAsync(user);

                // Act
                var count = await _repository.GetUserCountAsync();

                // Assert
                count.Should().Be(1);
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task GetUserCountAsync_WithMultipleUsers_ReturnsCorrectCount()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
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
                    await _repository!.CreateUserAsync(user);
                }

                // Act
                var count = await _repository!.GetUserCountAsync();

                // Assert
                count.Should().Be(5);
            }
            finally
            {
                CleanupDatabase();
            }
        }

        #endregion

        #region Integration Tests

        [Fact]
        public async Task UserRepository_CompleteWorkflow_CreatesAndRetrievesUserSuccessfully()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var newUser = new UserEntity
                {
                    Id = Ulid.NewUlid(),
                    Name = "workflowuser",
                    PasswordHash = "hashed_password_123"
                };

                // Act - Create user
                var createResult = await _repository!.CreateUserAsync(newUser);
                createResult.Should().BeTrue();

                // Act - Check if user exists
                var exists = await _repository.UserExistsAsync("workflowuser");
                exists.Should().BeTrue();

                // Act - Get total user count
                var userCount = await _repository.GetUserCountAsync();
                userCount.Should().Be(1);

                // Act - Retrieve user by username
                var retrievedUser = await _repository.GetUserByUsernameAsync("workflowuser");

                // Assert
                retrievedUser.Should().NotBeNull();
                retrievedUser!.Name.Should().Be("workflowuser");
                retrievedUser.PasswordHash.Should().Be("hashed_password_123");
                retrievedUser.Id.Should().Be(newUser.Id);
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task UserRepository_WithCaseSensitiveUsernames_TreatsUsernamesCorrectly()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var user1 = new UserEntity { Id = Ulid.NewUlid(), Name = "TestUser", PasswordHash = "hash1" };
                var user2 = new UserEntity { Id = Ulid.NewUlid(), Name = "testuser", PasswordHash = "hash2" };

                // Act
                await _repository!.CreateUserAsync(user1);
                await _repository.CreateUserAsync(user2);

                var foundUser1 = await _repository.GetUserByUsernameAsync("TestUser");
                var foundUser2 = await _repository.GetUserByUsernameAsync("testuser");
                var count = await _repository.GetUserCountAsync();

                // Assert - Usernames are case-sensitive in the database
                foundUser1.Should().NotBeNull();
                foundUser2.Should().NotBeNull();
                foundUser1!.Name.Should().Be("TestUser");
                foundUser2!.Name.Should().Be("testuser");
                count.Should().Be(2);
            }
            finally
            {
                CleanupDatabase();
            }
        }

        #endregion
    }
}
