using Ufo.Server.Models;

namespace Ufo.Server.Services;

/// <summary>
/// The write half of file-system browsing: create, rename, copy, move, delete.
/// </summary>
/// <remarks>
/// <para>
/// Every path a caller supplies goes through <see cref="IPathGuard"/> before it is
/// touched, and every name goes through <see cref="IFileNameValidator"/>. Together
/// those two are the whole of the containment story: the guard says which folders
/// may be written to, and the validator guarantees the name added to one is a
/// single segment that cannot climb out of it.
/// </para>
/// <para>
/// Nothing here throws for an outcome the user could have caused. A locked file, a
/// full disk, a folder deleted a moment ago - each comes back as a result carrying
/// the sentence to show, because the alternative is a 500 whose body is an
/// exception page.
/// </para>
/// </remarks>
public interface IFileSystemOperationService
{
    /// <summary>Creates one empty file or folder inside <paramref name="parentPath"/>.</summary>
    FileSystemOperationResult Create(string? parentPath, string? name, bool isFile);

    /// <summary>Renames one entry, leaving it in the folder it is already in.</summary>
    FileSystemOperationResult Rename(string? path, string? newName);

    /// <summary>Copies entries into a folder, each keeping its own name.</summary>
    FileSystemBatchResult Copy(
        IList<string>? paths,
        string? destinationFolderPath,
        bool overwrite,
        CancellationToken cancellationToken);

    /// <summary>Moves entries into a folder, each keeping its own name.</summary>
    FileSystemBatchResult Move(
        IList<string>? paths,
        string? destinationFolderPath,
        bool overwrite,
        CancellationToken cancellationToken);

    /// <summary>Deletes entries permanently. A folder goes with everything inside it.</summary>
    FileSystemBatchResult Delete(IList<string>? paths, CancellationToken cancellationToken);
}

public class FileSystemOperationService : IFileSystemOperationService
{
    private readonly ILogger<FileSystemOperationService> _logger;
    private readonly IPathGuard _pathGuard;
    private readonly IFileNameValidator _fileNameValidator;

    public FileSystemOperationService(
        ILogger<FileSystemOperationService> logger,
        IPathGuard pathGuard,
        IFileNameValidator fileNameValidator)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pathGuard = pathGuard ?? throw new ArgumentNullException(nameof(pathGuard));
        _fileNameValidator = fileNameValidator ?? throw new ArgumentNullException(nameof(fileNameValidator));
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    #region Create

    public FileSystemOperationResult Create(string? parentPath, string? name, bool isFile)
    {
        if (!_pathGuard.TryResolve(parentPath, out var resolvedParentPath))
        {
            return FileSystemOperationResult.Rejected(
                FileSystemOperationStatus.Forbidden,
                $"'{parentPath}' is not a folder this server is allowed to write to.");
        }

        if (!Directory.Exists(resolvedParentPath))
        {
            return FileSystemOperationResult.Rejected(
                FileSystemOperationStatus.NotFound,
                $"The folder '{parentPath}' no longer exists.");
        }

        var trimmedName = name?.Trim() ?? string.Empty;

        if (!_fileNameValidator.TryValidate(trimmedName, out var rejectionReason))
        {
            return FileSystemOperationResult.Rejected(FileSystemOperationStatus.InvalidName, rejectionReason);
        }

        var targetPath = Path.Combine(resolvedParentPath, trimmedName);

        if (Exists(targetPath))
        {
            return FileSystemOperationResult.Rejected(
                FileSystemOperationStatus.Conflict,
                $"'{trimmedName}' already exists in this folder.");
        }

        try
        {
            if (isFile)
            {
                // CreateNew rather than Create: two clients racing on the same name
                // should produce one file and one conflict, not one silent truncation.
                using var stream = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            }
            else
            {
                Directory.CreateDirectory(targetPath);
            }

            _logger.LogInformation("Created {Kind} {Path}.", isFile ? "file" : "folder", targetPath);

            return FileSystemOperationResult.Succeeded(targetPath);
        }
        catch (IOException exception) when (Exists(targetPath))
        {
            _logger.LogWarning(exception, "Lost a create race for {Path}.", targetPath);

            return FileSystemOperationResult.Rejected(
                FileSystemOperationStatus.Conflict,
                $"'{trimmedName}' already exists in this folder.");
        }
        catch (Exception exception) when (IsExpectedFileSystemFailure(exception))
        {
            _logger.LogWarning(exception, "Could not create {Path}.", targetPath);

            return FileSystemOperationResult.Rejected(
                StatusFor(exception),
                $"'{trimmedName}' could not be created. {DescribeFailure(exception)}");
        }
    }

    #endregion

    #region Rename

    public FileSystemOperationResult Rename(string? path, string? newName)
    {
        if (!_pathGuard.TryResolve(path, out var resolvedPath))
        {
            return FileSystemOperationResult.Rejected(
                FileSystemOperationStatus.Forbidden,
                $"'{path}' is not something this server is allowed to change.");
        }

        var isDirectory = Directory.Exists(resolvedPath);

        if (!isDirectory && !File.Exists(resolvedPath))
        {
            return FileSystemOperationResult.Rejected(
                FileSystemOperationStatus.NotFound,
                $"'{path}' no longer exists. It may have been renamed, moved, or deleted.");
        }

        // A rename writes into the entry's parent, and the parent of an allowed
        // root is outside every allowed root there is.
        if (IsProtectedRoot(resolvedPath))
        {
            return FileSystemOperationResult.Rejected(
                FileSystemOperationStatus.Forbidden,
                "A drive or top-level location cannot be renamed.");
        }

        if (IsSymbolicLink(path))
        {
            return FileSystemOperationResult.Rejected(FileSystemOperationStatus.Forbidden, SymbolicLinkRefusal);
        }

        var trimmedName = newName?.Trim() ?? string.Empty;

        if (!_fileNameValidator.TryValidate(trimmedName, out var rejectionReason))
        {
            return FileSystemOperationResult.Rejected(FileSystemOperationStatus.InvalidName, rejectionReason);
        }

        if (Path.GetDirectoryName(resolvedPath) is not { Length: > 0 } parentPath)
        {
            return FileSystemOperationResult.Rejected(
                FileSystemOperationStatus.Failed,
                "A drive or top-level location cannot be renamed here.");
        }

        var targetPath = Path.Combine(parentPath, trimmedName);

        // The name is a single segment and the parent is already inside an allowed
        // root, so this should be redundant. "Should be" is not the standard for
        // the check that decides where a write lands.
        if (!_pathGuard.TryResolveQuietly(targetPath, out _))
        {
            return FileSystemOperationResult.Rejected(
                FileSystemOperationStatus.Forbidden,
                $"'{trimmedName}' is not somewhere this server is allowed to write.");
        }

        // Nothing to do, and going ahead would report a conflict against itself.
        if (string.Equals(targetPath, resolvedPath, StringComparison.Ordinal))
        {
            return FileSystemOperationResult.Succeeded(resolvedPath);
        }

        // "notes.txt" to "Notes.txt" on a case-insensitive volume: the entry the
        // existence check finds is the one being renamed, so treating it as a
        // collision would make changing a name's case impossible. Move handles it.
        var isCaseOnlyRename = targetPath.Equals(resolvedPath, PathComparison);

        if (!isCaseOnlyRename && Exists(targetPath))
        {
            return FileSystemOperationResult.Rejected(
                FileSystemOperationStatus.Conflict,
                $"'{trimmedName}' already exists in this folder.");
        }

        try
        {
            if (isDirectory)
            {
                Directory.Move(resolvedPath, targetPath);
            }
            else
            {
                File.Move(resolvedPath, targetPath, overwrite: false);
            }

            _logger.LogInformation("Renamed {Path} to {NewName}.", resolvedPath, trimmedName);

            return FileSystemOperationResult.Succeeded(targetPath);
        }
        catch (Exception exception) when (IsExpectedFileSystemFailure(exception))
        {
            _logger.LogWarning(exception, "Could not rename {Path} to {NewName}.", resolvedPath, trimmedName);

            return FileSystemOperationResult.Rejected(
                StatusFor(exception),
                $"'{Path.GetFileName(resolvedPath)}' could not be renamed. {DescribeFailure(exception)}");
        }
    }

    #endregion

    #region Copy and move

    public FileSystemBatchResult Copy(
        IList<string>? paths,
        string? destinationFolderPath,
        bool overwrite,
        CancellationToken cancellationToken) =>
        Transfer(paths, destinationFolderPath, overwrite, isMove: false, cancellationToken);

    public FileSystemBatchResult Move(
        IList<string>? paths,
        string? destinationFolderPath,
        bool overwrite,
        CancellationToken cancellationToken) =>
        Transfer(paths, destinationFolderPath, overwrite, isMove: true, cancellationToken);

    /// <summary>
    /// Copy and move differ only in what happens to the source, so they share
    /// everything up to that point - the destination check, the self-containment
    /// check, and the per-entry failure reporting.
    /// </summary>
    private FileSystemBatchResult Transfer(
        IList<string>? paths,
        string? destinationFolderPath,
        bool overwrite,
        bool isMove,
        CancellationToken cancellationToken)
    {
        var result = new FileSystemBatchResult();
        var verb = isMove ? "moved" : "copied";

        if (!_pathGuard.TryResolve(destinationFolderPath, out var resolvedDestination)
            || !Directory.Exists(resolvedDestination))
        {
            foreach (var path in paths ?? [])
            {
                result.Failures.Add(Failure(path, $"The destination folder '{destinationFolderPath}' is not available."));
            }

            return result;
        }

        foreach (var path in paths ?? [])
        {
            if (cancellationToken.IsCancellationRequested)
            {
                result.Failures.Add(Failure(path, $"It was not {verb} - the request was cancelled."));
                continue;
            }

            var failure = TransferOne(path, resolvedDestination, overwrite, isMove, cancellationToken);

            if (failure is null)
            {
                result.SucceededCount++;
            }
            else
            {
                result.Failures.Add(failure);
            }
        }

        return result;
    }

    /// <returns>Null when the entry was transferred, otherwise why it was not.</returns>
    private FileSystemItemFailure? TransferOne(
        string path,
        string resolvedDestinationFolder,
        bool overwrite,
        bool isMove,
        CancellationToken cancellationToken)
    {
        if (!_pathGuard.TryResolve(path, out var resolvedSource))
        {
            return Failure(path, "It is outside the folders this server is allowed to read.");
        }

        var isDirectory = Directory.Exists(resolvedSource);

        if (!isDirectory && !File.Exists(resolvedSource))
        {
            return Failure(path, "It no longer exists.");
        }

        var name = Path.GetFileName(resolvedSource);

        if (string.IsNullOrEmpty(name) || (isMove && IsProtectedRoot(resolvedSource)))
        {
            // Only a move is refused outright: it takes the root away, whereas
            // copying one somewhere writable is an ordinary thing to want.
            return Failure(path, "A drive or top-level location cannot be moved.");
        }

        // A copy reads the link's target, which is harmless and probably what was
        // meant. A move would take the target away from wherever it really lives.
        if (isMove && IsSymbolicLink(path))
        {
            return Failure(path, SymbolicLinkRefusal);
        }

        var targetPath = Path.Combine(resolvedDestinationFolder, name);

        if (string.Equals(targetPath, resolvedSource, PathComparison))
        {
            return Failure(path, "It is already in that folder.");
        }

        // Copying a folder into itself or into one of its own children would
        // recurse into the copy it is making. Checked before anything is written,
        // because by the time the recursion notices, half a tree exists.
        if (isDirectory && IsWithin(resolvedDestinationFolder, resolvedSource))
        {
            return Failure(path, "A folder cannot be copied or moved into itself.");
        }

        var targetExists = Exists(targetPath);

        if (targetExists && !overwrite)
        {
            return Failure(path, $"'{name}' already exists in the destination folder.", isConflict: true);
        }

        try
        {
            // Only a change of kind forces the destination to be removed first:
            // neither Copy nor Move will write a file over a folder or the other
            // way round. Everything else is overwritten in place, because
            // deleting first and then failing - a full disk, a locked source -
            // leaves the user with neither the old file nor the new one.
            if (targetExists && Directory.Exists(targetPath) != isDirectory)
            {
                DeleteEntry(targetPath);
                targetExists = false;
            }

            var transferredEverything = true;

            if (isMove)
            {
                transferredEverything = MoveEntry(
                    resolvedSource, targetPath, isDirectory, targetExists, overwrite, cancellationToken);
            }
            else if (isDirectory)
            {
                transferredEverything = CopyDirectory(resolvedSource, targetPath, cancellationToken);
            }
            else
            {
                File.Copy(resolvedSource, targetPath, overwrite: true);
            }

            if (!transferredEverything)
            {
                // Reported rather than counted as a success. The source is still
                // there, which is the half that matters - a move that deleted it
                // would have taken the skipped entries with it.
                return Failure(
                    path,
                    "Part of it is outside the folders this server may read and was left behind, "
                    + "so nothing was removed from the original.");
            }

            _logger.LogInformation("{Verb} {Source} to {Target}.", isMove ? "Moved" : "Copied", resolvedSource, targetPath);

            return null;
        }
        catch (OperationCanceledException)
        {
            // Never allowed to unwind past the batch loop: the entries already
            // handled are a real outcome, and losing the whole result to report
            // a cancellation tells the user nothing about what did happen.
            _logger.LogInformation("Transfer of {Source} was cancelled.", resolvedSource);

            return Failure(path, "It was not finished - the request was cancelled.");
        }
        catch (Exception exception) when (IsExpectedFileSystemFailure(exception))
        {
            _logger.LogWarning(
                exception,
                "Could not {Verb} {Source} to {Target}.",
                isMove ? "move" : "copy",
                resolvedSource,
                targetPath);

            return Failure(path, DescribeFailure(exception));
        }
    }

    /// <summary>
    /// Moves an entry, falling back to copy-then-delete where a rename cannot reach.
    /// </summary>
    /// <remarks>
    /// <see cref="Directory.Move(string, string)"/> is a rename, and a rename can
    /// neither cross a volume boundary - exactly what dragging a folder from C: to
    /// D: in the two panes asks for - nor write into a folder that is already
    /// there. Both end in the same fallback: copy the tree across, then drop the
    /// original, and only ever in that order.
    /// </remarks>
    /// <returns>
    /// False when the copy left something behind, and then the source has
    /// deliberately not been deleted. Deleting it would take the skipped entries
    /// with it, which is the one outcome a move must never produce.
    /// </returns>
    private bool MoveEntry(
        string sourcePath,
        string targetPath,
        bool isDirectory,
        bool targetExists,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        if (!isDirectory)
        {
            // The caller has already turned a collision into a conflict unless
            // the user agreed to replace, so by here overwriting is the answer.
            File.Move(sourcePath, targetPath, overwrite: true);
            return true;
        }

        if (!targetExists)
        {
            try
            {
                Directory.Move(sourcePath, targetPath);
                return true;
            }
            catch (IOException exception)
            {
                _logger.LogInformation(
                    exception,
                    "Falling back to copy-and-delete for {Source}; a rename could not reach {Target}.",
                    sourcePath,
                    targetPath);
            }

            // Something arrived at the destination between the check and the
            // move. Merging into it would write over what another writer just
            // put there, which is not what an un-overwritten move agreed to.
            if (Exists(targetPath) && !overwrite)
            {
                throw new IOException(
                    $"'{Path.GetFileName(targetPath)}' appeared in the destination while it was being moved.");
            }
        }

        if (!CopyDirectory(sourcePath, targetPath, cancellationToken))
        {
            return false;
        }

        Directory.Delete(sourcePath, recursive: true);

        return true;
    }

    /// <summary>
    /// Copies a folder and everything under it.
    /// </summary>
    /// <remarks>
    /// The walk is iterative rather than recursive: a deep tree is a stack
    /// overflow, and that takes the whole process down rather than one request.
    /// Symbolic links inside the tree are re-checked, so a link out of an allowed
    /// root does not become a real copy of whatever it pointed at.
    /// </remarks>
    /// <returns>
    /// False when an entry was skipped for being outside the allowed roots. The
    /// caller needs to know, because a move that deleted the source afterwards
    /// would destroy exactly the entries this refused to copy.
    /// </returns>
    private bool CopyDirectory(string sourcePath, string targetPath, CancellationToken cancellationToken)
    {
        var copiedEverything = true;
        var pending = new Stack<(string Source, string Target)>();
        pending.Push((sourcePath, targetPath));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (currentSource, currentTarget) = pending.Pop();

            Directory.CreateDirectory(currentTarget);

            foreach (var childFilePath in Directory.EnumerateFiles(currentSource))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!_pathGuard.IsAllowedChild(childFilePath))
                {
                    copiedEverything = false;
                    continue;
                }

                File.Copy(childFilePath, Path.Combine(currentTarget, Path.GetFileName(childFilePath)), overwrite: true);
            }

            foreach (var childFolderPath in Directory.EnumerateDirectories(currentSource))
            {
                if (!_pathGuard.IsAllowedChild(childFolderPath))
                {
                    copiedEverything = false;
                    continue;
                }

                pending.Push((childFolderPath, Path.Combine(currentTarget, Path.GetFileName(childFolderPath))));
            }
        }

        return copiedEverything;
    }

    #endregion

    #region Delete

    public FileSystemBatchResult Delete(IList<string>? paths, CancellationToken cancellationToken)
    {
        var result = new FileSystemBatchResult();

        foreach (var path in paths ?? [])
        {
            if (cancellationToken.IsCancellationRequested)
            {
                result.Failures.Add(Failure(path, "It was not deleted - the request was cancelled."));
                continue;
            }

            var failure = DeleteOne(path);

            if (failure is null)
            {
                result.SucceededCount++;
            }
            else
            {
                result.Failures.Add(failure);
            }
        }

        return result;
    }

    private FileSystemItemFailure? DeleteOne(string path)
    {
        if (!_pathGuard.TryResolve(path, out var resolvedPath))
        {
            return Failure(path, "It is outside the folders this server is allowed to read.");
        }

        if (!Exists(resolvedPath))
        {
            return Failure(path, "It no longer exists.");
        }

        // Deleting a drive or an allowed root means deleting everything the user
        // can see, and no confirmation dialog makes that worth allowing through a
        // file browser.
        if (IsProtectedRoot(resolvedPath))
        {
            return Failure(path, "A drive or top-level location cannot be deleted.");
        }

        if (IsSymbolicLink(path))
        {
            return Failure(path, SymbolicLinkRefusal);
        }

        try
        {
            DeleteEntry(resolvedPath);

            _logger.LogInformation("Deleted {Path}.", resolvedPath);

            return null;
        }
        catch (OperationCanceledException)
        {
            return Failure(path, "It was not finished - the request was cancelled.");
        }
        catch (Exception exception) when (IsExpectedFileSystemFailure(exception))
        {
            _logger.LogWarning(exception, "Could not delete {Path}.", resolvedPath);

            return Failure(path, DescribeFailure(exception));
        }
    }

    /// <summary>Removes a file, or a folder with everything under it.</summary>
    private static void DeleteEntry(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
        else
        {
            File.Delete(path);
        }
    }

    #endregion

    #region Shared helpers

    private static bool Exists(string path) => Directory.Exists(path) || File.Exists(path);

    /// <summary>
    /// Whether this is a location the browser must not restructure: a drive root,
    /// or one of the roots the server was configured to expose.
    /// </summary>
    /// <remarks>
    /// The path guard cannot answer this, and that is the trap. An allowed root
    /// resolves <i>inside</i> the allow-list, because it is the allow-list - so a
    /// guard that only asks "may I touch this?" says yes, while renaming or moving
    /// it writes to its parent, which is outside every root there is.
    /// </remarks>
    private bool IsProtectedRoot(string resolvedPath) =>
        Path.GetDirectoryName(resolvedPath) is not { Length: > 0 }
        || _pathGuard.AllowedRoots.Any(allowedRoot => resolvedPath.Equals(allowedRoot, PathComparison));

    /// <summary>
    /// Whether the path the caller named is itself a symbolic link.
    /// </summary>
    /// <remarks>
    /// Asked of the path as given, before the guard has been near it. The guard's
    /// whole job is to resolve links, so what comes out the other side is the
    /// target - and a destructive operation aimed at the target is not what the
    /// user pointed at. Deleting it destroys the real file wherever it lives and
    /// leaves the link behind, dangling.
    /// </remarks>
    private bool IsSymbolicLink(string? requestedPath)
    {
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            return false;
        }

        try
        {
            // Lexical only: Path.GetFullPath does not follow links, which is the
            // entire point of asking here rather than after the guard.
            var lexicalPath = Path.GetFullPath(requestedPath.Trim());

            FileSystemInfo entry = Directory.Exists(lexicalPath)
                ? new DirectoryInfo(lexicalPath)
                : new FileInfo(lexicalPath);

            return entry.LinkTarget is not null;
        }
        catch (Exception exception) when (IsExpectedFileSystemFailure(exception) || exception is PathTooLongException)
        {
            _logger.LogDebug(exception, "Could not inspect {Path} for a link target.", requestedPath);
            return false;
        }
    }

    /// <summary>The sentence shown when a destructive operation is aimed at a link.</summary>
    private const string SymbolicLinkRefusal =
        "It is a symbolic link. Acting on it here would act on whatever it points at, "
        + "somewhere else entirely, so it has been left alone.";

    /// <summary>Whether <paramref name="candidatePath"/> is <paramref name="root"/> or sits under it.</summary>
    private static bool IsWithin(string candidatePath, string root)
    {
        if (candidatePath.Equals(root, PathComparison))
        {
            return true;
        }

        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return candidatePath.StartsWith(rootWithSeparator, PathComparison);
    }

    private static FileSystemItemFailure Failure(string path, string reason, bool isConflict = false) =>
        new()
        {
            Path = path,
            Name = SafeFileName(path),
            Reason = reason,
            IsConflict = isConflict
        };

    /// <summary>
    /// The entry's own name for a message. A path malformed enough that the
    /// framework will not parse it still has to be named somehow, so it names itself.
    /// </summary>
    private static string SafeFileName(string path)
    {
        try
        {
            var name = Path.GetFileName(path);

            return string.IsNullOrEmpty(name) ? path : name;
        }
        catch (ArgumentException)
        {
            return path;
        }
    }

    /// <summary>
    /// The failures a caller can cause, as opposed to a bug. Anything outside this
    /// set is left to propagate rather than being reported as the user's fault.
    /// </summary>
    private static bool IsExpectedFileSystemFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException;

    private static FileSystemOperationStatus StatusFor(Exception exception) => exception switch
    {
        UnauthorizedAccessException => FileSystemOperationStatus.Forbidden,
        DirectoryNotFoundException or FileNotFoundException => FileSystemOperationStatus.NotFound,
        ArgumentException => FileSystemOperationStatus.InvalidName,
        _ => FileSystemOperationStatus.Failed
    };

    /// <summary>
    /// One sentence for the user. The framework's own message names the full path
    /// and sometimes an HRESULT, neither of which belongs in a popup - except for a
    /// plain <see cref="IOException"/>, where it is the only thing that says
    /// whether the disk was full or the file was open in something else.
    /// </summary>
    private static string DescribeFailure(Exception exception) => exception switch
    {
        UnauthorizedAccessException =>
            "The account running UFO does not have permission, or the item is read-only.",
        DirectoryNotFoundException or FileNotFoundException => "It no longer exists.",
        PathTooLongException => "The resulting path would be too long for this system.",
        IOException ioException => ioException.Message,
        _ => "The file system refused the change."
    };

    #endregion
}
