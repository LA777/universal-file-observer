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

public class TagsServiceTests : BaseTest, IDisposable
{
    private readonly Mock<ILogger<TagsService>> _loggerMock = new();
    private readonly Mock<ITagsRepository> _repositoryMock = new();
    private readonly Ulid _userId = Ulid.NewUlid();
    private readonly string _testRoot;
    private readonly string _allowedFolder;
    private readonly string _outsideFolder;

    public TagsServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"ufo-tags-{Guid.NewGuid():N}");
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

    private TagsService CreateSut(params string[] allowedRoots)
    {
        var pathGuard = new PathGuard(
            new Mock<ILogger<PathGuard>>().Object,
            Options.Create(new UfoHostOptions { AllowedRoots = allowedRoots }));

        return new TagsService(_repositoryMock.Object, pathGuard, _loggerMock.Object);
    }

    private TagEntity GivenTag(string name, string colorHex = "#ff0000")
    {
        var tag = new TagEntity { Name = name, ColorHex = colorHex, UserId = _userId };

        _repositoryMock
            .Setup(repository => repository.GetTagsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([tag]);

        return tag;
    }

    private void GivenAssignments(params (Ulid TagId, string FullPath)[] assignments) =>
        _repositoryMock
            .Setup(repository => repository.GetFsItemTagsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignments
                .Select(assignment => new FsItemTagEntity
                {
                    TagId = assignment.TagId,
                    FullPath = assignment.FullPath
                })
                .ToList());

    /// <summary>Captures the paths and direction a tag write would have taken.</summary>
    private List<(string Path, bool IsApplied)> CaptureWrites()
    {
        var writes = new List<(string, bool)>();

        _repositoryMock
            .Setup(repository => repository.SetFsItemTagAsync(
                It.IsAny<Ulid>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Callback<Ulid, IReadOnlyList<string>, bool, CancellationToken>(
                (_, paths, isApplied, _) => writes.AddRange(paths.Select(path => (path, isApplied))))
            .ReturnsAsync(new ServerResult { Result = Result.Success });

        return writes;
    }

    #region Creating

    [Fact]
    public async Task CreateTagAsync_CreatesATag()
    {
        _repositoryMock
            .Setup(repository => repository.GetTagsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _repositoryMock
            .Setup(repository => repository.GetOrCreateTagAsync(It.IsAny<TagEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TagEntity tag, CancellationToken _) => tag);

        var tag = await CreateSut().CreateTagAsync(
            new CreateTagRequest { Name = "  Important  ", ColorHex = "#ff0000" },
            _userId,
            CancellationToken.None);

        tag.Should().NotBeNull();
        // Trimmed, because a tag whose name has edges nobody can see is one the
        // user can never type again.
        tag!.Name.Should().Be("Important");
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#f00")]
    [InlineData("#gggggg")]
    [InlineData("javascript:alert(1)")]
    public async Task CreateTagAsync_RefusesAColourThatIsNotOne(string colorHex)
    {
        // The colour is written straight into a style attribute, so anything that
        // is not six hex digits behind a hash has no business being there.
        var tag = await CreateSut().CreateTagAsync(
            new CreateTagRequest { Name = "Important", ColorHex = colorHex },
            _userId,
            CancellationToken.None);

        tag.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateTagAsync_RefusesATagWithNoName(string name)
    {
        var tag = await CreateSut().CreateTagAsync(
            new CreateTagRequest { Name = name, ColorHex = "#ff0000" },
            _userId,
            CancellationToken.None);

        tag.Should().BeNull();
    }

    #endregion

    #region Reading

    [Fact]
    public async Task GetFsItemTagsAsync_SendsTheVocabularyWithTheAssignments()
    {
        var tag = GivenTag("Important");
        GivenAssignments((tag.Id, _allowedFolder));

        var result = await CreateSut().GetFsItemTagsAsync(_userId, CancellationToken.None);

        // An assignment is a tag id, and an id without a name and colour cannot
        // be drawn - so the two travel together or neither is any use.
        result.Tags.Should().ContainSingle().Which.Name.Should().Be("Important");
        result.TagIdsByPath.Should().ContainKey(_allowedFolder);
    }

    [Fact]
    public async Task GetFsItemTagsAsync_KeepsSeveralTagsOnOneItem()
    {
        // The whole difference from a flag or a rating.
        var important = new TagEntity { Name = "Important", ColorHex = "#ff0000", UserId = _userId };
        var archive = new TagEntity { Name = "Archive", ColorHex = "#0000ff", UserId = _userId };

        _repositoryMock
            .Setup(repository => repository.GetTagsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([important, archive]);
        GivenAssignments((important.Id, _allowedFolder), (archive.Id, _allowedFolder));

        var result = await CreateSut().GetFsItemTagsAsync(_userId, CancellationToken.None);

        result.TagIdsByPath[_allowedFolder].Should().BeEquivalentTo([important.Id, archive.Id]);
    }

    [Fact]
    public async Task GetFsItemTagsAsync_LeavesOutAPathTheServerMayNoLongerRead()
    {
        var tag = GivenTag("Important");
        GivenAssignments((tag.Id, _allowedFolder), (tag.Id, _outsideFolder));

        var result = await CreateSut(_allowedFolder).GetFsItemTagsAsync(_userId, CancellationToken.None);

        result.TagIdsByPath.Should().ContainSingle().Which.Key.Should().Be(_allowedFolder);
    }

    [Fact]
    public async Task GetTagsByPathAsync_GroupsTheTagsTheSnapshotWalkWillFreeze()
    {
        var tag = GivenTag("Important");
        GivenAssignments((tag.Id, _allowedFolder));

        var byPath = await CreateSut().GetTagsByPathAsync(_userId, CancellationToken.None);

        byPath.Should().ContainKey(_allowedFolder);
        byPath[_allowedFolder].Should().ContainSingle().Which.Name.Should().Be("Important");
    }

    #endregion

    #region Applying

    [Fact]
    public async Task SetFsItemTagAsync_AppliesToEveryPathItWasGiven()
    {
        var tag = GivenTag("Important");
        GivenAssignments();
        var writes = CaptureWrites();
        var secondFolder = Path.Combine(_testRoot, "second");
        Directory.CreateDirectory(secondFolder);

        var result = await CreateSut().SetFsItemTagAsync(
            new SetFsItemTagRequest { TagId = tag.Id, FullPaths = [_allowedFolder, secondFolder], IsApplied = true },
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Success);
        writes.Should().HaveCount(2);
        writes.Should().OnlyContain(write => write.IsApplied);
    }

    [Fact]
    public async Task SetFsItemTagAsync_RefusesATagThatIsNotTheUsersOwn()
    {
        // Otherwise a caller could hang somebody else's tag on their own files
        // and read its name and colour back out of the listing.
        GivenTag("Important");
        GivenAssignments();
        CaptureWrites();

        var result = await CreateSut().SetFsItemTagAsync(
            new SetFsItemTagRequest { TagId = Ulid.NewUlid(), FullPaths = [_allowedFolder], IsApplied = true },
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Error);
        result.Message.Should().Contain("not one of your tags");

        _repositoryMock.Verify(
            repository => repository.SetFsItemTagAsync(
                It.IsAny<Ulid>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SetFsItemTagAsync_RefusesToTagSomethingOutsideTheAllowedRoots()
    {
        var tag = GivenTag("Important");
        GivenAssignments();
        CaptureWrites();

        var result = await CreateSut(_allowedFolder).SetFsItemTagAsync(
            new SetFsItemTagRequest { TagId = tag.Id, FullPaths = [_outsideFolder], IsApplied = true },
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Error);
    }

    [Fact]
    public async Task SetFsItemTagAsync_StillRemovesATagFromSomethingOutsideTheAllowedRoots()
    {
        // Taking a tag off is the way out of one on a path that has left the
        // allow-list. Refusing would leave tags that cannot be removed.
        var tag = GivenTag("Important");
        GivenAssignments();
        var writes = CaptureWrites();

        var result = await CreateSut(_allowedFolder).SetFsItemTagAsync(
            new SetFsItemTagRequest { TagId = tag.Id, FullPaths = [_outsideFolder], IsApplied = false },
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Success);
        writes.Should().Contain(write => write.Path == _outsideFolder && !write.IsApplied);
    }

    [Fact]
    public async Task SetFsItemTagAsync_KeysTheRowOnThePathTheListingProduced()
    {
        // The trap flags and ratings were both fixed for: the guard's answer is
        // trimmed and link-resolved, and nothing else resolves anything.
        var tag = GivenTag("Important");
        GivenAssignments();
        var awkwardName = Path.Combine(_testRoot, "trailing ");
        var writes = CaptureWrites();

        var result = await CreateSut().SetFsItemTagAsync(
            new SetFsItemTagRequest { TagId = tag.Id, FullPaths = [awkwardName], IsApplied = true },
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Success);
        writes.Should().ContainSingle().Which.Path.Should().Be(awkwardName);
    }

    [Fact]
    public async Task SetFsItemTagAsync_RefusesAnEmptyRequest()
    {
        var tag = GivenTag("Important");
        GivenAssignments();
        CaptureWrites();

        var result = await CreateSut().SetFsItemTagAsync(
            new SetFsItemTagRequest { TagId = tag.Id, FullPaths = [], IsApplied = true },
            _userId,
            CancellationToken.None);

        result.Result.Should().Be(Result.Error);
    }

    #endregion
}
