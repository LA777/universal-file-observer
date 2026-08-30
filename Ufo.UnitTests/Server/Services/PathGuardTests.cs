using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Ufo.Abstractions.Options;
using Ufo.Server.Services;

namespace Ufo.UnitTests.Server.Services;

public class PathGuardTests : BaseTest, IDisposable
{
    private readonly Mock<ILogger<PathGuard>> _loggerMock = new();
    private readonly string _allowedRoot;
    private readonly string _forbiddenRoot;

    public PathGuardTests()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"ufo-guard-{Guid.NewGuid():N}");
        _allowedRoot = Path.Combine(testRoot, "allowed");
        _forbiddenRoot = Path.Combine(testRoot, "forbidden");

        Directory.CreateDirectory(_allowedRoot);
        Directory.CreateDirectory(_forbiddenRoot);
    }

    private PathGuard CreateSut(params string[] allowedRoots) =>
        new(_loggerMock.Object, Options.Create(new UfoHostOptions { AllowedRoots = allowedRoots }));

    [Fact]
    public void TryResolve_AllowsAnyPath_WhenNoRootsAreConfigured()
    {
        var sut = CreateSut();

        // An empty allow-list is how the desktop application keeps browsing the
        // whole machine.
        sut.IsRestricted.Should().BeFalse();
        sut.TryResolve(_forbiddenRoot, out var resolvedPath).Should().BeTrue();
        resolvedPath.Should().Be(_forbiddenRoot);
    }

    [Fact]
    public void TryResolve_CanonicalisesRelativeSegments()
    {
        var sut = CreateSut();
        var pathWithRelativeSegments = Path.Combine(_allowedRoot, "..", "allowed", "child");

        sut.TryResolve(pathWithRelativeSegments, out var resolvedPath).Should().BeTrue();

        resolvedPath.Should().Be(Path.Combine(_allowedRoot, "child"));
    }

    [Fact]
    public void TryResolve_AllowsThePathsUnderAConfiguredRoot()
    {
        var sut = CreateSut(_allowedRoot);

        sut.IsRestricted.Should().BeTrue();
        sut.TryResolve(Path.Combine(_allowedRoot, "nested", "file.txt"), out _).Should().BeTrue();
    }

    [Fact]
    public void TryResolve_AllowsTheConfiguredRootItself()
    {
        var sut = CreateSut(_allowedRoot);

        sut.TryResolve(_allowedRoot, out var resolvedPath).Should().BeTrue();
        resolvedPath.Should().Be(_allowedRoot);
    }

    [Fact]
    public void TryResolve_RejectsAPathOutsideEveryRoot()
    {
        var sut = CreateSut(_allowedRoot);

        sut.TryResolve(_forbiddenRoot, out var resolvedPath).Should().BeFalse();
        resolvedPath.Should().BeEmpty();
    }

    [Fact]
    public void TryResolve_RejectsTraversalOutOfARoot()
    {
        var sut = CreateSut(_allowedRoot);
        var traversalPath = Path.Combine(_allowedRoot, "..", "forbidden", "secret.txt");

        sut.TryResolve(traversalPath, out _).Should().BeFalse();
    }

    [Fact]
    public void TryResolve_RejectsASiblingSharingTheRootsPrefix()
    {
        // "/tmp/x/allowed-other" must not pass because it starts with
        // "/tmp/x/allowed"; the separator has to be part of the comparison.
        var siblingPath = _allowedRoot + "-other";
        Directory.CreateDirectory(siblingPath);
        var sut = CreateSut(_allowedRoot);

        sut.TryResolve(siblingPath, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolve_RejectsAnEmptyPath(string? path)
    {
        var sut = CreateSut(_allowedRoot);

        sut.TryResolve(path, out var resolvedPath).Should().BeFalse();
        resolvedPath.Should().BeEmpty();
    }

    [Fact]
    public void AllowedRoots_IgnoresBlankEntriesAndTrailingSeparators()
    {
        var sut = CreateSut(_allowedRoot + Path.DirectorySeparatorChar, "   ", _allowedRoot);

        sut.AllowedRoots.Should().ContainSingle().Which.Should().Be(_allowedRoot);
    }

    /// <summary>
    /// Creates a symbolic link, reporting false where the OS forbids it
    /// (unprivileged Windows without developer mode). The link tests below are
    /// inert there rather than failing for an environmental reason.
    /// </summary>
    private static bool TryCreateDirectorySymbolicLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    [Fact]
    public void TryResolve_RejectsAPathReachedThroughASymlinkedDirectory()
    {
        // The bypass this guards against: "<allowed>/link/secret.txt" where "link"
        // points outside. Path.GetFullPath is purely lexical and leaves it alone,
        // and resolving only the final component sees no link on "secret.txt", so
        // a leaf-only check lets the read through.
        var linkPath = Path.Combine(_allowedRoot, "link");
        if (!TryCreateDirectorySymbolicLink(linkPath, _forbiddenRoot))
        {
            return;
        }

        File.WriteAllText(Path.Combine(_forbiddenRoot, "secret.txt"), "secret");
        var sut = CreateSut(_allowedRoot);

        sut.TryResolve(Path.Combine(linkPath, "secret.txt"), out _).Should().BeFalse();
    }

    [Fact]
    public void TryResolve_RejectsASymlinkedDirectoryItself()
    {
        var linkPath = Path.Combine(_allowedRoot, "link");
        if (!TryCreateDirectorySymbolicLink(linkPath, _forbiddenRoot))
        {
            return;
        }

        var sut = CreateSut(_allowedRoot);

        sut.TryResolve(linkPath, out _).Should().BeFalse();
    }

    [Fact]
    public void TryResolve_AllowsASymlinkThatStaysInsideTheRoot()
    {
        var targetPath = Path.Combine(_allowedRoot, "real");
        Directory.CreateDirectory(targetPath);
        var linkPath = Path.Combine(_allowedRoot, "link");
        if (!TryCreateDirectorySymbolicLink(linkPath, targetPath))
        {
            return;
        }

        var sut = CreateSut(_allowedRoot);

        sut.TryResolve(Path.Combine(linkPath, "file.txt"), out var resolvedPath).Should().BeTrue();
        // Reported as the physical path, so callers cannot be handed a path whose
        // meaning changes if the link is repointed.
        resolvedPath.Should().Be(Path.Combine(targetPath, "file.txt"));
    }

    [Fact]
    public void TryResolve_RejectsASymlinkCycle()
    {
        var firstLink = Path.Combine(_allowedRoot, "first");
        var secondLink = Path.Combine(_allowedRoot, "second");

        if (!TryCreateDirectorySymbolicLink(firstLink, secondLink))
        {
            return;
        }

        if (!TryCreateDirectorySymbolicLink(secondLink, firstLink))
        {
            return;
        }

        var sut = CreateSut(_allowedRoot);

        // Must terminate on the hop limit rather than loop.
        sut.TryResolve(Path.Combine(firstLink, "anything.txt"), out _).Should().BeFalse();
    }

    [Fact]
    public void AllowedRoots_ResolvesARootThatIsItselfASymlink()
    {
        var realRoot = Path.Combine(Path.GetDirectoryName(_allowedRoot)!, "real-root");
        Directory.CreateDirectory(realRoot);
        var linkedRoot = Path.Combine(Path.GetDirectoryName(_allowedRoot)!, "linked-root");
        if (!TryCreateDirectorySymbolicLink(linkedRoot, realRoot))
        {
            return;
        }

        // Configuring the link must still admit paths under the physical target,
        // which is how "/tmp" behaves on macOS.
        var sut = CreateSut(linkedRoot);

        sut.AllowedRoots.Should().ContainSingle().Which.Should().Be(realRoot);
        sut.TryResolve(Path.Combine(linkedRoot, "child.txt"), out var resolvedPath).Should().BeTrue();
        resolvedPath.Should().Be(Path.Combine(realRoot, "child.txt"));
    }

    [Fact]
    public void IsAllowedChild_AllowsAnOrdinaryEntryWithoutWalkingItsAncestors()
    {
        var childPath = Path.Combine(_allowedRoot, "child.txt");
        File.WriteAllText(childPath, "child");
        var sut = CreateSut(_allowedRoot);

        // The cheap path: not a link, so the caller's guarantee about the parent
        // settles it.
        sut.IsAllowedChild(childPath).Should().BeTrue();
    }

    [Fact]
    public void IsAllowedChild_RejectsASymlinkOutOfTheRoot()
    {
        var linkPath = Path.Combine(_allowedRoot, "link");
        if (!TryCreateDirectorySymbolicLink(linkPath, _forbiddenRoot))
        {
            return;
        }

        var sut = CreateSut(_allowedRoot);

        sut.IsAllowedChild(linkPath).Should().BeFalse();
    }

    [Fact]
    public void IsAllowedChild_AllowsASymlinkThatStaysInsideTheRoot()
    {
        var targetPath = Path.Combine(_allowedRoot, "real");
        Directory.CreateDirectory(targetPath);
        var linkPath = Path.Combine(_allowedRoot, "link");
        if (!TryCreateDirectorySymbolicLink(linkPath, targetPath))
        {
            return;
        }

        var sut = CreateSut(_allowedRoot);

        sut.IsAllowedChild(linkPath).Should().BeTrue();
    }

    [Fact]
    public void IsAllowedChild_AllowsEverythingWhenUnrestricted()
    {
        var sut = CreateSut();

        sut.IsAllowedChild(_forbiddenRoot).Should().BeTrue();
    }

    [Fact]
    public void TryResolveQuietly_DoesNotLogARejectionAsAWarning()
    {
        var sut = CreateSut(_allowedRoot);

        sut.TryResolveQuietly(_forbiddenRoot, out _).Should().BeFalse();

        // The parent of an allowed root fails this check on every single folder
        // listing, so a warning per rejection is how the log fills with noise that
        // looks like an attack and is not.
        _loggerMock.Verify(
            logger => logger.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public void TryResolve_LogsARejectionAsAWarning()
    {
        var sut = CreateSut(_allowedRoot);

        sut.TryResolve(_forbiddenRoot, out _).Should().BeFalse();

        // A path the caller asked for by name is a different matter: that one is
        // worth seeing.
        _loggerMock.Verify(
            logger => logger.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    public void Dispose()
    {
        var testRoot = Path.GetDirectoryName(_allowedRoot);
        if (testRoot != null && Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
