using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Ufo.Abstractions;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.DataTransferObjects;
using Ufo.Server.Services;

namespace Ufo.UnitTests.Server.Services;

public class ExportServiceTests : BaseTest
{
    private static readonly DateTimeOffset ExportMoment = new(2026, 9, 13, 17, 3, 11, TimeSpan.Zero);

    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IUserSettingsService> _userSettingsServiceMock = new();
    private readonly Mock<IKeyBindingsService> _keyBindingsServiceMock = new();
    private readonly Mock<IFolderTabsRepository> _folderTabsRepositoryMock = new();
    private readonly Mock<IFsItemFlagsService> _fsItemFlagsServiceMock = new();
    private readonly Mock<IFsItemRatingsService> _fsItemRatingsServiceMock = new();
    private readonly Mock<ITagsService> _tagsServiceMock = new();
    private readonly Mock<ILabelsService> _labelsServiceMock = new();
    private readonly Mock<ISnapshotRepository> _snapshotRepositoryMock = new();
    private readonly Mock<IApplicationVersionService> _applicationVersionServiceMock = new();
    private readonly Ulid _userId = Ulid.NewUlid();
    private readonly Ulid _snapshotId = Ulid.NewUlid();

    public ExportServiceTests()
    {
        _applicationVersionServiceMock.SetupGet(service => service.Version).Returns("1.2.3");

        _userRepositoryMock
            .Setup(repository => repository.GetUserByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserEntity { Id = _userId, Name = "alex", CreatedAt = "2026-01-01T00:00:00Z", IsAdmin = true, PasswordHash = "secret" });
        _userSettingsServiceMock
            .Setup(service => service.GetUserSettingsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSettingsDto { UserId = _userId, Theme = UiThemes.Light });
        _keyBindingsServiceMock
            .Setup(service => service.GetKeyBindingsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new KeyBindingDto { ActionId = KeyBindingActions.Copy, PrimaryKey = "F9", DefaultPrimaryKey = "F5" }]);
        _folderTabsRepositoryMock
            .Setup(repository => repository.GetFolderTabsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new FolderTabEntity { PanelId = "left", FolderPath = "/data", Position = 0, UserId = _userId }]);

        _fsItemFlagsServiceMock
            .Setup(service => service.GetFlaggedPathsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(["/data/report.pdf"]);
        _fsItemRatingsServiceMock
            .Setup(service => service.GetRatingsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int> { ["/data/report.pdf"] = 7 });
        var tagId = Ulid.NewUlid();
        _tagsServiceMock
            .Setup(service => service.GetFsItemTagsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FsItemTagsDto
            {
                Tags = [new TagDto { Id = tagId, Name = "Important", ColorHex = "#00ff00" }],
                TagIdsByPath = new Dictionary<string, List<Ulid>> { ["/data/report.pdf"] = [tagId] }
            });

        _labelsServiceMock
            .Setup(service => service.GetAllLabelsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LabelDto { Id = Ulid.NewUlid(), Name = "keep", ColorHex = "#ff0000", UserId = _userId }]);
        _snapshotRepositoryMock
            .Setup(repository => repository.GetAllSnapshotsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SnapshotEntity { Id = _snapshotId, UserId = _userId, User = null! }]);
        _snapshotRepositoryMock
            .Setup(repository => repository.GetSnapshotByIdAsync(_snapshotId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SnapshotEntity
            {
                Id = _snapshotId,
                UserId = _userId,
                User = null!,
                Description = "full",
                RootFolder = new FolderEntity { Name = "Root", Sha256Hash = "abc", UserId = _userId, User = null! }
            });
    }

    private ExportService CreateSut() => new(
        _userRepositoryMock.Object,
        _userSettingsServiceMock.Object,
        _keyBindingsServiceMock.Object,
        _folderTabsRepositoryMock.Object,
        _fsItemFlagsServiceMock.Object,
        _fsItemRatingsServiceMock.Object,
        _tagsServiceMock.Object,
        _labelsServiceMock.Object,
        _snapshotRepositoryMock.Object,
        _applicationVersionServiceMock.Object,
        new FixedTimeProvider(ExportMoment),
        Mock.Of<ILogger<ExportService>>());

    [Theory]
    [InlineData(ExportScope.User, "user")]
    [InlineData(ExportScope.FileSystem, "filesystem")]
    [InlineData(ExportScope.Snapshots, "snapshots")]
    [InlineData(ExportScope.All, "all")]
    public async Task ExportAsync_NamesTheArchiveAfterTheScopeAndTheMoment(ExportScope scope, string scopeName)
    {
        var archive = await CreateSut().ExportAsync(scope, _userId, CancellationToken.None);

        archive.FileName.Should().Be($"ufo-export-{scopeName}-20260913-170311.zip");
        EntryName(archive).Should().Be($"ufo-export-{scopeName}-20260913-170311.json");
    }

    [Fact]
    public async Task ExportAsync_StampsTheHeaderWithFormatVersionBuildAndMoment()
    {
        var document = await ExportDocumentAsync(ExportScope.User);

        document.Format.Should().Be(ExportDocument.FormatName);
        document.FormatVersion.Should().Be(ExportDocument.CurrentFormatVersion);
        document.ApplicationVersion.Should().Be("1.2.3");
        document.ExportedAt.Should().Be(ExportMoment);
        document.Scope.Should().Be("user");
    }

    [Fact]
    public async Task ExportAsync_ForUser_CarriesTheAccountAndItsSettingsButNeverThePasswordHash()
    {
        var archive = await CreateSut().ExportAsync(ExportScope.User, _userId, CancellationToken.None);
        var document = Read(archive);

        document.User.Should().NotBeNull();
        document.User!.Id.Should().Be(_userId);
        document.User.Name.Should().Be("alex");
        document.User.IsAdmin.Should().BeTrue();
        document.User.Settings.Theme.Should().Be(UiThemes.Light);
        document.User.KeyBindings.Should().ContainSingle().Which.PrimaryKey.Should().Be("F9");
        document.User.FolderTabs.Should().ContainSingle().Which.FolderPath.Should().Be("/data");
        ReadJson(archive).Should().NotContain("secret").And.NotContain("passwordHash", "the hash is not the user's to carry around");
        document.FileSystem.Should().BeNull();
        document.Snapshots.Should().BeNull();
    }

    [Fact]
    public async Task ExportAsync_ForFileSystem_CarriesFlagsRatingsAndTagsOnly()
    {
        var document = await ExportDocumentAsync(ExportScope.FileSystem);

        document.FileSystem.Should().NotBeNull();
        document.FileSystem!.FlaggedPaths.Should().Equal("/data/report.pdf");
        document.FileSystem.Ratings.Should().ContainKey("/data/report.pdf").WhoseValue.Should().Be(7);
        document.FileSystem.Tags.Should().ContainSingle().Which.Name.Should().Be("Important");
        document.FileSystem.TagIdsByPath.Should().ContainKey("/data/report.pdf");
        document.User.Should().BeNull();
        document.Snapshots.Should().BeNull();
        _snapshotRepositoryMock.Verify(
            repository => repository.GetAllSnapshotsAsync(It.IsAny<Ulid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExportAsync_ForSnapshots_ReadsEachSnapshotWholeAndCarriesTheLabels()
    {
        var document = await ExportDocumentAsync(ExportScope.Snapshots);

        document.Snapshots.Should().NotBeNull();
        document.Snapshots!.Labels.Should().ContainSingle().Which.Name.Should().Be("keep");
        // The tree comes from the by-id read, not the list query that stops at the root.
        var snapshot = document.Snapshots.Snapshots.Should().ContainSingle().Subject;
        snapshot.Id.Should().Be(_snapshotId);
        snapshot.Description.Should().Be("full");
        snapshot.RootFolder.Should().NotBeNull();
        _snapshotRepositoryMock.Verify(
            repository => repository.GetSnapshotByIdAsync(_snapshotId, _userId, It.IsAny<CancellationToken>()), Times.Once);
        document.User.Should().BeNull();
        document.FileSystem.Should().BeNull();
    }

    [Fact]
    public async Task ExportAsync_ForAll_CarriesEverySection()
    {
        var document = await ExportDocumentAsync(ExportScope.All);

        document.User.Should().NotBeNull();
        document.FileSystem.Should().NotBeNull();
        document.Snapshots.Should().NotBeNull();
    }

    [Fact]
    public async Task ExportAsync_WithNothingToExport_WritesEmptySectionsRatherThanOmittingThem()
    {
        _fsItemFlagsServiceMock
            .Setup(service => service.GetFlaggedPathsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _fsItemRatingsServiceMock
            .Setup(service => service.GetRatingsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int>());
        _tagsServiceMock
            .Setup(service => service.GetFsItemTagsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FsItemTagsDto());
        _labelsServiceMock
            .Setup(service => service.GetAllLabelsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _snapshotRepositoryMock
            .Setup(repository => repository.GetAllSnapshotsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var document = await ExportDocumentAsync(ExportScope.All);

        // A file that says "no snapshots" is an answer; a file with no section is a question.
        document.FileSystem.Should().NotBeNull();
        document.FileSystem!.FlaggedPaths.Should().BeEmpty();
        document.Snapshots.Should().NotBeNull();
        document.Snapshots!.Snapshots.Should().BeEmpty();
    }

    [Fact]
    public async Task ExportAsync_SkipsASnapshotThatVanishesBetweenTheListAndTheRead()
    {
        _snapshotRepositoryMock
            .Setup(repository => repository.GetSnapshotByIdAsync(_snapshotId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SnapshotEntity?)null);

        var document = await ExportDocumentAsync(ExportScope.Snapshots);

        document.Snapshots!.Snapshots.Should().BeEmpty();
    }

    [Fact]
    public async Task ExportAsync_ScopesEveryReadToTheCallingUser()
    {
        await CreateSut().ExportAsync(ExportScope.All, _userId, CancellationToken.None);

        _userRepositoryMock.Verify(repository => repository.GetUserByIdAsync(_userId, It.IsAny<CancellationToken>()), Times.Once);
        _fsItemFlagsServiceMock.Verify(service => service.GetFlaggedPathsAsync(_userId, It.IsAny<CancellationToken>()), Times.Once);
        _labelsServiceMock.Verify(service => service.GetAllLabelsAsync(_userId, It.IsAny<CancellationToken>()), Times.Once);
        _snapshotRepositoryMock.Verify(repository => repository.GetAllSnapshotsAsync(_userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    private async Task<ExportDocument> ExportDocumentAsync(ExportScope scope) =>
        Read(await CreateSut().ExportAsync(scope, _userId, CancellationToken.None));

    private static ExportDocument Read(ExportArchive archive) =>
        JsonSerializer.Deserialize<ExportDocument>(ReadJson(archive), ExportArchiveWriter.JsonOptions)!;

    private static string ReadJson(ExportArchive archive)
    {
        using var zip = new ZipArchive(new MemoryStream(archive.Content), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.Entries.Single().Open());

        return reader.ReadToEnd();
    }

    private static string EntryName(ExportArchive archive)
    {
        using var zip = new ZipArchive(new MemoryStream(archive.Content), ZipArchiveMode.Read);

        return zip.Entries.Single().FullName;
    }

    /// <summary>A clock that always answers the same moment, so file names are predictable.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
