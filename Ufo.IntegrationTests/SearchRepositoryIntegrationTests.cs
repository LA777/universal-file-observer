using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Moq;
using System.Diagnostics;
using Ufo.Abstractions.Database;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Requests;
using Ufo.Database.Contexts;
using Ufo.Database.Repositories;

namespace Ufo.IntegrationTests
{
    public class SearchRepositoryIntegrationTests : IAsyncLifetime
    {
        private UserEntity testUser = new() { Id = Ulid.NewUlid(), Name = "TestUser" };
        private Mock<ILogger<SearchRepository>> _loggerMock;
        private Mock<IDbConnectionFactory> _dbConnectionFactoryMock;
        private SqliteConnection _sqLiteConnection;
        private FileSystemRepository? _fileSystemRepository;
        private SearchRepository? _searchRepository;       

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

            _loggerMock = new Mock<ILogger<SearchRepository>>();
            var fileSystemSqLiteRepositoryLoggerMock = new Mock<ILogger<FileSystemRepository>>();
            _fileSystemRepository = new FileSystemRepository(_dbConnectionFactoryMock.Object, fileSystemSqLiteRepositoryLoggerMock.Object);

            await DapperDataContext.InitiateDatabaseAsync(_sqLiteConnection);
            _searchRepository = new SearchRepository(_dbConnectionFactoryMock.Object, _loggerMock.Object);

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

        #region SearchAsync - Files Only Tests

        [Fact]
        public async Task SearchAsync_WithFilesOnlyIncluded_ReturnsOnlyMatchingFiles()
        {
            // Arrange
            var snapshot = CreateSnapshotWithFiles();
            await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);

            var searchRequest = new SearchRequest { Query = "file1", IncludeFiles = true, IncludeFolders = false };

            // Act
            var result = await _searchRepository!.SearchAsync(searchRequest, testUser.Id);

            // Assert
            result.Files.Should().NotBeEmpty();
            result.Files.Should().Contain(f => f.Name.Contains("file1"));
            result.Folders.Should().BeEmpty();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task SearchAsync_WhenQueryIsEmptyOrNull_ReturnsNoResults(string? invalidQuery)
        {
            // Arrange           
            // Seed data to ensure we aren't just getting an empty result because the DB is empty
            var snapshot = CreateSnapshotWithFiles();
            await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);

            var searchRequest = new SearchRequest
            {
                Query = invalidQuery!,
                IncludeFiles = true,
                IncludeFolders = true
            };

            // Act
            var result = await _searchRepository!.SearchAsync(searchRequest, testUser.Id);

            // Assert
            result.Should().NotBeNull();
            result.Files.Should().BeEmpty("because an empty query should not match any files");
            result.Folders.Should().BeEmpty("because an empty query should not match any folders");
        }

        [Fact]
        public async Task SearchAsync_WithNonMatchingQuery_ReturnsEmptyResults()
        {
            // Arrange          
            var snapshot = CreateSnapshotWithFiles();
            await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);

            var searchRequest = new SearchRequest { Query = "nonexistent", IncludeFiles = true, IncludeFolders = false };

            // Act
            var result = await _searchRepository!.SearchAsync(searchRequest, testUser.Id);

            // Assert
            result.Files.Should().BeEmpty();
            result.Folders.Should().BeEmpty();
        }

        [Fact]
        public async Task SearchAsync_WithPartialFileNameMatch_ReturnsMatchingFiles()
        {
            // Arrange           
            var snapshot = CreateSnapshotWithFiles();
            await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);

            var searchRequest = new SearchRequest { Query = "fil", IncludeFiles = true, IncludeFolders = false };

            // Act
            var result = await _searchRepository!.SearchAsync(searchRequest, testUser.Id);

            // Assert
            result.Files.Should().NotBeEmpty();
            result.Files.Should().AllSatisfy(f => f.Name.Contains("fil"));
        }

        [Fact]
        public async Task SearchAsync_WithMultipleMatchingFiles_ReturnsAllMatches()
        {
            // Arrange           
            var snapshot = CreateSnapshotWithFiles();
            await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);

            var searchRequest = new SearchRequest { Query = "file", IncludeFiles = true, IncludeFolders = false };

            // Act
            var result = await _searchRepository!.SearchAsync(searchRequest, testUser.Id);

            // Assert
            result.Files.Should().NotBeEmpty();
            result.Files.Count.Should().BeGreaterThanOrEqualTo(3);
        }

        #endregion

        #region SearchAsync - Folders Only Tests

        [Fact]
        public async Task SearchAsync_WithFoldersOnlyIncluded_ReturnsOnlyMatchingFolders()
        {
            // Arrange           
            var snapshot = CreateSnapshotWithFolders();
            await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);

            var searchRequest = new SearchRequest { Query = "Documents", IncludeFiles = false, IncludeFolders = true };

            // Act
            var result = await _searchRepository!.SearchAsync(searchRequest, testUser.Id);

            // Assert
            result.Folders.Should().NotBeEmpty();
            result.Folders.Should().Contain(f => f.Name.Contains("Documents"));
            result.Files.Should().BeEmpty();
        }

        [Fact]
        public async Task SearchAsync_WithPartialFolderNameMatch_ReturnsMatchingFolders()
        {
            // Arrange            
            var snapshot = CreateSnapshotWithFolders();
            await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);

            var searchRequest = new SearchRequest { Query = "document", IncludeFiles = false, IncludeFolders = true };

            // Act
            var result = await _searchRepository!.SearchAsync(searchRequest, testUser.Id);

            // Assert
            result.Folders.Should().NotBeEmpty();
            result.Folders.Should().HaveCount(2);
            var names = result.Folders.Select(f => f.Name).ToList();
            names.Should().Contain("Documents");
            names.Should().Contain("SubDocuments");
            result.Folders.Should().AllSatisfy(f =>
            {
                f.Name.Should().Contain("Document");
            });
        }

        [Fact]
        public async Task SearchAsync_WithMultipleMatchingFolders_ReturnsAllMatches()
        {
            // Arrange            
            var snapshot = CreateLargeSnapshot();
            await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);

            var searchRequest = new SearchRequest { Query = "folder", IncludeFiles = false, IncludeFolders = true };

            // Act
            var result = await _searchRepository!.SearchAsync(searchRequest, testUser.Id);

            // Assert
            result.Folders.Should().HaveCount(50);
            result.Folders.Should().AllSatisfy(f =>
            {
                f.Name.Should().Contain("Folder");
            });
        }

        #endregion

        #region SearchAsync - Both Files and Folders Tests

        [Fact]
        public async Task SearchAsync_WithBothIncluded_ReturnsMatchingFilesAndFolders()
        {
            // Arrange           
            var snapshot = CreateSnapshotWithFilesAndFolders();
            await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);

            var searchRequest = new SearchRequest { Query = "Doc", IncludeFiles = true, IncludeFolders = true };

            // Act
            var result = await _searchRepository!.SearchAsync(searchRequest, testUser.Id);

            // Assert
            result.Should().NotBeNull();
            // Should return results for both files and folders
            (result.Files.Count + result.Folders.Count).Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task SearchAsync_WithBothIncludedAndNoMatches_ReturnsEmptyLists()
        {
            // Arrange           
            var snapshot = CreateSnapshotWithFilesAndFolders();
            await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);

            var searchRequest = new SearchRequest { Query = "xyz123", IncludeFiles = true, IncludeFolders = true };

            // Act
            var result = await _searchRepository!.SearchAsync(searchRequest, testUser.Id);

            // Assert
            result.Files.Should().BeEmpty();
            result.Folders.Should().BeEmpty();
        }

        [Fact]
        public async Task SearchAsync_WithBothIncludedAndPartialMatch_ReturnsAllMatches()
        {
            // Arrange           
            var snapshot = CreateSnapshotWithFilesAndFolders();
            await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);

            var searchRequest = new SearchRequest { Query = "document", IncludeFiles = true, IncludeFolders = true };

            // Act
            var result = await _searchRepository!.SearchAsync(searchRequest, testUser.Id);

            // Assert
            result.Should().NotBeNull();
            // Should find files and/or folders matching "document"
            (result.Files.Count + result.Folders.Count).Should().BeGreaterThan(0);
        }

        #endregion

        #region SearchAsync - Edge Cases

        [Fact]
        public async Task SearchAsync_WithCaseSensitiveQuery_ReturnsCaseInsensitiveMatches()
        {
            // Arrange            
            var snapshot = CreateSnapshotWithFiles();
            await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);

            var searchRequest = new SearchRequest { Query = "FILE", IncludeFiles = true, IncludeFolders = false };

            // Act
            var result = await _searchRepository!.SearchAsync(searchRequest, testUser.Id);

            // Assert - SQLite FTS is typically case-insensitive
            result.Files.Should().NotBeEmpty();
        }

        [Fact]
        public async Task SearchAsync_WithMultipleSnapshots_ReturnsResultsFromAllSnapshots()
        {
            // Arrange           
            var snapshot1 = CreateSnapshotWithFiles();
            var snapshot2 = CreateSnapshotWithFiles();

            await _fileSystemRepository!.AddSnapshotAsync(snapshot1, testUser.Id);
            await _fileSystemRepository.AddSnapshotAsync(snapshot2, testUser.Id);

            var searchRequest = new SearchRequest { Query = "file", IncludeFiles = true, IncludeFolders = false };

            // Act
            var result = await _searchRepository!.SearchAsync(searchRequest, testUser.Id);

            // Assert
            result.Files.Should().NotBeEmpty();
            // Each file should have snapshots associated
            result.Files.Should().AllSatisfy(f => f.Snapshots.Should().NotBeEmpty());
        }

        [Fact]
        public async Task SearchAsync_WithSpecialCharactersInQuery_HandlesCorrectly()
        {
            // Arrange           
            var snapshot = CreateSnapshotWithFiles();
            await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);

            // Test with special characters that might be in filenames
            var searchRequest = new SearchRequest { Query = ".", IncludeFiles = true, IncludeFolders = false };

            // Act & Assert - Should not throw
            var result = await _searchRepository!.SearchAsync(searchRequest, testUser.Id);
            result.Should().NotBeNull();
        }

        [Fact]
        public async Task SearchAsync_WithVeryLongQuery_ReturnsAppropriateResults()
        {
            // Arrange            
            var snapshot = CreateSnapshotWithFiles();
            await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);

            var searchRequest = new SearchRequest
            {
                Query = "verylongquerythatdoesnotexistinanyfilenames",
                IncludeFiles = true,
                IncludeFolders = false
            };

            // Act
            var result = await _searchRepository!.SearchAsync(searchRequest, testUser.Id);

            // Assert
            result.Files.Should().BeEmpty();
        }

        [Fact]
        public async Task SearchAsync_WithDuplicateFiles_ReturnsUniqueResults()
        {
            // Arrange           
            var snapshot1 = CreateSnapshotWithFiles();
            var snapshot2 = CreateSnapshotWithFiles();
            // Create same files in second snapshot

            await _fileSystemRepository!.AddSnapshotAsync(snapshot1, testUser.Id);
            await _fileSystemRepository.AddSnapshotAsync(snapshot2, testUser.Id);

            var searchRequest = new SearchRequest { Query = "file1", IncludeFiles = true, IncludeFolders = false };

            // Act
            var result = await _searchRepository!.SearchAsync(searchRequest, testUser.Id);

            // Assert
            // Should return the same file with multiple snapshots, not duplicated
            var file1Results = result.Files.Where(f => f.Name.Contains("file1")).ToList();
            file1Results.Should().HaveCount(1);
            file1Results.First().Snapshots.Should().HaveCount(2);
        }

        #endregion

        #region SearchAsync - Performance Tests

        [Fact]
        public async Task SearchAsync_WithLargeDataset_CompletesInReasonableTime()
        {
            // Arrange
            var snapshot = CreateLargeSnapshot();
            await _fileSystemRepository!.AddSnapshotAsync(snapshot, testUser.Id);

            var searchRequest = new SearchRequest { Query = "File", IncludeFiles = true, IncludeFolders = true };

            var stopwatch = Stopwatch.StartNew();

            // Act
            var result = await _searchRepository!.SearchAsync(searchRequest, testUser.Id);

            stopwatch.Stop();

            // Assert
            Assert.True(stopwatch.ElapsedMilliseconds < 5000, $"Search took {stopwatch.ElapsedMilliseconds}ms");
            result.Should().NotBeNull();
        }

        #endregion

        #region SearchAsync - User Isolation Tests

        [Fact]
        public async Task SearchAsync_WithMultipleUsers_ReturnsOnlyCurrentUserResults()
        {
            // Arrange
            // Create a second user
            var secondUser = new UserEntity { Id = Ulid.NewUlid(), Name = "TestUser2" };

            // Insert second user                
            await _sqLiteConnection.ExecuteAsync(
                "INSERT INTO Users (Id, Name, PasswordHash) VALUES (@Id, @Name, @PasswordHash)",
                new { secondUser.Id, secondUser.Name, PasswordHash = "hash2" });

            // Create snapshots with same file names for both users
            var snapshot1 = CreateSnapshotWithFilesForUser(testUser);
            var snapshot2 = CreateSnapshotWithFilesForUser(secondUser);

            await _fileSystemRepository!.AddSnapshotAsync(snapshot1, testUser.Id);
            await _fileSystemRepository.AddSnapshotAsync(snapshot2, secondUser.Id);

            var searchRequest = new SearchRequest { Query = "file1", IncludeFiles = true, IncludeFolders = false };

            // Act - Search as first user
            var result1 = await _searchRepository!.SearchAsync(searchRequest, testUser.Id);
            // Act - Search as second user
            var result2 = await _searchRepository.SearchAsync(searchRequest, secondUser.Id);

            // Assert
            result1.Files.Count.Should().Be(1);
            result2.Files.Count.Should().Be(1);

            // Verify that each user only sees their own files
            result1.Files.Should().AllSatisfy(f => f.UserId.Should().Be(testUser.Id));
            result2.Files.Should().AllSatisfy(f => f.UserId.Should().Be(secondUser.Id));

            // Verify results are different (different file IDs from different users)
            var result1Ids = result1.Files.Select(f => f.Id).ToHashSet();
            var result2Ids = result2.Files.Select(f => f.Id).ToHashSet();
            result1Ids.Intersect(result2Ids).Should().BeEmpty("because different users should have different file instances");
        }

        [Fact]
        public async Task SearchAsync_WithUserIsolation_UserACannotSeeUserBData()
        {
            // Arrange
            var secondUser = new UserEntity { Id = Ulid.NewUlid(), Name = "TestUser2" };

            // Insert second user
            await _sqLiteConnection.ExecuteAsync(
                "INSERT INTO Users (Id, Name, PasswordHash) VALUES (@Id, @Name, @PasswordHash)",
                new { secondUser.Id, secondUser.Name, PasswordHash = "hash2" });

            // User 2 has files with unique names
            var snapshot2 = new SnapshotEntity
            {
                Description = "User 2 Snapshot",
                UserId = secondUser.Id,
                User = secondUser
            };
            var pc2 = new PcEntity { Name = "PC2", DeviceId = Guid.NewGuid().ToString(), UserId = secondUser.Id, User = secondUser };
            var storageDrive2 = new StorageDriveEntity
            {
                Name = "Drive2",
                DeviceId = Guid.NewGuid().ToString(),
                SerialNumber = Guid.NewGuid().ToString(),
                TotalSize = 1000000,
                Description = "Storage Drive 2",
                MediaType = "SSD",
                InterfaceType = "SATA",
                UserId = secondUser.Id,
                User = secondUser
            };
            var volume2 = new VolumeEntity
            {
                DriveLetter = "D:",
                VolumeName = "Volume2",
                VolumeSerialNumber = Guid.NewGuid().ToString(),
                VolumeSize = 500000,
                Description = "Volume 2",
                UserId = secondUser.Id,
                User = secondUser
            };
            var volumeInfo2 = new VolumeInfoEntity { FreeSpace = 250000, DriveStatus = "OK", UserId = secondUser.Id, User = secondUser };
            var rootFolder2 = new FsFolderEntity { Name = "Root2", Size = 0, Sha256Hash = "root2", UserId = secondUser.Id, User = secondUser };

            var uniqueFile = new FsFileEntity
            {
                Name = "secretfile_user2",
                FileExtension = ".txt",
                Size = 100,
                Sha256Hash = "secret",
                UserId = secondUser.Id,
                User = secondUser
            };
            rootFolder2.Files.Add(uniqueFile);
            uniqueFile.ParentFolders.Add(rootFolder2);

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

            // Add user 2's data
            await _fileSystemRepository!.AddSnapshotAsync(snapshot2, secondUser.Id);

            // Act - User 1 searches for user 2's unique file
            var searchRequest = new SearchRequest
            {
                Query = "secretfile_user2",
                IncludeFiles = true,
                IncludeFolders = false
            };
            var result = await _searchRepository!.SearchAsync(searchRequest, testUser.Id);

            // Assert - User 1 should get no results
            result.Files.Should().BeEmpty("because User 1 should not be able to see User 2's files");
        }

        [Fact]
        public async Task SearchAsync_WithUserIsolation_FolderIsolationWorks()
        {
            // Arrange
            var secondUser = new UserEntity { Id = Ulid.NewUlid(), Name = "TestUser2" };

            // Insert second user
            await _sqLiteConnection.ExecuteAsync(
                "INSERT INTO Users (Id, Name, PasswordHash) VALUES (@Id, @Name, @PasswordHash)",
                new { secondUser.Id, secondUser.Name, PasswordHash = "hash2" });

            // User 1 has a folder
            var snapshot1 = CreateSnapshotWithFoldersForUser(testUser);
            await _fileSystemRepository!.AddSnapshotAsync(snapshot1, testUser.Id);

            // User 2 has a folder with same name
            var snapshot2 = CreateSnapshotWithFoldersForUser(secondUser);
            await _fileSystemRepository.AddSnapshotAsync(snapshot2, secondUser.Id);

            var searchRequest = new SearchRequest
            {
                Query = "Documents",
                IncludeFiles = false,
                IncludeFolders = true
            };

            // Act
            var result1 = await _searchRepository!.SearchAsync(searchRequest, testUser.Id);
            var result2 = await _searchRepository.SearchAsync(searchRequest, secondUser.Id);

            // Assert
            result1.Folders.Should().NotBeEmpty();
            result2.Folders.Should().NotBeEmpty();

            // Each user should only see their own folders
            result1.Folders.Should().AllSatisfy(f => f.UserId.Should().Be(testUser.Id));
            result2.Folders.Should().AllSatisfy(f => f.UserId.Should().Be(secondUser.Id));

            // Folder IDs should be different
            var result1Ids = result1.Folders.Select(f => f.Id).ToHashSet();
            var result2Ids = result2.Folders.Select(f => f.Id).ToHashSet();
            result1Ids.Intersect(result2Ids).Should().BeEmpty("because different users should have different folder instances");
        }

        #endregion

        #region Helper Methods

        private SnapshotEntity CreateSnapshotWithFiles()
        {
            var snapshot = new SnapshotEntity { Description = "Test Snapshot with Files", UserId = testUser.Id, User = testUser };
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
            var volumeInfo = new VolumeInfoEntity { FreeSpace = 250000, DriveStatus = "OK", UserId = testUser.Id, User = testUser };
            var rootFolder = new FsFolderEntity { Name = "Root", Size = 0, Sha256Hash = "abc123", UserId = testUser.Id, User = testUser };

            var file1 = new FsFileEntity { Name = "file1", FileExtension = ".txt", Size = 100, Sha256Hash = "hash1", UserId = testUser.Id, User = testUser };
            var file2 = new FsFileEntity { Name = "file2", FileExtension = ".pdf", Size = 200, Sha256Hash = "hash2", UserId = testUser.Id, User = testUser };
            var file3 = new FsFileEntity { Name = "file3", FileExtension = ".docx", Size = 300, Sha256Hash = "hash3", UserId = testUser.Id, User = testUser };

            rootFolder.Files.Add(file1);
            rootFolder.Files.Add(file2);
            rootFolder.Files.Add(file3);
            file1.ParentFolders.Add(rootFolder);
            file2.ParentFolders.Add(rootFolder);
            file3.ParentFolders.Add(rootFolder);

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

        private SnapshotEntity CreateSnapshotWithFilesForUser(UserEntity user)
        {
            var snapshot = new SnapshotEntity { Description = "Test Snapshot with Files", UserId = user.Id, User = user };
            var pc = new PcEntity { Name = "TestPC", DeviceId = Guid.NewGuid().ToString(), UserId = user.Id, User = user };
            var storageDrive = new StorageDriveEntity
            {
                Name = "Test Drive",
                DeviceId = Guid.NewGuid().ToString(),
                SerialNumber = Guid.NewGuid().ToString(),
                TotalSize = 1000000,
                Description = "Test Storage Drive",
                MediaType = "SSD",
                InterfaceType = "SATA",
                UserId = user.Id,
                User = user
            };
            var volume = new VolumeEntity
            {
                DriveLetter = "C:",
                VolumeName = "TestVolume",
                VolumeSerialNumber = Guid.NewGuid().ToString(),
                VolumeSize = 500000,
                Description = "Test Volume",
                UserId = user.Id,
                User = user
            };
            var volumeInfo = new VolumeInfoEntity { FreeSpace = 250000, DriveStatus = "OK", UserId = user.Id, User = user };
            var rootFolder = new FsFolderEntity { Name = "Root", Size = 0, Sha256Hash = "abc123", UserId = user.Id, User = user };

            var file1 = new FsFileEntity { Name = "file1", FileExtension = ".txt", Size = 100, Sha256Hash = "hash1", UserId = user.Id, User = user };
            var file2 = new FsFileEntity { Name = "file2", FileExtension = ".pdf", Size = 200, Sha256Hash = "hash2", UserId = user.Id, User = user };
            var file3 = new FsFileEntity { Name = "file3", FileExtension = ".docx", Size = 300, Sha256Hash = "hash3", UserId = user.Id, User = user };

            rootFolder.Files.Add(file1);
            rootFolder.Files.Add(file2);
            rootFolder.Files.Add(file3);
            file1.ParentFolders.Add(rootFolder);
            file2.ParentFolders.Add(rootFolder);
            file3.ParentFolders.Add(rootFolder);

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

        private SnapshotEntity CreateSnapshotWithFolders()
        {
            var snapshot = new SnapshotEntity { Description = "Test Snapshot with Folders", UserId = testUser.Id, User = testUser };
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
            var volumeInfo = new VolumeInfoEntity { FreeSpace = 250000, DriveStatus = "OK", UserId = testUser.Id, User = testUser };
            var rootFolder = new FsFolderEntity { Name = "Root", Size = 0, Sha256Hash = "root", UserId = testUser.Id, User = testUser };

            var folder1 = new FsFolderEntity { Name = "Documents", Size = 500, Sha256Hash = "doc", UserId = testUser.Id, User = testUser };
            var folder2 = new FsFolderEntity { Name = "SubDocuments", Size = 300, Sha256Hash = "subdoc", UserId = testUser.Id, User = testUser };

            rootFolder.ChildFolders.Add(folder1);
            folder1.ParentFolders.Add(rootFolder);
            folder1.ChildFolders.Add(folder2);
            folder2.ParentFolders.Add(folder1);

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

        private SnapshotEntity CreateSnapshotWithFoldersForUser(UserEntity user)
        {
            var snapshot = new SnapshotEntity { Description = "Test Snapshot with Folders", UserId = user.Id, User = user };
            var pc = new PcEntity { Name = "TestPC", DeviceId = Guid.NewGuid().ToString(), UserId = user.Id, User = user };
            var storageDrive = new StorageDriveEntity
            {
                Name = "Test Drive",
                DeviceId = Guid.NewGuid().ToString(),
                SerialNumber = Guid.NewGuid().ToString(),
                TotalSize = 1000000,
                Description = "Test Storage Drive",
                MediaType = "SSD",
                InterfaceType = "SATA",
                UserId = user.Id,
                User = user
            };
            var volume = new VolumeEntity
            {
                DriveLetter = "C:",
                VolumeName = "TestVolume",
                VolumeSerialNumber = Guid.NewGuid().ToString(),
                VolumeSize = 500000,
                Description = "Test Volume",
                UserId = user.Id,
                User = user
            };
            var volumeInfo = new VolumeInfoEntity { FreeSpace = 250000, DriveStatus = "OK", UserId = user.Id, User = user };
            var rootFolder = new FsFolderEntity { Name = "Root", Size = 0, Sha256Hash = "root", UserId = user.Id, User = user };

            var folder1 = new FsFolderEntity { Name = "Documents", Size = 500, Sha256Hash = "doc", UserId = user.Id, User = user };
            var folder2 = new FsFolderEntity { Name = "SubDocuments", Size = 300, Sha256Hash = "subdoc", UserId = user.Id, User = user };

            rootFolder.ChildFolders.Add(folder1);
            folder1.ParentFolders.Add(rootFolder);
            folder1.ChildFolders.Add(folder2);
            folder2.ParentFolders.Add(folder1);

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

        private SnapshotEntity CreateSnapshotWithFilesAndFolders()
        {
            var snapshot = new SnapshotEntity { Description = "Test Snapshot with Files and Folders", UserId = testUser.Id, User = testUser };
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
            var volumeInfo = new VolumeInfoEntity { FreeSpace = 250000, DriveStatus = "OK", UserId = testUser.Id, User = testUser };
            var rootFolder = new FsFolderEntity { Name = "Root", Size = 0, Sha256Hash = "root", UserId = testUser.Id, User = testUser };

            // Add folders
            var documentsFolder = new FsFolderEntity { Name = "Documents", Size = 500, Sha256Hash = "doc", UserId = testUser.Id, User = testUser };
            rootFolder.ChildFolders.Add(documentsFolder);
            documentsFolder.ParentFolders.Add(rootFolder);

            // Add files to folders
            var docFile = new FsFileEntity { Name = "document.docx", FileExtension = ".docx", Size = 150, Sha256Hash = "docfile", UserId = testUser.Id, User = testUser };
            documentsFolder.Files.Add(docFile);
            docFile.ParentFolders.Add(documentsFolder);

            // Add files to root
            var textFile = new FsFileEntity { Name = "readme.txt", FileExtension = ".txt", Size = 100, Sha256Hash = "txtfile", UserId = testUser.Id, User = testUser };
            rootFolder.Files.Add(textFile);
            textFile.ParentFolders.Add(rootFolder);

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
            var snapshot = new SnapshotEntity { Description = "Large Test Snapshot", UserId = testUser.Id, User = testUser };
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
            var volumeInfo = new VolumeInfoEntity { FreeSpace = 250000, DriveStatus = "OK", UserId = testUser.Id, User = testUser };
            var rootFolder = new FsFolderEntity { Name = "Root", Size = 0, Sha256Hash = "root", UserId = testUser.Id, User = testUser };

            // Create many folders and files
            var random = new Random(42);
            var folders = new Queue<FsFolderEntity>();
            folders.Enqueue(rootFolder);

            int folderCount = 0;
            while (folders.Count > 0 && folderCount < 10)
            {
                var currentFolder = folders.Dequeue();
                for (int i = 0; i < 5; i++)
                {
                    var newFolder = new FsFolderEntity
                    {
                        Name = $"Folder_{folderCount}_{i}",
                        Size = random.Next(100, 1000),
                        Sha256Hash = $"hash_{folderCount}_{i}",
                        UserId = testUser.Id,
                        User = testUser
                    };

                    currentFolder.ChildFolders.Add(newFolder);
                    newFolder.ParentFolders.Add(currentFolder);

                    for (int j = 0; j < 3; j++)
                    {
                        var file = new FsFileEntity
                        {
                            Name = $"File_{folderCount}_{i}_{j}",
                            FileExtension = ".txt",
                            Size = random.Next(10, 500),
                            Sha256Hash = $"filehash_{folderCount}_{i}_{j}",
                            UserId = testUser.Id,
                            User = testUser
                        };

                        newFolder.Files.Add(file);
                        file.ParentFolders.Add(newFolder);
                    }

                    folders.Enqueue(newFolder);
                }

                folderCount++;
            }

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

        #endregion
    }
}
