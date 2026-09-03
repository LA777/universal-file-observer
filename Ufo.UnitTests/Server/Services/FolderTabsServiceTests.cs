using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Ufo.Abstractions;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.Options;
using Ufo.Abstractions.Requests;
using Ufo.Server.Services;

namespace Ufo.UnitTests.Server.Services;

public class FolderTabsServiceTests : BaseTest, IDisposable
{
    private readonly Mock<ILogger<FolderTabsService>> _loggerMock = new();
    private readonly Mock<IFolderTabsRepository> _repositoryMock = new();
    private readonly Ulid _userId = Ulid.NewUlid();
    private readonly string _testRoot;
    private readonly string _allowedFolder;
    private readonly string _outsideFolder;

    public FolderTabsServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"ufo-tabs-{Guid.NewGuid():N}");
        _allowedFolder = Path.Combine(_testRoot, "library");
        _outsideFolder = Path.Combine(_testRoot, "secrets");

        Directory.CreateDirectory(_allowedFolder);
        Directory.CreateDirectory(_outsideFolder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private FolderTabsService CreateSut(params string[] allowedRoots)
    {
        var pathGuard = new PathGuard(
            new Mock<ILogger<PathGuard>>().Object,
            Options.Create(new UfoHostOptions { AllowedRoots = allowedRoots }));

        return new FolderTabsService(_repositoryMock.Object, pathGuard, _loggerMock.Object);
    }

    private void GivenSavedTabs(params FolderTabEntity[] tabs) =>
        _repositoryMock
            .Setup(repository => repository.GetFolderTabsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tabs);

    private List<FolderTabEntity> CaptureSavedTabs()
    {
        var savedTabs = new List<FolderTabEntity>();

        _repositoryMock
            .Setup(repository => repository.SaveFolderTabsAsync(
                It.IsAny<IReadOnlyList<FolderTabEntity>>(),
                _userId,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<FolderTabEntity>, Ulid, string, CancellationToken>(
                (tabs, _, _, _) => savedTabs.AddRange(tabs))
            .ReturnsAsync(new ServerResult { Result = Result.Success });

        return savedTabs;
    }

    private static FolderTabsRequest RequestFor(string panelId, params string[] folderPaths) =>
        new() { PanelId = panelId, FolderPaths = folderPaths };

    #region Reading

    [Fact]
    public async Task GetFolderTabsAsync_AnswersWithNothingWhenNoneAreLocked()
    {
        GivenSavedTabs();

        var folderTabs = await CreateSut().GetFolderTabsAsync(_userId, CancellationToken.None);

        folderTabs.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFolderTabsAsync_LeavesOutAFolderTheServerMayNoLongerRead()
    {
        // A tab locked while the server was unrestricted must not come back and
        // hand the user a folder outside the roots it is now confined to. The
        // allow-list is configuration and can be tightened between sessions.
        GivenSavedTabs(
            new FolderTabEntity { PanelId = "left", FolderPath = _allowedFolder, Position = 0, UserId = _userId },
            new FolderTabEntity { PanelId = "left", FolderPath = _outsideFolder, Position = 1, UserId = _userId });

        var folderTabs = await CreateSut(_allowedFolder).GetFolderTabsAsync(_userId, CancellationToken.None);

        folderTabs.Should().ContainSingle()
            .Which.FolderPath.Should().Be(_allowedFolder);
    }

    #endregion

    #region Saving

    [Fact]
    public async Task SaveFolderTabsAsync_KeepsTheOrderItWasGiven()
    {
        var savedTabs = CaptureSavedTabs();
        var secondFolder = Path.Combine(_testRoot, "second");
        Directory.CreateDirectory(secondFolder);

        var result = await CreateSut().SaveFolderTabsAsync(
            RequestFor("left", secondFolder, _allowedFolder),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Success);
        savedTabs.Select(tab => tab.FolderPath).Should().Equal(secondFolder, _allowedFolder);
        savedTabs.Select(tab => tab.Position).Should().Equal(0, 1);
    }

    [Fact]
    public async Task SaveFolderTabsAsync_AcceptsAnEmptyListAsUnlockingTheLastTab()
    {
        var savedTabs = CaptureSavedTabs();

        var result = await CreateSut().SaveFolderTabsAsync(
            RequestFor("left"),
            _userId,
            CancellationToken.None);

        // Sent rather than skipped: it is the only way to say "keep nothing".
        result.Result.Should().Be(Result.Success);
        savedTabs.Should().BeEmpty();

        _repositoryMock.Verify(
            repository => repository.SaveFolderTabsAsync(
                It.IsAny<IReadOnlyList<FolderTabEntity>>(),
                _userId,
                "left",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SaveFolderTabsAsync_DropsTheSameFolderTwice()
    {
        var savedTabs = CaptureSavedTabs();

        var result = await CreateSut().SaveFolderTabsAsync(
            RequestFor("left", _allowedFolder, _allowedFolder),
            _userId,
            CancellationToken.None);

        // A duplicate, not a mistake worth failing the whole save for.
        result.Result.Should().Be(Result.Success);
        savedTabs.Should().ContainSingle();
    }

    [Fact]
    public async Task SaveFolderTabsAsync_RefusesAFolderOutsideTheAllowedRoots()
    {
        CaptureSavedTabs();

        var result = await CreateSut(_allowedFolder).SaveFolderTabsAsync(
            RequestFor("left", _outsideFolder),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Error);

        _repositoryMock.Verify(
            repository => repository.SaveFolderTabsAsync(
                It.IsAny<IReadOnlyList<FolderTabEntity>>(),
                It.IsAny<Ulid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SaveFolderTabsAsync_RefusesAFolderThatIsNotThere()
    {
        CaptureSavedTabs();

        var result = await CreateSut().SaveFolderTabsAsync(
            RequestFor("left", Path.Combine(_testRoot, "never-existed")),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Error);
    }

    [Fact]
    public async Task SaveFolderTabsAsync_RefusesAPanelThatDoesNotExist()
    {
        CaptureSavedTabs();

        var result = await CreateSut().SaveFolderTabsAsync(
            RequestFor("middle", _allowedFolder),
            _userId,
            CancellationToken.None);

        // A row for a panel nothing renders is a row nothing would ever restore.
        result.Result.Should().Be(Result.Error);
        result.Message.Should().Contain("middle");
    }

    [Fact]
    public async Task SaveFolderTabsAsync_RefusesMoreTabsThanAPanelWillKeep()
    {
        CaptureSavedTabs();

        var manyPaths = Enumerable.Range(0, 51).Select(_ => _allowedFolder).ToArray();

        var result = await CreateSut().SaveFolderTabsAsync(
            RequestFor("left", manyPaths),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Error);
    }

    [Fact]
    public async Task SaveFolderTabsAsync_TouchesOnlyThePanelItWasGiven()
    {
        CaptureSavedTabs();

        await CreateSut().SaveFolderTabsAsync(
            RequestFor("right", _allowedFolder),
            _userId,
            CancellationToken.None);

        // The two panes save independently. A whole-account replace would have
        // each one deleting the other's tabs every time it saved.
        _repositoryMock.Verify(
            repository => repository.SaveFolderTabsAsync(
                It.IsAny<IReadOnlyList<FolderTabEntity>>(),
                _userId,
                "right",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion
}
