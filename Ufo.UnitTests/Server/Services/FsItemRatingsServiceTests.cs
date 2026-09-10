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

public class FsItemRatingsServiceTests : BaseTest, IDisposable
{
    private readonly Mock<ILogger<FsItemRatingsService>> _loggerMock = new();
    private readonly Mock<IFsItemRatingsRepository> _repositoryMock = new();
    private readonly Ulid _userId = Ulid.NewUlid();
    private readonly string _testRoot;
    private readonly string _allowedFolder;
    private readonly string _outsideFolder;

    public FsItemRatingsServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"ufo-ratings-{Guid.NewGuid():N}");
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

    private FsItemRatingsService CreateSut(params string[] allowedRoots)
    {
        var pathGuard = new PathGuard(
            new Mock<ILogger<PathGuard>>().Object,
            Options.Create(new UfoHostOptions { AllowedRoots = allowedRoots }));

        return new FsItemRatingsService(_repositoryMock.Object, pathGuard, _loggerMock.Object);
    }

    private void GivenRated(params (string Path, int Rating)[] ratings) =>
        _repositoryMock
            .Setup(repository => repository.GetFsItemRatingsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ratings
                .Select(rating => new FsItemRatingEntity
                {
                    FullPath = rating.Path,
                    Rating = rating.Rating,
                    UserId = _userId
                })
                .ToList());

    /// <summary>Captures the paths and the rating a save would have written.</summary>
    private List<(string Path, int Rating)> CaptureWrites()
    {
        var writes = new List<(string, int)>();

        _repositoryMock
            .Setup(repository => repository.SetFsItemRatingsAsync(
                _userId,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<Ulid, IReadOnlyList<string>, int, CancellationToken>(
                (_, paths, rating, _) => writes.AddRange(paths.Select(path => (path, rating))))
            .ReturnsAsync(new ServerResult { Result = Result.Success });

        return writes;
    }

    private static FsItemRatingsRequest RequestFor(int rating, params string[] paths) =>
        new() { FullPaths = paths, Rating = rating };

    #region Reading

    [Fact]
    public async Task GetRatingsAsync_AnswersWithNothingWhenNothingIsRated()
    {
        GivenRated();

        (await CreateSut().GetRatingsAsync(_userId, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetRatingsAsync_KeysTheAnswerByPath()
    {
        GivenRated((_allowedFolder, 7));

        var ratings = await CreateSut().GetRatingsAsync(_userId, CancellationToken.None);

        ratings.Should().ContainKey(_allowedFolder).WhoseValue.Should().Be(7);
    }

    [Fact]
    public async Task GetRatingsAsync_LeavesOutAPathTheServerMayNoLongerRead()
    {
        // The allow-list is configuration and can be tightened between sessions.
        GivenRated((_allowedFolder, 5), (_outsideFolder, 9));

        var ratings = await CreateSut(_allowedFolder).GetRatingsAsync(_userId, CancellationToken.None);

        ratings.Should().ContainSingle().Which.Key.Should().Be(_allowedFolder);
    }

    [Fact]
    public async Task GetRatingsAsync_LetsTheMostRecentRatingWinACaseOnlyCollision()
    {
        // On a case-insensitive volume two rows can name what is really one file,
        // because the UNIQUE is byte-exact. The reader keys them case-insensitively
        // there, so one has to win - and it must be the same one every time rather
        // than whichever the database returned first. Ulid ids sort by creation
        // time, the select orders by Id, so the later row wins.
        var firstSpelling = Path.Combine(_testRoot, "Report.pdf");
        var secondSpelling = Path.Combine(_testRoot, "report.pdf");

        GivenRated((firstSpelling, 3), (secondSpelling, 9));

        var ratings = await CreateSut().GetRatingsAsync(_userId, CancellationToken.None);

        if (OperatingSystem.IsLinux())
        {
            // Two genuinely different files there, and both keep their rating.
            ratings.Should().HaveCount(2);
            return;
        }

        ratings.Should().ContainSingle().Which.Value.Should().Be(9);
    }

    #endregion

    #region Setting

    [Fact]
    public async Task SetRatingsAsync_RatesEveryPathItWasGiven()
    {
        var writes = CaptureWrites();
        var secondFolder = Path.Combine(_testRoot, "second");
        Directory.CreateDirectory(secondFolder);

        var result = await CreateSut().SetRatingsAsync(
            RequestFor(8, _allowedFolder, secondFolder),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Success);
        writes.Should().HaveCount(2);
        writes.Should().OnlyContain(write => write.Rating == 8);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public async Task SetRatingsAsync_AcceptsBothEndsOfTheScale(int rating)
    {
        var writes = CaptureWrites();

        var result = await CreateSut().SetRatingsAsync(
            RequestFor(rating, _allowedFolder),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Success);
        writes.Should().ContainSingle().Which.Rating.Should().Be(rating);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public async Task SetRatingsAsync_RefusesAValueOffTheScale(int rating)
    {
        CaptureWrites();

        var result = await CreateSut().SetRatingsAsync(
            RequestFor(rating, _allowedFolder),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Error);
        result.Message.Should().Contain("between 0 and 10");
    }

    [Fact]
    public async Task SetRatingsAsync_TreatsZeroAsClearingRatherThanAsAValue()
    {
        var writes = CaptureWrites();

        var result = await CreateSut().SetRatingsAsync(
            RequestFor(0, _allowedFolder),
            _userId,
            CancellationToken.None);

        // Zero is unrated, which is the absence of a row rather than a stored 0.
        result.Result.Should().Be(Result.Success);
        writes.Should().ContainSingle().Which.Rating.Should().Be(0);
    }

    [Fact]
    public async Task SetRatingsAsync_RefusesToRateSomethingOutsideTheAllowedRoots()
    {
        CaptureWrites();

        var result = await CreateSut(_allowedFolder).SetRatingsAsync(
            RequestFor(6, _outsideFolder),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Error);

        _repositoryMock.Verify(
            repository => repository.SetFsItemRatingsAsync(
                It.IsAny<Ulid>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SetRatingsAsync_StillClearsARatingOnSomethingOutsideTheAllowedRoots()
    {
        // Clearing is the way out of a rating whose path has left the allow-list.
        // Refusing would leave the user a rating they can neither see nor change.
        var writes = CaptureWrites();

        var result = await CreateSut(_allowedFolder).SetRatingsAsync(
            RequestFor(0, _outsideFolder),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Success);
        writes.Should().Contain(write => write.Path == _outsideFolder && write.Rating == 0);
    }

    [Fact]
    public async Task SetRatingsAsync_KeysTheRowOnThePathTheListingProduced()
    {
        // Listings and the snapshot walk hand back what enumeration gave them,
        // never symlink-resolved. Storing the guard's resolved answer would key
        // the row on a path nothing else produces, so the rating would never be
        // found again - the same trap the flags feature had to be fixed for.
        var linkPath = Path.Combine(_testRoot, "shortcut");

        if (!TryCreateDirectorySymbolicLink(linkPath, _allowedFolder))
        {
            // Unprivileged Windows without developer mode; nothing to assert.
            return;
        }

        var writes = CaptureWrites();

        var result = await CreateSut().SetRatingsAsync(
            RequestFor(4, linkPath),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Success);
        writes.Should().ContainSingle().Which.Path.Should().Be(linkPath);
    }

    [Fact]
    public async Task SetRatingsAsync_KeepsANameThatEndsInASpace()
    {
        // The guard trims before it answers, which would rate the trimmed
        // neighbour - or nothing at all.
        var awkwardName = Path.Combine(_testRoot, "trailing ");
        var writes = CaptureWrites();

        var result = await CreateSut().SetRatingsAsync(
            RequestFor(3, awkwardName),
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Success);
        writes.Should().ContainSingle().Which.Path.Should().Be(awkwardName);
    }

    [Fact]
    public async Task SetRatingsAsync_RefusesAnEmptyRequest()
    {
        CaptureWrites();

        var result = await CreateSut().SetRatingsAsync(RequestFor(5), _userId, CancellationToken.None);

        result.Result.Should().Be(Result.Error);
    }

    [Fact]
    public async Task SetRatingsAsync_RefusesAPathLongerThanItWillStore()
    {
        CaptureWrites();

        var overlongPath = Path.Combine(_testRoot, new string('a', 5000));

        var result = await CreateSut().SetRatingsAsync(
            RequestFor(5, overlongPath),
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

    #endregion
}
