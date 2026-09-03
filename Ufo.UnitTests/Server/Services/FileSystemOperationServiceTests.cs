using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Ufo.Abstractions.Options;
using Ufo.Server.Models;
using Ufo.Server.Services;

namespace Ufo.UnitTests.Server.Services;

/// <summary>
/// Exercised against a real temporary directory rather than an abstraction over
/// one. Every interesting thing here - a rename that only changes case, a move
/// that cannot be a rename, a folder copied into its own child - is a property of
/// the file system, and a fake that agreed with these tests would prove nothing
/// about the file system the server actually writes to.
/// </summary>
public class FileSystemOperationServiceTests : BaseTest, IDisposable
{
    private readonly Mock<ILogger<FileSystemOperationService>> _loggerMock = new();
    private readonly Mock<ILogger<PathGuard>> _pathGuardLoggerMock = new();
    private readonly string _testRoot;
    private readonly string _sourceFolder;
    private readonly string _destinationFolder;

    public FileSystemOperationServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"ufo-fsops-{Guid.NewGuid():N}");
        _sourceFolder = Path.Combine(_testRoot, "source");
        _destinationFolder = Path.Combine(_testRoot, "destination");

        Directory.CreateDirectory(_sourceFolder);
        Directory.CreateDirectory(_destinationFolder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private FileSystemOperationService CreateSut(params string[] allowedRoots)
    {
        var pathGuard = new PathGuard(
            _pathGuardLoggerMock.Object,
            Options.Create(new UfoHostOptions { AllowedRoots = allowedRoots }));

        return new FileSystemOperationService(_loggerMock.Object, pathGuard, new FileNameValidator());
    }

    private string WriteFile(string folderPath, string name, string content = "content")
    {
        var filePath = Path.Combine(folderPath, name);
        File.WriteAllText(filePath, content);

        return filePath;
    }

    #region Create

    [Fact]
    public void Create_MakesAnEmptyFile()
    {
        var result = CreateSut().Create(_sourceFolder, "notes.txt", isFile: true);

        result.IsSuccess.Should().BeTrue();
        result.Path.Should().Be(Path.Combine(_sourceFolder, "notes.txt"));
        File.Exists(result.Path).Should().BeTrue();
        new FileInfo(result.Path!).Length.Should().Be(0);
    }

    [Fact]
    public void Create_MakesAFolder()
    {
        var result = CreateSut().Create(_sourceFolder, "reports", isFile: false);

        result.IsSuccess.Should().BeTrue();
        Directory.Exists(result.Path).Should().BeTrue();
    }

    [Fact]
    public void Create_TrimsTheNameBeforeUsingIt()
    {
        var result = CreateSut().Create(_sourceFolder, "  spaced.txt  ", isFile: true);

        result.IsSuccess.Should().BeTrue();
        Path.GetFileName(result.Path).Should().Be("spaced.txt");
    }

    [Fact]
    public void Create_RefusesANameAlreadyTaken()
    {
        WriteFile(_sourceFolder, "notes.txt");

        var result = CreateSut().Create(_sourceFolder, "notes.txt", isFile: true);

        result.Status.Should().Be(FileSystemOperationStatus.Conflict);
        // The existing file keeps its contents: a create is never a truncation.
        File.ReadAllText(Path.Combine(_sourceFolder, "notes.txt")).Should().Be("content");
    }

    [Fact]
    public void Create_RefusesANameThatWouldEscapeTheFolder()
    {
        var result = CreateSut().Create(_sourceFolder, "../escaped.txt", isFile: true);

        result.Status.Should().Be(FileSystemOperationStatus.InvalidName);
        File.Exists(Path.Combine(_testRoot, "escaped.txt")).Should().BeFalse();
    }

    [Fact]
    public void Create_RefusesAFolderOutsideTheAllowedRoots()
    {
        var outsideFolder = Path.Combine(Path.GetTempPath(), $"ufo-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outsideFolder);

        try
        {
            var result = CreateSut(_testRoot).Create(outsideFolder, "notes.txt", isFile: true);

            result.Status.Should().Be(FileSystemOperationStatus.Forbidden);
            File.Exists(Path.Combine(outsideFolder, "notes.txt")).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(outsideFolder, recursive: true);
        }
    }

    [Fact]
    public void Create_ReportsAFolderThatIsNoLongerThere()
    {
        var result = CreateSut().Create(Path.Combine(_testRoot, "never-existed"), "notes.txt", isFile: true);

        result.Status.Should().Be(FileSystemOperationStatus.NotFound);
    }

    #endregion

    #region Rename

    [Fact]
    public void Rename_ChangesTheNameAndKeepsTheContents()
    {
        var filePath = WriteFile(_sourceFolder, "before.txt", "kept");

        var result = CreateSut().Rename(filePath, "after.txt");

        result.IsSuccess.Should().BeTrue();
        File.Exists(filePath).Should().BeFalse();
        File.ReadAllText(Path.Combine(_sourceFolder, "after.txt")).Should().Be("kept");
    }

    [Fact]
    public void Rename_LeavesTheEntryWhereItIs()
    {
        var filePath = WriteFile(_sourceFolder, "before.txt");

        var result = CreateSut().Rename(filePath, "after.txt");

        // A rename is not a move, whatever the new name looks like.
        Path.GetDirectoryName(result.Path).Should().Be(_sourceFolder);
    }

    [Fact]
    public void Rename_AcceptsTheNameTheEntryAlreadyHas()
    {
        var filePath = WriteFile(_sourceFolder, "notes.txt");

        var result = CreateSut().Rename(filePath, "notes.txt");

        // Nothing to do is not a collision with itself, which is what the
        // existence check would otherwise make of it.
        result.IsSuccess.Should().BeTrue();
        File.Exists(filePath).Should().BeTrue();
    }

    [Fact]
    public void Rename_ChangesOnlyTheCapitalisation()
    {
        var filePath = WriteFile(_sourceFolder, "notes.txt", "kept");

        var result = CreateSut().Rename(filePath, "Notes.txt");

        result.IsSuccess.Should().BeTrue();
        Path.GetFileName(result.Path).Should().Be("Notes.txt");
        File.ReadAllText(result.Path!).Should().Be("kept");

        // On a case-insensitive volume this is the same entry under a new label,
        // so the count is what proves nothing was duplicated or lost.
        Directory.GetFiles(_sourceFolder).Should().ContainSingle();
    }

    [Fact]
    public void Rename_RefusesANameAlreadyTaken()
    {
        WriteFile(_sourceFolder, "taken.txt", "theirs");
        var filePath = WriteFile(_sourceFolder, "mine.txt", "mine");

        var result = CreateSut().Rename(filePath, "taken.txt");

        result.Status.Should().Be(FileSystemOperationStatus.Conflict);
        File.ReadAllText(Path.Combine(_sourceFolder, "taken.txt")).Should().Be("theirs");
        File.Exists(filePath).Should().BeTrue();
    }

    [Fact]
    public void Rename_RefusesANameThatWouldMoveTheEntry()
    {
        var filePath = WriteFile(_sourceFolder, "notes.txt");

        var result = CreateSut().Rename(filePath, "../notes.txt");

        result.Status.Should().Be(FileSystemOperationStatus.InvalidName);
        File.Exists(filePath).Should().BeTrue();
        File.Exists(Path.Combine(_testRoot, "notes.txt")).Should().BeFalse();
    }

    [Fact]
    public void Rename_RefusesAnAllowedRootItself()
    {
        // The escape this exists to stop: the root resolves inside the allow-list
        // because it *is* the allow-list, but its parent does not - so renaming it
        // writes one level above every root the server was configured to expose.
        //
        // The allowed root is a folder inside the fixture rather than the fixture
        // itself, so that a regression relocates something Dispose still cleans up
        // instead of stranding it in the temp directory.
        var result = CreateSut(_sourceFolder).Rename(_sourceFolder, "escaped");

        result.IsSuccess.Should().BeFalse();
        Directory.Exists(_sourceFolder).Should().BeTrue();
        Directory.Exists(Path.Combine(_testRoot, "escaped")).Should().BeFalse();
    }

    [Fact]
    public void Move_RefusesAnAllowedRootItself()
    {
        var result = CreateSut(_testRoot, _destinationFolder)
            .Move([_testRoot], _destinationFolder, overwrite: false, CancellationToken.None);

        result.SucceededCount.Should().Be(0);
        Directory.Exists(_testRoot).Should().BeTrue();
    }

    [Fact]
    public void Rename_ReportsAnEntryThatIsNoLongerThere()
    {
        var result = CreateSut().Rename(Path.Combine(_sourceFolder, "gone.txt"), "after.txt");

        result.Status.Should().Be(FileSystemOperationStatus.NotFound);
    }

    [Fact]
    public void Rename_RenamesAFolderWithItsContents()
    {
        var nestedFolder = Path.Combine(_sourceFolder, "before");
        Directory.CreateDirectory(nestedFolder);
        WriteFile(nestedFolder, "inside.txt", "kept");

        var result = CreateSut().Rename(nestedFolder, "after");

        result.IsSuccess.Should().BeTrue();
        File.ReadAllText(Path.Combine(_sourceFolder, "after", "inside.txt")).Should().Be("kept");
    }

    #endregion

    #region Copy

    [Fact]
    public void Copy_LeavesTheSourceWhereItIs()
    {
        var filePath = WriteFile(_sourceFolder, "notes.txt", "kept");

        var result = CreateSut().Copy([filePath], _destinationFolder, overwrite: false, CancellationToken.None);

        result.SucceededCount.Should().Be(1);
        result.Failures.Should().BeEmpty();
        File.ReadAllText(filePath).Should().Be("kept");
        File.ReadAllText(Path.Combine(_destinationFolder, "notes.txt")).Should().Be("kept");
    }

    [Fact]
    public void Copy_TakesAFolderWithEverythingUnderIt()
    {
        var treeRoot = Path.Combine(_sourceFolder, "tree");
        var deepFolder = Path.Combine(treeRoot, "level-one", "level-two");
        Directory.CreateDirectory(deepFolder);
        WriteFile(treeRoot, "top.txt", "top");
        WriteFile(deepFolder, "deep.txt", "deep");

        var result = CreateSut().Copy([treeRoot], _destinationFolder, overwrite: false, CancellationToken.None);

        result.SucceededCount.Should().Be(1);
        File.ReadAllText(Path.Combine(_destinationFolder, "tree", "top.txt")).Should().Be("top");
        File.ReadAllText(Path.Combine(_destinationFolder, "tree", "level-one", "level-two", "deep.txt"))
            .Should().Be("deep");
    }

    [Fact]
    public void Copy_ReportsACollisionAsSomethingTheUserCanAnswer()
    {
        var filePath = WriteFile(_sourceFolder, "notes.txt", "mine");
        WriteFile(_destinationFolder, "notes.txt", "theirs");

        var result = CreateSut().Copy([filePath], _destinationFolder, overwrite: false, CancellationToken.None);

        result.SucceededCount.Should().Be(0);
        result.Failures.Should().ContainSingle()
            .Which.IsConflict.Should().BeTrue();

        // Nothing was overwritten on the way to asking.
        File.ReadAllText(Path.Combine(_destinationFolder, "notes.txt")).Should().Be("theirs");
    }

    [Fact]
    public void Copy_ReplacesWhatIsThereOnceOverwriteIsAsked()
    {
        var filePath = WriteFile(_sourceFolder, "notes.txt", "mine");
        WriteFile(_destinationFolder, "notes.txt", "theirs");

        var result = CreateSut().Copy([filePath], _destinationFolder, overwrite: true, CancellationToken.None);

        result.SucceededCount.Should().Be(1);
        File.ReadAllText(Path.Combine(_destinationFolder, "notes.txt")).Should().Be("mine");
    }

    [Fact]
    public void Copy_RefusesAFolderIntoItsOwnChild()
    {
        var parentFolder = Path.Combine(_sourceFolder, "parent");
        var childFolder = Path.Combine(parentFolder, "child");
        Directory.CreateDirectory(childFolder);

        var result = CreateSut().Copy([parentFolder], childFolder, overwrite: false, CancellationToken.None);

        // Left to run, the walk would keep finding the copy it was making.
        result.SucceededCount.Should().Be(0);
        result.Failures.Should().ContainSingle()
            .Which.Reason.Should().Contain("itself");
        Directory.Exists(Path.Combine(childFolder, "parent")).Should().BeFalse();
    }

    [Fact]
    public void Copy_RefusesAFolderIntoTheFolderItIsAlreadyIn()
    {
        var nestedFolder = Path.Combine(_sourceFolder, "nested");
        Directory.CreateDirectory(nestedFolder);

        var result = CreateSut().Copy([nestedFolder], _sourceFolder, overwrite: false, CancellationToken.None);

        result.SucceededCount.Should().Be(0);
        result.Failures.Should().ContainSingle();
    }

    [Fact]
    public void Copy_CarriesOnPastAnEntryItCannotTake()
    {
        var goodFilePath = WriteFile(_sourceFolder, "good.txt", "good");
        var missingFilePath = Path.Combine(_sourceFolder, "gone.txt");

        var result = CreateSut().Copy(
            [missingFilePath, goodFilePath],
            _destinationFolder,
            overwrite: false,
            CancellationToken.None);

        // One bad path is not a reason to abandon the other nineteen files.
        result.SucceededCount.Should().Be(1);
        result.Failures.Should().ContainSingle().Which.Name.Should().Be("gone.txt");
        File.Exists(Path.Combine(_destinationFolder, "good.txt")).Should().BeTrue();
    }

    [Fact]
    public void Copy_RefusesADestinationOutsideTheAllowedRoots()
    {
        var outsideFolder = Path.Combine(Path.GetTempPath(), $"ufo-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outsideFolder);
        var filePath = WriteFile(_sourceFolder, "notes.txt");

        try
        {
            var result = CreateSut(_testRoot).Copy(
                [filePath],
                outsideFolder,
                overwrite: false,
                CancellationToken.None);

            result.SucceededCount.Should().Be(0);
            result.Failures.Should().ContainSingle();
            File.Exists(Path.Combine(outsideFolder, "notes.txt")).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(outsideFolder, recursive: true);
        }
    }

    [Fact]
    public void Copy_RefusesASourceOutsideTheAllowedRoots()
    {
        var outsideFolder = Path.Combine(Path.GetTempPath(), $"ufo-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outsideFolder);
        var outsideFilePath = WriteFile(outsideFolder, "secret.txt");

        try
        {
            var result = CreateSut(_testRoot).Copy(
                [outsideFilePath],
                _destinationFolder,
                overwrite: false,
                CancellationToken.None);

            result.SucceededCount.Should().Be(0);
            File.Exists(Path.Combine(_destinationFolder, "secret.txt")).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(outsideFolder, recursive: true);
        }
    }

    #endregion

    #region Move

    [Fact]
    public void Move_TakesTheEntryOutOfTheSourceFolder()
    {
        var filePath = WriteFile(_sourceFolder, "notes.txt", "kept");

        var result = CreateSut().Move([filePath], _destinationFolder, overwrite: false, CancellationToken.None);

        result.SucceededCount.Should().Be(1);
        File.Exists(filePath).Should().BeFalse();
        File.ReadAllText(Path.Combine(_destinationFolder, "notes.txt")).Should().Be("kept");
    }

    [Fact]
    public void Move_TakesAFolderWithEverythingUnderIt()
    {
        var treeRoot = Path.Combine(_sourceFolder, "tree");
        Directory.CreateDirectory(treeRoot);
        WriteFile(treeRoot, "inside.txt", "kept");

        var result = CreateSut().Move([treeRoot], _destinationFolder, overwrite: false, CancellationToken.None);

        result.SucceededCount.Should().Be(1);
        Directory.Exists(treeRoot).Should().BeFalse();
        File.ReadAllText(Path.Combine(_destinationFolder, "tree", "inside.txt")).Should().Be("kept");
    }

    [Fact]
    public void Move_LeavesTheSourceAloneWhenTheDestinationIsTaken()
    {
        var filePath = WriteFile(_sourceFolder, "notes.txt", "mine");
        WriteFile(_destinationFolder, "notes.txt", "theirs");

        var result = CreateSut().Move([filePath], _destinationFolder, overwrite: false, CancellationToken.None);

        result.Failures.Should().ContainSingle().Which.IsConflict.Should().BeTrue();
        // A move that stops half way is a file the user no longer has anywhere.
        File.ReadAllText(filePath).Should().Be("mine");
        File.ReadAllText(Path.Combine(_destinationFolder, "notes.txt")).Should().Be("theirs");
    }

    [Fact]
    public void Move_ReplacesAFolderWithAFileWhenOverwriteIsAsked()
    {
        var filePath = WriteFile(_sourceFolder, "entry", "mine");
        Directory.CreateDirectory(Path.Combine(_destinationFolder, "entry"));

        var result = CreateSut().Move([filePath], _destinationFolder, overwrite: true, CancellationToken.None);

        // Neither Move nor Copy will write a file over a directory, so what is
        // there has to go first - which is the whole reason overwrite is a
        // separate answer from the user rather than a default.
        result.SucceededCount.Should().Be(1);
        File.ReadAllText(Path.Combine(_destinationFolder, "entry")).Should().Be("mine");
    }

    [Fact]
    public void Move_RefusesAFolderIntoItsOwnChild()
    {
        var parentFolder = Path.Combine(_sourceFolder, "parent");
        var childFolder = Path.Combine(parentFolder, "child");
        Directory.CreateDirectory(childFolder);

        var result = CreateSut().Move([parentFolder], childFolder, overwrite: false, CancellationToken.None);

        result.SucceededCount.Should().Be(0);
        Directory.Exists(parentFolder).Should().BeTrue();
    }

    #endregion

    #region Failures that must not destroy anything

    [Fact]
    public void Copy_LeavesTheDestinationIntactWhenTheCopyCannotFinish()
    {
        // The source is a folder and the destination a file of the same name, so
        // the destination has to go first - but if the write then fails, deleting
        // it early would have cost the user both copies. Here the kinds match, so
        // nothing is deleted up front and a failure is survivable.
        var sourceFilePath = WriteFile(_sourceFolder, "notes.txt", "mine");
        var destinationFilePath = WriteFile(_destinationFolder, "notes.txt", "theirs");

        // Held open for writing, so the copy over it cannot succeed.
        using (var lockHandle = new FileStream(destinationFilePath, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            var result = CreateSut().Copy(
                [sourceFilePath],
                _destinationFolder,
                overwrite: true,
                CancellationToken.None);

            result.SucceededCount.Should().Be(0);
            result.Failures.Should().ContainSingle();
        }

        // The point of the test: what was there is still there.
        File.Exists(destinationFilePath).Should().BeTrue();
        File.ReadAllText(destinationFilePath).Should().Be("theirs");
    }

    [Fact]
    public void Move_KeepsTheSourceWhenPartOfItCouldNotBeCopied()
    {
        // A link out of the allowed roots is skipped by the copy. If the move then
        // deleted the source tree, it would delete the one thing it refused to
        // carry across - the worst outcome the operation can produce.
        //
        // Reaching that path takes a destination that already exists: a move onto
        // free space is a rename, which carries the whole tree across untouched
        // and never consults the copy at all.
        var outsideFolder = Path.Combine(Path.GetTempPath(), $"ufo-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outsideFolder);
        WriteFile(outsideFolder, "secret.txt");

        var treeRoot = Path.Combine(_sourceFolder, "tree");
        Directory.CreateDirectory(treeRoot);
        WriteFile(treeRoot, "ordinary.txt", "kept");
        Directory.CreateDirectory(Path.Combine(_destinationFolder, "tree"));

        if (!TryCreateDirectorySymbolicLink(Path.Combine(treeRoot, "escape"), outsideFolder))
        {
            // Unprivileged Windows without developer mode; nothing to assert.
            Directory.Delete(outsideFolder, recursive: true);
            return;
        }

        try
        {
            var result = CreateSut(_testRoot).Move(
                [treeRoot],
                _destinationFolder,
                overwrite: true,
                CancellationToken.None);

            result.SucceededCount.Should().Be(0);
            result.Failures.Should().ContainSingle()
                .Which.Reason.Should().Contain("nothing was removed");

            Directory.Exists(treeRoot).Should().BeTrue();
            File.ReadAllText(Path.Combine(treeRoot, "ordinary.txt")).Should().Be("kept");
            File.Exists(Path.Combine(outsideFolder, "secret.txt")).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(outsideFolder, recursive: true);
        }
    }

    [Fact]
    public void Copy_ReportsATreeItCouldNotTakeWholeRatherThanCountingItDone()
    {
        var outsideFolder = Path.Combine(Path.GetTempPath(), $"ufo-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outsideFolder);

        var treeRoot = Path.Combine(_sourceFolder, "tree");
        Directory.CreateDirectory(treeRoot);
        WriteFile(treeRoot, "ordinary.txt");

        if (!TryCreateDirectorySymbolicLink(Path.Combine(treeRoot, "escape"), outsideFolder))
        {
            Directory.Delete(outsideFolder, recursive: true);
            return;
        }

        try
        {
            var result = CreateSut(_testRoot).Copy(
                [treeRoot],
                _destinationFolder,
                overwrite: false,
                CancellationToken.None);

            // An incomplete copy counted as a success is a user told their files
            // arrived when some of them did not.
            result.SucceededCount.Should().Be(0);
            result.Failures.Should().ContainSingle()
                .Which.Reason.Should().Contain("left behind");
        }
        finally
        {
            Directory.Delete(outsideFolder, recursive: true);
        }
    }

    [Fact]
    public void Delete_RefusesASymbolicLinkRatherThanDeletingWhatItPointsAt()
    {
        var targetFolder = Path.Combine(_testRoot, "real");
        Directory.CreateDirectory(targetFolder);
        var targetFilePath = WriteFile(targetFolder, "important.txt", "irreplaceable");

        var linkPath = Path.Combine(_sourceFolder, "shortcut");

        if (!TryCreateDirectorySymbolicLink(linkPath, targetFolder))
        {
            return;
        }

        var result = CreateSut().Delete([linkPath], CancellationToken.None);

        result.SucceededCount.Should().Be(0);
        // The file the link pointed at is what a resolved path would have deleted.
        File.ReadAllText(targetFilePath).Should().Be("irreplaceable");
    }

    [Fact]
    public void Delete_ReportsWhatItManagedBeforeCancellation()
    {
        var filePath = WriteFile(_sourceFolder, "notes.txt");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Cancellation escaping the batch loop would discard the whole result,
        // leaving the caller unable to say what had already happened.
        var result = CreateSut().Delete([filePath], cancellation.Token);

        result.SucceededCount.Should().Be(0);
        result.Failures.Should().ContainSingle().Which.Reason.Should().Contain("cancelled");
        File.Exists(filePath).Should().BeTrue();
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

    #region Delete

    [Fact]
    public void Delete_RemovesAFile()
    {
        var filePath = WriteFile(_sourceFolder, "notes.txt");

        var result = CreateSut().Delete([filePath], CancellationToken.None);

        result.SucceededCount.Should().Be(1);
        result.Failures.Should().BeEmpty();
        File.Exists(filePath).Should().BeFalse();
    }

    [Fact]
    public void Delete_RemovesAFolderWithEverythingUnderIt()
    {
        var treeRoot = Path.Combine(_sourceFolder, "tree");
        var deepFolder = Path.Combine(treeRoot, "level-one");
        Directory.CreateDirectory(deepFolder);
        WriteFile(deepFolder, "deep.txt");

        var result = CreateSut().Delete([treeRoot], CancellationToken.None);

        result.SucceededCount.Should().Be(1);
        Directory.Exists(treeRoot).Should().BeFalse();
    }

    [Fact]
    public void Delete_CarriesOnPastAnEntryItCannotRemove()
    {
        var filePath = WriteFile(_sourceFolder, "notes.txt");
        var missingPath = Path.Combine(_sourceFolder, "gone.txt");

        var result = CreateSut().Delete([missingPath, filePath], CancellationToken.None);

        result.SucceededCount.Should().Be(1);
        result.Failures.Should().ContainSingle().Which.Name.Should().Be("gone.txt");
        File.Exists(filePath).Should().BeFalse();
    }

    [Fact]
    public void Delete_RefusesAnAllowedRootItself()
    {
        var result = CreateSut(_testRoot).Delete([_testRoot], CancellationToken.None);

        // Deleting the configured root deletes everything the user can see, which
        // no confirmation dialog makes into a reasonable thing to allow.
        result.SucceededCount.Should().Be(0);
        result.Failures.Should().ContainSingle()
            .Which.Reason.Should().Contain("top-level");
        Directory.Exists(_testRoot).Should().BeTrue();
    }

    [Fact]
    public void Delete_RefusesAPathOutsideTheAllowedRoots()
    {
        var outsideFolder = Path.Combine(Path.GetTempPath(), $"ufo-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outsideFolder);
        var outsideFilePath = WriteFile(outsideFolder, "secret.txt");

        try
        {
            var result = CreateSut(_testRoot).Delete([outsideFilePath], CancellationToken.None);

            result.SucceededCount.Should().Be(0);
            File.Exists(outsideFilePath).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(outsideFolder, recursive: true);
        }
    }

    [Fact]
    public void Delete_ReportsAnEntryThatIsAlreadyGone()
    {
        var result = CreateSut().Delete([Path.Combine(_sourceFolder, "gone.txt")], CancellationToken.None);

        result.SucceededCount.Should().Be(0);
        result.Failures.Should().ContainSingle().Which.Reason.Should().Contain("no longer exists");
    }

    #endregion
}
