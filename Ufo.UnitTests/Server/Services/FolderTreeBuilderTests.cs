using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Cryptography;
using System.Text;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Options;
using Ufo.Extensions;
using Ufo.Server.Services;

namespace Ufo.UnitTests.Server.Services;

public class FolderTreeBuilderTests : BaseTest, IDisposable
{
    private readonly Mock<ILogger<FolderTreeBuilder>> _loggerMock = new();

    /// <summary>
    /// These tests are about the walk itself, so they use a guard with no allow-list -
    /// the desktop configuration, which admits everything.
    /// </summary>
    private static PathGuard UnrestrictedPathGuard =>
        new(new Mock<ILogger<PathGuard>>().Object, Options.Create(new UfoHostOptions()));
    private readonly string _rootPath;
    private readonly UserEntity _user;
    private readonly SnapshotEntity _snapshot;

    public FolderTreeBuilderTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), $"ufo-tree-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootPath);

        _user = new UserEntity { Name = "tree-builder-user" };
        _snapshot = new SnapshotEntity { User = _user, UserId = _user.Id };
    }

    /// <summary>
    /// Most tests pin the parallelism so a failure is reproducible; the wide-tree test
    /// deliberately leaves it at the default instead.
    /// </summary>
    private FolderTreeBuilder CreateSut(int degreeOfParallelism = 4) =>
        new(_loggerMock.Object, UnrestrictedPathGuard, degreeOfParallelism);

    /// <summary>A builder configured the way a container runs: with an allow-list.</summary>
    private FolderTreeBuilder CreateRestrictedSut(params string[] allowedRoots) =>
        new(_loggerMock.Object,
            new PathGuard(new Mock<ILogger<PathGuard>>().Object, Options.Create(new UfoHostOptions { AllowedRoots = allowedRoots })),
            degreeOfParallelism: 4);

    private string WriteFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_rootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);

        return fullPath;
    }

    private static string ExpectedFileHash(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    /// <summary>
    /// Recomputes a folder's hash the way the production code documents it, so the test
    /// asserts against the rule rather than against a hard-coded digest.
    /// </summary>
    private static string ExpectedFolderHash(IEnumerable<(string NameWithExtension, string Hash)> files, IEnumerable<(string Name, string Hash)> subfolders)
    {
        var stringBuilder = new StringBuilder();

        foreach (var (nameWithExtension, hash) in files)
        {
            stringBuilder.AppendLine($"{nameWithExtension},{hash}");
        }

        foreach (var (name, hash) in subfolders)
        {
            stringBuilder.AppendLine($"{name},{hash}");
        }

        return stringBuilder.ToString().GetHashSha256();
    }

    [Fact]
    public async Task BuildAsync_HashesEveryFileWithSha256()
    {
        WriteFile("alpha.txt", "alpha-content");
        WriteFile("nested/beta.txt", "beta-content");
        var sut = CreateSut();

        var rootFolder = await sut.BuildAsync(_rootPath, _snapshot, _user);

        rootFolder.Files.Single().Sha256Hash.Should().Be(ExpectedFileHash("alpha-content"));
        rootFolder.ChildFolders.Single().Files.Single().Sha256Hash.Should().Be(ExpectedFileHash("beta-content"));
    }

    [Fact]
    public async Task BuildAsync_RecordsFileMetadata()
    {
        WriteFile("report.csv", "a,b,c");
        var sut = CreateSut();

        var rootFolder = await sut.BuildAsync(_rootPath, _snapshot, _user);

        var file = rootFolder.Files.Single();
        file.Name.Should().Be("report");
        file.FileExtension.Should().Be(".csv");
        file.Size.Should().Be(5);
        file.UserId.Should().Be(_user.Id);
        file.Snapshots.Should().ContainSingle().Which.Should().BeSameAs(_snapshot);
        file.ParentFolders.Should().ContainSingle().Which.Should().BeSameAs(rootFolder);
    }

    [Fact]
    public async Task BuildAsync_LinksFoldersToTheirParentAndSnapshot()
    {
        WriteFile("nested/deeper/leaf.txt", "leaf");
        var sut = CreateSut();

        var rootFolder = await sut.BuildAsync(_rootPath, _snapshot, _user);

        rootFolder.ParentFolders.Should().BeEmpty();
        rootFolder.Snapshots.Should().ContainSingle().Which.Should().BeSameAs(_snapshot);

        var nested = rootFolder.ChildFolders.Single();
        nested.Name.Should().Be("nested");
        nested.ParentFolders.Should().ContainSingle().Which.Should().BeSameAs(rootFolder);

        var deeper = nested.ChildFolders.Single();
        deeper.Name.Should().Be("deeper");
        deeper.ParentFolders.Should().ContainSingle().Which.Should().BeSameAs(nested);
        deeper.Files.Single().Name.Should().Be("leaf");
    }

    [Fact]
    public async Task BuildAsync_RollsSizeUpThroughTheWholeSubtree()
    {
        WriteFile("one.txt", new string('a', 10));
        WriteFile("nested/two.txt", new string('b', 20));
        WriteFile("nested/deeper/three.txt", new string('c', 30));
        var sut = CreateSut();

        var rootFolder = await sut.BuildAsync(_rootPath, _snapshot, _user);

        var nested = rootFolder.ChildFolders.Single();
        nested.ChildFolders.Single().Size.Should().Be(30);
        nested.Size.Should().Be(50);
        rootFolder.Size.Should().Be(60);
    }

    [Fact]
    public async Task BuildAsync_ComposesFolderHashFromItsContents()
    {
        WriteFile("b.txt", "second");
        WriteFile("a.txt", "first");
        WriteFile("child/inner.txt", "inner");
        var sut = CreateSut();

        var rootFolder = await sut.BuildAsync(_rootPath, _snapshot, _user);

        // The names here are the names on disk. They used to read "inner..txt" and
        // "a..txt", because the hash joined the stem to an extension that already
        // carried its own dot - and this test was written to match, which is how a
        // bug ends up with a test defending it.
        var childHash = ExpectedFolderHash(
            [("inner.txt", ExpectedFileHash("inner"))],
            []);

        rootFolder.ChildFolders.Single().Sha256Hash.Should().Be(childHash);
        rootFolder.Sha256Hash.Should().Be(ExpectedFolderHash(
            [("a.txt", ExpectedFileHash("first")), ("b.txt", ExpectedFileHash("second"))],
            [("child", childHash)]));
    }

    [Fact]
    public async Task BuildAsync_HashesAFileWithNoExtensionUnderItsRealName()
    {
        // The case that separates the two spellings most sharply: with nothing to
        // append, the old formula still added a dot and hashed "README." - a name
        // that exists nowhere on the disk being indexed.
        WriteFile("README", "readme");
        var sut = CreateSut();

        var rootFolder = await sut.BuildAsync(_rootPath, _snapshot, _user);

        rootFolder.Sha256Hash.Should().Be(ExpectedFolderHash(
            [("README", ExpectedFileHash("readme"))],
            []));
    }

    [Fact]
    public async Task BuildAsync_SortsChildFoldersAndFilesByName()
    {
        // Written in an order the sort has to undo, and read back by workers that can
        // finish in any order at all.
        WriteFile("zebra.txt", "z");
        WriteFile("apple.txt", "a");
        WriteFile("mango.txt", "m");
        Directory.CreateDirectory(Path.Combine(_rootPath, "zulu"));
        Directory.CreateDirectory(Path.Combine(_rootPath, "alpha"));
        Directory.CreateDirectory(Path.Combine(_rootPath, "mike"));
        var sut = CreateSut();

        var rootFolder = await sut.BuildAsync(_rootPath, _snapshot, _user);

        rootFolder.Files.Select(file => file.Name).Should().ContainInOrder("apple", "mango", "zebra");
        rootFolder.ChildFolders.Select(folder => folder.Name).Should().ContainInOrder("alpha", "mike", "zulu");
    }

    [Fact]
    public async Task BuildAsync_ProducesTheSameTreeOnEveryRun()
    {
        for (var fileIndex = 0; fileIndex < 40; fileIndex++)
        {
            WriteFile($"folder-{fileIndex % 8}/file-{fileIndex}.txt", $"content-{fileIndex}");
        }

        var firstRun = await CreateSut().BuildAsync(_rootPath, _snapshot, _user);
        var secondRun = await CreateSut(degreeOfParallelism: 1).BuildAsync(_rootPath, _snapshot, _user);

        // The hash covers the whole subtree, so one comparison settles the ordering,
        // the sizes and every file hash underneath it.
        secondRun.Sha256Hash.Should().Be(firstRun.Sha256Hash);
        secondRun.Size.Should().Be(firstRun.Size);
        secondRun.ChildFolders.Select(folder => folder.Name)
            .Should().Equal(firstRun.ChildFolders.Select(folder => folder.Name));
    }

    [Fact]
    public async Task BuildAsync_HandlesAWideAndDeepTreeAtTheDefaultParallelism()
    {
        var expectedFileCount = 0;
        for (var branchIndex = 0; branchIndex < 12; branchIndex++)
        {
            var branchPath = Path.Combine($"branch-{branchIndex}", "level-1", "level-2", "level-3");
            for (var fileIndex = 0; fileIndex < 5; fileIndex++)
            {
                WriteFile(Path.Combine(branchPath, $"file-{fileIndex}.bin"), $"branch-{branchIndex}-file-{fileIndex}");
                expectedFileCount++;
            }
        }

        var sut = new FolderTreeBuilder(_loggerMock.Object, UnrestrictedPathGuard);

        var rootFolder = await sut.BuildAsync(_rootPath, _snapshot, _user);

        CountFiles(rootFolder).Should().Be(expectedFileCount);
        AllFolders(rootFolder).Should().OnlyContain(folder => folder.Sha256Hash != string.Empty);
        AllFolders(rootFolder).SelectMany(folder => folder.Files)
            .Should().OnlyContain(file => file.Sha256Hash != string.Empty);
    }

    [Fact]
    public async Task BuildAsync_HandlesAnEmptyFolder()
    {
        var sut = CreateSut();

        var rootFolder = await sut.BuildAsync(_rootPath, _snapshot, _user);

        rootFolder.Files.Should().BeEmpty();
        rootFolder.ChildFolders.Should().BeEmpty();
        rootFolder.Size.Should().Be(0);
        rootFolder.Sha256Hash.Should().Be(string.Empty.GetHashSha256());
    }

    [Fact]
    public async Task BuildAsync_GivesIdenticalFoldersTheSameHash()
    {
        // This is the premise the repository's de-duplication rests on.
        WriteFile("left/same.txt", "identical");
        WriteFile("right/same.txt", "identical");
        var sut = CreateSut();

        var rootFolder = await sut.BuildAsync(_rootPath, _snapshot, _user);

        var left = rootFolder.ChildFolders.Single(folder => folder.Name == "left");
        var right = rootFolder.ChildFolders.Single(folder => folder.Name == "right");
        left.Sha256Hash.Should().Be(right.Sha256Hash);
    }

    [Fact]
    public async Task BuildAsync_ReadsAFileThatIsStillOpenForWriting()
    {
        var filePath = WriteFile("locked.txt", "locked-content");
        using var writeHandle = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        var sut = CreateSut();

        var rootFolder = await sut.BuildAsync(_rootPath, _snapshot, _user);

        rootFolder.Files.Single().Sha256Hash.Should().Be(ExpectedFileHash("locked-content"));
    }

    [Fact]
    public async Task BuildAsync_DoesNotFollowASymlinkLoopForever()
    {
        // Directory enumeration follows links, so without a guard this walk never ends.
        WriteFile("real/inner.txt", "inner");
        Directory.CreateSymbolicLink(Path.Combine(_rootPath, "real", "loop"), _rootPath);
        var sut = CreateSut();

        var buildTask = sut.BuildAsync(_rootPath, _snapshot, _user);

        // A generous bound: the point is that it terminates at all, not how fast.
        var finishedInTime = await Task.WhenAny(buildTask, Task.Delay(TimeSpan.FromSeconds(30))) == buildTask;
        finishedInTime.Should().BeTrue("the walk must terminate when a link points back up its own tree");

        var rootFolder = await buildTask;
        var real = rootFolder.ChildFolders.Single(folder => folder.Name == "real");
        real.Files.Single().Name.Should().Be("inner");

        // The link is still part of the tree; it is simply not descended into again.
        var loop = real.ChildFolders.Single(folder => folder.Name == "loop");
        loop.ChildFolders.Should().BeEmpty();
        loop.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildAsync_ExcludesADirectorySymlinkOutOfTheAllowedRoot()
    {
        // The snapshot walk is the arbitrary-read hole with teeth: it descends into
        // whatever enumeration hands it, hashes every file, and persists the names and
        // sizes. Guarding only the path the snapshot was requested for leaves one link
        // inside the root enough to index the whole machine.
        var forbiddenRoot = Path.Combine(Path.GetDirectoryName(_rootPath)!, $"ufo-forbidden-{Guid.NewGuid():N}");
        Directory.CreateDirectory(forbiddenRoot);
        File.WriteAllText(Path.Combine(forbiddenRoot, "secret.txt"), "secret");

        WriteFile("inside.txt", "inside");
        if (!TryCreateDirectorySymbolicLink(Path.Combine(_rootPath, "escape"), forbiddenRoot))
        {
            return;
        }

        var sut = CreateRestrictedSut(_rootPath);

        var rootFolder = await sut.BuildAsync(_rootPath, _snapshot, _user);

        rootFolder.ChildFolders.Should().NotContain(folder => folder.Name == "escape");
        AllFolders(rootFolder).SelectMany(folder => folder.Files)
            .Should().NotContain(file => file.Name == "secret");
        rootFolder.Files.Single().Name.Should().Be("inside");

        Directory.Delete(forbiddenRoot, recursive: true);
    }

    [Fact]
    public async Task BuildAsync_ExcludesAFileSymlinkOutOfTheAllowedRoot()
    {
        var forbiddenRoot = Path.Combine(Path.GetDirectoryName(_rootPath)!, $"ufo-forbidden-{Guid.NewGuid():N}");
        Directory.CreateDirectory(forbiddenRoot);
        var secretFilePath = Path.Combine(forbiddenRoot, "secret.txt");
        File.WriteAllText(secretFilePath, "secret");

        try
        {
            File.CreateSymbolicLink(Path.Combine(_rootPath, "leak.txt"), secretFilePath);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        var sut = CreateRestrictedSut(_rootPath);

        var rootFolder = await sut.BuildAsync(_rootPath, _snapshot, _user);

        // Without the check the link is hashed, and the digest of a file outside the
        // allowed roots is itself the leak - it confirms the contents to anyone holding
        // a candidate copy.
        rootFolder.Files.Should().NotContain(file => file.Name == "leak");

        Directory.Delete(forbiddenRoot, recursive: true);
    }

    [Fact]
    public async Task BuildAsync_KeepsASymlinkThatStaysInsideTheAllowedRoot()
    {
        WriteFile("real/inner.txt", "inner");
        if (!TryCreateDirectorySymbolicLink(Path.Combine(_rootPath, "link"), Path.Combine(_rootPath, "real")))
        {
            return;
        }

        var sut = CreateRestrictedSut(_rootPath);

        var rootFolder = await sut.BuildAsync(_rootPath, _snapshot, _user);

        // The allow-list must not cost a restricted host the links it legitimately has
        // inside its own library.
        rootFolder.ChildFolders.Should().Contain(folder => folder.Name == "link");
    }

    private static bool TryCreateDirectorySymbolicLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            // Unprivileged Windows without developer mode.
            return false;
        }
    }

    [Fact]
    public async Task BuildAsync_TakesSizeAndHashFromTheSameRead()
    {
        WriteFile("sized.bin", new string('x', 1234));
        var sut = CreateSut();

        var rootFolder = await sut.BuildAsync(_rootPath, _snapshot, _user);

        var file = rootFolder.Files.Single();
        file.Size.Should().Be(1234);
        file.Sha256Hash.Should().Be(ExpectedFileHash(new string('x', 1234)));
    }

    [Fact]
    public async Task BuildAsync_ThrowsWhenTheRootDoesNotExist()
    {
        var sut = CreateSut();

        await sut.Invoking(builder => builder.BuildAsync(Path.Combine(_rootPath, "missing"), _snapshot, _user))
            .Should().ThrowAsync<DirectoryNotFoundException>();
    }

    [Fact]
    public async Task BuildAsync_ObservesCancellation()
    {
        for (var fileIndex = 0; fileIndex < 50; fileIndex++)
        {
            WriteFile($"folder-{fileIndex % 5}/file-{fileIndex}.txt", $"content-{fileIndex}");
        }

        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();
        var sut = CreateSut();

        await sut.Invoking(builder => builder.BuildAsync(_rootPath, _snapshot, _user, cancellationTokenSource.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task BuildAsync_RejectsAMissingRootPath()
    {
        var sut = CreateSut();

        await sut.Invoking(builder => builder.BuildAsync("  ", _snapshot, _user))
            .Should().ThrowAsync<ArgumentException>();
    }

    private static int CountFiles(FolderEntity folder) =>
        folder.Files.Count + folder.ChildFolders.Sum(CountFiles);

    private static List<FolderEntity> AllFolders(FolderEntity folder) =>
        [folder, .. folder.ChildFolders.SelectMany(AllFolders)];

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
