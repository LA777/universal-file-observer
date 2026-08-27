using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Requests;

public record SearchRequest
{
    private string _query = string.Empty;

    // Optional when other criteria are set; when non-empty must be >= 3 chars (validated in controller).
    public string Query
    {
        get => _query;
        set => _query = value?.Trim() ?? string.Empty;
    }

    public bool IncludeFolders { get; set; } = true;

    public bool IncludeFiles { get; set; } = true;

    /// <summary>File extension filter, e.g. ".mp4". Files only.</summary>
    [MaxLength(128)]
    public string? Extension { get; set; }

    /// <summary>Minimum size in bytes.</summary>
    public long? MinSize { get; set; }

    /// <summary>Maximum size in bytes.</summary>
    public long? MaxSize { get; set; }

    /// <summary>Snapshot timestamp range start.</summary>
    public DateTimeOffset? DateFrom { get; set; }

    /// <summary>Snapshot timestamp range end.</summary>
    public DateTimeOffset? DateTo { get; set; }

    /// <summary>Restrict the search to these snapshots (empty = all).</summary>
    public List<Ulid> SnapshotIds { get; set; } = [];

    /// <summary>Restrict the search to snapshots carrying these labels (empty = all).</summary>
    public List<Ulid> LabelIds { get; set; } = [];

    [JsonIgnore]
    public bool HasAnyCriteria =>
        Query.Length > 0
        || !string.IsNullOrWhiteSpace(Extension)
        || MinSize.HasValue
        || MaxSize.HasValue
        || DateFrom.HasValue
        || DateTo.HasValue
        || SnapshotIds.Count > 0
        || LabelIds.Count > 0;
}
