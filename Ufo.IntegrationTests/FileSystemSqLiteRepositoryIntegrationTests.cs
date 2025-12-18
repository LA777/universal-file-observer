using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Diagnostics;
using Ufo.Abstractions;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Options;
using Ufo.Database.Contexts;
using Ufo.Database.Handlers;
using Ufo.Database.Repositories;
using FluentAssertions;

namespace Ufo.IntegrationTests
{
    public class FileSystemSqLiteRepositoryIntegrationTests : IAsyncDisposable
    {
        private readonly string _connectionString;
        private readonly Mock<ILogger<FileSystemSqLiteRepository>> _loggerMock;
        private readonly Mock<IOptionsMonitor<DatabaseOptions>> _optionsMonitorMock;
        private FileSystemSqLiteRepository? _repository;

        public FileSystemSqLiteRepositoryIntegrationTests()
        {
            var databaseFileName = $"test-{Guid.NewGuid()}.db";
            _connectionString = $"Data Source={databaseFileName};Foreign Keys=True";
            _loggerMock = new Mock<ILogger<FileSystemSqLiteRepository>>();
            _optionsMonitorMock = new Mock<IOptionsMonitor<DatabaseOptions>>();
            _optionsMonitorMock.Setup(o => o.CurrentValue)
                .Returns(new DatabaseOptions { ConnectionString = _connectionString });
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
            _repository = new FileSystemSqLiteRepository(_optionsMonitorMock.Object, _loggerMock.Object);
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

        #region AddSnapshotAsync Tests

        [Fact]
        public async Task AddSnapshotAsync_WithSimpleFolder_CreatesSnapshotSuccessfully()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var snapshot = CreateSnapshotWithSimpleFolder();

                // Act
                var result = await _repository!.AddSnapshotAsync(snapshot);

                // Assert
                Assert.Equal(1, result);
                var retrievedSnapshot = await _repository.GetSnapshotByIdAsync(snapshot.Id);
                Assert.NotNull(retrievedSnapshot);
                Assert.Equal(snapshot.Id, retrievedSnapshot.Id);
                Assert.Equal(snapshot.Timestamp, retrievedSnapshot.Timestamp);
                retrievedSnapshot.Should().BeEquivalentTo(snapshot, options =>
                    options.ExcludingCircularReferences());
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task AddSnapshotAsync_WithNestedFolders_CreatesHierarchyCorrectly()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var snapshot = CreateSnapshotWithNestedFolders();

                // Act
                await _repository!.AddSnapshotAsync(snapshot);

                // Assert
                var retrievedSnapshot = await _repository.GetSnapshotByIdAsync(snapshot.Id);
                Assert.NotNull(retrievedSnapshot);
                Assert.NotNull(retrievedSnapshot.RootFolder);
                Assert.NotEmpty(retrievedSnapshot.RootFolder.ChildFolders);
                Assert.Single(retrievedSnapshot.RootFolder.ChildFolders);
                var childFolder = retrievedSnapshot.RootFolder.ChildFolders.First();
                Assert.NotEmpty(childFolder.ChildFolders);
                retrievedSnapshot.Should().BeEquivalentTo(snapshot, options =>
                    options.ExcludingCircularReferences());
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task AddSnapshotAsync_WithFilesInFolder_CreatesFilesCorrectly()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var snapshot = CreateSnapshotWithFiles();

                // Act
                await _repository!.AddSnapshotAsync(snapshot);

                // Assert
                var retrievedSnapshot = await _repository.GetSnapshotByIdAsync(snapshot.Id);
                Assert.NotNull(retrievedSnapshot?.RootFolder);
                Assert.NotEmpty(retrievedSnapshot.RootFolder.Files);
                Assert.Equal(3, retrievedSnapshot.RootFolder.Files.Count);
                Assert.All(retrievedSnapshot.RootFolder.Files, file => Assert.False(string.IsNullOrEmpty(file.FileExtension)));
                retrievedSnapshot.Should().BeEquivalentTo(snapshot, options =>
                    options.ExcludingCircularReferences());
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task AddSnapshotAsync_WithComplexStructure_CreatesAllRelationshipsCorrectly()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var snapshot = CreateComplexSnapshot();

                // Act
                await _repository!.AddSnapshotAsync(snapshot);

                // Assert
                var retrievedSnapshot = await _repository.GetSnapshotByIdAsync(snapshot.Id);
                Assert.NotNull(retrievedSnapshot);
                Assert.NotNull(retrievedSnapshot.RootFolder);
                Assert.NotNull(retrievedSnapshot.VolumeInfo);
                Assert.NotNull(retrievedSnapshot.VolumeInfo.Volume);
                Assert.NotNull(retrievedSnapshot.VolumeInfo.Volume.StorageDrive);

                // Verify folder structure
                Assert.NotEmpty(retrievedSnapshot.RootFolder.ChildFolders);

                // Verify files exist
                var allFolders = GetAllFolders(retrievedSnapshot.RootFolder);
                var totalFiles = allFolders.Sum(f => f.Files.Count);
                Assert.True(totalFiles > 0);

                // Verify snapshot data integrity
                retrievedSnapshot.Should().BeEquivalentTo(snapshot, options =>
                    options.ExcludingCircularReferences());
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task AddSnapshotAsync_BindsPcToStorageDrive()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var snapshot = CreateSnapshotWithSystemInfo();

                // Act
                await _repository!.AddSnapshotAsync(snapshot);

                // Assert
                var retrievedSnapshot = await _repository.GetSnapshotByIdAsync(snapshot.Id);
                Assert.NotNull(retrievedSnapshot.VolumeInfo);
                Assert.NotNull(retrievedSnapshot.VolumeInfo.Volume);
                Assert.NotNull(retrievedSnapshot.VolumeInfo.Volume.StorageDrive);
                Assert.NotEmpty(retrievedSnapshot.VolumeInfo.Volume.StorageDrive.Pcs);
                var pc = retrievedSnapshot.VolumeInfo.Volume.StorageDrive.Pcs.First();
                Assert.False(string.IsNullOrEmpty(pc.Name));
                retrievedSnapshot.Should().BeEquivalentTo(snapshot, options =>
                   options.ExcludingCircularReferences());
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task AddSnapshotAsync_WithMultipleSnapshots_MaintainsDataIntegrity()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var snapshot1 = CreateSnapshotWithSystemInfo();
                var snapshot2 = CreateSnapshotWithSystemInfo();

                // Act
                await _repository!.AddSnapshotAsync(snapshot1);
                await _repository.AddSnapshotAsync(snapshot2);

                // Assert
                var retrievedSnapshot1 = await _repository.GetSnapshotByIdAsync(snapshot1.Id);
                var retrievedSnapshot2 = await _repository.GetSnapshotByIdAsync(snapshot2.Id);
                Assert.NotNull(retrievedSnapshot1);
                Assert.NotNull(retrievedSnapshot2);
                Assert.NotEqual(snapshot1.Id, snapshot2.Id);
                Assert.Equal(retrievedSnapshot1.Id, snapshot1.Id);
                Assert.Equal(retrievedSnapshot2.Id, snapshot2.Id);
                retrievedSnapshot1.Should().BeEquivalentTo(snapshot1, options =>
                   options.ExcludingCircularReferences());
                retrievedSnapshot2.Should().BeEquivalentTo(snapshot2, options =>
                   options.ExcludingCircularReferences());
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task AddSnapshotAsync_CreatesVolumeInfo()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var snapshot = CreateSnapshotWithSystemInfo();

                // Act
                await _repository!.AddSnapshotAsync(snapshot);

                // Assert
                var retrievedSnapshot = await _repository.GetSnapshotByIdAsync(snapshot.Id);
                Assert.NotNull(retrievedSnapshot.VolumeInfo);
                Assert.NotEqual(Ulid.Empty, retrievedSnapshot.VolumeInfo.Id);
                Assert.NotEqual(Ulid.Empty, retrievedSnapshot.VolumeInfo.VolumeId);
                Assert.NotNull(retrievedSnapshot.VolumeInfo.Volume);
                retrievedSnapshot.Should().BeEquivalentTo(snapshot, options =>
                  options.ExcludingCircularReferences());
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task AddSnapshotAsync_WithDuplicatePc_ReusesPcFromDatabase()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var snapshot1 = CreateSnapshotWithSystemInfo();
                var originalPcId = snapshot1.VolumeInfo!.Volume!.StorageDrive!.Pcs[0].Id;

                await _repository!.AddSnapshotAsync(snapshot1);

                var snapshot2 = CreateSnapshotWithSystemInfo();
                snapshot2.VolumeInfo!.Volume!.StorageDrive!.Pcs[0].Name = snapshot1.VolumeInfo.Volume.StorageDrive.Pcs[0].Name;
                snapshot2.VolumeInfo.Volume.StorageDrive.Pcs[0].DeviceId = snapshot1.VolumeInfo.Volume.StorageDrive.Pcs[0].DeviceId;

                // Act
                await _repository.AddSnapshotAsync(snapshot2);

                // Assert
                var retrievedSnapshot2 = await _repository.GetSnapshotByIdAsync(snapshot2.Id);
                var pcFromSnapshot2 = retrievedSnapshot2.VolumeInfo!.Volume!.StorageDrive!.Pcs.First();
                Assert.Equal(originalPcId, pcFromSnapshot2.Id);
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task AddSnapshotAsync_WithDuplicateVolume_ReusesVolumeFromDatabase()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var snapshot1 = CreateSnapshotWithSystemInfo();
                var originalVolumeId = snapshot1.VolumeInfo!.Volume!.Id;

                await _repository!.AddSnapshotAsync(snapshot1);

                var snapshot2 = CreateSnapshotWithSystemInfo();
                snapshot2.VolumeInfo!.Volume!.VolumeSerialNumber = snapshot1.VolumeInfo.Volume.VolumeSerialNumber;

                // Act
                await _repository.AddSnapshotAsync(snapshot2);

                // Assert
                var retrievedSnapshot2 = await _repository.GetSnapshotByIdAsync(snapshot2.Id);
                Assert.Equal(originalVolumeId, retrievedSnapshot2.VolumeInfo!.VolumeId);
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task AddSnapshotAsync_WithLargeFolderStructure_CompletesSuccessfully()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var snapshot = CreateLargeSnapshot();
                var stopwatch = Stopwatch.StartNew();

                // Act
                await _repository!.AddSnapshotAsync(snapshot);
                stopwatch.Stop();

                // Assert
                Assert.True(stopwatch.ElapsedMilliseconds < 10000, $"Operation took {stopwatch.ElapsedMilliseconds}ms");
                var retrievedSnapshot = await _repository.GetSnapshotByIdAsync(snapshot.Id);
                Assert.NotNull(retrievedSnapshot);
                var allFolders = GetAllFolders(retrievedSnapshot!.RootFolder!);
                Assert.True(allFolders.Count > 10, "Expected large folder structure");

                // Calculate total amount of folders and files in both snapshots and compare counts
                var originalFolderCount = GetAllFolders(snapshot.RootFolder!).Count;
                var originalFileCount = GetTotalFileCount(snapshot.RootFolder!);

                var retrievedFolderCount = GetAllFolders(retrievedSnapshot.RootFolder!).Count;
                var retrievedFileCount = GetTotalFileCount(retrievedSnapshot.RootFolder!);

                Assert.Equal(originalFolderCount, retrievedFolderCount);
                Assert.Equal(originalFileCount, retrievedFileCount);

                var jsonOriginal = System.Text.Json.JsonSerializer.Serialize(snapshot);
                var jsonRetrieved = System.Text.Json.JsonSerializer.Serialize(retrievedSnapshot);

                retrievedSnapshot.Should().BeEquivalentTo(snapshot, options =>
                  options.ExcludingCircularReferences());
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task AddSnapshotAsync_WithDuplicateFiles_ReusesFiles()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                // Snapshot 1 and Snapshot 2 will share the same files.
                // Count of files in DB should be 1 for each file after adding both snapshots.
                var snapshot1 = CreateSnapshotWithSystemInfo();
                var sharedFile = new FsFileEntity
                {
                    Name = "sharedfile",
                    FileExtension = ".txt",
                    Size = 1024,
                    Sha256Hash = "sharedhashvalue123"
                };
                snapshot1.RootFolder!.Files.Add(sharedFile);
                sharedFile.ParentFolders.Add(snapshot1.RootFolder);

                await _repository!.AddSnapshotAsync(snapshot1);
                var originalFileId = sharedFile.Id;

                // Create snapshot 2 with the same file (same hash and properties)
                var snapshot2 = new SnapshotEntity { Timestamp = DateTimeOffset.Now };
                var pc2 = new PcEntity
                {
                    Name = "TestPc5",
                    DeviceId = Guid.NewGuid().ToString()
                };
                var storageDrive2 = new StorageDriveEntity
                {
                    Name = "Drive5",
                    DeviceId = Guid.NewGuid().ToString(),
                    SerialNumber = Guid.NewGuid().ToString(),
                    TotalSize = 1099511627776,
                    Description = "Disk drive",
                    MediaType = "Fixed hard disk media",
                    InterfaceType = "SATA"
                };
                var volume2 = new VolumeEntity
                {
                    DriveLetter = "H:",
                    VolumeName = "HelperDrive",
                    VolumeSerialNumber = Guid.NewGuid().ToString(),
                    VolumeSize = 549755813888,
                    Description = "Local Fixed Disk"
                };
                var volumeInfo2 = new VolumeInfoEntity { FreeSpace = 274877906944, DriveStatus = "OK" };
                var rootFolder2 = new FsFolderEntity { Name = "H:\\", Size = 0, Sha256Hash = "h_drive_hash" };

                pc2.Snapshots.Add(snapshot2);
                pc2.StorageDrives.Add(storageDrive2);
                storageDrive2.Pcs.Add(pc2);
                storageDrive2.Volumes.Add(volume2);
                volume2.StorageDrive = storageDrive2;
                volume2.StorageDriveId = storageDrive2.Id;
                volume2.VolumeInfos.Add(volumeInfo2);
                volumeInfo2.Volume = volume2;
                volumeInfo2.VolumeId = volume2.Id;
                volumeInfo2.Snapshot = snapshot2;
                volumeInfo2.SnapshotId = snapshot2.Id;
                snapshot2.VolumeInfo = volumeInfo2;
                snapshot2.RootFolder = rootFolder2;

                // Add the same file to snapshot 2 (same hash)
                rootFolder2.Files.Add(sharedFile);
                sharedFile.ParentFolders.Add(rootFolder2);

                // Act
                await _repository.AddSnapshotAsync(snapshot2);

                // Assert
                var retrievedSnapshot2 = await _repository.GetSnapshotByIdAsync(snapshot2.Id);
                var fileFromSnapshot2 = retrievedSnapshot2.RootFolder!.Files.First();
                Assert.Equal(originalFileId, fileFromSnapshot2.Id);

                // Verify there's only one file with this hash in the database
                await using var connection = new SqliteConnection(_connectionString);
                var fileCount = await connection.QueryFirstOrDefaultAsync<int>(
                    "SELECT COUNT(*) FROM Files WHERE Sha256Hash = @Hash",
                    new { Hash = "sharedhashvalue123" });
                Assert.Equal(1, fileCount);
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task AddSnapshotAsync_WithDuplicateFolder_ReusesFolder()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                // Snapshot 1 and Snapshot 2 will share the same folder.
                // Count of folders in DB should be 1 after adding both snapshots.
                var snapshot1 = CreateSnapshotWithSystemInfo();
                var sharedFolder = new FsFolderEntity
                {
                    Name = "SharedFolder",
                    Size = 5000,
                    Sha256Hash = "sharedfolderhash456"
                };
                snapshot1.RootFolder!.ChildFolders.Add(sharedFolder);
                sharedFolder.ParentFolders.Add(snapshot1.RootFolder);

                await _repository!.AddSnapshotAsync(snapshot1);
                var originalFolderId = sharedFolder.Id;

                // Create snapshot 2 with the same folder (same hash and properties)
                var snapshot2 = new SnapshotEntity { Timestamp = DateTimeOffset.Now };
                var pc2 = new PcEntity
                {
                    Name = "TestPc6",
                    DeviceId = Guid.NewGuid().ToString()
                };
                var storageDrive2 = new StorageDriveEntity
                {
                    Name = "Drive6",
                    DeviceId = Guid.NewGuid().ToString(),
                    SerialNumber = Guid.NewGuid().ToString(),
                    TotalSize = 1099511627776,
                    Description = "Disk drive",
                    MediaType = "Fixed hard disk media",
                    InterfaceType = "SATA"
                };
                var volume2 = new VolumeEntity
                {
                    DriveLetter = "I:",
                    VolumeName = "ImageDrive",
                    VolumeSerialNumber = Guid.NewGuid().ToString(),
                    VolumeSize = 549755813888,
                    Description = "Local Fixed Disk"
                };
                var volumeInfo2 = new VolumeInfoEntity { FreeSpace = 274877906944, DriveStatus = "OK" };
                var rootFolder2 = new FsFolderEntity { Name = "I:\\", Size = 0, Sha256Hash = "i_drive_hash" };

                pc2.Snapshots.Add(snapshot2);
                pc2.StorageDrives.Add(storageDrive2);
                storageDrive2.Pcs.Add(pc2);
                storageDrive2.Volumes.Add(volume2);
                volume2.StorageDrive = storageDrive2;
                volume2.StorageDriveId = storageDrive2.Id;
                volume2.VolumeInfos.Add(volumeInfo2);
                volumeInfo2.Volume = volume2;
                volumeInfo2.VolumeId = volume2.Id;
                volumeInfo2.Snapshot = snapshot2;
                volumeInfo2.SnapshotId = snapshot2.Id;
                snapshot2.VolumeInfo = volumeInfo2;
                snapshot2.RootFolder = rootFolder2;

                // Add the same folder to snapshot 2 (same hash)
                rootFolder2.ChildFolders.Add(sharedFolder);
                sharedFolder.ParentFolders.Add(rootFolder2);

                // Act
                await _repository.AddSnapshotAsync(snapshot2);

                // Assert
                var retrievedSnapshot2 = await _repository.GetSnapshotByIdAsync(snapshot2.Id);
                var folderFromSnapshot2 = retrievedSnapshot2.RootFolder!.ChildFolders.First();
                Assert.Equal(originalFolderId, folderFromSnapshot2.Id);

                // Verify there's only one folder with this hash in the database
                await using var connection = new SqliteConnection(_connectionString);
                var folderCount = await connection.QueryFirstOrDefaultAsync<int>(
                    "SELECT COUNT(*) FROM Folders WHERE Sha256Hash = @Hash",
                    new { Hash = "sharedfolderhash456" });
                Assert.Equal(1, folderCount);
            }
            finally
            {
                CleanupDatabase();
            }
        }

        #endregion

        #region DeleteSnapshotByIdAsync Tests

        [Fact]
        public async Task DeleteSnapshotByIdAsync_WhenSnapshotExists_DeletesSnapshotAndRelatedEntities()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var snapshot = CreateSnapshotWithFiles();

                await _repository!.AddSnapshotAsync(snapshot);

                // Act
                var result = await _repository.DeleteSnapshotByIdAsync(snapshot.Id);

                // Assert
                result.Should().Be(DeleteResult.Success);

                var allSnapshots = await _repository.GetAllSnapshotsAsync();
                allSnapshots.Should().NotContain(s => s.Id == snapshot.Id);
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task DeleteSnapshotByIdAsync_WhenSnapshotNotFound_ReturnsNotFound()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                var missingId = Ulid.NewUlid();

                // Act
                var result = await _repository!.DeleteSnapshotByIdAsync(missingId);

                // Assert
                result.Should().Be(DeleteResult.NotFound);
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task DeleteSnapshotByIdAsync_DeletesFoldersIfTheyNotShared()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                // Create snapshot 1 with unique folder structure
                var snapshot1 = CreateSnapshotWithSystemInfo();
                var sharedFolder = new FsFolderEntity
                {
                    Name = "SharedFolder",
                    Size = 100,
                    Sha256Hash = "sharedhash"
                };
                var uniqueFolder1 = new FsFolderEntity
                {
                    Name = "UniqueFolder1",
                    Size = 200,
                    Sha256Hash = "uniquehash1"
                };
                snapshot1.RootFolder!.ChildFolders.Add(sharedFolder);
                sharedFolder.ParentFolders.Add(snapshot1.RootFolder);
                snapshot1.RootFolder.ChildFolders.Add(uniqueFolder1);
                uniqueFolder1.ParentFolders.Add(snapshot1.RootFolder);

                // Create snapshot 2 with shared folder but different PC and StorageDrive
                var snapshot2 = new SnapshotEntity { Timestamp = DateTimeOffset.Now };
                var pc2 = new PcEntity
                {
                    Name = "TestPc2",
                    DeviceId = Guid.NewGuid().ToString()
                };
                var storageDrive2 = new StorageDriveEntity
                {
                    Name = "Drive2",
                    DeviceId = Guid.NewGuid().ToString(),
                    SerialNumber = Guid.NewGuid().ToString(),
                    TotalSize = 1099511627776,
                    Description = "Disk drive",
                    MediaType = "Fixed hard disk media",
                    InterfaceType = "SATA"
                };
                var volume2 = new VolumeEntity
                {
                    DriveLetter = "D:",
                    VolumeName = "DataDrive",
                    VolumeSerialNumber = Guid.NewGuid().ToString(),
                    VolumeSize = 549755813888,
                    Description = "Local Fixed Disk"
                };
                var volumeInfo2 = new VolumeInfoEntity { FreeSpace = 274877906944, DriveStatus = "OK" };
                var rootFolder2 = new FsFolderEntity { Name = "D:\\", Size = 0, Sha256Hash = "d_drive_hash" };

                pc2.Snapshots.Add(snapshot2);
                pc2.StorageDrives.Add(storageDrive2);
                storageDrive2.Pcs.Add(pc2);
                storageDrive2.Volumes.Add(volume2);
                volume2.StorageDrive = storageDrive2;
                volume2.StorageDriveId = storageDrive2.Id;
                volume2.VolumeInfos.Add(volumeInfo2);
                volumeInfo2.Volume = volume2;
                volumeInfo2.VolumeId = volume2.Id;
                volumeInfo2.Snapshot = snapshot2;
                volumeInfo2.SnapshotId = snapshot2.Id;
                snapshot2.VolumeInfo = volumeInfo2;
                snapshot2.RootFolder = rootFolder2;

                // Add shared folder to snapshot2
                rootFolder2.ChildFolders.Add(sharedFolder);
                sharedFolder.ParentFolders.Add(rootFolder2);

                await _repository!.AddSnapshotAsync(snapshot1);
                await _repository.AddSnapshotAsync(snapshot2);
                var sharedFolderId = sharedFolder.Id;
                var uniqueFolderId = uniqueFolder1.Id;

                var jsonSnapshot1BeforeDb = System.Text.Json.JsonSerializer.Serialize(snapshot1);
                var jsonSnapshot2BeforeDb = System.Text.Json.JsonSerializer.Serialize(snapshot2);
                var snapshot1FromDb = await _repository.GetSnapshotByIdAsync(snapshot1.Id);
                var snapshot2FromDb = await _repository.GetSnapshotByIdAsync(snapshot2.Id);
                var jsonSnapshot1FromDb = System.Text.Json.JsonSerializer.Serialize(snapshot1FromDb);
                var jsonSnapshot2FromDb = System.Text.Json.JsonSerializer.Serialize(snapshot2FromDb);


                // Act - Delete snapshot 1
                var result = await _repository.DeleteSnapshotByIdAsync(snapshot1.Id);

                // Assert
                result.Should().Be(DeleteResult.Success);

                // Shared folder should still exist (used by snapshot 2)
                var snapshot2After = await _repository.GetSnapshotByIdAsync(snapshot2.Id);
                var allFolders = GetAllFolders(snapshot2After.RootFolder!);
                allFolders.Should().Contain(f => f.Id == sharedFolderId);

                // Unique folder should be deleted
                await using var connection = new SqliteConnection(_connectionString);
                var uniqueFolderExists = await connection.QueryFirstOrDefaultAsync<FsFolderEntity>(
                    "SELECT * FROM Folders WHERE Id = @Id",
                    new { Id = uniqueFolderId });
                uniqueFolderExists.Should().BeNull();
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task DeleteSnapshotByIdAsync_DeletesFilesIfTheyNotShared()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                // Create snapshot 1 with shared and unique files
                var snapshot1 = CreateSnapshotWithSystemInfo();
                var sharedFile = new FsFileEntity
                {
                    Name = "shared",
                    FileExtension = ".txt",
                    Size = 100,
                    Sha256Hash = "sharedhashfile"
                };
                var uniqueFile = new FsFileEntity
                {
                    Name = "unique",
                    FileExtension = ".txt",
                    Size = 200,
                    Sha256Hash = "uniquehashfile"
                };
                snapshot1.RootFolder!.Files.Add(sharedFile);
                sharedFile.ParentFolders.Add(snapshot1.RootFolder);
                snapshot1.RootFolder.Files.Add(uniqueFile);
                uniqueFile.ParentFolders.Add(snapshot1.RootFolder);

                // Create snapshot 2 with shared file but different PC and StorageDrive
                var snapshot2 = new SnapshotEntity { Timestamp = DateTimeOffset.Now };
                var pc2 = new PcEntity
                {
                    Name = "TestPc3",
                    DeviceId = Guid.NewGuid().ToString()
                };
                var storageDrive2 = new StorageDriveEntity
                {
                    Name = "Drive3",
                    DeviceId = Guid.NewGuid().ToString(),
                    SerialNumber = Guid.NewGuid().ToString(),
                    TotalSize = 1099511627776,
                    Description = "Disk drive",
                    MediaType = "Fixed hard disk media",
                    InterfaceType = "SATA"
                };
                var volume2 = new VolumeEntity
                {
                    DriveLetter = "E:",
                    VolumeName = "ExtraDrive",
                    VolumeSerialNumber = Guid.NewGuid().ToString(),
                    VolumeSize = 549755813888,
                    Description = "Local Fixed Disk"
                };
                var volumeInfo2 = new VolumeInfoEntity { FreeSpace = 274877906944, DriveStatus = "OK" };
                var rootFolder2 = new FsFolderEntity { Name = "E:\\", Size = 0, Sha256Hash = "e_drive_hash" };

                pc2.Snapshots.Add(snapshot2);
                pc2.StorageDrives.Add(storageDrive2);
                storageDrive2.Pcs.Add(pc2);
                storageDrive2.Volumes.Add(volume2);
                volume2.StorageDrive = storageDrive2;
                volume2.StorageDriveId = storageDrive2.Id;
                volume2.VolumeInfos.Add(volumeInfo2);
                volumeInfo2.Volume = volume2;
                volumeInfo2.VolumeId = volume2.Id;
                volumeInfo2.Snapshot = snapshot2;
                volumeInfo2.SnapshotId = snapshot2.Id;
                snapshot2.VolumeInfo = volumeInfo2;
                snapshot2.RootFolder = rootFolder2;

                // Add shared file to snapshot 2
                rootFolder2.Files.Add(sharedFile);
                sharedFile.ParentFolders.Add(rootFolder2);

                await _repository!.AddSnapshotAsync(snapshot1);
                await _repository.AddSnapshotAsync(snapshot2);
                var sharedFileId = sharedFile.Id;
                var uniqueFileId = uniqueFile.Id;

                // Act - Delete snapshot 1
                var result = await _repository.DeleteSnapshotByIdAsync(snapshot1.Id);

                // Assert
                result.Should().Be(DeleteResult.Success);

                // Shared file should still exist (used by snapshot 2)
                var snapshot2After = await _repository.GetSnapshotByIdAsync(snapshot2.Id);
                snapshot2After.RootFolder!.Files.Should().Contain(f => f.Id == sharedFileId);

                // Unique file should be deleted
                await using var connection = new SqliteConnection(_connectionString);
                var uniqueFileExists = await connection.QueryFirstOrDefaultAsync<FsFileEntity>(
                    "SELECT * FROM Files WHERE Id = @Id",
                    new { Id = uniqueFileId });
                uniqueFileExists.Should().BeNull();
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task DeleteSnapshotByIdAsync_DeletesPcIfItNotShared()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                // Create snapshot 1 with unique PC
                var snapshot1 = CreateSnapshotWithSystemInfo();
                var pcId1 = snapshot1.VolumeInfo!.Volume!.StorageDrive!.Pcs[0].Id;

                // Create snapshot 2 with different PC
                var snapshot2 = new SnapshotEntity { Timestamp = DateTimeOffset.Now };
                var pc2 = new PcEntity
                {
                    Name = "UniqueTestPc",
                    DeviceId = Guid.NewGuid().ToString()
                };
                var storageDrive2 = new StorageDriveEntity
                {
                    Name = "Drive4",
                    DeviceId = Guid.NewGuid().ToString(),
                    SerialNumber = Guid.NewGuid().ToString(),
                    TotalSize = 1099511627776,
                    Description = "Disk drive",
                    MediaType = "Fixed hard disk media",
                    InterfaceType = "SATA"
                };
                var volume2 = new VolumeEntity
                {
                    DriveLetter = "F:",
                    VolumeName = "FileDrive",
                    VolumeSerialNumber = Guid.NewGuid().ToString(),
                    VolumeSize = 549755813888,
                    Description = "Local Fixed Disk"
                };
                var volumeInfo2 = new VolumeInfoEntity { FreeSpace = 274877906944, DriveStatus = "OK" };
                var rootFolder2 = new FsFolderEntity { Name = "F:\\", Size = 0, Sha256Hash = "f_drive_hash" };

                pc2.Snapshots.Add(snapshot2);
                pc2.StorageDrives.Add(storageDrive2);
                storageDrive2.Pcs.Add(pc2);
                storageDrive2.Volumes.Add(volume2);
                volume2.StorageDrive = storageDrive2;
                volume2.StorageDriveId = storageDrive2.Id;
                volume2.VolumeInfos.Add(volumeInfo2);
                volumeInfo2.Volume = volume2;
                volumeInfo2.VolumeId = volume2.Id;
                volumeInfo2.Snapshot = snapshot2;
                volumeInfo2.SnapshotId = snapshot2.Id;
                snapshot2.VolumeInfo = volumeInfo2;
                snapshot2.RootFolder = rootFolder2;

                await _repository!.AddSnapshotAsync(snapshot1);
                await _repository.AddSnapshotAsync(snapshot2);
                var pc2Id = pc2.Id;

                // Act - Delete snapshot 1
                var result = await _repository.DeleteSnapshotByIdAsync(snapshot1.Id);

                // Assert
                result.Should().Be(DeleteResult.Success);

                // PC from snapshot 2 should still exist
                var snapshot2After = await _repository.GetSnapshotByIdAsync(snapshot2.Id);
                snapshot2After.VolumeInfo!.Volume!.StorageDrive!.Pcs.Should().Contain(p => p.Id == pc2Id);

                // PC from snapshot 1 should be deleted
                await using var connection = new SqliteConnection(_connectionString);
                var pc1Exists = await connection.QueryFirstOrDefaultAsync<PcEntity>(
                    "SELECT * FROM Pcs WHERE Id = @Id",
                    new { Id = pcId1 });
                pc1Exists.Should().BeNull();
            }
            finally
            {
                CleanupDatabase();
            }
        }

        [Fact]
        public async Task DeleteSnapshotByIdAsync_DeletesStorageDriveAndVolumeIfItNotShared()
        {
            // Arrange
            await InitializeDatabaseAsync();
            try
            {
                // Create snapshot 1 with unique StorageDrive
                var snapshot1 = CreateSnapshotWithSystemInfo();
                var storageDriveId1 = snapshot1.VolumeInfo!.Volume!.StorageDrive!.Id;
                var volumerId1 = snapshot1.VolumeInfo!.Volume!.Id;

                // Create snapshot 2 with different StorageDrive
                var snapshot2 = new SnapshotEntity { Description = "Test Snapshot 28171" };
                var pc2 = new PcEntity
                {
                    Name = "TestPc4",
                    DeviceId = Guid.NewGuid().ToString()
                };
                var storageDrive2 = new StorageDriveEntity
                {
                    Name = "UniqueDrive",
                    DeviceId = Guid.NewGuid().ToString(),
                    SerialNumber = Guid.NewGuid().ToString(),
                    TotalSize = 1099511627776,
                    Description = "Disk drive",
                    MediaType = "Fixed hard disk media",
                    InterfaceType = "SATA"
                };
                var volume2 = new VolumeEntity
                {
                    DriveLetter = "G:",
                    VolumeName = "GameDrive",
                    VolumeSerialNumber = Guid.NewGuid().ToString(),
                    VolumeSize = 549755813888,
                    Description = "Local Fixed Disk"
                };
                var volumeInfo2 = new VolumeInfoEntity { FreeSpace = 274877906944, DriveStatus = "OK" };
                var rootFolder2 = new FsFolderEntity { Name = "G:\\", Size = 0, Sha256Hash = "g_drive_hash" };

                pc2.Snapshots.Add(snapshot2);
                pc2.StorageDrives.Add(storageDrive2);
                storageDrive2.Pcs.Add(pc2);
                storageDrive2.Volumes.Add(volume2);
                volume2.StorageDrive = storageDrive2;
                volume2.StorageDriveId = storageDrive2.Id;
                volume2.VolumeInfos.Add(volumeInfo2);
                volumeInfo2.Volume = volume2;
                volumeInfo2.VolumeId = volume2.Id;
                volumeInfo2.Snapshot = snapshot2;
                volumeInfo2.SnapshotId = snapshot2.Id;
                snapshot2.VolumeInfo = volumeInfo2;
                snapshot2.RootFolder = rootFolder2;

                await _repository!.AddSnapshotAsync(snapshot1);
                await _repository.AddSnapshotAsync(snapshot2);
                var storageDrive2Id = storageDrive2.Id;  

                // Act - Delete snapshot 1
                var result = await _repository.DeleteSnapshotByIdAsync(snapshot1.Id);

                // Assert
                result.Should().Be(DeleteResult.Success);

                // StorageDrive from snapshot 2 should still exist
                var snapshot2After = await _repository.GetSnapshotByIdAsync(snapshot2.Id);
                snapshot2After.VolumeInfo!.Volume!.StorageDrive!.Id.Should().Be(storageDrive2Id);

                // StorageDrive from snapshot 1 should be deleted
                await using var connection = new SqliteConnection(_connectionString);
                var storageDrive1Exists = await connection.QueryFirstOrDefaultAsync<StorageDriveEntity>(
                    "SELECT * FROM StorageDrives WHERE Id = @Id",
                    new { Id = storageDriveId1 });
                storageDrive1Exists.Should().BeNull();

                // Volume from snapshot 1 should be deleted
                var volume1Exists = await connection.QueryFirstOrDefaultAsync<VolumeEntity>(
                    "SELECT * FROM Volumes WHERE Id = @Id",
                    new { Id = volumerId1 });
                volume1Exists.Should().BeNull();
            }
            finally
            {
                CleanupDatabase();
            }
        }        

        #endregion

        #region Helper Methods

        // Helper methods
        private SnapshotEntity CreateSnapshotWithSimpleFolder()
        {
            var snapshot = new SnapshotEntity { Description = "Test Snapshot 93853" };
            var pc = new PcEntity { Name = "TestPC", DeviceId = Guid.NewGuid().ToString() };
            var storageDrive = new StorageDriveEntity
            {
                Name = "Test Drive",
                DeviceId = Guid.NewGuid().ToString(),
                SerialNumber = Guid.NewGuid().ToString(),
                TotalSize = 1000000,
                Description = "Test Storage Drive",
                MediaType = "SSD",
                InterfaceType = "SATA"
            };
            var volume = new VolumeEntity
            {
                DriveLetter = "C:",
                VolumeName = "TestVolume",
                VolumeSerialNumber = Guid.NewGuid().ToString(),
                VolumeSize = 500000,
                Description = "Test Volume"
            };
            var volumeInfo = new VolumeInfoEntity
            {
                FreeSpace = 250000,
                DriveStatus = "OK"
            };
            var rootFolder = new FsFolderEntity
            {
                Name = "Root",
                Size = 0,
                Sha256Hash = "abc123"
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

        private SnapshotEntity CreateSnapshotWithNestedFolders()
        {
            var snapshot = CreateSnapshotWithSimpleFolder();
            var rootFolder = snapshot.RootFolder;

            var childFolder1 = new FsFolderEntity
            {
                Name = "ChildFolder1",
                Size = 100,
                Sha256Hash = "child1hash"
            };

            var grandchildFolder = new FsFolderEntity
            {
                Name = "GrandchildFolder",
                Size = 50,
                Sha256Hash = "grandchildhash"
            };

            rootFolder!.ChildFolders.Add(childFolder1);
            childFolder1.ParentFolders.Add(rootFolder);

            childFolder1.ChildFolders.Add(grandchildFolder);
            grandchildFolder.ParentFolders.Add(childFolder1);

            return snapshot;
        }

        private SnapshotEntity CreateSnapshotWithFiles()
        {
            var snapshot = CreateSnapshotWithSimpleFolder();
            var rootFolder = snapshot.RootFolder;

            var file1 = new FsFileEntity
            {
                Name = "file1",
                FileExtension = ".txt",
                Size = 100,
                Sha256Hash = "filehash1"
            };

            var file2 = new FsFileEntity
            {
                Name = "file2",
                FileExtension = ".pdf",
                Size = 200,
                Sha256Hash = "filehash2"
            };

            var file3 = new FsFileEntity
            {
                Name = "file3",
                FileExtension = ".jpg",
                Size = 300,
                Sha256Hash = "filehash3"
            };

            rootFolder!.Files.Add(file1);
            rootFolder.Files.Add(file2);
            rootFolder.Files.Add(file3);

            file1.ParentFolders.Add(rootFolder);
            file2.ParentFolders.Add(rootFolder);
            file3.ParentFolders.Add(rootFolder);

            return snapshot;
        }

        private SnapshotEntity CreateComplexSnapshot()
        {
            var snapshot = CreateSnapshotWithFiles();
            var rootFolder = snapshot.RootFolder;

            var folder1 = new FsFolderEntity
            {
                Name = "Documents",
                Size = 500,
                Sha256Hash = "dochash"
            };

            var folder2 = new FsFolderEntity
            {
                Name = "SubDocuments",
                Size = 300,
                Sha256Hash = "subdochash"
            };

            rootFolder!.ChildFolders.Add(folder1);
            folder1.ParentFolders.Add(rootFolder);

            folder1.ChildFolders.Add(folder2);
            folder2.ParentFolders.Add(folder1);

            var docFile = new FsFileEntity
            {
                Name = "document",
                FileExtension = ".docx",
                Size = 150,
                Sha256Hash = "docfilehash"
            };

            folder1.Files.Add(docFile);
            docFile.ParentFolders.Add(folder1);

            var subDocFile = new FsFileEntity
            {
                Name = "subdocument",
                FileExtension = ".doc",
                Size = 120,
                Sha256Hash = "subdocfilehash"
            };

            folder2.Files.Add(subDocFile);
            subDocFile.ParentFolders.Add(folder2);

            return snapshot;
        }

        private SnapshotEntity CreateSnapshotWithSystemInfo()
        {
            var snapshot = new SnapshotEntity { Description = "Test Snapshot 35464" };
            var pc = new PcEntity
            {
                Name = "TestPc30290",
                DeviceId = Guid.NewGuid().ToString()
            };
            var storageDrive = new StorageDriveEntity
            {
                Name = "Samsung SSD 860 EVO",
                DeviceId = @"\\.\PHYSICALDRIVE0",
                SerialNumber = Guid.NewGuid().ToString(),
                TotalSize = 1099511627776,
                Description = "Disk drive",
                MediaType = "Fixed hard disk media",
                InterfaceType = "SATA"
            };
            var volume = new VolumeEntity
            {
                DriveLetter = "C:",
                VolumeName = "SystemDrive",
                VolumeSerialNumber = Guid.NewGuid().ToString(),
                VolumeSize = 549755813888,
                Description = "Local Fixed Disk"
            };
            var volumeInfo = new VolumeInfoEntity
            {
                FreeSpace = 274877906944,
                DriveStatus = "OK"
            };
            var rootFolder = new FsFolderEntity
            {
                Name = "C:\\",
                Size = 0,
                Sha256Hash = "c_drive_hash"
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

        private SnapshotEntity CreateLargeSnapshot()
        {
            var snapshot = CreateSnapshotWithSystemInfo();
            var rootFolder = snapshot.RootFolder;

            var random = new Random(42);
            var folders = new Queue<FsFolderEntity>();
            folders.Enqueue(rootFolder!);

            int folderCount = 0;
            while (folders.Count > 0 && folderCount < 15)
            {
                var currentFolder = folders.Dequeue();
                for (int i = 0; i < 3; i++)
                {
                    var newFolder = new FsFolderEntity
                    {
                        Name = $"Folder_{folderCount}_{i}",
                        Size = random.Next(100, 1000),
                        Sha256Hash = $"hash_{folderCount}_{i}"
                    };

                    currentFolder.ChildFolders.Add(newFolder);
                    newFolder.ParentFolders.Add(currentFolder);

                    for (int j = 0; j < 5; j++)
                    {
                        var file = new FsFileEntity
                        {
                            Name = $"File_{folderCount}_{i}_{j}",
                            FileExtension = ".txt",
                            Size = random.Next(10, 500),
                            Sha256Hash = $"filehash_{folderCount}_{i}_{j}"
                        };

                        newFolder.Files.Add(file);
                        file.ParentFolders.Add(newFolder);
                    }

                    folders.Enqueue(newFolder);
                }

                folderCount++;
            }

            // Sort folders and files by name recursively
            SortFoldersAndFilesRecursively(rootFolder!);

            return snapshot;
        }

        private void SortFoldersAndFilesRecursively(FsFolderEntity folderEntity)
        {
            // Sort child folders by name
            folderEntity.ChildFolders = folderEntity.ChildFolders.OrderBy(f => f.Name).ToList();

            // Sort files by name
            folderEntity.Files = folderEntity.Files.OrderBy(f => f.Name).ToList();

            // Recursively sort child folders
            foreach (var childFolder in folderEntity.ChildFolders)
            {
                SortFoldersAndFilesRecursively(childFolder);
            }
        }

        private List<FsFolderEntity> GetAllFolders(FsFolderEntity rootFolder)
        {
            var allFolders = new List<FsFolderEntity> { rootFolder };
            var queue = new Queue<FsFolderEntity>();
            queue.Enqueue(rootFolder);

            while (queue.Count > 0)
            {
                var folder = queue.Dequeue();
                foreach (var childFolder in folder.ChildFolders)
                {
                    allFolders.Add(childFolder);
                    queue.Enqueue(childFolder);
                }
            }

            return allFolders;
        }

        private int GetTotalFileCount(FsFolderEntity folder)
        {
            int count = folder.Files.Count;
            foreach (var childFolder in folder.ChildFolders)
            {
                count += GetTotalFileCount(childFolder);
            }
            return count;
        }

        #endregion
    }
}
