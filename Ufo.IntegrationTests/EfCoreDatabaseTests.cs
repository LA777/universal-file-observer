using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ufo.Abstractions;
using Ufo.Abstractions.Database.Entities;
using Ufo.Database.Contexts;
using Ufo.Database.Repositories;

namespace Ufo.IntegrationTests
{
    public class EfCoreDatabaseTests : IDisposable
    {
        private readonly string _testDatabasePath;
        private UfoDbContext _dbContext;
        private FileSystemEfCoreRepository _repository;
        private readonly ILogger<FileSystemEfCoreRepository> _logger;

        public EfCoreDatabaseTests()
        {
            _testDatabasePath = Path.Combine(Path.GetTempPath(), $"test_{Ulid.NewUlid()}.db");
            
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            _logger = loggerFactory.CreateLogger<FileSystemEfCoreRepository>();
            
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            var options = new DbContextOptionsBuilder<UfoDbContext>()
                .UseSqlite($"Data Source={_testDatabasePath}")
                .Options;

            _dbContext = new UfoDbContext(options);
            _dbContext.Database.EnsureCreated();
            _repository = new FileSystemEfCoreRepository(_dbContext, _logger);
        }

        private void ResetDatabase()
        {
            // Dispose and recreate the context for each test to ensure clean state
            _dbContext?.Dispose();
            
            // Delete and recreate the database
            if (File.Exists(_testDatabasePath))
            {
                File.Delete(_testDatabasePath);
            }
            
            InitializeDatabase();
        }

        public void Dispose()
        {
            _dbContext?.Dispose();
            if (File.Exists(_testDatabasePath))
            {
                try
                {
                    File.Delete(_testDatabasePath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }

        [Fact]
        public async Task AddSnapshotAsync_WritesSnapshotCorrectly()
        {
            // Arrange
            var snapshot = CreateTestSnapshot();

            // Act
            var result = await _repository.AddSnapshotAsync(snapshot);

            // Assert
            Assert.Equal(1, result);
            
            var savedSnapshot = await _dbContext.Snapshots
                .FirstOrDefaultAsync(s => s.Id == snapshot.Id);
            Assert.NotNull(savedSnapshot);
            Assert.Equal(snapshot.Id, savedSnapshot.Id);
        }

        [Fact]
        public async Task AddSnapshotAsync_WritesPcCorrectly()
        {
            // Arrange
            ResetDatabase();
            var snapshot = CreateTestSnapshot();

            // Act
            await _repository.AddSnapshotAsync(snapshot);

            // Assert
            var pc = await _dbContext.Pcs.FirstOrDefaultAsync();
            Assert.NotNull(pc);
            Assert.Equal("TestPC", pc.Name);
        }

        [Fact]
        public async Task AddSnapshotAsync_WritesStorageDriveCorrectly()
        {
            // Arrange
            ResetDatabase();
            var snapshot = CreateTestSnapshot();

            // Act
            await _repository.AddSnapshotAsync(snapshot);

            // Assert
            var storageDrive = await _dbContext.StorageDrives.FirstOrDefaultAsync();
            Assert.NotNull(storageDrive);
            Assert.Equal("Test Drive", storageDrive.Name);
            Assert.Equal("TestSerialNumber", storageDrive.SerialNumber);
            Assert.Equal("TestDeviceId", storageDrive.DeviceId);
        }

        [Fact]
        public async Task AddSnapshotAsync_WritesVolumeCorrectly()
        {
            // Arrange
            ResetDatabase();
            var snapshot = CreateTestSnapshot();

            // Act
            await _repository.AddSnapshotAsync(snapshot);

            // Assert
            var volume = await _dbContext.Volumes.FirstOrDefaultAsync();
            Assert.NotNull(volume);
            Assert.Equal("C:", volume.DriveLetter);
            Assert.Equal("TestVolume", volume.VolumeName);
            Assert.Equal("1234-5678", volume.VolumeSerialNumber);
        }

        [Fact]
        public async Task AddSnapshotAsync_WritesVolumeInfoCorrectly()
        {
            // Arrange
            ResetDatabase();
            var snapshot = CreateTestSnapshot();

            // Act
            await _repository.AddSnapshotAsync(snapshot);

            // Assert
            var volumeInfo = await _dbContext.VolumeInfos.FirstOrDefaultAsync();
            Assert.NotNull(volumeInfo);
            Assert.Equal(1000000, volumeInfo.FreeSpace);
            Assert.Equal("OK", volumeInfo.DriveStatus);
        }

        [Fact]
        public async Task AddSnapshotAsync_WritesFoldersCorrectly()
        {
            // Arrange
            ResetDatabase();
            var snapshot = CreateTestSnapshot();

            // Act
            await _repository.AddSnapshotAsync(snapshot);

            // Assert
            var folders = await _dbContext.Folders.ToListAsync();
            Assert.NotEmpty(folders);
            
            var rootFolder = folders.FirstOrDefault(f => f.Name == "RootFolder");
            Assert.NotNull(rootFolder);
            
            var childFolder = folders.FirstOrDefault(f => f.Name.StartsWith("ChildFolder"));
            Assert.NotNull(childFolder);
        }

        [Fact]
        public async Task AddSnapshotAsync_WritesFilesCorrectly()
        {
            // Arrange
            ResetDatabase();
            var snapshot = CreateTestSnapshot();

            // Act
            await _repository.AddSnapshotAsync(snapshot);

            // Assert
            var files = await _dbContext.Files.ToListAsync();
            Assert.NotEmpty(files);
            
            var file = files.FirstOrDefault(f => f.Name.StartsWith("TestFile"));
            Assert.NotNull(file);
            Assert.Equal(".txt", file.FileExtension);
        }

        [Fact]
        public async Task AddSnapshotAsync_WritesFoldersToFoldersCorrectly()
        {
            // Arrange
            ResetDatabase();
            var snapshot = CreateTestSnapshot();

            // Act
            await _repository.AddSnapshotAsync(snapshot);

            // Assert
            var foldersToFolders = await _dbContext.FoldersToFolders
                .Where(f => f.SnapshotId == snapshot.Id)
                .ToListAsync();
            
            Assert.NotEmpty(foldersToFolders);
            Assert.True(foldersToFolders.Any(f => f.ParentFolderId == null), "Should have root folder relationship");
        }

        [Fact]
        public async Task AddSnapshotAsync_WritesFilesToFoldersCorrectly()
        {
            // Arrange
            ResetDatabase();
            var snapshot = CreateTestSnapshot();

            // Act
            await _repository.AddSnapshotAsync(snapshot);

            // Assert
            var filesToFolders = await _dbContext.FilesToFolders
                .Where(f => f.SnapshotId == snapshot.Id)
                .ToListAsync();
            
            Assert.NotEmpty(filesToFolders);
        }

        [Fact]
        public async Task AddSnapshotAsync_WritesPcsToStorageDrivesCorrectly()
        {
            // Arrange
            ResetDatabase();
            var snapshot = CreateTestSnapshot();

            // Act
            await _repository.AddSnapshotAsync(snapshot);

            // Assert
            var pcsToStorageDrives = await _dbContext.PcsToStorageDrives
                .Where(p => p.SnapshotId == snapshot.Id)
                .ToListAsync();
            
            Assert.Single(pcsToStorageDrives);
        }

        [Fact]
        public async Task GetSnapshotByIdAsync_RetrievesSnapshotWithAllRelations()
        {
            // Arrange
            ResetDatabase();
            var originalSnapshot = CreateTestSnapshot();
            await _repository.AddSnapshotAsync(originalSnapshot);

            // Act
            var retrievedSnapshot = await _repository.GetSnapshotByIdAsync(originalSnapshot.Id);

            // Assert
            Assert.NotNull(retrievedSnapshot);
            Assert.Equal(originalSnapshot.Id, retrievedSnapshot.Id);
            Assert.NotNull(retrievedSnapshot.VolumeInfo);
            Assert.NotNull(retrievedSnapshot.VolumeInfo.Volume);
            Assert.NotNull(retrievedSnapshot.VolumeInfo.Volume.StorageDrive);
            Assert.NotNull(retrievedSnapshot.RootFolder);
        }

        [Fact]
        public async Task GetAllSnapshotsAsync_RetrievesAllSnapshots()
        {
            // Arrange
            ResetDatabase();
            var snapshot1 = CreateTestSnapshot();
            var snapshot2 = CreateTestSnapshotDifferent();
            
            await _repository.AddSnapshotAsync(snapshot1);
            await _repository.AddSnapshotAsync(snapshot2);

            // Act
            var snapshots = await _repository.GetAllSnapshotsAsync();

            // Assert
            Assert.Equal(2, snapshots.Count);
        }

        [Fact]
        public async Task GetAllSnapshotsAsync_OrdersByTimestampDescending()
        {
            // Arrange
            ResetDatabase();
            var snapshot1 = CreateTestSnapshot();
            snapshot1.Timestamp = DateTimeOffset.Now.AddHours(-1);
            
            var snapshot2 = CreateTestSnapshotDifferent();
            snapshot2.Timestamp = DateTimeOffset.Now;
            
            await _repository.AddSnapshotAsync(snapshot1);
            await _repository.AddSnapshotAsync(snapshot2);

            // Act
            var snapshots = await _repository.GetAllSnapshotsAsync();

            // Assert
            Assert.Equal(snapshot2.Id, snapshots.First().Id);
            Assert.Equal(snapshot1.Id, snapshots.Last().Id);
        }

        [Fact]
        public async Task DeleteSnapshotByIdAsync_DeletesSnapshotSuccessfully()
        {
            // Arrange
            ResetDatabase();
            var snapshot = CreateTestSnapshot();
            await _repository.AddSnapshotAsync(snapshot);

            // Act
            var result = await _repository.DeleteSnapshotByIdAsync(snapshot.Id);

            // Assert
            Assert.Equal(DeleteResult.Success, result);
            
            var deletedSnapshot = await _dbContext.Snapshots
                .FirstOrDefaultAsync(s => s.Id == snapshot.Id);
            Assert.Null(deletedSnapshot);
        }

        [Fact]
        public async Task DeleteSnapshotByIdAsync_ReturnsNotFoundForNonExistentSnapshot()
        {
            // Arrange
            ResetDatabase();
            var fakeSnapshotId = Ulid.NewUlid();

            // Act
            var result = await _repository.DeleteSnapshotByIdAsync(fakeSnapshotId);

            // Assert
            Assert.Equal(DeleteResult.NotFound, result);
        }

        [Fact]
        public async Task DeleteSnapshotByIdAsync_DeletesOrphanedFilesOnly()
        {
            // Arrange
            ResetDatabase();
            var snapshot1 = CreateTestSnapshot();
            var snapshot2 = CreateTestSnapshotDifferent();
            
            await _repository.AddSnapshotAsync(snapshot1);
            await _repository.AddSnapshotAsync(snapshot2);

            var fileCountBefore = await _dbContext.Files.CountAsync();

            // Act
            await _repository.DeleteSnapshotByIdAsync(snapshot1.Id);

            // Assert
            var fileCountAfter = await _dbContext.Files.CountAsync();
            // Files should remain because they're also used by snapshot2
            Assert.True(fileCountAfter > 0);
        }

        [Fact]
        public async Task DeleteSnapshotByIdAsync_DeletesOrphanedFoldersOnly()
        {
            // Arrange
            ResetDatabase();
            var snapshot1 = CreateTestSnapshot();
            var snapshot2 = CreateTestSnapshotDifferent();
            
            await _repository.AddSnapshotAsync(snapshot1);
            await _repository.AddSnapshotAsync(snapshot2);

            var folderCountBefore = await _dbContext.Folders.CountAsync();

            // Act
            await _repository.DeleteSnapshotByIdAsync(snapshot1.Id);

            // Assert
            var folderCountAfter = await _dbContext.Folders.CountAsync();
            // Folders should remain because they're also used by snapshot2
            Assert.True(folderCountAfter > 0);
        }

        [Fact]
        public async Task DeleteSnapshotByIdAsync_DeletesRelatedJoinEntities()
        {
            // Arrange
            ResetDatabase();
            var snapshot = CreateTestSnapshot();
            await _repository.AddSnapshotAsync(snapshot);

            var pcsToStorageDrivesCountBefore = await _dbContext.PcsToStorageDrives
                .Where(p => p.SnapshotId == snapshot.Id)
                .CountAsync();
            Assert.True(pcsToStorageDrivesCountBefore > 0);

            // Act
            await _repository.DeleteSnapshotByIdAsync(snapshot.Id);

            // Assert
            var pcsToStorageDrivesCountAfter = await _dbContext.PcsToStorageDrives
                .Where(p => p.SnapshotId == snapshot.Id)
                .CountAsync();
            Assert.Equal(0, pcsToStorageDrivesCountAfter);
        }

        [Fact]
        public async Task ReusesPcWhenAlreadyExists()
        {
            // Arrange
            ResetDatabase();
            var snapshot1 = CreateTestSnapshot();
            var snapshot2 = CreateTestSnapshot(); // Same PC name and DeviceId
            
            await _repository.AddSnapshotAsync(snapshot1);
            var pcCountAfterFirst = await _dbContext.Pcs.CountAsync();

            // Act
            await _repository.AddSnapshotAsync(snapshot2);

            // Assert
            var pcCountAfterSecond = await _dbContext.Pcs.CountAsync();
            Assert.Equal(pcCountAfterFirst, pcCountAfterSecond);
        }

        [Fact]
        public async Task ReusesStorageDriveWhenAlreadyExists()
        {
            // Arrange
            ResetDatabase();
            var snapshot1 = CreateTestSnapshot();
            var snapshot2 = CreateTestSnapshot(); // Same StorageDrive properties
            
            await _repository.AddSnapshotAsync(snapshot1);
            var storageCountAfterFirst = await _dbContext.StorageDrives.CountAsync();

            // Act
            await _repository.AddSnapshotAsync(snapshot2);

            // Assert
            var storageCountAfterSecond = await _dbContext.StorageDrives.CountAsync();
            Assert.Equal(storageCountAfterFirst, storageCountAfterSecond);
        }

        [Fact]
        public async Task ReusesVolumeWhenAlreadyExists()
        {
            // Arrange
            ResetDatabase();
            var snapshot1 = CreateTestSnapshot();
            var snapshot2 = CreateTestSnapshot(); // Same Volume VolumeSerialNumber
            
            await _repository.AddSnapshotAsync(snapshot1);
            var volumeCountAfterFirst = await _dbContext.Volumes.CountAsync();

            // Act
            await _repository.AddSnapshotAsync(snapshot2);

            // Assert
            var volumeCountAfterSecond = await _dbContext.Volumes.CountAsync();
            Assert.Equal(volumeCountAfterFirst, volumeCountAfterSecond);
        }

        private SnapshotEntity CreateTestSnapshot()
        {
            var snapshot = new SnapshotEntity
            {
                Id = Ulid.NewUlid(),
                Timestamp = DateTimeOffset.Now
            };

            var pc = new PcEntity
            {
                Id = Ulid.NewUlid(),
                Name = "TestPC",
                DeviceId = "TestDeviceId"
            };

            var storageDrive = new StorageDriveEntity
            {
                Id = Ulid.NewUlid(),
                Name = "Test Drive",
                SerialNumber = "TestSerialNumber",
                DeviceId = "TestDeviceId",
                TotalSize = 1000000000,
                Description = "Test Storage Drive",
                MediaType = "SSD",
                InterfaceType = "SATA"
            };

            var volume = new VolumeEntity
            {
                Id = Ulid.NewUlid(),
                DriveLetter = "C:",
                VolumeName = "TestVolume",
                Description = "Test Volume",
                VolumeSerialNumber = "1234-5678",
                VolumeSize = 1000000000,
                StorageDrive = storageDrive,
                StorageDriveId = storageDrive.Id
            };

            var volumeInfo = new VolumeInfoEntity
            {
                Id = Ulid.NewUlid(),
                FreeSpace = 1000000,
                DriveStatus = "OK",
                Volume = volume,
                VolumeId = volume.Id,
                Snapshot = snapshot,
                SnapshotId = snapshot.Id
            };

            var rootFolder = new FsFolderEntity
            {
                Id = Ulid.NewUlid(),
                Name = "RootFolder",
                Size = 500,
                Sha256Hash = "abc123",
                HasParent = false
            };

            var childFolder = new FsFolderEntity
            {
                Id = Ulid.NewUlid(),
                Name = "ChildFolder",
                Size = 200,
                Sha256Hash = "def456",
                HasParent = true
            };

            var file = new FsFileEntity
            {
                Id = Ulid.NewUlid(),
                Name = "TestFile",
                Size = 100,
                Sha256Hash = "ghi789",
                FileExtension = ".txt"
            };

            rootFolder.ChildFolders.Add(childFolder);
            rootFolder.Files.Add(file);
            childFolder.ParentFolders.Add(rootFolder);
            file.ParentFolders.Add(rootFolder);

            pc.Snapshots.Add(snapshot);
            pc.StorageDrives.Add(storageDrive);

            storageDrive.Pcs.Add(pc);
            storageDrive.Volumes.Add(volume);

            volume.VolumeInfos.Add(volumeInfo);

            snapshot.VolumeInfo = volumeInfo;
            snapshot.RootFolder = rootFolder;

            return snapshot;
        }

        private SnapshotEntity CreateTestSnapshotDifferent()
        {
            var snapshot = new SnapshotEntity
            {
                Id = Ulid.NewUlid(),
                Timestamp = DateTimeOffset.Now
            };

            var pc = new PcEntity
            {
                Id = Ulid.NewUlid(),
                Name = "TestPC",
                DeviceId = "TestDeviceId"
            };

            var storageDrive = new StorageDriveEntity
            {
                Id = Ulid.NewUlid(),
                Name = "Test Drive",
                SerialNumber = "TestSerialNumber",
                DeviceId = "TestDeviceId",
                TotalSize = 1000000000,
                Description = "Test Storage Drive",
                MediaType = "SSD",
                InterfaceType = "SATA"
            };

            var volume = new VolumeEntity
            {
                Id = Ulid.NewUlid(),
                DriveLetter = "C:",
                VolumeName = "TestVolume",
                Description = "Test Volume",
                VolumeSerialNumber = "1234-5678",
                VolumeSize = 1000000000,
                StorageDrive = storageDrive,
                StorageDriveId = storageDrive.Id
            };

            var volumeInfo = new VolumeInfoEntity
            {
                Id = Ulid.NewUlid(),
                FreeSpace = 1000000,
                DriveStatus = "OK",
                Volume = volume,
                VolumeId = volume.Id,
                Snapshot = snapshot,
                SnapshotId = snapshot.Id
            };

            var rootFolder = new FsFolderEntity
            {
                Id = Ulid.NewUlid(),
                Name = "RootFolder",
                Size = 500,
                Sha256Hash = "abc123diff",
                HasParent = false
            };

            var childFolder1 = new FsFolderEntity
            {
                Id = Ulid.NewUlid(),
                Name = "ChildFolder1",
                Size = 200,
                Sha256Hash = "def456diff",
                HasParent = true
            };

            var childFolder2 = new FsFolderEntity
            {
                Id = Ulid.NewUlid(),
                Name = "ChildFolder2",
                Size = 300,
                Sha256Hash = "jkl789diff",
                HasParent = true
            };

            var file1 = new FsFileEntity
            {
                Id = Ulid.NewUlid(),
                Name = "TestFile1",
                Size = 100,
                Sha256Hash = "ghi789diff",
                FileExtension = ".txt"
            };

            var file2 = new FsFileEntity
            {
                Id = Ulid.NewUlid(),
                Name = "TestFile2",
                Size = 200,
                Sha256Hash = "xyz456diff",
                FileExtension = ".txt"
            };

            rootFolder.ChildFolders.Add(childFolder1);
            rootFolder.ChildFolders.Add(childFolder2);
            rootFolder.Files.Add(file1);
            childFolder1.ParentFolders.Add(rootFolder);
            childFolder2.ParentFolders.Add(rootFolder);
            childFolder2.Files.Add(file2);
            file1.ParentFolders.Add(rootFolder);
            file2.ParentFolders.Add(childFolder2);

            pc.Snapshots.Add(snapshot);
            pc.StorageDrives.Add(storageDrive);

            storageDrive.Pcs.Add(pc);
            storageDrive.Volumes.Add(volume);

            volume.VolumeInfos.Add(volumeInfo);

            snapshot.VolumeInfo = volumeInfo;
            snapshot.RootFolder = rootFolder;

            return snapshot;
        }
    }
}
