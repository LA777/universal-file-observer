namespace Ufo.Abstractions.Requests;

/// <summary>Criteria for a live (non-indexed) search of the local file system.</summary>
public record FileSystemSearchRequest
{
    /// <summary>Root folder to search under. Required.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Case-insensitive name substring; empty matches everything.</summary>
    public string Query { get; set; } = string.Empty;

    public bool IncludeFiles { get; set; } = true;

    public bool IncludeFolders { get; set; } = true;

    /// <summary>File extension filter, e.g. ".mp4". Files only.</summary>
    public string? Extension { get; set; }

    /// <summary>Minimum size in bytes (files only).</summary>
    public long? MinSize { get; set; }

    /// <summary>Maximum size in bytes (files only).</summary>
    public long? MaxSize { get; set; }

    /// <summary>Last-write-time range start.</summary>
    public DateTimeOffset? DateFrom { get; set; }

    /// <summary>Last-write-time range end.</summary>
    public DateTimeOffset? DateTo { get; set; }

    /// <summary>Result cap; clamped server-side to 1..2000.</summary>
    public int MaxResults { get; set; } = 500;
}
