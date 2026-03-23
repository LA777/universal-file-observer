using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Moq;
using System.Diagnostics;
using Ufo.Abstractions;
using Ufo.Abstractions.Database;
using Ufo.Abstractions.Database.Entities;
using Ufo.Database.Contexts;
using Ufo.Database.Repositories;

namespace Ufo.IntegrationTests
{
    public class FileSystemRepositoryIntegrationTests : IAsyncLifetime
    {
        private UserEntity testUser = new() { Id = Ulid.NewUlid(), Name = "TestUser" };
        private Mock<ILogger<FileSystemRepository>> _loggerMock;
        private Mock<IDbConnectionFactory> _dbConnectionFactoryMock;
        private SqliteConnection _sqLiteConnection;
        private FileSystemRepository _fileSystemRepository;

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

            _loggerMock = new Mock<ILogger<FileSystemRepository>>();

            await DapperDataContext.InitiateDatabaseAsync(_sqLiteConnection);
            _fileSystemRepository = new FileSystemRepository(_dbConnectionFactoryMock.Object, _loggerMock.Object);

            // Insert test user
            await _sqLiteConnection.ExecuteAsync(
                "INSERT INTO Users (Id, Name, PasswordHash) VALUES (@Id, @Name, @PasswordHash)",
                new { testUser.Id, testUser.Name, PasswordHash = "hash" });
        }

        public async Task DisposeAsync()
        {
            if (_sqLiteConnection is not null)
            {
                await _sqLiteConnection.DisposeAsync();
            }
        }

        #endregion

        #region AddSnapshotAsync Tests

        [Fact]
        public async Task AddSnapshotAsync_WithSimpleFolder_CreatesSnapshotSuccessfully()
        {
            // Arrange
            var snapshot = CreateSnapshotWithSimpleFolder(testUser.Id);

            // Act
            var result = await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);

            // Assert
            Assert.Equal(1, result);
            var retrievedSnapshot = await _fileSystemRepository.GetSnapshotByIdAsync(snapshot.Id, testUser.Id);
            Assert.NotNull(retrievedSnapshot);
            Assert.Equal(snapshot.Id, retrievedSnapshot.Id);
            Assert.Equal(snapshot.Timestamp, retrievedSnapshot.Timestamp);
            retrievedSnapshot.Should().BeEquivalentTo(snapshot, options =>
                options.ExcludingCircularReferences());
        }

        [Fact]
        public async Task AddSnapshotAsync_WithNestedFolders_CreatesHierarchyCorrectly()
        {
            // Arrange
            var snapshot = CreateSnapshotWithNestedFolders(testUser.Id);

            // Act
            await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);

            // Assert
            var retrievedSnapshot = await _fileSystemRepository.GetSnapshotByIdAsync(snapshot.Id, testUser.Id);
            Assert.NotNull(retrievedSnapshot);
            Assert.NotNull(retrievedSnapshot.RootFolder);
            Assert.NotEmpty(retrievedSnapshot.RootFolder.ChildFolders);
            Assert.Single(retrievedSnapshot.RootFolder.ChildFolders);
            var childFolder = retrievedSnapshot.RootFolder.ChildFolders.First();
            Assert.NotEmpty(childFolder.ChildFolders);
            retrievedSnapshot.Should().BeEquivalentTo(snapshot, options =>
                options.ExcludingCircularReferences());
        }

        [Fact]
        public async Task AddSnapshotAsync_WithFilesInFolder_CreatesFilesCorrectly()
        {
            // Arrange
            var snapshot = CreateSnapshotWithFiles(testUser.Id);

            // Act
            await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);

            // Assert
            var retrievedSnapshot = await _fileSystemRepository.GetSnapshotByIdAsync(snapshot.Id, testUser.Id);
            Assert.NotNull(retrievedSnapshot?.RootFolder);
            Assert.NotEmpty(retrievedSnapshot.RootFolder.Files);
            Assert.Equal(3, retrievedSnapshot.RootFolder.Files.Count);
            Assert.All(retrievedSnapshot.RootFolder.Files, file => Assert.False(string.IsNullOrEmpty(file.FileExtension)));
            retrievedSnapshot.Should().BeEquivalentTo(snapshot, options =>
                options.ExcludingCircularReferences());
        }

        [Fact]
        public async Task AddSnapshotAsync_WithComplexStructure_CreatesAllRelationshipsCorrectly()
        {
            // Arrange
            var snapshot = CreateComplexSnapshot(testUser.Id);

            // Act
            await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);

            // Assert
            var retrievedSnapshot = await _fileSystemRepository.GetSnapshotByIdAsync(snapshot.Id, testUser.Id);
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

        [Fact]
        public async Task AddSnapshotAsync_BindsPcToStorageDrive()
        {
            // Arrange
            var snapshot = CreateSnapshotWithSystemInfo(testUser.Id);

            // Act
            await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);

            // Assert
            var retrievedSnapshot = await _fileSystemRepository.GetSnapshotByIdAsync(snapshot.Id, testUser.Id);
            Assert.NotNull(retrievedSnapshot.VolumeInfo);
            Assert.NotNull(retrievedSnapshot.VolumeInfo.Volume);
            Assert.NotNull(retrievedSnapshot.VolumeInfo.Volume.StorageDrive);
            Assert.NotEmpty(retrievedSnapshot.VolumeInfo.Volume.StorageDrive.Pcs);
            var pc = retrievedSnapshot.VolumeInfo.Volume.StorageDrive.Pcs.First();
            Assert.False(string.IsNullOrEmpty(pc.Name));
            retrievedSnapshot.Should().BeEquivalentTo(snapshot, options =>
               options.ExcludingCircularReferences());
        }

        [Fact]
        public async Task AddSnapshotAsync_WithMultipleSnapshots_MaintainsDataIntegrity()
        {
            // Arrange
            var snapshot1 = CreateSnapshotWithSystemInfo(testUser.Id);
            var snapshot2 = CreateSnapshotWithSystemInfo(testUser.Id);

            // Act
            await _fileSystemRepository!.AddSnapshotAsync(snapshot1, testUser.Id);
            await _fileSystemRepository.AddSnapshotAsync(snapshot2, testUser.Id);

            // Assert
            var retrievedSnapshot1 = await _fileSystemRepository.GetSnapshotByIdAsync(snapshot1.Id, testUser.Id);
            var retrievedSnapshot2 = await _fileSystemRepository.GetSnapshotByIdAsync(snapshot2.Id, testUser.Id);
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

        [Fact]
        public async Task AddSnapshotAsync_CreatesVolumeInfo()
        {
            // Arrange
            var snapshot = CreateSnapshotWithSystemInfo(testUser.Id);

            // Act
            await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);

            // Assert
            var retrievedSnapshot = await _fileSystemRepository.GetSnapshotByIdAsync(snapshot.Id, testUser.Id);
            Assert.NotNull(retrievedSnapshot.VolumeInfo);
            Assert.NotEqual(Ulid.Empty, retrievedSnapshot.VolumeInfo.Id);
            Assert.NotEqual(Ulid.Empty, retrievedSnapshot.VolumeInfo.VolumeId);
            Assert.NotNull(retrievedSnapshot.VolumeInfo.Volume);
            retrievedSnapshot.Should().BeEquivalentTo(snapshot, options =>
              options.ExcludingCircularReferences());
        }

        [Fact]
        public async Task AddSnapshotAsync_WithDuplicatePc_ReusesPcFromDatabase()
        {
            // Arrange
            var snapshot1 = CreateSnapshotWithSystemInfo(testUser.Id);
            var originalPcId = snapshot1.VolumeInfo!.Volume!.StorageDrive!.Pcs[0].Id;

            await _fileSystemRepository!.AddSnapshotAsync(snapshot1, testUser.Id);

            var snapshot2 = CreateSnapshotWithSystemInfo(testUser.Id);
            snapshot2.VolumeInfo!.Volume!.StorageDrive!.Pcs[0].Name = snapshot1.VolumeInfo.Volume.StorageDrive.Pcs[0].Name;
            snapshot2.VolumeInfo.Volume.StorageDrive.Pcs[0].DeviceId = snapshot1.VolumeInfo.Volume.StorageDrive.Pcs[0].DeviceId;

            // Act
            await _fileSystemRepository.AddSnapshotAsync(snapshot2, testUser.Id);

            // Assert
            var retrievedSnapshot2 = await _fileSystemRepository.GetSnapshotByIdAsync(snapshot2.Id, testUser.Id);
            var pcFromSnapshot2 = retrievedSnapshot2.VolumeInfo!.Volume!.StorageDrive!.Pcs.First();
            Assert.Equal(originalPcId, pcFromSnapshot2.Id);
        }

        [Fact]
        public async Task AddSnapshotAsync_WithDuplicateVolume_ReusesVolumeFromDatabase()
        {
            // Arrange
            var snapshot1 = CreateSnapshotWithSystemInfo(testUser.Id);
            var originalVolumeId = snapshot1.VolumeInfo!.Volume!.Id;

            await _fileSystemRepository!.AddSnapshotAsync(snapshot1, testUser.Id);

            var snapshot2 = CreateSnapshotWithSystemInfo(testUser.Id);
            snapshot2.VolumeInfo!.Volume!.VolumeSerialNumber = snapshot1.VolumeInfo.Volume.VolumeSerialNumber;

            // Act
            await _fileSystemRepository.AddSnapshotAsync(snapshot2, testUser.Id);

            // Assert
            var retrievedSnapshot2 = await _fileSystemRepository.GetSnapshotByIdAsync(snapshot2.Id, testUser.Id);
            Assert.Equal(originalVolumeId, retrievedSnapshot2.VolumeInfo!.VolumeId);
        }

        [Fact]
        public async Task AddSnapshotAsync_WithLargeFolderStructure_CompletesSuccessfully()
        {
            // Arrange
            var snapshot = CreateLargeSnapshot(testUser.Id);
            var stopwatch = Stopwatch.StartNew();

            // Act
            await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);
            stopwatch.Stop();

            // Assert
            Assert.True(stopwatch.ElapsedMilliseconds < 10000, $"Operation took {stopwatch.ElapsedMilliseconds}ms");
            var retrievedSnapshot = await _fileSystemRepository.GetSnapshotByIdAsync(snapshot.Id, testUser.Id);
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

            retrievedSnapshot.Should().BeEquivalentTo(snapshot, options =>
              options.ExcludingCircularReferences());
        }

        [Fact]
        public async Task AddSnapshotAsync_WithDuplicateFiles_ReusesFiles()
        {
            // Arrange
            // Snapshot 1 and Snapshot 2 will share the same files.
            // Count of files in DB should be 1 for each file after adding both snapshots.
            var snapshot1 = CreateSnapshotWithSystemInfo(testUser.Id);
            var sharedFile = new FsFileEntity
            {
                Name = "sharedfile",
                FileExtension = ".txt",
                Size = 1024,
                Sha256Hash = "sharedhashvalue123",
                UserId = testUser.Id,
                User = testUser
            };
            snapshot1.RootFolder!.Files.Add(sharedFile);
            sharedFile.ParentFolders.Add(snapshot1.RootFolder);

            await _fileSystemRepository!.AddSnapshotAsync(snapshot1, testUser.Id);
            var originalFileId = sharedFile.Id;

            // Create snapshot 2 with the same file (same hash and properties)
            var snapshot2 = new SnapshotEntity
            {
                Timestamp = DateTimeOffset.Now,
                UserId = testUser.Id,
                User = testUser
            };
            var pc2 = new PcEntity
            {
                Name = "TestPc5",
                DeviceId = Guid.NewGuid().ToString(),
                UserId = testUser.Id,
                User = testUser
            };
            var storageDrive2 = new StorageDriveEntity
            {
                Name = "Drive5",
                DeviceId = Guid.NewGuid().ToString(),
                SerialNumber = Guid.NewGuid().ToString(),
                TotalSize = 1099511627776,
                Description = "Disk drive",
                MediaType = "Fixed hard disk media",
                InterfaceType = "SATA",
                UserId = testUser.Id,
                User = testUser
            };
            var volume2 = new VolumeEntity
            {
                DriveLetter = "H:",
                VolumeName = "HelperDrive",
                VolumeSerialNumber = Guid.NewGuid().ToString(),
                VolumeSize = 549755813888,
                Description = "Local Fixed Disk",
                UserId = testUser.Id,
                User = testUser
            };
            var volumeInfo2 = new VolumeInfoEntity
            {
                FreeSpace = 274877906944,
                DriveStatus = "OK",
                UserId = testUser.Id,
                User = testUser
            };
            var rootFolder2 = new FsFolderEntity
            {
                Name = "H:\\",
                Size = 0,
                Sha256Hash = "h_drive_hash",
                UserId = testUser.Id,
                User = testUser
            };

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
            await _fileSystemRepository.AddSnapshotAsync(snapshot2, testUser.Id);

            // Assert
            var retrievedSnapshot2 = await _fileSystemRepository.GetSnapshotByIdAsync(snapshot2.Id, testUser.Id);
            var fileFromSnapshot2 = retrievedSnapshot2.RootFolder!.Files.First();
            Assert.Equal(originalFileId, fileFromSnapshot2.Id);

            // Verify there's only one file with this hash in the database
            //await using var connection = new SqliteConnection(_connectionString);
            var fileCount = await _sqLiteConnection.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM Files WHERE Sha256Hash = @Hash",
                new { Hash = "sharedhashvalue123" });
            Assert.Equal(1, fileCount);
        }

        [Fact]
        public async Task AddSnapshotAsync_WithDuplicateFolder_ReusesFolder()
        {
            // Arrange
            // Snapshot 1 and Snapshot 2 will share the same folder.
            // Count of folders in DB should be 1 after adding both snapshots.
            var snapshot1 = CreateSnapshotWithSystemInfo(testUser.Id);
            var sharedFolder = new FsFolderEntity
            {
                Name = "SharedFolder",
                Size = 5000,
                Sha256Hash = "sharedfolderhash456",
                UserId = testUser.Id,
                User = testUser
            };
            snapshot1.RootFolder!.ChildFolders.Add(sharedFolder);
            sharedFolder.ParentFolders.Add(snapshot1.RootFolder);

            await _fileSystemRepository!.AddSnapshotAsync(snapshot1, testUser.Id);
            var originalFolderId = sharedFolder.Id;

            // Create snapshot 2 with the same folder (same hash and properties)
            var snapshot2 = new SnapshotEntity
            {
                Timestamp = DateTimeOffset.Now,
                UserId = testUser.Id,
                User = testUser
            };
            var pc2 = new PcEntity
            {
                Name = "TestPc6",
                DeviceId = Guid.NewGuid().ToString(),
                UserId = testUser.Id,
                User = testUser
            };
            var storageDrive2 = new StorageDriveEntity
            {
                Name = "Drive6",
                DeviceId = Guid.NewGuid().ToString(),
                SerialNumber = Guid.NewGuid().ToString(),
                TotalSize = 1099511627776,
                Description = "Disk drive",
                MediaType = "Fixed hard disk media",
                InterfaceType = "SATA",
                UserId = testUser.Id,
                User = testUser
            };
            var volume2 = new VolumeEntity
            {
                DriveLetter = "I:",
                VolumeName = "ImageDrive",
                VolumeSerialNumber = Guid.NewGuid().ToString(),
                VolumeSize = 549755813888,
                Description = "Local Fixed Disk",
                UserId = testUser.Id,
                User = testUser
            };
            var volumeInfo2 = new VolumeInfoEntity
            {
                FreeSpace = 274877906944,
                DriveStatus = "OK",
                UserId = testUser.Id,
                User = testUser
            };
            var rootFolder2 = new FsFolderEntity
            {
                Name = "I:\\",
                Size = 0,
                Sha256Hash = "i_drive_hash",
                UserId = testUser.Id,
                User = testUser
            };

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
            await _fileSystemRepository.AddSnapshotAsync(snapshot2, testUser.Id);

            // Assert
            var retrievedSnapshot2 = await _fileSystemRepository.GetSnapshotByIdAsync(snapshot2.Id, testUser.Id);
            var folderFromSnapshot2 = retrievedSnapshot2.RootFolder!.ChildFolders.First();
            Assert.Equal(originalFolderId, folderFromSnapshot2.Id);

            // Verify there's only one folder with this hash in the database
            //await using var connection = new SqliteConnection(_connectionString);
            var folderCount = await _sqLiteConnection.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM Folders WHERE Sha256Hash = @Hash",
                new { Hash = "sharedfolderhash456" });
            Assert.Equal(1, folderCount);
        }

        #endregion

        #region DeleteSnapshotByIdAsync Tests

        [Fact]
        public async Task DeleteSnapshotByIdAsync_WhenSnapshotExists_DeletesSnapshotAndRelatedEntities()
        {
            // Arrange
            var snapshot = CreateSnapshotWithFiles(testUser.Id);

            await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);

            // Act
            var result = await _fileSystemRepository.DeleteSnapshotByIdAsync(snapshot.Id, testUser.Id);

            // Assert
            result.Should().Be(DatabaseActionResult.Success);

            var allSnapshots = await _fileSystemRepository.GetAllSnapshotsAsync(testUser.Id);
            allSnapshots.Should().NotContain(s => s.Id == snapshot.Id);
        }

        [Fact]
        public async Task DeleteSnapshotByIdAsync_WhenSnapshotNotFound_ReturnsNotFound()
        {
            // Arrange
            var missingId = Ulid.NewUlid();

            // Act
            var result = await _fileSystemRepository!.DeleteSnapshotByIdAsync(missingId, testUser.Id);

            // Assert
            result.Should().Be(DatabaseActionResult.NotFound);
        }

        [Fact]
        public async Task DeleteSnapshotByIdAsync_DeletesFoldersIfTheyNotShared()
        {
            // Arrange
            // Create snapshot 1 with unique folder structure
            var snapshot1 = CreateSnapshotWithSystemInfo(testUser.Id);
            var sharedFolder = new FsFolderEntity
            {
                Name = "SharedFolder",
                Size = 100,
                Sha256Hash = "sharedhash",
                UserId = testUser.Id,
                User = testUser
            };
            var uniqueFolder1 = new FsFolderEntity
            {
                Name = "UniqueFolder1",
                Size = 200,
                Sha256Hash = "uniquehash1",
                UserId = testUser.Id,
                User = testUser
            };
            snapshot1.RootFolder!.ChildFolders.Add(sharedFolder);
            sharedFolder.ParentFolders.Add(snapshot1.RootFolder);
            snapshot1.RootFolder.ChildFolders.Add(uniqueFolder1);
            uniqueFolder1.ParentFolders.Add(snapshot1.RootFolder);

            // Create snapshot 2 with shared folder but different PC and StorageDrive
            var snapshot2 = new SnapshotEntity
            {
                Timestamp = DateTimeOffset.Now,
                UserId = testUser.Id,
                User = testUser
            };
            var pc2 = new PcEntity
            {
                Name = "TestPc2",
                DeviceId = Guid.NewGuid().ToString(),
                UserId = testUser.Id,
                User = testUser
            };
            var storageDrive2 = new StorageDriveEntity
            {
                Name = "Drive2",
                DeviceId = Guid.NewGuid().ToString(),
                SerialNumber = Guid.NewGuid().ToString(),
                TotalSize = 1099511627776,
                Description = "Disk drive",
                MediaType = "Fixed hard disk media",
                InterfaceType = "SATA",
                UserId = testUser.Id,
                User = testUser
            };
            var volume2 = new VolumeEntity
            {
                DriveLetter = "D:",
                VolumeName = "DataDrive",
                VolumeSerialNumber = Guid.NewGuid().ToString(),
                VolumeSize = 549755813888,
                Description = "Local Fixed Disk",
                UserId = testUser.Id,
                User = testUser
            };
            var volumeInfo2 = new VolumeInfoEntity
            {
                FreeSpace = 274877906944,
                DriveStatus = "OK",
                UserId = testUser.Id,
                User = testUser
            };
            var rootFolder2 = new FsFolderEntity
            {
                Name = "D:\\",
                Size = 0,
                Sha256Hash = "d_drive_hash",
                UserId = testUser.Id,
                User = testUser
            };

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

            await _fileSystemRepository!.AddSnapshotAsync(snapshot1, testUser.Id);
            await _fileSystemRepository.AddSnapshotAsync(snapshot2, testUser.Id);
            var sharedFolderId = sharedFolder.Id;
            var uniqueFolderId = uniqueFolder1.Id;

            var jsonSnapshot1BeforeDb = System.Text.Json.JsonSerializer.Serialize(snapshot1);
            var jsonSnapshot2BeforeDb = System.Text.Json.JsonSerializer.Serialize(snapshot2);
            var snapshot1FromDb = await _fileSystemRepository.GetSnapshotByIdAsync(snapshot1.Id, testUser.Id);
            var snapshot2FromDb = await _fileSystemRepository.GetSnapshotByIdAsync(snapshot2.Id, testUser.Id);
            var jsonSnapshot1FromDb = System.Text.Json.JsonSerializer.Serialize(snapshot1FromDb);
            var jsonSnapshot2FromDb = System.Text.Json.JsonSerializer.Serialize(snapshot2FromDb);


            // Act - Delete snapshot 1
            var result = await _fileSystemRepository.DeleteSnapshotByIdAsync(snapshot1.Id, testUser.Id);

            // Assert
            result.Should().Be(DatabaseActionResult.Success);

            // Shared folder should still exist (used by snapshot 2)
            var snapshot2After = await _fileSystemRepository.GetSnapshotByIdAsync(snapshot2.Id, testUser.Id);
            var allFolders = GetAllFolders(snapshot2After.RootFolder!);
            allFolders.Should().Contain(f => f.Id == sharedFolderId);

            // Unique folder should be deleted
            //await using var connection = new SqliteConnection(_connectionString);
            var uniqueFolderExists = await _sqLiteConnection.QueryFirstOrDefaultAsync<FsFolderEntity>(
                "SELECT * FROM Folders WHERE Id = @Id",
                new { Id = uniqueFolderId });
            uniqueFolderExists.Should().BeNull();
        }

        [Fact]
        public async Task DeleteSnapshotByIdAsync_DeletesFilesIfTheyNotShared()
        {
            // Arrange
            // Create snapshot 1 with shared and unique files
            var snapshot1 = CreateSnapshotWithSystemInfo(testUser.Id);
            var sharedFile = new FsFileEntity
            {
                Name = "shared",
                FileExtension = ".txt",
                Size = 100,
                Sha256Hash = "sharedhashfile",
                UserId = testUser.Id,
                User = testUser
            };
            var uniqueFile = new FsFileEntity
            {
                Name = "unique",
                FileExtension = ".txt",
                Size = 200,
                Sha256Hash = "uniquehashfile",
                UserId = testUser.Id,
                User = testUser
            };
            snapshot1.RootFolder!.Files.Add(sharedFile);
            sharedFile.ParentFolders.Add(snapshot1.RootFolder);
            snapshot1.RootFolder.Files.Add(uniqueFile);
            uniqueFile.ParentFolders.Add(snapshot1.RootFolder);

            // Create snapshot 2 with shared file but different PC and StorageDrive
            var snapshot2 = new SnapshotEntity
            {
                Timestamp = DateTimeOffset.Now,
                UserId = testUser.Id,
                User = testUser
            };
            var pc2 = new PcEntity
            {
                Name = "TestPc3",
                DeviceId = Guid.NewGuid().ToString(),
                UserId = testUser.Id,
                User = testUser
            };
            var storageDrive2 = new StorageDriveEntity
            {
                Name = "Drive3",
                DeviceId = Guid.NewGuid().ToString(),
                SerialNumber = Guid.NewGuid().ToString(),
                TotalSize = 1099511627776,
                Description = "Disk drive",
                MediaType = "Fixed hard disk media",
                InterfaceType = "SATA",
                UserId = testUser.Id,
                User = testUser
            };
            var volume2 = new VolumeEntity
            {
                DriveLetter = "E:",
                VolumeName = "ExtraDrive",
                VolumeSerialNumber = Guid.NewGuid().ToString(),
                VolumeSize = 549755813888,
                Description = "Local Fixed Disk",
                UserId = testUser.Id,
                User = testUser
            };
            var volumeInfo2 = new VolumeInfoEntity
            {
                FreeSpace = 274877906944,
                DriveStatus = "OK",
                UserId = testUser.Id,
                User = testUser
            };
            var rootFolder2 = new FsFolderEntity
            {
                Name = "E:\\",
                Size = 0,
                Sha256Hash = "e_drive_hash",
                UserId = testUser.Id,
                User = testUser
            };

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

            await _fileSystemRepository!.AddSnapshotAsync(snapshot1, testUser.Id);
            await _fileSystemRepository.AddSnapshotAsync(snapshot2, testUser.Id);
            var sharedFileId = sharedFile.Id;
            var uniqueFileId = uniqueFile.Id;

            // Act - Delete snapshot 1
            var result = await _fileSystemRepository.DeleteSnapshotByIdAsync(snapshot1.Id, testUser.Id);

            // Assert
            result.Should().Be(DatabaseActionResult.Success);

            // Shared file should still exist (used by snapshot 2)
            var snapshot2After = await _fileSystemRepository.GetSnapshotByIdAsync(snapshot2.Id, testUser.Id);
            snapshot2After.RootFolder!.Files.Should().Contain(f => f.Id == sharedFileId);

            // Unique file should be deleted
            //await using var connection = new SqliteConnection(_connectionString);
            var uniqueFileExists = await _sqLiteConnection.QueryFirstOrDefaultAsync<FsFileEntity>(
                "SELECT * FROM Files WHERE Id = @Id",
                new { Id = uniqueFileId });
            uniqueFileExists.Should().BeNull();
        }

        [Fact]
        public async Task DeleteSnapshotByIdAsync_DeletesPcIfItNotShared()
        {
            // Arrange
            // Create snapshot 1 with unique PC
            var snapshot1 = CreateSnapshotWithSystemInfo(testUser.Id);
            var pcId1 = snapshot1.VolumeInfo!.Volume!.StorageDrive!.Pcs[0].Id;

            // Create snapshot 2 with different PC
            var snapshot2 = new SnapshotEntity
            {
                Timestamp = DateTimeOffset.Now,
                UserId = testUser.Id,
                User = testUser
            };
            var pc2 = new PcEntity
            {
                Name = "UniqueTestPc",
                DeviceId = Guid.NewGuid().ToString(),
                UserId = testUser.Id,
                User = testUser
            };
            var storageDrive2 = new StorageDriveEntity
            {
                Name = "Drive4",
                DeviceId = Guid.NewGuid().ToString(),
                SerialNumber = Guid.NewGuid().ToString(),
                TotalSize = 1099511627776,
                Description = "Disk drive",
                MediaType = "Fixed hard disk media",
                InterfaceType = "SATA",
                UserId = testUser.Id,
                User = testUser
            };
            var volume2 = new VolumeEntity
            {
                DriveLetter = "F:",
                VolumeName = "FileDrive",
                VolumeSerialNumber = Guid.NewGuid().ToString(),
                VolumeSize = 549755813888,
                Description = "Local Fixed Disk",
                UserId = testUser.Id,
                User = testUser
            };
            var volumeInfo2 = new VolumeInfoEntity
            {
                FreeSpace = 274877906944,
                DriveStatus = "OK",
                UserId = testUser.Id,
                User = testUser
            };
            var rootFolder2 = new FsFolderEntity
            {
                Name = "F:\\",
                Size = 0,
                Sha256Hash = "f_drive_hash",
                UserId = testUser.Id,
                User = testUser
            };

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

            await _fileSystemRepository!.AddSnapshotAsync(snapshot1, testUser.Id);
            await _fileSystemRepository.AddSnapshotAsync(snapshot2, testUser.Id);
            var pc2Id = pc2.Id;

            // Act - Delete snapshot 1
            var result = await _fileSystemRepository.DeleteSnapshotByIdAsync(snapshot1.Id, testUser.Id);

            // Assert
            result.Should().Be(DatabaseActionResult.Success);

            // PC from snapshot 2 should still exist
            var snapshot2After = await _fileSystemRepository.GetSnapshotByIdAsync(snapshot2.Id, testUser.Id);
            snapshot2After.VolumeInfo!.Volume!.StorageDrive!.Pcs.Should().Contain(p => p.Id == pc2Id);

            // PC from snapshot 1 should be deleted
            //await using var connection = new SqliteConnection(_connectionString);
            var pc1Exists = await _sqLiteConnection.QueryFirstOrDefaultAsync<PcEntity>(
                "SELECT * FROM Pcs WHERE Id = @Id",
                new { Id = pcId1 });
            pc1Exists.Should().BeNull();
        }

        [Fact]
        public async Task DeleteSnapshotByIdAsync_DeletesStorageDriveAndVolumeIfItNotShared()
        {
            // Arrange
            // Create snapshot 1 with unique StorageDrive
            var snapshot1 = CreateSnapshotWithSystemInfo(testUser.Id);
            var storageDriveId1 = snapshot1.VolumeInfo!.Volume!.StorageDrive!.Id;
            var volumerId1 = snapshot1.VolumeInfo!.Volume!.Id;

            // Create snapshot 2 with different StorageDrive
            var snapshot2 = new SnapshotEntity
            {
                Description = "Test Snapshot 28171",
                UserId = testUser.Id,
                User = testUser
            };
            var pc2 = new PcEntity
            {
                Name = "TestPc4",
                DeviceId = Guid.NewGuid().ToString(),
                UserId = testUser.Id,
                User = testUser
            };
            var storageDrive2 = new StorageDriveEntity
            {
                Name = "UniqueDrive",
                DeviceId = Guid.NewGuid().ToString(),
                SerialNumber = Guid.NewGuid().ToString(),
                TotalSize = 1099511627776,
                Description = "Disk drive",
                MediaType = "Fixed hard disk media",
                InterfaceType = "SATA",
                UserId = testUser.Id,
                User = testUser
            };
            var volume2 = new VolumeEntity
            {
                DriveLetter = "G:",
                VolumeName = "GameDrive",
                VolumeSerialNumber = Guid.NewGuid().ToString(),
                VolumeSize = 549755813888,
                Description = "Local Fixed Disk",
                UserId = testUser.Id,
                User = testUser
            };
            var volumeInfo2 = new VolumeInfoEntity
            {
                FreeSpace = 274877906944,
                DriveStatus = "OK",
                UserId = testUser.Id,
                User = testUser
            };
            var rootFolder2 = new FsFolderEntity
            {
                Name = "G:\\",
                Size = 0,
                Sha256Hash = "g_drive_hash",
                UserId = testUser.Id,
                User = testUser
            };

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

            await _fileSystemRepository!.AddSnapshotAsync(snapshot1, testUser.Id);
            await _fileSystemRepository.AddSnapshotAsync(snapshot2, testUser.Id);
            var storageDrive2Id = storageDrive2.Id;

            // Act - Delete snapshot 1
            var result = await _fileSystemRepository.DeleteSnapshotByIdAsync(snapshot1.Id, testUser.Id);

            // Assert
            result.Should().Be(DatabaseActionResult.Success);

            // StorageDrive from snapshot 2 should still exist
            var snapshot2After = await _fileSystemRepository.GetSnapshotByIdAsync(snapshot2.Id, testUser.Id);
            snapshot2After.VolumeInfo!.Volume!.StorageDrive!.Id.Should().Be(storageDrive2Id);

            // StorageDrive from snapshot 1 should be deleted
            //await using var connection = new SqliteConnection(_connectionString);
            var storageDrive1Exists = await _sqLiteConnection.QueryFirstOrDefaultAsync<StorageDriveEntity>(
                "SELECT * FROM StorageDrives WHERE Id = @Id",
                new { Id = storageDriveId1 });
            storageDrive1Exists.Should().BeNull();

            // Volume from snapshot 1 should be deleted
            var volume1Exists = await _sqLiteConnection.QueryFirstOrDefaultAsync<VolumeEntity>(
                "SELECT * FROM Volumes WHERE Id = @Id",
                new { Id = volumerId1 });
            volume1Exists.Should().BeNull();
        }

        #endregion

        #region User Isolation Tests

        [Fact]
        public async Task AddSnapshotAsync_WithMultipleUsers_EachUserSeesOnlyTheirSnapshots()
        {
            // Arrange
            // Create a second user
            var secondUser = new UserEntity { Id = Ulid.NewUlid(), Name = "TestUser2" };

            // Insert second user
            await _sqLiteConnection.ExecuteAsync(
                "INSERT INTO Users (Id, Name, PasswordHash) VALUES (@Id, @Name, @PasswordHash)",
                new { secondUser.Id, secondUser.Name, PasswordHash = "hash2" });

            // Add snapshot for user 1
            var snapshot1 = CreateSnapshotWithSystemInfo(testUser.Id);
            await _fileSystemRepository!.AddSnapshotAsync(snapshot1, testUser.Id);

            // Add snapshot for user 2
            var snapshot2 = CreateSnapshotWithSystemInfo(secondUser.Id);
            await _fileSystemRepository.AddSnapshotAsync(snapshot2, secondUser.Id);

            // Act & Assert - User 1 retrieves their snapshot
            var user1Snapshot = await _fileSystemRepository.GetSnapshotByIdAsync(snapshot1.Id, testUser.Id);
            user1Snapshot.Should().NotBeNull();
            user1Snapshot.Id.Should().Be(snapshot1.Id);

            // User 1 cannot retrieve user 2's snapshot (because of UserId filtering in SQL)
            // This should return null or the method should not find it
            var allUser1Snapshots = await _fileSystemRepository.GetAllSnapshotsAsync(testUser.Id);
            allUser1Snapshots.Should().NotBeEmpty();
            allUser1Snapshots.Should().AllSatisfy(s => s.UserId.Should().Be(testUser.Id));

            // User 2 retrieves their snapshot
            var user2Snapshot = await _fileSystemRepository.GetSnapshotByIdAsync(snapshot2.Id, secondUser.Id);
            user2Snapshot.Should().NotBeNull();
            user2Snapshot.Id.Should().Be(snapshot2.Id);

            var allUser2Snapshots = await _fileSystemRepository.GetAllSnapshotsAsync(secondUser.Id);
            allUser2Snapshots.Should().NotBeEmpty();
            allUser2Snapshots.Should().AllSatisfy(s => s.UserId.Should().Be(secondUser.Id));

            // Verify snapshots are different
            allUser1Snapshots.Should().NotContain(s => s.Id == snapshot2.Id);
            allUser2Snapshots.Should().NotContain(s => s.Id == snapshot1.Id);
        }

        [Fact]
        public async Task DeleteSnapshotByIdAsync_WithUserIsolation_UserCanOnlyDeleteOwnSnapshots()
        {
            // Arrange
            var secondUser = new UserEntity { Id = Ulid.NewUlid(), Name = "TestUser2" };

            // Insert second user
            await _sqLiteConnection.ExecuteAsync(
                "INSERT INTO Users (Id, Name, PasswordHash) VALUES (@Id, @Name, @PasswordHash)",
                new { secondUser.Id, secondUser.Name, PasswordHash = "hash2" });

            // Add snapshots for both users
            var snapshot1 = CreateSnapshotWithSystemInfo(testUser.Id);
            await _fileSystemRepository!.AddSnapshotAsync(snapshot1, testUser.Id);

            var snapshot2 = CreateSnapshotWithSystemInfo(secondUser.Id);
            await _fileSystemRepository.AddSnapshotAsync(snapshot2, secondUser.Id);

            // Act - User 1 tries to delete User 2's snapshot
            var result = await _fileSystemRepository.DeleteSnapshotByIdAsync(snapshot2.Id, testUser.Id);

            // Assert - Should return NotFound because User 1 can only see their own snapshots
            result.Should().Be(DatabaseActionResult.NotFound);

            // User 2's snapshot should still exist
            var snapshot2After = await _fileSystemRepository.GetSnapshotByIdAsync(snapshot2.Id, secondUser.Id);
            snapshot2After.Should().NotBeNull();

            // Act - User 2 successfully deletes their own snapshot
            var result2 = await _fileSystemRepository.DeleteSnapshotByIdAsync(snapshot2.Id, secondUser.Id);

            // Assert
            result2.Should().Be(DatabaseActionResult.Success);

            // Snapshot should no longer exist for User 2
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await _fileSystemRepository.GetSnapshotByIdAsync(snapshot2.Id, secondUser.Id));
        }

        [Fact]
        public async Task AddSnapshotAsync_WithUserIsolation_SameFileNamesInDifferentUsers()
        {
            // Arrange
            var secondUser = new UserEntity { Id = Ulid.NewUlid(), Name = "TestUser2" };

            // Insert second user
            //await using var sqLiteConnection = new SqliteConnection(_connectionString);
            await _sqLiteConnection.ExecuteAsync(
                "INSERT INTO Users (Id, Name, PasswordHash) VALUES (@Id, @Name, @PasswordHash)",
                new { secondUser.Id, secondUser.Name, PasswordHash = "hash2" });

            // Create snapshots with same file names for both users
            var snapshot1 = CreateSnapshotWithFiles(testUser.Id);
            snapshot1.RootFolder!.Files[0].Name = "myfile.txt";
            snapshot1.RootFolder.Files[0].Sha256Hash = "hash_user1_file1";

            var snapshot2 = CreateSnapshotWithFiles(secondUser.Id);
            snapshot2.RootFolder!.Files[0].Name = "myfile.txt"; // Same name
            snapshot2.RootFolder.Files[0].Sha256Hash = "hash_user2_file1"; // Different hash

            await _fileSystemRepository!.AddSnapshotAsync(snapshot1, testUser.Id);
            await _fileSystemRepository.AddSnapshotAsync(snapshot2, secondUser.Id);

            // Act & Assert
            var user1Snapshot = await _fileSystemRepository.GetSnapshotByIdAsync(snapshot1.Id, testUser.Id);
            var user2Snapshot = await _fileSystemRepository.GetSnapshotByIdAsync(snapshot2.Id, secondUser.Id);

            user1Snapshot.RootFolder!.Files.Should().Contain(f => f.Name == "myfile.txt");
            user2Snapshot.RootFolder!.Files.Should().Contain(f => f.Name == "myfile.txt");

            // But the files should have different hashes and IDs
            var user1File = user1Snapshot.RootFolder.Files.First(f => f.Name == "myfile.txt");
            var user2File = user2Snapshot.RootFolder.Files.First(f => f.Name == "myfile.txt");

            user1File.Sha256Hash.Should().Be("hash_user1_file1");
            user2File.Sha256Hash.Should().Be("hash_user2_file1");
            user1File.Id.Should().NotBe(user2File.Id);
            user1File.UserId.Should().Be(testUser.Id);
            user2File.UserId.Should().Be(secondUser.Id);
        }

        [Fact]
        public async Task AddSnapshotAsync_WithUserIsolation_FolderIsolation()
        {
            // Arrange
            var secondUser = new UserEntity { Id = Ulid.NewUlid(), Name = "TestUser2" };

            // Insert second user
            //await using var sqLiteConnection = new SqliteConnection(_connectionString);
            await _sqLiteConnection.ExecuteAsync(
                "INSERT INTO Users (Id, Name, PasswordHash) VALUES (@Id, @Name, @PasswordHash)",
                new { secondUser.Id, secondUser.Name, PasswordHash = "hash2" });

            // Create snapshots with same folder names but different hashes
            var snapshot1 = CreateSnapshotWithNestedFolders(testUser.Id);
            var folder1InSnapshot1 = snapshot1.RootFolder!.ChildFolders.First();
            folder1InSnapshot1.Sha256Hash = "hash_user1_documents";

            var snapshot2 = CreateSnapshotWithNestedFolders(secondUser.Id);
            var folder1InSnapshot2 = snapshot2.RootFolder!.ChildFolders.First();
            folder1InSnapshot2.Sha256Hash = "hash_user2_documents";

            await _fileSystemRepository!.AddSnapshotAsync(snapshot1, testUser.Id);
            await _fileSystemRepository.AddSnapshotAsync(snapshot2, secondUser.Id);

            // Act & Assert
            var user1Snapshot = await _fileSystemRepository.GetSnapshotByIdAsync(snapshot1.Id, testUser.Id);
            var user2Snapshot = await _fileSystemRepository.GetSnapshotByIdAsync(snapshot2.Id, secondUser.Id);

            var user1Folders = user1Snapshot.RootFolder!.ChildFolders;
            var user2Folders = user2Snapshot.RootFolder!.ChildFolders;

            user1Folders.Should().NotBeEmpty();
            user2Folders.Should().NotBeEmpty();

            // Folders from different users should have different IDs
            var user1FolderIds = user1Folders.Select(f => f.Id).ToHashSet();
            var user2FolderIds = user2Folders.Select(f => f.Id).ToHashSet();

            user1FolderIds.Intersect(user2FolderIds).Should().BeEmpty("because different users should have separate folder instances");

            // Verify user isolation
            user1Folders.Should().AllSatisfy(f => f.UserId.Should().Be(testUser.Id));
            user2Folders.Should().AllSatisfy(f => f.UserId.Should().Be(secondUser.Id));
        }

        [Fact]
        public async Task GetLatestSnapshotWithAllEntitiesAsync_WithUserIsolation_ReturnOnlyUserSnapshot()
        {
            // Arrange
            var secondUser = new UserEntity { Id = Ulid.NewUlid(), Name = "TestUser2" };

            // Insert second user
            //await using var sqLiteConnection = new SqliteConnection(_connectionString);
            await _sqLiteConnection.ExecuteAsync(
                "INSERT INTO Users (Id, Name, PasswordHash) VALUES (@Id, @Name, @PasswordHash)",
                new { secondUser.Id, secondUser.Name, PasswordHash = "hash2" });

            // Add older snapshot for user 1
            var snapshot1Old = CreateSnapshotWithSystemInfo(testUser.Id);
            snapshot1Old.Timestamp = DateTimeOffset.Now.AddDays(-1);
            await _fileSystemRepository!.AddSnapshotAsync(snapshot1Old, testUser.Id);

            // Add newer snapshot for user 1
            var snapshot1New = CreateSnapshotWithSystemInfo(testUser.Id);
            snapshot1New.Timestamp = DateTimeOffset.Now;
            await _fileSystemRepository.AddSnapshotAsync(snapshot1New, testUser.Id);

            // Add latest snapshot for user 2 (even newer)
            var snapshot2Latest = CreateSnapshotWithSystemInfo(secondUser.Id);
            snapshot2Latest.Timestamp = DateTimeOffset.Now.AddSeconds(1);
            await _fileSystemRepository.AddSnapshotAsync(snapshot2Latest, secondUser.Id);

            // Act - User 1 gets their latest snapshot
            var user1Latest = await _fileSystemRepository.GetLatestSnapshotWithAllEntitiesAsync(testUser.Id);

            // Assert - Should get User 1's latest snapshot, not User 2's
            user1Latest.Should().NotBeNull();
            user1Latest.Id.Should().Be(snapshot1New.Id);
            user1Latest.UserId.Should().Be(testUser.Id);
            user1Latest.Id.Should().NotBe(snapshot2Latest.Id);

            // Act - User 2 gets their latest snapshot
            var user2Latest = await _fileSystemRepository.GetLatestSnapshotWithAllEntitiesAsync(secondUser.Id);

            // Assert
            user2Latest.Should().NotBeNull();
            user2Latest.Id.Should().Be(snapshot2Latest.Id);
            user2Latest.UserId.Should().Be(secondUser.Id);
        }

        #endregion

        #region Helper Methods

        // Helper methods
        private SnapshotEntity CreateSnapshotWithSimpleFolder(Ulid userId)
        {
            var snapshot = new SnapshotEntity
            {
                Description = "Test Snapshot 93853",
                UserId = userId,
                User = null!
            };
            var pc = new PcEntity
            {
                Name = "TestPC",
                DeviceId = Guid.NewGuid().ToString(),
                UserId = userId,
                User = null!
            };
            var storageDrive = new StorageDriveEntity
            {
                Name = "Test Drive",
                DeviceId = Guid.NewGuid().ToString(),
                SerialNumber = Guid.NewGuid().ToString(),
                TotalSize = 1000000,
                Description = "Test Storage Drive",
                MediaType = "SSD",
                InterfaceType = "SATA",
                UserId = userId,
                User = null!
            };
            var volume = new VolumeEntity
            {
                DriveLetter = "C:",
                VolumeName = "TestVolume",
                VolumeSerialNumber = Guid.NewGuid().ToString(),
                VolumeSize = 500000,
                Description = "Test Volume",
                UserId = userId,
                User = null!
            };
            var volumeInfo = new VolumeInfoEntity
            {
                FreeSpace = 250000,
                DriveStatus = "OK",
                UserId = userId,
                User = null!
            };
            var rootFolder = new FsFolderEntity
            {
                Name = "Root",
                Size = 0,
                Sha256Hash = "abc123",
                UserId = userId,
                User = null!
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

        private SnapshotEntity CreateSnapshotWithNestedFolders(Ulid userId)
        {
            var snapshot = CreateSnapshotWithSimpleFolder(userId);
            var rootFolder = snapshot.RootFolder;

            var childFolder1 = new FsFolderEntity
            {
                Name = "ChildFolder1",
                Size = 100,
                Sha256Hash = "child1hash",
                UserId = userId,
                User = null!
            };

            var grandchildFolder = new FsFolderEntity
            {
                Name = "GrandchildFolder",
                Size = 50,
                Sha256Hash = "grandchildhash",
                UserId = userId,
                User = null!
            };

            rootFolder!.ChildFolders.Add(childFolder1);
            childFolder1.ParentFolders.Add(rootFolder);

            childFolder1.ChildFolders.Add(grandchildFolder);
            grandchildFolder.ParentFolders.Add(childFolder1);

            return snapshot;
        }

        private SnapshotEntity CreateSnapshotWithFiles(Ulid userId)
        {
            var snapshot = CreateSnapshotWithSimpleFolder(userId);
            var rootFolder = snapshot.RootFolder;

            var file1 = new FsFileEntity
            {
                Name = "file1",
                FileExtension = ".txt",
                Size = 100,
                Sha256Hash = "filehash1",
                UserId = userId,
                User = null!
            };

            var file2 = new FsFileEntity
            {
                Name = "file2",
                FileExtension = ".pdf",
                Size = 200,
                Sha256Hash = "filehash2",
                UserId = userId,
                User = null!
            };

            var file3 = new FsFileEntity
            {
                Name = "file3",
                FileExtension = ".jpg",
                Size = 300,
                Sha256Hash = "filehash3",
                UserId = userId,
                User = null!
            };

            rootFolder!.Files.Add(file1);
            rootFolder.Files.Add(file2);
            rootFolder.Files.Add(file3);

            file1.ParentFolders.Add(rootFolder);
            file2.ParentFolders.Add(rootFolder);
            file3.ParentFolders.Add(rootFolder);

            return snapshot;
        }

        private SnapshotEntity CreateComplexSnapshot(Ulid userId)
        {
            var snapshot = CreateSnapshotWithFiles(userId);
            var rootFolder = snapshot.RootFolder;

            var folder1 = new FsFolderEntity
            {
                Name = "Documents",
                Size = 500,
                Sha256Hash = "dochash",
                UserId = userId,
                User = null!
            };

            var folder2 = new FsFolderEntity
            {
                Name = "SubDocuments",
                Size = 300,
                Sha256Hash = "subdochash",
                UserId = userId,
                User = null!
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
                Sha256Hash = "docfilehash",
                UserId = userId,
                User = null!
            };

            folder1.Files.Add(docFile);
            docFile.ParentFolders.Add(folder1);

            var subDocFile = new FsFileEntity
            {
                Name = "subdocument",
                FileExtension = ".doc",
                Size = 120,
                Sha256Hash = "subdocfilehash",
                UserId = userId,
                User = null!
            };

            folder2.Files.Add(subDocFile);
            subDocFile.ParentFolders.Add(folder2);

            return snapshot;
        }

        private SnapshotEntity CreateSnapshotWithSystemInfo(Ulid userId)
        {
            var snapshot = new SnapshotEntity
            {
                Description = "Test Snapshot 35464",
                UserId = userId,
                User = null!
            };
            var pc = new PcEntity
            {
                Name = "TestPc30290",
                DeviceId = Guid.NewGuid().ToString(),
                UserId = userId,
                User = null!
            };
            var storageDrive = new StorageDriveEntity
            {
                Name = "Samsung SSD 860 EVO",
                DeviceId = @"\\.\PHYSICALDRIVE0",
                SerialNumber = Guid.NewGuid().ToString(),
                TotalSize = 1099511627776,
                Description = "Disk drive",
                MediaType = "Fixed hard disk media",
                InterfaceType = "SATA",
                UserId = userId,
                User = null!
            };
            var volume = new VolumeEntity
            {
                DriveLetter = "C:",
                VolumeName = "SystemDrive",
                VolumeSerialNumber = Guid.NewGuid().ToString(),
                VolumeSize = 549755813888,
                Description = "Local Fixed Disk",
                UserId = userId,
                User = null!
            };
            var volumeInfo = new VolumeInfoEntity
            {
                FreeSpace = 274877906944,
                DriveStatus = "OK",
                UserId = userId,
                User = null!
            };
            var rootFolder = new FsFolderEntity
            {
                Name = "C:\\",
                Size = 0,
                Sha256Hash = "c_drive_hash",
                UserId = userId,
                User = null!
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

        private SnapshotEntity CreateLargeSnapshot(Ulid userId)
        {
            var snapshot = CreateSnapshotWithSystemInfo(userId);
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
                        Sha256Hash = $"hash_{folderCount}_{i}",
                        UserId = userId,
                        User = null!
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
                            Sha256Hash = $"filehash_{folderCount}_{i}_{j}",
                            UserId = userId,
                            User = null!
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
