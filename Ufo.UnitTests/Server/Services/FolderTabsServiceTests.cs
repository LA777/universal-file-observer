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

    private List<FolderTabEntity> CaptureLockedTabs()
    {
        var lockedTabs = new List<FolderTabEntity>();

        _repositoryMock
            .Setup(repository => repository.LockFolderTabAsync(
                It.IsAny<FolderTabEntity>(),
                It.IsAny<CancellationToken>()))
            .Callback<FolderTabEntity, CancellationToken>((tab, _) => lockedTabs.Add(tab))
            .ReturnsAsync(new ServerResult { Result = Result.Success });

        return lockedTabs;
    }

    private List<string> CaptureUnlockedPaths()
    {
        var unlockedPaths = new List<string>();

        _repositoryMock
            .Setup(repository => repository.UnlockFolderTabAsync(
                It.IsAny<Ulid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<Ulid, string, string, CancellationToken>((_, _, path, _) => unlockedPaths.Add(path))
            .ReturnsAsync(new ServerResult { Result = Result.Success });

        return unlockedPaths;
    }

    private static FolderTabRequest RequestFor(string panelId, string folderPath) =>
        new() { PanelId = panelId, FolderPath = folderPath };

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

    [Fact]
    public async Task GetFolderTabsAsync_KeepsATabWhoseFolderIsMissingButFlagsIt()
    {
        var missingFolder = Path.Combine(_testRoot, "unplugged");

        GivenSavedTabs(
            new FolderTabEntity { PanelId = "left", FolderPath = _allowedFolder, Position = 0, UserId = _userId },
            new FolderTabEntity { PanelId = "left", FolderPath = missingFolder, Position = 1, UserId = _userId });

        var folderTabs = await CreateSut().GetFolderTabsAsync(_userId, CancellationToken.None);

        // Kept, because a missing folder is usually a drive that is not plugged
        // in - dropping the tab would throw away a pin set on purpose. Flagged,
        // so the strip can show it as unavailable and not open it on arrival,
        // which is what made every login begin with an error.
        folderTabs.Should().HaveCount(2);
        folderTabs.Single(tab => tab.FolderPath == _allowedFolder).IsAvailable.Should().BeTrue();
        folderTabs.Single(tab => tab.FolderPath == missingFolder).IsAvailable.Should().BeFalse();
    }

    #endregion

    #region Locking

    [Fact]
    public async Task LockFolderTabAsync_StoresTheResolvedFolder()
    {
        var lockedTabs = CaptureLockedTabs();

        var result = await CreateSut().LockFolderTabAsync(
            RequestFor("left", _allowedFolder),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Success);
        lockedTabs.Should().ContainSingle().Which.FolderPath.Should().Be(_allowedFolder);
    }

    [Fact]
    public async Task LockFolderTabAsync_TouchesOnlyTheTabItWasGiven()
    {
        CaptureLockedTabs();

        await CreateSut().LockFolderTabAsync(
            RequestFor("left", _allowedFolder),
            _userId,
            CancellationToken.None);

        // The whole point of one row at a time: locking must be incapable of
        // removing another tab, however wrong the caller is about the others.
        _repositoryMock.Verify(
            repository => repository.UnlockFolderTabAsync(
                It.IsAny<Ulid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LockFolderTabAsync_RefusesAFolderOutsideTheAllowedRoots()
    {
        CaptureLockedTabs();

        var result = await CreateSut(_allowedFolder).LockFolderTabAsync(
            RequestFor("left", _outsideFolder),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Error);

        _repositoryMock.Verify(
            repository => repository.LockFolderTabAsync(
                It.IsAny<FolderTabEntity>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LockFolderTabAsync_RefusesAFolderThatIsNotThere()
    {
        CaptureLockedTabs();

        var result = await CreateSut().LockFolderTabAsync(
            RequestFor("left", Path.Combine(_testRoot, "never-existed")),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Error);
    }

    [Fact]
    public async Task LockFolderTabAsync_RefusesAPanelThatDoesNotExist()
    {
        CaptureLockedTabs();

        var result = await CreateSut().LockFolderTabAsync(
            RequestFor("middle", _allowedFolder),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Error);
        result.Message.Should().Contain("middle");
    }

    #endregion

    #region Unlocking

    [Fact]
    public async Task UnlockFolderTabAsync_RemovesTheTab()
    {
        var unlockedPaths = CaptureUnlockedPaths();

        var result = await CreateSut().UnlockFolderTabAsync(
            RequestFor("left", _allowedFolder),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Success);
        unlockedPaths.Should().Contain(_allowedFolder);
    }

    [Fact]
    public async Task UnlockFolderTabAsync_WorksOnAFolderThatIsNoLongerThere()
    {
        // Unlocking is the way out of a tab whose folder has gone. Refusing
        // because the folder is unreachable would leave the user with a locked
        // tab they can neither close nor unlock.
        var unlockedPaths = CaptureUnlockedPaths();
        var missingFolder = Path.Combine(_testRoot, "unplugged");

        var result = await CreateSut().UnlockFolderTabAsync(
            RequestFor("left", missingFolder),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Success);
        unlockedPaths.Should().Contain(missingFolder);
    }

    [Fact]
    public async Task UnlockFolderTabAsync_WorksOnAFolderOutsideTheAllowedRoots()
    {
        // Same reasoning: the roots may have been tightened since the tab was
        // locked, and the row still has to be removable.
        var unlockedPaths = CaptureUnlockedPaths();

        var result = await CreateSut(_allowedFolder).UnlockFolderTabAsync(
            RequestFor("left", _outsideFolder),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Success);
        unlockedPaths.Should().Contain(_outsideFolder);
    }

    [Fact]
    public async Task UnlockFolderTabAsync_RefusesAPanelThatDoesNotExist()
    {
        CaptureUnlockedPaths();

        var result = await CreateSut().UnlockFolderTabAsync(
            RequestFor("middle", _allowedFolder),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Error);
    }

    #endregion
}
