using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Diagnostics;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Options;
using Ufo.Database.Contexts;
using Ufo.Database.Handlers;
using Ufo.Database.Repositories;

namespace Ufo.IntegrationTests
{
    public class LabelsRepositoryIntegrationTests : IAsyncDisposable
    {
        private readonly string _connectionString;
        private UserEntity testUser = new() { Id = Ulid.NewUlid(), Name = "TestUser" };
        private readonly Mock<ILogger<LabelsRepository>> _loggerMock;
        private readonly Mock<IOptionsMonitor<DatabaseOptions>> _optionsMonitorMock;
        private FileSystemRepository? _fileSystemRepository;
        private LabelsRepository? _repository;

        public LabelsRepositoryIntegrationTests()
        {
            var databaseFileName = $"test-{Guid.NewGuid()}.db";
            _connectionString = $"Data Source={databaseFileName};Foreign Keys=True";
            _loggerMock = new Mock<ILogger<LabelsRepository>>();
            _optionsMonitorMock = new Mock<IOptionsMonitor<DatabaseOptions>>();
            _optionsMonitorMock.Setup(o => o.CurrentValue)
                .Returns(new DatabaseOptions { ConnectionString = _connectionString });

            var fileSystemSqLiteRepositoryLoggerMock = new Mock<ILogger<FileSystemRepository>>();
            _fileSystemRepository = new FileSystemRepository(_optionsMonitorMock.Object, fileSystemSqLiteRepositoryLoggerMock.Object);
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
            _repository = new LabelsRepository(_optionsMonitorMock.Object, _loggerMock.Object);

            // Insert test user
            await using var sqLiteConnection = new SqliteConnection(_connectionString);
            await sqLiteConnection.OpenAsync();
            await sqLiteConnection.ExecuteAsync(
                "INSERT INTO Users (Id, Name, PasswordHash) VALUES (@Id, @Name, @PasswordHash)",
                new { testUser.Id, testUser.Name, PasswordHash = "hash" });
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

        #region AddLabelAsync Tests

        [Fact]
        public async Task AddLabelAsync_WithValidLabel_CreatesLabelSuccessfully()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var label = new LabelEntity { Name = "Important", ColorHex = "#FF0000", UserId = testUser.Id, User = testUser };

                // Act
                var result = await _repository!.AddLabelAsync(label, testUser.Id);

                // Assert
                result.All(r => r.Result == Result.Success).Should().BeTrue();
                var allLabels = await _repository.GetAllLabelsAsync(testUser.Id);
                allLabels.Should().Contain(l => l.Name == "Important" && l.ColorHex == "#FF0000");
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task AddLabelAsync_WithMultipleLabels_CreatesAllLabelsSuccessfully()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var label1 = new LabelEntity { Name = "Important", ColorHex = "#FF0000", UserId = testUser.Id, User = testUser };
                var label2 = new LabelEntity { Name = "Archived", ColorHex = "#808080", UserId = testUser.Id, User = testUser };
                var label3 = new LabelEntity { Name = "Recent", ColorHex = "#00FF00", UserId = testUser.Id, User = testUser };

                // Act
                var result1 = await _repository!.AddLabelAsync(label1, testUser.Id);
                var result2 = await _repository.AddLabelAsync(label2, testUser.Id);
                var result3 = await _repository.AddLabelAsync(label3, testUser.Id);

                // Assert
                result1.All(r => r.Result == Result.Success).Should().BeTrue();
                result2.All(r => r.Result == Result.Success).Should().BeTrue();
                result3.All(r => r.Result == Result.Success).Should().BeTrue();
                var allLabels = await _repository.GetAllLabelsAsync(testUser.Id);
                allLabels.Should().HaveCount(3);
                allLabels.Should().Contain(l => l.Name == "Important");
                allLabels.Should().Contain(l => l.Name == "Archived");
                allLabels.Should().Contain(l => l.Name == "Recent");
            }
            finally
            {
                CleanupDatabase();
            }
        }

        #endregion

        #region GetAllLabelsAsync Tests

        [Fact]
        public async Task GetAllLabelsAsync_WhenNoLabelsExist_ReturnsEmptyList()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                // Act
                var labels = await _repository!.GetAllLabelsAsync(testUser.Id);

                // Assert
                labels.Should().BeEmpty();
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task GetAllLabelsAsync_WhenLabelsExist_ReturnsAllLabels()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var label1 = new LabelEntity { Name = "Priority1", ColorHex = "#FF0000", UserId = testUser.Id, User = testUser };
                var label2 = new LabelEntity { Name = "Priority2", ColorHex = "#FFFF00", UserId = testUser.Id, User = testUser };

                await _repository!.AddLabelAsync(label1, testUser.Id);
                await _repository.AddLabelAsync(label2, testUser.Id);

                // Act
                var labels = await _repository.GetAllLabelsAsync(testUser.Id);

                // Assert
                labels.Should().HaveCount(2);
                labels.Should().Contain(l => l.Name == "Priority1");
                labels.Should().Contain(l => l.Name == "Priority2");
            }
            finally
            {
                CleanupDatabase();
            }
        }

        #endregion

        #region GetLabelsBySnapshotIdAsync Tests

        [Fact]
        public async Task GetLabelsBySnapshotIdAsync_WhenSnapshotHasNoLabels_ReturnsEmptyList()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var snapshotId = Ulid.NewUlid();

                // Act
                var labels = await _repository!.GetLabelsBySnapshotIdAsync(snapshotId, testUser.Id);

                // Assert
                labels.Should().BeEmpty();
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task GetLabelsBySnapshotIdAsync_WhenSnapshotHasLabels_ReturnsOnlyAssociatedLabels()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var label1 = new LabelEntity { Name = "Tagged", ColorHex = "#0000FF", UserId = testUser.Id, User = testUser };
                var label2 = new LabelEntity { Name = "Unrelated", ColorHex = "#00FF00", UserId = testUser.Id, User = testUser };
                var snapshot = CreateSnapshotWithSimpleFolder();
                await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);

                // Add labels
                await _repository!.AddLabelAsync(label1, testUser.Id);
                await _repository.AddLabelAsync(label2, testUser.Id);

                // Associate label1 with snapshot
                await _repository.AddLabelToSnapshotAsync(label1.Id, snapshot.Id, testUser.Id);

                // Act
                var labels = await _repository.GetLabelsBySnapshotIdAsync(snapshot.Id, testUser.Id);

                // Assert
                labels.Should().HaveCount(1);
                labels.Should().Contain(l => l.Name == "Tagged");
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task GetLabelsBySnapshotIdAsync_WithMultipleLabelsOnSnapshot_ReturnsAllAssociatedLabels()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var label1 = new LabelEntity { Name = "Label1", ColorHex = "#FF0000", UserId = testUser.Id, User = testUser };
                var label2 = new LabelEntity { Name = "Label2", ColorHex = "#00FF00", UserId = testUser.Id, User = testUser };
                var label3 = new LabelEntity { Name = "Label3", ColorHex = "#0000FF", UserId = testUser.Id, User = testUser };
                var snapshot = CreateSnapshotWithSimpleFolder();
                await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);

                // Add labels
                await _repository!.AddLabelAsync(label1, testUser.Id);
                await _repository.AddLabelAsync(label2, testUser.Id);
                await _repository.AddLabelAsync(label3, testUser.Id);

                // Associate label1 and label2 with snapshot
                await _repository.AddLabelToSnapshotAsync(label1.Id, snapshot.Id, testUser.Id);
                await _repository.AddLabelToSnapshotAsync(label2.Id, snapshot.Id, testUser.Id);

                // Act
                var labels = await _repository.GetLabelsBySnapshotIdAsync(snapshot.Id, testUser.Id);

                // Assert
                labels.Should().HaveCount(2);
                labels.Should().Contain(l => l.Name == "Label1");
                labels.Should().Contain(l => l.Name == "Label2");
                labels.Should().NotContain(l => l.Name == "Label3");
            }
            finally
            {
                CleanupDatabase();
            }
        }

        #endregion

        #region UpdateLabel Tests

        [Fact]
        public async Task UpdateLabelAsync_WithValidLabel_UpdatesLabelSuccessfully()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var label = new LabelEntity { Name = "Original", ColorHex = "#FF0000", UserId = testUser.Id, User = testUser };
                await _repository!.AddLabelAsync(label, testUser.Id);

                label.Name = "Updated";
                label.ColorHex = "#00FF00";

                // Act
                var result = await _repository.UpdateLabelAsync(label, testUser.Id);

                // Assert
                Assert.Equal(Result.Success, result.Result);
                var allLabels = await _repository.GetAllLabelsAsync(testUser.Id);
                var updatedLabel = allLabels.FirstOrDefault(l => l.Id == label.Id);
                updatedLabel.Should().NotBeNull();
                updatedLabel!.Name.Should().Be("Updated");
                updatedLabel.ColorHex.Should().Be("#00FF00");
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task UpdateLabelAsync_WithNonExistentLabel_DoesNotThrow()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var nonExistentLabel = new LabelEntity { Name = "NonExistent", ColorHex = "#FFFFFF", UserId = testUser.Id, User = testUser };
                nonExistentLabel.GetType().GetProperty("Id")?.SetValue(nonExistentLabel, Ulid.NewUlid());

                // Act & Assert - Should not throw
                var result = await _repository!.UpdateLabelAsync(nonExistentLabel, testUser.Id);
                Assert.Equal(Result.NotFound, result.Result);
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task UpdateLabelAsync_UpdatesOnlyTargetLabel()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var label1 = new LabelEntity { Name = "Label1", ColorHex = "#FF0000", UserId = testUser.Id, User = testUser };
                var label2 = new LabelEntity { Name = "Label2", ColorHex = "#00FF00", UserId = testUser.Id, User = testUser };

                var addLabelResult1 = await _repository!.AddLabelAsync(label1, testUser.Id);
                var addLabelResult2 = await _repository.AddLabelAsync(label2, testUser.Id);

                label1.Name = "ModifiedLabel1";

                // Act
                var updateLabelResult = await _repository.UpdateLabelAsync(label1, testUser.Id);

                // Assert
                addLabelResult1.All(r => r.Result == Result.Success).Should().BeTrue();
                addLabelResult2.All(r => r.Result == Result.Success).Should().BeTrue();
                Assert.Equal(Result.Success, updateLabelResult.Result);
                var allLabels = await _repository.GetAllLabelsAsync(testUser.Id);
                var updatedLabel1 = allLabels.First(l => l.Id == label1.Id);
                var unchangedLabel2 = allLabels.First(l => l.Id == label2.Id);

                updatedLabel1.Name.Should().Be("ModifiedLabel1");
                unchangedLabel2.Name.Should().Be("Label2");
            }
            finally
            {
                CleanupDatabase();
            }
        }

        #endregion

        #region DeleteLabelByIdAsync Tests

        [Fact]
        public async Task DeleteLabelByIdAsync_WhenLabelExists_DeletesLabelSuccessfully()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var label = new LabelEntity { Name = "ToDelete", ColorHex = "#FF0000", UserId = testUser.Id, User = testUser };
                await _repository!.AddLabelAsync(label, testUser.Id);

                // Act
                var deleteResult = await _repository.DeleteLabelByIdAsync(label.Id, testUser.Id);

                // Assert
                Assert.Equal(Result.Success, deleteResult.Result);
                var allLabels = await _repository.GetAllLabelsAsync(testUser.Id);
                allLabels.Should().NotContain(l => l.Id == label.Id);
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task DeleteLabelByIdAsync_WhenLabelNotFound_ReturnsZero()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var nonExistentLabelId = Ulid.NewUlid();

                // Act
                var deleteResult = await _repository!.DeleteLabelByIdAsync(nonExistentLabelId, testUser.Id);

                // Assert
                Assert.Equal(Result.NotFound, deleteResult.Result);
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task DeleteLabelByIdAsync_RemovesAssociationsFromLabelsToSnapshots()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var label = new LabelEntity { Name = "AssociatedLabel", ColorHex = "#00FF00", UserId = testUser.Id, User = testUser };
                await _repository!.AddLabelAsync(label, testUser.Id);

                var snapshot = CreateSnapshotWithSimpleFolder();
                await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);
                await _repository.AddLabelToSnapshotAsync(label.Id, snapshot.Id, testUser.Id);

                // Act
                await _repository.DeleteLabelByIdAsync(label.Id, testUser.Id);

                // Assert
                // Verify label is deleted
                var allLabels = await _repository.GetAllLabelsAsync(testUser.Id);
                allLabels.Should().NotContain(l => l.Id == label.Id);

                // Verify association is deleted
                await using var connection = new SqliteConnection(_connectionString);
                var association = await connection.QueryFirstOrDefaultAsync(
                    "SELECT * FROM LabelsToSnapshots WHERE LabelId = @LabelId",
                    new { LabelId = label.Id });
                Assert.Null(association);
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task DeleteLabelByIdAsync_OnlyDeletesTargetLabel()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var label1 = new LabelEntity { Name = "Label1", ColorHex = "#FF0000", UserId = testUser.Id, User = testUser };
                var label2 = new LabelEntity { Name = "Label2", ColorHex = "#00FF00", UserId = testUser.Id, User = testUser };

                await _repository!.AddLabelAsync(label1, testUser.Id);
                await _repository.AddLabelAsync(label2, testUser.Id);

                // Act
                await _repository.DeleteLabelByIdAsync(label1.Id, testUser.Id);

                // Assert
                var allLabels = await _repository.GetAllLabelsAsync(testUser.Id);
                allLabels.Should().HaveCount(1);
                allLabels.Should().Contain(l => l.Id == label2.Id);
                allLabels.Should().NotContain(l => l.Id == label1.Id);
            }
            finally
            {
                CleanupDatabase();
            }
        }

        #endregion

        #region AddLabelToSnapshotAsync Tests

        [Fact]
        public async Task AddLabelToSnapshotAsync_WithValidLabelAndSnapshot_CreatesAssociation()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var label = new LabelEntity { Name = "TestLabel", ColorHex = "#0000FF", UserId = testUser.Id, User = testUser };
                await _repository!.AddLabelAsync(label, testUser.Id);
                var snapshot = CreateSnapshotWithSimpleFolder();
                await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);

                // Act
                var result = await _repository.AddLabelToSnapshotAsync(label.Id, snapshot.Id, testUser.Id);

                // Assert
                Assert.Equal(Result.Success, result.Result);
                var labels = await _repository.GetLabelsBySnapshotIdAsync(snapshot.Id, testUser.Id);
                labels.Should().HaveCount(1);
                labels.Should().Contain(l => l.Id == label.Id);
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task AddLabelToSnapshotAsync_WithMultipleLabelsSameSnapshot_CreatesAllAssociations()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var label1 = new LabelEntity { Name = "Label1", ColorHex = "#FF0000", UserId = testUser.Id, User = testUser };
                var label2 = new LabelEntity { Name = "Label2", ColorHex = "#00FF00", UserId = testUser.Id, User = testUser };
                var label3 = new LabelEntity { Name = "Label3", ColorHex = "#0000FF", UserId = testUser.Id, User = testUser };

                await _repository!.AddLabelAsync(label1, testUser.Id);
                await _repository.AddLabelAsync(label2, testUser.Id);
                await _repository.AddLabelAsync(label3, testUser.Id);

                var snapshot = CreateSnapshotWithSimpleFolder();
                await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);

                // Act
                await _repository.AddLabelToSnapshotAsync(label1.Id, snapshot.Id, testUser.Id);
                await _repository.AddLabelToSnapshotAsync(label2.Id, snapshot.Id, testUser.Id);
                await _repository.AddLabelToSnapshotAsync(label3.Id, snapshot.Id, testUser.Id);

                // Assert
                var labels = await _repository.GetLabelsBySnapshotIdAsync(snapshot.Id, testUser.Id);
                labels.Should().HaveCount(3);
                labels.Should().Contain(l => l.Id == label1.Id);
                labels.Should().Contain(l => l.Id == label2.Id);
                labels.Should().Contain(l => l.Id == label3.Id);
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task AddLabelToSnapshotAsync_WithSameLabelMultipleSnapshots_CreatesMultipleAssociations()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var label = new LabelEntity { Name = "SharedLabel", ColorHex = "#FFFF00", UserId = testUser.Id, User = testUser };
                await _repository!.AddLabelAsync(label, testUser.Id);

                var snapshot1 = CreateSnapshotWithSimpleFolder();
                await _fileSystemRepository!.AddSnapshotAsync(snapshot1, testUser.Id);
                var snapshot2 = CreateSnapshotWithSimpleFolder();
                await _fileSystemRepository!.AddSnapshotAsync(snapshot2, testUser.Id);

                // Act
                await _repository.AddLabelToSnapshotAsync(label.Id, snapshot1.Id, testUser.Id);
                await _repository.AddLabelToSnapshotAsync(label.Id, snapshot2.Id, testUser.Id);

                // Assert
                var labels1 = await _repository.GetLabelsBySnapshotIdAsync(snapshot1.Id, testUser.Id);
                var labels2 = await _repository.GetLabelsBySnapshotIdAsync(snapshot2.Id, testUser.Id);

                labels1.Should().HaveCount(1);
                labels2.Should().HaveCount(1);
                labels1.Should().Contain(l => l.Id == label.Id);
                labels2.Should().Contain(l => l.Id == label.Id);
            }
            finally
            {
                CleanupDatabase();
            }
        }

        #endregion

        #region RemoveLabelFromSnapshotAsync Tests

        [Fact]
        public async Task RemoveLabelFromSnapshotAsync_WithValidAssociation_RemovesAssociation()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var label = new LabelEntity { Name = "RemoveLabel", ColorHex = "#FF00FF", UserId = testUser.Id, User = testUser };
                await _repository!.AddLabelAsync(label, testUser.Id);
                var snapshot = CreateSnapshotWithSimpleFolder();
                await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);
                await _repository.AddLabelToSnapshotAsync(label.Id, snapshot.Id, testUser.Id);

                // Act
                var result = await _repository.RemoveLabelFromSnapshotAsync(label.Id, snapshot.Id, testUser.Id);

                // Assert
                Assert.Equal(Result.Success, result.Result);
                var labels = await _repository.GetLabelsBySnapshotIdAsync(snapshot.Id, testUser.Id);
                labels.Should().BeEmpty();
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task RemoveLabelFromSnapshotAsync_WhenLabelDoesNotExistInDatabase_ReturnsNotFound()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var labelId = Ulid.NewUlid();

                var snapshot = CreateSnapshotWithSimpleFolder();
                await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);

                // Act
                var result = await _repository!.RemoveLabelFromSnapshotAsync(labelId, snapshot.Id, testUser.Id);

                // Assert
                Assert.Equal(Result.NotFound, result.Result);
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task RemoveLabelFromSnapshotAsync_WhenSnapshotDoesNotExistInDatabase_ReturnsNotFound()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var label = new LabelEntity { Name = "TestLabel", ColorHex = "#FF0000", UserId = testUser.Id, User = testUser };
                await _repository!.AddLabelAsync(label, testUser.Id);

                var snapshotId = Ulid.NewUlid();

                // Act
                var result = await _repository!.RemoveLabelFromSnapshotAsync(label.Id, snapshotId, testUser.Id);

                // Assert
                Assert.Equal(Result.NotFound, result.Result);
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task RemoveLabelFromSnapshotAsync_WhenAssociationNotFound_ReturnsNotFound()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var label = new LabelEntity { Name = "TestLabel", ColorHex = "#FF0000", UserId = testUser.Id, User = testUser };
                await _repository!.AddLabelAsync(label, testUser.Id);

                var snapshot = CreateSnapshotWithSimpleFolder();
                await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);

                // Act
                var result = await _repository!.RemoveLabelFromSnapshotAsync(label.Id, snapshot.Id, testUser.Id);

                // Assert
                Assert.Equal(Result.NotFound, result.Result);
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task RemoveLabelFromSnapshotAsync_OnlyRemovesTargetAssociation()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var label1 = new LabelEntity { Name = "Label1", ColorHex = "#FF0000", UserId = testUser.Id, User = testUser };
                var label2 = new LabelEntity { Name = "Label2", ColorHex = "#00FF00", UserId = testUser.Id, User = testUser };

                await _repository!.AddLabelAsync(label1, testUser.Id);
                await _repository.AddLabelAsync(label2, testUser.Id);

                var snapshot = CreateSnapshotWithSimpleFolder();
                await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);
                await _repository.AddLabelToSnapshotAsync(label1.Id, snapshot.Id, testUser.Id);
                await _repository.AddLabelToSnapshotAsync(label2.Id, snapshot.Id, testUser.Id);

                // Act
                await _repository.RemoveLabelFromSnapshotAsync(label1.Id, snapshot.Id, testUser.Id);

                // Assert
                var labels = await _repository.GetLabelsBySnapshotIdAsync(snapshot.Id, testUser.Id);
                labels.Should().HaveCount(1);
                labels.Should().Contain(l => l.Id == label2.Id);
                labels.Should().NotContain(l => l.Id == label1.Id);
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task RemoveLabelFromSnapshotAsync_RemovesOnlyFromTargetSnapshot()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var label = new LabelEntity { Name = "Label", ColorHex = "#0000FF", UserId = testUser.Id, User = testUser };
                await _repository!.AddLabelAsync(label, testUser.Id);

                var snapshot1 = CreateSnapshotWithSimpleFolder();
                await _fileSystemRepository!.AddSnapshotAsync(snapshot1, testUser.Id);
                var snapshot2 = CreateSnapshotWithSimpleFolder();
                await _fileSystemRepository!.AddSnapshotAsync(snapshot2, testUser.Id);

                await _repository.AddLabelToSnapshotAsync(label.Id, snapshot1.Id, testUser.Id);
                await _repository.AddLabelToSnapshotAsync(label.Id, snapshot2.Id, testUser.Id);

                // Act
                await _repository.RemoveLabelFromSnapshotAsync(label.Id, snapshot1.Id, testUser.Id);

                // Assert
                var labels1 = await _repository.GetLabelsBySnapshotIdAsync(snapshot1.Id, testUser.Id);
                var labels2 = await _repository.GetLabelsBySnapshotIdAsync(snapshot2.Id, testUser.Id);

                labels1.Should().BeEmpty();
                labels2.Should().HaveCount(1);
                labels2.Should().Contain(l => l.Id == label.Id);
            }
            finally
            {
                CleanupDatabase();
            }
        }

        #endregion

        private SnapshotEntity CreateSnapshotWithSimpleFolder()
        {
            var snapshot = new SnapshotEntity { Description = "Test Snapshot 93853", UserId = testUser.Id, User = testUser };

            var pc = new PcEntity { Name = "TestPC", DeviceId = Guid.NewGuid().ToString(), UserId = testUser.Id, User = testUser };
            var storageDrive = new StorageDriveEntity
            {
                Name = "Test Drive",
                DeviceId = Guid.NewGuid().ToString(),
                SerialNumber = Guid.NewGuid().ToString(),
                TotalSize = 1000000,
                Description = "Test Storage Drive",
                MediaType = "SSD",
                InterfaceType = "SATA",
                UserId = testUser.Id,
                User = testUser
            };
            var volume = new VolumeEntity
            {
                DriveLetter = "C:",
                VolumeName = "TestVolume",
                VolumeSerialNumber = Guid.NewGuid().ToString(),
                VolumeSize = 500000,
                Description = "Test Volume",
                UserId = testUser.Id,
                User = testUser
            };
            var volumeInfo = new VolumeInfoEntity
            {
                FreeSpace = 250000,
                DriveStatus = "OK",
                UserId = testUser.Id,
                User = testUser
            };
            var rootFolder = new FsFolderEntity
            {
                Name = "Root",
                Size = 0,
                Sha256Hash = "abc123",
                UserId = testUser.Id,
                User = testUser
            };

            pc.Snapshots.Add(snapshot);
            pc.StorageDrives.Add(storageDrive);
            storageDrive.Pcs.Add(pc);
            storageDrive.Volumes.Add(volume);
            volume.StorageDrive = storageDrive;
            volume.StorageDriveId = storageDrive.Id;
            volume.VolumeInfos.Add(volumeInfo);
            volumeInfo.Volume = volume;
            volumeInfo.VolumeId = volume.Id;
            volumeInfo.Snapshot = snapshot;
            volumeInfo.SnapshotId = snapshot.Id;
            snapshot.VolumeInfo = volumeInfo;
            snapshot.RootFolder = rootFolder;

            return snapshot;
        }
    }
}
