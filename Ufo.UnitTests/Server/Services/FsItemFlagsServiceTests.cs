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

public class FsItemFlagsServiceTests : BaseTest, IDisposable
{
    private readonly Mock<ILogger<FsItemFlagsService>> _loggerMock = new();
    private readonly Mock<IFsItemFlagsRepository> _repositoryMock = new();
    private readonly Ulid _userId = Ulid.NewUlid();
    private readonly string _testRoot;
    private readonly string _allowedFolder;
    private readonly string _outsideFolder;

    public FsItemFlagsServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"ufo-flags-{Guid.NewGuid():N}");
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

    private FsItemFlagsService CreateSut(params string[] allowedRoots)
    {
        var pathGuard = new PathGuard(
            new Mock<ILogger<PathGuard>>().Object,
            Options.Create(new UfoHostOptions { AllowedRoots = allowedRoots }));

        return new FsItemFlagsService(_repositoryMock.Object, pathGuard, _loggerMock.Object);
    }

    private void GivenFlagged(params string[] paths) =>
        _repositoryMock
            .Setup(repository => repository.GetFsItemFlagsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(paths.Select(path => new FsItemFlagEntity { FullPath = path, UserId = _userId }).ToList());

    /// <summary>Captures the paths and direction a save would have written.</summary>
    private List<(string Path, bool IsFlagEnabled)> CaptureWrites()
    {
        var writes = new List<(string, bool)>();

        _repositoryMock
            .Setup(repository => repository.SetFsItemFlagsAsync(
                _userId,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Callback<Ulid, IReadOnlyList<string>, bool, CancellationToken>(
                (_, paths, isFlagEnabled, _) => writes.AddRange(paths.Select(path => (path, isFlagEnabled))))
            .ReturnsAsync(new ServerResult { Result = Result.Success });

        return writes;
    }

    private static FsItemFlagsRequest RequestFor(bool isFlagEnabled, params string[] paths) =>
        new() { FullPaths = paths, IsFlagEnabled = isFlagEnabled };

    [Fact]
    public async Task GetFlaggedPathsAsync_AnswersWithNothingWhenNoneAreFlagged()
    {
        GivenFlagged();

        (await CreateSut().GetFlaggedPathsAsync(_userId, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetFlaggedPathsAsync_LeavesOutAPathTheServerMayNoLongerRead()
    {
        // The allow-list is configuration and can be tightened between sessions.
        GivenFlagged(_allowedFolder, _outsideFolder);

        var flagged = await CreateSut(_allowedFolder).GetFlaggedPathsAsync(_userId, CancellationToken.None);

        flagged.Should().ContainSingle().Which.Should().Be(_allowedFolder);
    }

    [Fact]
    public async Task SetFlagsAsync_FlagsEveryPathItWasGiven()
    {
        var writes = CaptureWrites();
        var secondFolder = Path.Combine(_testRoot, "second");
        Directory.CreateDirectory(secondFolder);

        var result = await CreateSut().SetFlagsAsync(
            RequestFor(true, _allowedFolder, secondFolder),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Success);
        writes.Should().HaveCount(2);
        writes.Should().OnlyContain(write => write.IsFlagEnabled);
    }

    [Fact]
    public async Task SetFlagsAsync_RefusesToFlagSomethingOutsideTheAllowedRoots()
    {
        CaptureWrites();

        var result = await CreateSut(_allowedFolder).SetFlagsAsync(
            RequestFor(true, _outsideFolder),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Error);

        _repositoryMock.Verify(
            repository => repository.SetFsItemFlagsAsync(
                It.IsAny<Ulid>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SetFlagsAsync_StillClearsAFlagOnSomethingOutsideTheAllowedRoots()
    {
        // Clearing is the way out of a flag whose path has left the allow-list.
        // Refusing would leave the user a flag they can neither see nor remove.
        var writes = CaptureWrites();

        var result = await CreateSut(_allowedFolder).SetFlagsAsync(
            RequestFor(false, _outsideFolder),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Success);
        writes.Should().Contain(write => write.Path == _outsideFolder && !write.IsFlagEnabled);
    }

    [Fact]
    public async Task SetFlagsAsync_KeysTheRowOnThePathTheListingProduced()
    {
        // Listings and the snapshot walk hand back what enumeration gave them -
        // never symlink-resolved. Storing the guard's resolved answer would key
        // the row on a path nothing else ever produces, so the marker would never
        // appear and the flag could never be found again.
        var linkPath = Path.Combine(_testRoot, "shortcut");

        if (!TryCreateDirectorySymbolicLink(linkPath, _allowedFolder))
        {
            // Unprivileged Windows without developer mode; nothing to assert.
            return;
        }

        var writes = CaptureWrites();

        var result = await CreateSut().SetFlagsAsync(RequestFor(true, linkPath), _userId, CancellationToken.None);

        result.Result.Should().Be(Result.Success);
        writes.Should().ContainSingle().Which.Path.Should().Be(linkPath);
    }

    [Fact]
    public async Task SetFlagsAsync_KeepsANameThatEndsInASpace()
    {
        // The guard trims before it answers. Harmless when its answer was only
        // ever an authorisation; not once the path became the key, because the
        // trimmed form names a different file - or none at all.
        var awkwardName = Path.Combine(_testRoot, "trailing ");
        var writes = CaptureWrites();

        var result = await CreateSut().SetFlagsAsync(RequestFor(true, awkwardName), _userId, CancellationToken.None);

        result.Result.Should().Be(Result.Success);
        writes.Should().ContainSingle().Which.Path.Should().Be(awkwardName);
    }

    [Fact]
    public async Task SetFlagsAsync_RefusesAPathLongerThanItWillStore()
    {
        CaptureWrites();

        // The entity's [MaxLength] is a sqlite-net attribute the raw SQL never
        // consults, and the column is plain TEXT - so the limit has to be here.
        var overlongPath = Path.Combine(_testRoot, new string('a', 5000));

        var result = await CreateSut().SetFlagsAsync(
            RequestFor(true, overlongPath),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Error);
    }

    private static bool TryCreateDirectorySymbolicLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception exception)
            when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    [Fact]
    public async Task SetFlagsAsync_RefusesAnEmptyRequest()
    {
        CaptureWrites();

        var result = await CreateSut().SetFlagsAsync(RequestFor(true), _userId, CancellationToken.None);

        result.Result.Should().Be(Result.Error);
    }

    [Fact]
    public async Task SetFlagsAsync_RefusesMoreThanOneRequestMayName()
    {
        CaptureWrites();

        var manyPaths = Enumerable.Range(0, 1001).Select(_ => _allowedFolder).ToArray();

        var result = await CreateSut().SetFlagsAsync(
            RequestFor(true, manyPaths),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Error);
    }
}
