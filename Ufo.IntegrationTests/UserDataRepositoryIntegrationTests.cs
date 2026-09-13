using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Moq;
using Ufo.Abstractions;
using Ufo.Abstractions.Database;
using Ufo.Abstractions.Database.Entities;
using Ufo.Database;
using Ufo.Database.Contexts;
using Ufo.Database.Repositories;

namespace Ufo.IntegrationTests;

/// <summary>
/// The three wholesale deletions behind the Settings page's danger zone, against
/// a real SQLite database with foreign keys on - the point being what each one
/// takes with it and, just as much, what it leaves alone.
/// </summary>
public class UserDataRepositoryIntegrationTests : IAsyncLifetime
{
    private readonly UserEntity testUser = new() { Id = Ulid.NewUlid(), Name = "TestUser" };
    private readonly UserEntity otherUser = new() { Id = Ulid.NewUlid(), Name = "OtherUser" };
    private SqliteConnection _sqLiteConnection = null!;
    private SnapshotRepository _snapshotRepository = null!;
    private UserDataRepository _userDataRepository = null!;

    #region Database Initialization and Cleanup

    public async Task InitializeAsync()
    {
        var dbName = $"testdb-{Guid.NewGuid()}";
        var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared;Foreign Keys=True";

        var dbConnectionFactoryMock = new Mock<IDbConnectionFactory>();
        _sqLiteConnection = new SqliteConnection(connectionString);
        await _sqLiteConnection.OpenAsync();
        dbConnectionFactoryMock.Setup(factory => factory.GetSqliteConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _sqLiteConnection);

        await DapperDataContext.InitiateDatabaseAsync(_sqLiteConnection);
        _snapshotRepository = new SnapshotRepository(dbConnectionFactoryMock.Object, Mock.Of<ILogger<SnapshotRepository>>());
        _userDataRepository = new UserDataRepository(dbConnectionFactoryMock.Object, Mock.Of<ILogger<UserDataRepository>>());

        foreach (var user in new[] { testUser, otherUser })
        {
            await _sqLiteConnection.ExecuteAsync(
                "INSERT INTO Users (Id, Name, PasswordHash) VALUES (@Id, @Name, @PasswordHash)",
                new { user.Id, user.Name, PasswordHash = "hash" });
        }
    }

    public async Task DisposeAsync()
    {
        await _sqLiteConnection.DisposeAsync();
    }

    #endregion

    #region DeleteSnapshotsAsync

    [Fact]
    public async Task DeleteSnapshotsAsync_RemovesEverySnapshotOfTheUserAndWhatHungOffThem()
    {
        await _snapshotRepository.AddSnapshotAsync(CreateSnapshotWithFiles(testUser.Id), testUser.Id);
        await _snapshotRepository.AddSnapshotAsync(CreateSnapshotWithFiles(testUser.Id), testUser.Id);
        var labelId = await InsertLabelAsync(testUser.Id);
        var snapshotIds = await _sqLiteConnection.QueryAsync<Ulid>(
            "SELECT Id FROM Snapshots WHERE UserId = @UserId", new { UserId = testUser.Id });
        foreach (var snapshotId in snapshotIds)
        {
            await _sqLiteConnection.ExecuteAsync(
                "INSERT INTO LabelsToSnapshots (LabelId, SnapshotId) VALUES (@LabelId, @SnapshotId)",
                new { LabelId = labelId, SnapshotId = snapshotId });
        }

        var deletedSnapshots = await _userDataRepository.DeleteSnapshotsAsync(testUser.Id);

        deletedSnapshots.Should().Be(2);
        foreach (var table in new[] { "Snapshots", "VolumeInfos", "Volumes", "StorageDrives", "Pcs", "Folders", "Files", "Labels" })
        {
            (await CountRowsAsync(table, testUser.Id)).Should().Be(0, $"{table} should hold nothing of the user's");
        }
        (await CountAllRowsAsync("FilesToFolders")).Should().Be(0);
        (await CountAllRowsAsync("FoldersToFolders")).Should().Be(0);
        (await CountAllRowsAsync("PcsToStorageDrives")).Should().Be(0);
        (await CountAllRowsAsync("LabelsToSnapshots")).Should().Be(0);
    }

    [Fact]
    public async Task DeleteSnapshotsAsync_LeavesAnotherUsersSnapshotsUntouched()
    {
        await _snapshotRepository.AddSnapshotAsync(CreateSnapshotWithFiles(testUser.Id), testUser.Id);
        var otherSnapshot = CreateSnapshotWithFiles(otherUser.Id);
        await _snapshotRepository.AddSnapshotAsync(otherSnapshot, otherUser.Id);
        await InsertLabelAsync(otherUser.Id);

        await _userDataRepository.DeleteSnapshotsAsync(testUser.Id);

        (await CountRowsAsync("Snapshots", otherUser.Id)).Should().Be(1);
        (await CountRowsAsync("Files", otherUser.Id)).Should().Be(3);
        (await CountRowsAsync("Folders", otherUser.Id)).Should().Be(1);
        (await CountRowsAsync("Pcs", otherUser.Id)).Should().Be(1);
        (await CountRowsAsync("Labels", otherUser.Id)).Should().Be(1);

        // Not merely counted but still readable as a whole graph.
        var retrieved = await _snapshotRepository.GetSnapshotByIdAsync(otherSnapshot.Id, otherUser.Id);
        retrieved.Should().NotBeNull();
        retrieved!.RootFolder!.Files.Should().HaveCount(3);
    }

    [Fact]
    public async Task DeleteSnapshotsAsync_LeavesFileSystemDataAndSettingsAlone()
    {
        await _snapshotRepository.AddSnapshotAsync(CreateSnapshotWithFiles(testUser.Id), testUser.Id);
        await InsertFileSystemDataAsync(testUser.Id);
        await InsertSettingsAsync(testUser.Id);

        await _userDataRepository.DeleteSnapshotsAsync(testUser.Id);

        (await CountRowsAsync("FsItemFlags", testUser.Id)).Should().Be(1);
        (await CountRowsAsync("FsItemRatings", testUser.Id)).Should().Be(1);
        (await CountRowsAsync("Tags", testUser.Id)).Should().Be(1);
        (await CountAllRowsAsync("FsItemTags")).Should().Be(1);
        (await CountRowsAsync("UserSettings", testUser.Id)).Should().Be(1);
        (await CountRowsAsync("UserKeyBindings", testUser.Id)).Should().Be(1);
        (await CountRowsAsync("FolderTabs", testUser.Id)).Should().Be(1);
    }

    [Fact]
    public async Task DeleteSnapshotsAsync_TakesTheSnapshotCopiesOfTagsButNotTheTagsThemselves()
    {
        var snapshot = CreateSnapshotWithFiles(testUser.Id);
        await _snapshotRepository.AddSnapshotAsync(snapshot, testUser.Id);
        var tagId = await InsertTagAsync(testUser.Id);
        var fileId = snapshot.RootFolder!.Files[0].Id;
        await _sqLiteConnection.ExecuteAsync(
            "INSERT INTO TagsToSnapshotFiles (SnapshotId, FolderId, FileId, TagId) VALUES (@SnapshotId, @FolderId, @FileId, @TagId)",
            new { SnapshotId = snapshot.Id, FolderId = snapshot.RootFolder.Id, FileId = fileId, TagId = tagId });
        await _sqLiteConnection.ExecuteAsync(
            "INSERT INTO TagsToSnapshotFolders (SnapshotId, ParentFolderId, ChildFolderId, TagId) VALUES (@SnapshotId, NULL, @ChildFolderId, @TagId)",
            new { SnapshotId = snapshot.Id, ChildFolderId = snapshot.RootFolder.Id, TagId = tagId });

        await _userDataRepository.DeleteSnapshotsAsync(testUser.Id);

        (await CountAllRowsAsync("TagsToSnapshotFiles")).Should().Be(0);
        (await CountAllRowsAsync("TagsToSnapshotFolders")).Should().Be(0);
        (await CountRowsAsync("Tags", testUser.Id)).Should().Be(1, "the vocabulary is file-system data, not snapshot data");
    }

    [Fact]
    public async Task DeleteSnapshotsAsync_WhenThereAreNone_AnswersZeroWithoutFailing()
    {
        var deletedSnapshots = await _userDataRepository.DeleteSnapshotsAsync(testUser.Id);

        deletedSnapshots.Should().Be(0);
    }

    #endregion

    #region DeleteFileSystemDataAsync

    [Fact]
    public async Task DeleteFileSystemDataAsync_RemovesFlagsRatingsTagsAndEveryAssignment()
    {
        var snapshot = CreateSnapshotWithFiles(testUser.Id);
        await _snapshotRepository.AddSnapshotAsync(snapshot, testUser.Id);
        var tagId = await InsertTagAsync(testUser.Id);
        await _sqLiteConnection.ExecuteAsync(
            "INSERT INTO FsItemTags (TagId, FullPath) VALUES (@TagId, @FullPath)",
            new { TagId = tagId, FullPath = "/data/report.pdf" });
        await _sqLiteConnection.ExecuteAsync(
            "INSERT INTO TagsToSnapshotFiles (SnapshotId, FolderId, FileId, TagId) VALUES (@SnapshotId, @FolderId, @FileId, @TagId)",
            new { SnapshotId = snapshot.Id, FolderId = snapshot.RootFolder!.Id, FileId = snapshot.RootFolder.Files[0].Id, TagId = tagId });
        await InsertFlagAsync(testUser.Id, "/data/report.pdf");
        await InsertFlagAsync(testUser.Id, "/data/other.pdf");
        await InsertRatingAsync(testUser.Id, "/data/report.pdf", 7);

        var deletedRows = await _userDataRepository.DeleteFileSystemDataAsync(testUser.Id);

        deletedRows.Should().Be(4, "two flags, one rating and one tag");
        (await CountRowsAsync("FsItemFlags", testUser.Id)).Should().Be(0);
        (await CountRowsAsync("FsItemRatings", testUser.Id)).Should().Be(0);
        (await CountRowsAsync("Tags", testUser.Id)).Should().Be(0);
        (await CountAllRowsAsync("FsItemTags")).Should().Be(0);
        (await CountAllRowsAsync("TagsToSnapshotFiles")).Should().Be(0);
    }

    [Fact]
    public async Task DeleteFileSystemDataAsync_LeavesAnotherUsersMarksUntouched()
    {
        await InsertFileSystemDataAsync(testUser.Id);
        await InsertFileSystemDataAsync(otherUser.Id);

        await _userDataRepository.DeleteFileSystemDataAsync(testUser.Id);

        (await CountRowsAsync("FsItemFlags", otherUser.Id)).Should().Be(1);
        (await CountRowsAsync("FsItemRatings", otherUser.Id)).Should().Be(1);
        (await CountRowsAsync("Tags", otherUser.Id)).Should().Be(1);
        (await CountAllRowsAsync("FsItemTags")).Should().Be(1, "the other user's assignment stays");
    }

    [Fact]
    public async Task DeleteFileSystemDataAsync_LeavesSnapshotsAndSettingsAlone()
    {
        await _snapshotRepository.AddSnapshotAsync(CreateSnapshotWithFiles(testUser.Id), testUser.Id);
        await InsertFileSystemDataAsync(testUser.Id);
        await InsertSettingsAsync(testUser.Id);

        await _userDataRepository.DeleteFileSystemDataAsync(testUser.Id);

        (await CountRowsAsync("Snapshots", testUser.Id)).Should().Be(1);
        (await CountRowsAsync("Files", testUser.Id)).Should().Be(3);
        (await CountRowsAsync("UserSettings", testUser.Id)).Should().Be(1);
        (await CountRowsAsync("UserKeyBindings", testUser.Id)).Should().Be(1);
        (await CountRowsAsync("FolderTabs", testUser.Id)).Should().Be(1);
    }

    [Fact]
    public async Task DeleteFileSystemDataAsync_WhenThereIsNone_AnswersZeroWithoutFailing()
    {
        var deletedRows = await _userDataRepository.DeleteFileSystemDataAsync(testUser.Id);

        deletedRows.Should().Be(0);
    }

    #endregion

    #region DeleteSettingsAsync

    [Fact]
    public async Task DeleteSettingsAsync_RemovesThemeShortcutsAndFolderTabs()
    {
        await InsertSettingsAsync(testUser.Id);

        var deletedRows = await _userDataRepository.DeleteSettingsAsync(testUser.Id);

        deletedRows.Should().Be(3);
        (await CountRowsAsync("UserSettings", testUser.Id)).Should().Be(0);
        (await CountRowsAsync("UserKeyBindings", testUser.Id)).Should().Be(0);
        (await CountRowsAsync("FolderTabs", testUser.Id)).Should().Be(0);
    }

    [Fact]
    public async Task DeleteSettingsAsync_LeavesAnotherUsersSettingsUntouched()
    {
        await InsertSettingsAsync(testUser.Id);
        await InsertSettingsAsync(otherUser.Id);

        await _userDataRepository.DeleteSettingsAsync(testUser.Id);

        (await CountRowsAsync("UserSettings", otherUser.Id)).Should().Be(1);
        (await CountRowsAsync("UserKeyBindings", otherUser.Id)).Should().Be(1);
        (await CountRowsAsync("FolderTabs", otherUser.Id)).Should().Be(1);
    }

    [Fact]
    public async Task DeleteSettingsAsync_LeavesSnapshotsAndFileSystemDataAlone()
    {
        await _snapshotRepository.AddSnapshotAsync(CreateSnapshotWithFiles(testUser.Id), testUser.Id);
        await InsertFileSystemDataAsync(testUser.Id);
        await InsertSettingsAsync(testUser.Id);

        await _userDataRepository.DeleteSettingsAsync(testUser.Id);

        (await CountRowsAsync("Snapshots", testUser.Id)).Should().Be(1);
        (await CountRowsAsync("FsItemFlags", testUser.Id)).Should().Be(1);
        (await CountRowsAsync("FsItemRatings", testUser.Id)).Should().Be(1);
        (await CountRowsAsync("Tags", testUser.Id)).Should().Be(1);
    }

    [Fact]
    public async Task DeleteSettingsAsync_WhenThereAreNone_AnswersZeroWithoutFailing()
    {
        var deletedRows = await _userDataRepository.DeleteSettingsAsync(testUser.Id);

        deletedRows.Should().Be(0);
    }

    #endregion

    #region Helpers

    private async Task<long> CountRowsAsync(string table, Ulid userId) =>
        await _sqLiteConnection.ExecuteScalarAsync<long>(
            $"SELECT COUNT(*) FROM {table} WHERE UserId = @UserId", new { UserId = userId });

    private async Task<long> CountAllRowsAsync(string table) =>
        await _sqLiteConnection.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM {table}");

    private async Task<Ulid> InsertLabelAsync(Ulid userId)
    {
        var labelId = Ulid.NewUlid();
        await _sqLiteConnection.ExecuteAsync(
            "INSERT INTO Labels (Id, Name, ColorHex, UserId) VALUES (@Id, @Name, @ColorHex, @UserId)",
            new { Id = labelId, Name = $"label-{labelId}", ColorHex = "#ff0000", UserId = userId });

        return labelId;
    }

    private async Task<Ulid> InsertTagAsync(Ulid userId)
    {
        var tagId = Ulid.NewUlid();
        await _sqLiteConnection.ExecuteAsync(
            "INSERT INTO Tags (Id, Name, ColorHex, UserId) VALUES (@Id, @Name, @ColorHex, @UserId)",
            new { Id = tagId, Name = $"tag-{tagId}", ColorHex = "#00ff00", UserId = userId });

        return tagId;
    }

    private Task InsertFlagAsync(Ulid userId, string fullPath) =>
        _sqLiteConnection.ExecuteAsync(
            "INSERT INTO FsItemFlags (Id, FullPath, UserId) VALUES (@Id, @FullPath, @UserId)",
            new { Id = Ulid.NewUlid(), FullPath = fullPath, UserId = userId });

    private Task InsertRatingAsync(Ulid userId, string fullPath, int rating) =>
        _sqLiteConnection.ExecuteAsync(
            "INSERT INTO FsItemRatings (Id, FullPath, Rating, UserId) VALUES (@Id, @FullPath, @Rating, @UserId)",
            new { Id = Ulid.NewUlid(), FullPath = fullPath, Rating = rating, UserId = userId });

    /// <summary>One flag, one rating, one tag with one assignment.</summary>
    private async Task InsertFileSystemDataAsync(Ulid userId)
    {
        var fullPath = $"/data/{userId}/report.pdf";
        await InsertFlagAsync(userId, fullPath);
        await InsertRatingAsync(userId, fullPath, 5);
        var tagId = await InsertTagAsync(userId);
        await _sqLiteConnection.ExecuteAsync(
            "INSERT INTO FsItemTags (TagId, FullPath) VALUES (@TagId, @FullPath)",
            new { TagId = tagId, FullPath = fullPath });
    }

    /// <summary>A theme, one rebound shortcut, one locked folder tab.</summary>
    private async Task InsertSettingsAsync(Ulid userId)
    {
        await _sqLiteConnection.ExecuteAsync(
            SqlScripts.UpsertUserSettingsSql,
            new { Id = Ulid.NewUlid(), Theme = UiThemes.Light, UserId = userId });
        await _sqLiteConnection.ExecuteAsync(
            SqlScripts.UpsertUserKeyBindingSql,
            new { Id = Ulid.NewUlid(), ActionId = KeyBindingActions.Copy, PrimaryKey = "F9", SecondaryKey = "", UserId = userId });
        await _sqLiteConnection.ExecuteAsync(
            "INSERT INTO FolderTabs (Id, PanelId, FolderPath, Position, UserId) VALUES (@Id, @PanelId, @FolderPath, 0, @UserId)",
            new { Id = Ulid.NewUlid(), PanelId = "left", FolderPath = $"/data/{userId}", UserId = userId });
    }

    /// <summary>
    /// A snapshot with its own machine identity and three files under the root,
    /// the same shape SnapshotRepositoryIntegrationTests builds.
    /// </summary>
    private static SnapshotEntity CreateSnapshotWithFiles(Ulid userId)
    {
        var snapshot = new SnapshotEntity { Description = "Danger zone test snapshot", UserId = userId, User = null! };
        var pc = new PcEntity { Name = "TestPC", UserId = userId, User = null! };
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
        var volumeInfo = new VolumeInfoEntity { FreeSpace = 250000, DriveStatus = "OK", UserId = userId, User = null! };
        var rootFolder = new FolderEntity { Name = "Root", Size = 0, Sha256Hash = $"root-{Ulid.NewUlid()}", UserId = userId, User = null! };

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

        foreach (var (name, extension, size) in new[] { ("file1", ".txt", 100L), ("file2", ".pdf", 200L), ("file3", ".jpg", 300L) })
        {
            var file = new FileEntity
            {
                Name = name,
                FileExtension = extension,
                Size = size,
                Sha256Hash = $"{name}-{Ulid.NewUlid()}",
                UserId = userId,
                User = null!
            };
            rootFolder.Files.Add(file);
            file.ParentFolders.Add(rootFolder);
        }

        return snapshot;
    }

    #endregion
}
