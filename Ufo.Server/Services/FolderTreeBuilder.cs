using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Ufo.Abstractions.Database.Entities;
using Ufo.Extensions;

namespace Ufo.Server.Services;

public interface IFolderTreeBuilder
{
    /// <summary>
    /// Walks <paramref name="rootPath"/> and returns the folder tree for it, with every
    /// file hashed and every folder hash and size rolled up from its contents.
    /// </summary>
    Task<FolderEntity> BuildAsync(string rootPath, SnapshotEntity snapshotEntity, UserEntity userEntity, CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds the folder tree for a snapshot in three passes rather than one recursive
/// walk: enumerate the directories, hash the files, then roll the hashes and sizes
/// up. Splitting it that way is what makes the middle pass - the only expensive one -
/// parallelisable, because a folder hash depends on its children and so cannot be
/// computed until they are all done.
/// </summary>
public sealed class FolderTreeBuilder : IFolderTreeBuilder
{
    /// <summary>
    /// Hashing a file is a sequential read plus SHA-256 over the bytes that come back,
    /// so the useful width is roughly the core count: fewer leaves the CPU waiting on
    /// the disk, many more just multiplies seeks without adding throughput.
    /// </summary>
    private static readonly int DefaultDegreeOfParallelism = Environment.ProcessorCount;

    /// <summary>
    /// The size File.OpenRead uses, which is what this walk used before it was
    /// parallelised, and measurably the best of the sizes tried. A large buffer is
    /// actively harmful once the hashing is spread across every core: at 128 KB, hashing
    /// 3000 small files measured over a hundred times slower, because each file
    /// allocated and then collected a buffer of its own on all the hashing threads at
    /// once. Reading a file larger than this costs nothing extra - SHA-256 asks for more
    /// than the buffer holds, and FileStream then reads straight through it.
    /// </summary>
    private const int FileReadBufferSize = 4096;

    private readonly ILogger<FolderTreeBuilder> _logger;
    private readonly IPathGuard _pathGuard;
    private readonly int _degreeOfParallelism;

    public FolderTreeBuilder(ILogger<FolderTreeBuilder> logger, IPathGuard pathGuard)
        : this(logger, pathGuard, DefaultDegreeOfParallelism)
    {
    }

    /// <summary>
    /// The degree of parallelism is only pinned explicitly by tests that need the
    /// ordering to be reproducible.
    /// </summary>
    public FolderTreeBuilder(ILogger<FolderTreeBuilder> logger, IPathGuard pathGuard, int degreeOfParallelism)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pathGuard = pathGuard ?? throw new ArgumentNullException(nameof(pathGuard));

        if (degreeOfParallelism < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(degreeOfParallelism));
        }

        _degreeOfParallelism = degreeOfParallelism;
    }

    public async Task<FolderEntity> BuildAsync(string rootPath, SnapshotEntity snapshotEntity, UserEntity userEntity, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(snapshotEntity);
        ArgumentNullException.ThrowIfNull(userEntity);

        _logger.LogInformation("Indexing {RootPath} with a parallelism of {DegreeOfParallelism}", rootPath, _degreeOfParallelism);

        var rootFolder = CreateFolderEntity(new DirectoryInfo(rootPath), snapshotEntity, parentFolder: null, userEntity);

        // Every pass is CPU and blocking-I/O work with no asynchronous API worth using,
        // so the whole walk is pushed onto the thread pool in one hop rather than
        // blocking the request thread.
        var fileCount = await Task.Run(
            () =>
            {
                var pendingFiles = EnumerateTree(rootFolder, rootPath, snapshotEntity, userEntity, cancellationToken);
                HashFiles(pendingFiles, cancellationToken);
                CompleteFolderHashesAndSizes(rootFolder);

                return pendingFiles.Count;
            },
            cancellationToken);

        _logger.LogInformation("Indexed {RootPath}: {FileCount} files", rootPath, fileCount);

        return rootFolder;
    }

    /// <summary>
    /// Walks the tree one level at a time, fanning the directories of each level out
    /// across the thread pool. Every folder node is populated by exactly one task, so
    /// the per-node lists are only ever touched by their owner and need no locking.
    /// </summary>
    private List<PendingFile> EnumerateTree(FolderEntity rootFolder, string rootPath, SnapshotEntity snapshotEntity, UserEntity userEntity, CancellationToken cancellationToken)
    {
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = _degreeOfParallelism,
            CancellationToken = cancellationToken
        };

        var walkContext = new WalkContext(snapshotEntity, userEntity);

        // Directory enumeration follows symbolic links and junctions, so a link that
        // points back up its own tree would otherwise be walked forever. Recording where
        // each directory really lives, after links are resolved, bounds the walk.
        walkContext.VisitedDirectories.TryAdd(ResolveCanonicalPath(rootPath), 0);

        var currentLevel = new List<PendingFolder> { new(rootFolder, rootPath) };

        while (currentLevel.Count > 0)
        {
            var nextLevel = new ConcurrentBag<PendingFolder>();

            Parallel.ForEach(currentLevel, parallelOptions, pendingFolder =>
            {
                PopulateFolder(pendingFolder, walkContext, nextLevel);
            });

            currentLevel = [.. nextLevel];
        }

        if (walkContext.ExcludedEntryCount > 0)
        {
            _logger.LogWarning(
                "Excluded {ExcludedEntryCount} entries under {RootPath} from the snapshot: they resolve outside the allowed roots.",
                walkContext.ExcludedEntryCount,
                rootPath);
        }

        return [.. walkContext.PendingFiles];
    }

    /// <summary>
    /// Reads one directory: creates the child folder nodes for the next level and the
    /// file nodes for this one. Hashes are deliberately left empty here - files are
    /// hashed in the pass that follows, folders in the pass after that.
    /// </summary>
    private void PopulateFolder(
        PendingFolder pendingFolder,
        WalkContext walkContext,
        ConcurrentBag<PendingFolder> nextLevel)
    {
        var (folder, path) = pendingFolder;

        foreach (var subFolderPath in Directory.EnumerateDirectories(path))
        {
            // Guarding only the path the snapshot was requested for is not enough. This
            // walk descends into whatever it enumerates, and enumeration follows
            // symbolic links and junctions, so one link inside an allowed root leads out
            // of it - and the snapshot would then record the name, size and SHA-256 of
            // every file below wherever it points. Rejected entries are left out of the
            // tree entirely rather than merely not descended into, because the folder
            // name alone is already more than the caller is allowed to see.
            if (!IsReadable(subFolderPath, walkContext))
            {
                continue;
            }

            var subFolder = CreateFolderEntity(new DirectoryInfo(subFolderPath), walkContext.Snapshot, folder, walkContext.User);
            folder.ChildFolders.Add(subFolder);

            // A directory already reached by another route - a link or junction pointing
            // back up its own tree - is still part of the tree and stays in it, but
            // descending into it a second time would never terminate.
            if (walkContext.VisitedDirectories.TryAdd(ResolveCanonicalPath(subFolderPath), 0))
            {
                nextLevel.Add(new PendingFolder(subFolder, subFolderPath));
            }
        }

        foreach (var filePath in Directory.EnumerateFiles(path))
        {
            // A symbolic link to a file outside the allowed roots is the same escape as
            // the directory case above, one file at a time.
            if (!IsReadable(filePath, walkContext))
            {
                continue;
            }

            var fileInfo = new FileInfo(filePath);
            var file = new FileEntity
            {
                Name = Path.GetFileNameWithoutExtension(fileInfo.Name),
                // Replaced during the hashing pass with the length of the handle the hash
                // is actually taken over, so size and hash always describe the same bytes.
                Size = fileInfo.Length,
                Sha256Hash = string.Empty,
                FileExtension = fileInfo.Extension,
                User = walkContext.User,
                UserId = walkContext.User.Id,
                CreatedAt = fileInfo.CreationTimeUtc.ToString("o"),
                UpdatedAt = fileInfo.LastWriteTimeUtc.ToString("o"),
                IsHidden = (fileInfo.Attributes & FileAttributes.Hidden) != 0
            };

            file.Snapshots.Add(walkContext.Snapshot);
            file.ParentFolders.Add(folder);
            folder.Files.Add(file);

            walkContext.PendingFiles.Add(new PendingFile(file, filePath));
        }
    }

    private static FolderEntity CreateFolderEntity(DirectoryInfo directoryInfo, SnapshotEntity snapshotEntity, FolderEntity? parentFolder, UserEntity userEntity)
    {
        var folder = new FolderEntity
        {
            Name = directoryInfo.Name,
            Sha256Hash = string.Empty,
            User = userEntity,
            UserId = userEntity.Id,
            CreatedAt = directoryInfo.CreationTimeUtc.ToString("o"),
            UpdatedAt = directoryInfo.CreationTimeUtc.ToString("o"),
            IsHidden = (directoryInfo.Attributes & FileAttributes.Hidden) != 0
        };

        folder.Snapshots.Add(snapshotEntity);

        if (parentFolder is not null)
        {
            folder.ParentFolders.Add(parentFolder);
        }

        return folder;
    }

    /// <summary>
    /// The expensive pass. Files are hashed off one flat list rather than per folder, so
    /// a directory holding one large file cannot leave the other workers idle.
    /// </summary>
    /// <remarks>
    /// Deliberately synchronous. Reading a file is blocking work on every platform the
    /// application ships to - asynchronous file I/O is emulated on Unix - and measured
    /// side by side at the same buffer size, Parallel.ForEachAsync was consistently
    /// several times slower than Parallel.ForEach here, paying more per file in
    /// scheduling than the read itself costs.
    /// </remarks>
    private void HashFiles(IReadOnlyList<PendingFile> pendingFiles, CancellationToken cancellationToken)
    {
        if (pendingFiles.Count == 0)
        {
            return;
        }

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = _degreeOfParallelism,
            CancellationToken = cancellationToken
        };

        Parallel.ForEach(pendingFiles, parallelOptions, pendingFile =>
        {
            var (sha256Hash, sizeInBytes) = ComputeFileHashSha256(pendingFile.Path);
            pendingFile.Entity.Sha256Hash = sha256Hash;
            pendingFile.Entity.Size = sizeInBytes;
        });
    }

    /// <summary>
    /// Returns the hash and the length of the same open handle. Enumeration recorded a
    /// length earlier, but a file written to in between would leave that length
    /// describing different bytes than the hash does - and the pair is the de-duplication
    /// key, so the two have to agree.
    /// </summary>
    private static (string Sha256Hash, long SizeInBytes) ComputeFileHashSha256(string filePath)
    {
        // FileShare.ReadWrite | FileShare.Delete keeps a file that something else is
        // writing from failing the whole snapshot; the hash is then simply of whatever
        // the bytes were at read time, which is all a point-in-time snapshot claims.
        using var fileStream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            FileReadBufferSize,
            FileOptions.SequentialScan);

        var sizeInBytes = fileStream.Length;

        return (Convert.ToHexStringLower(SHA256.HashData(fileStream)), sizeInBytes);
    }

    /// <summary>
    /// Whether the snapshot is allowed to record <paramref name="entryPath"/>, which
    /// this walk enumerated from a directory it had already resolved and allowed.
    /// </summary>
    /// <remarks>
    /// Deliberately the child form of the check rather than a full resolution of every
    /// entry. This walk is the one place in the application that visits every file on a
    /// library, and re-resolving each entry's whole ancestor chain would add syscalls
    /// proportional to depth to all of them - undoing the parallelisation the walk was
    /// built for. The containing directory is already known to sit inside an allowed
    /// root, so only an entry that is itself a link can lead out of one.
    /// </remarks>
    private bool IsReadable(string entryPath, WalkContext walkContext)
    {
        if (_pathGuard.IsAllowedChild(entryPath))
        {
            return true;
        }

        walkContext.RecordExcludedEntry();

        return false;
    }

    /// <summary>
    /// Where a directory really lives once symbolic links and junctions are resolved.
    /// Falls back to the path as given when the target cannot be resolved, which at worst
    /// costs the guard one duplicate rather than breaking the walk.
    /// </summary>
    private static string ResolveCanonicalPath(string directoryPath)
    {
        try
        {
            return new DirectoryInfo(directoryPath).ResolveLinkTarget(returnFinalTarget: true)?.FullName
                   ?? Path.GetFullPath(directoryPath);
        }
        catch (IOException)
        {
            return Path.GetFullPath(directoryPath);
        }
        catch (UnauthorizedAccessException)
        {
            return Path.GetFullPath(directoryPath);
        }
    }

    /// <summary>
    /// Post-order rollup: a folder's hash and size are only defined once every child
    /// folder below it has one. Iterative rather than recursive so that a deep tree is
    /// bounded by the disk rather than by the call stack.
    /// </summary>
    private static void CompleteFolderHashesAndSizes(FolderEntity rootFolder)
    {
        var expandedFolders = new HashSet<FolderEntity>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<(FolderEntity Folder, bool ChildrenVisited)>();
        stack.Push((rootFolder, false));

        while (stack.Count > 0)
        {
            var (folder, childrenVisited) = stack.Pop();

            if (childrenVisited)
            {
                // Parallel enumeration returns directories in whatever order the workers
                // finished, so the lists are sorted here to keep a snapshot of the same
                // tree byte-for-byte reproducible. This is the order the hash below
                // already used, so no hash changes because of it.
                folder.Files = [.. folder.Files.OrderBy(file => file.Name)];
                folder.ChildFolders = [.. folder.ChildFolders.OrderBy(childFolder => childFolder.Name)];

                folder.Sha256Hash = GetFolderSha256Hash(folder);
                folder.Size = folder.Files.Sum(file => file.Size) + folder.ChildFolders.Sum(childFolder => childFolder.Size);

                continue;
            }

            // A folder entity reachable through more than one parent only has to be
            // rolled up once.
            if (!expandedFolders.Add(folder))
            {
                continue;
            }

            stack.Push((folder, true));

            foreach (var childFolder in folder.ChildFolders)
            {
                stack.Push((childFolder, false));
            }
        }
    }

    /// <summary>
    /// A folder's identity is the sorted list of what it directly contains, by name and
    /// hash. Because child folders contribute their own hash the result covers the
    /// whole subtree.
    /// </summary>
    private static string GetFolderSha256Hash(FolderEntity folder)
    {
        var stringBuilder = new StringBuilder();
        var orderedFiles = folder.Files.OrderBy(file => file.Name);

        foreach (var file in orderedFiles)
        {
            var fileNameWithExtension = $"{file.Name}.{file.FileExtension}";
            if (string.IsNullOrWhiteSpace(fileNameWithExtension))
            {
                throw new ArgumentException(nameof(fileNameWithExtension));
            }

            if (string.IsNullOrWhiteSpace(file.Sha256Hash))
            {
                throw new ArgumentException(nameof(file.Sha256Hash));
            }

            stringBuilder.AppendLine($"{fileNameWithExtension},{file.Sha256Hash}");
        }

        var orderedSubfolders = folder.ChildFolders.OrderBy(childFolder => childFolder.Name);
        foreach (var subfolder in orderedSubfolders)
        {
            if (string.IsNullOrWhiteSpace(subfolder.Name))
            {
                throw new ArgumentException(nameof(subfolder.Name));
            }

            if (string.IsNullOrWhiteSpace(subfolder.Sha256Hash))
            {
                throw new ArgumentException(nameof(subfolder.Sha256Hash));
            }

            stringBuilder.AppendLine($"{subfolder.Name},{subfolder.Sha256Hash}");
        }

        var dataString = stringBuilder.ToString();

        return dataString.GetHashSha256();
    }

    private readonly record struct PendingFolder(FolderEntity Entity, string Path);

    private readonly record struct PendingFile(FileEntity Entity, string Path);

    /// <summary>
    /// The state one walk shares across its levels and its worker threads. The builder
    /// itself is a singleton and so cannot hold any of this in a field.
    /// </summary>
    private sealed class WalkContext(SnapshotEntity snapshot, UserEntity user)
    {
        private int _excludedEntryCount;

        public SnapshotEntity Snapshot { get; } = snapshot;

        public UserEntity User { get; } = user;

        public ConcurrentBag<PendingFile> PendingFiles { get; } = [];

        /// <summary>Resolved physical paths of the directories already descended into.</summary>
        public ConcurrentDictionary<string, byte> VisitedDirectories { get; } = new(StringComparer.Ordinal);

        /// <summary>Entries left out because they resolve outside the allowed roots.</summary>
        public int ExcludedEntryCount => Volatile.Read(ref _excludedEntryCount);

        public void RecordExcludedEntry() => Interlocked.Increment(ref _excludedEntryCount);
    }
}
