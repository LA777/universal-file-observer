namespace Ufo.Server.Models;

/// <summary>One hit of a live file-system search.</summary>
public class FsSearchResult
{
    public string Name { get; set; } = string.Empty;

    public string FullPath { get; set; } = string.Empty;

    public bool IsFile { get; set; }

    public long? Size { get; set; }

    public string? FileExtension { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }

    public bool IsHidden { get; set; }
}
