namespace Ufo.Server.Models;

/// <summary>How a file-system operation ended, in terms the controller maps to a status.</summary>
public enum FileSystemOperationStatus
{
    Success,

    /// <summary>The name is not one the host can store. The reason names the rule.</summary>
    InvalidName,

    /// <summary>Outside the allowed roots, or unreadable by the account running UFO.</summary>
    Forbidden,

    /// <summary>The source is no longer on disk.</summary>
    NotFound,

    /// <summary>Something is already there. Retrying with overwrite is the caller's next move.</summary>
    Conflict,

    /// <summary>The host refused the write itself - a lock, a read-only volume, a full disk.</summary>
    Failed
}

/// <summary>The outcome of an operation on a single entry.</summary>
public class FileSystemOperationResult
{
    public FileSystemOperationStatus Status { get; set; }

    /// <summary>One sentence naming what went wrong, or null on success.</summary>
    public string? Message { get; set; }

    /// <summary>Where the entry ended up, set only on success.</summary>
    public string? Path { get; set; }

    public bool IsSuccess => Status == FileSystemOperationStatus.Success;

    public static FileSystemOperationResult Succeeded(string path) =>
        new() { Status = FileSystemOperationStatus.Success, Path = path };

    public static FileSystemOperationResult Rejected(FileSystemOperationStatus status, string message) =>
        new() { Status = status, Message = message };
}

/// <summary>One entry a batch operation could not handle, and why.</summary>
public class FileSystemItemFailure
{
    public string Path { get; set; } = string.Empty;

    /// <summary>The entry's own name, so a message can read without the full path.</summary>
    public string Name { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// True when the only obstacle was something already at the destination. The
    /// client offers to replace exactly these, rather than re-sending the whole
    /// batch with overwrite set and destroying entries that failed for other reasons.
    /// </summary>
    public bool IsConflict { get; set; }
}

/// <summary>
/// The outcome of an operation over several entries.
/// </summary>
/// <remarks>
/// Copying twenty files where one is locked is not a failed request: nineteen of
/// them moved and the user needs to be told which one did not. So a partial
/// failure comes back as a 200 carrying the detail, and only a wholly invalid
/// request gets an error status.
/// </remarks>
public class FileSystemBatchResult
{
    public int SucceededCount { get; set; }

    public IList<FileSystemItemFailure> Failures { get; set; } = [];

    public bool HasFailures => Failures.Count > 0;
}
