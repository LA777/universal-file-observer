using System.Text.Json.Serialization;
using Cysharp.Serialization.Json;

namespace Ufo.Abstractions.DataTransferObjects;

/// <summary>
/// What a user takes away from the Settings page's Export section: one JSON
/// document, with the parts that were asked for and nothing for the rest.
/// </summary>
/// <remarks>
/// A single shape for every scope rather than one document type per button, so
/// a reader of the file - a later import, a script, a person - checks the same
/// two header fields whichever button produced it. A section not asked for is
/// null and, with nulls left out of the JSON, absent from the file.
/// </remarks>
public class ExportDocument
{
    public const string FormatName = "ufo-export";
    public const int CurrentFormatVersion = 1;

    [JsonPropertyOrder(1)]
    public string Format { get; set; } = FormatName;

    /// <summary>Bumped when the shape below changes in a way a reader must know about.</summary>
    [JsonPropertyOrder(2)]
    public int FormatVersion { get; set; } = CurrentFormatVersion;

    /// <summary>The build that wrote the file - three segments, as <c>GET /api/version</c> reports it.</summary>
    [JsonPropertyOrder(3)]
    public string ApplicationVersion { get; set; } = string.Empty;

    [JsonPropertyOrder(4)]
    public DateTimeOffset ExportedAt { get; set; }

    /// <summary>Which button produced the file: user, filesystem, snapshots or all.</summary>
    [JsonPropertyOrder(5)]
    public string Scope { get; set; } = string.Empty;

    [JsonPropertyOrder(10)]
    public UserExport? User { get; set; }

    [JsonPropertyOrder(11)]
    public FileSystemExport? FileSystem { get; set; }

    [JsonPropertyOrder(12)]
    public SnapshotsExport? Snapshots { get; set; }
}

/// <summary>
/// The account and everything the user has arranged about the application:
/// theme, shortcuts, locked folder tabs. The password hash is never part of it.
/// </summary>
public class UserExport
{
    [JsonConverter(typeof(UlidJsonConverter))]
    [JsonPropertyOrder(1)]
    public Ulid Id { get; set; }

    [JsonPropertyOrder(2)]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyOrder(3)]
    public string CreatedAt { get; set; } = string.Empty;

    [JsonPropertyOrder(4)]
    public bool IsAdmin { get; set; }

    [JsonPropertyOrder(5)]
    public UserSettingsDto Settings { get; set; } = new();

    /// <summary>Every action with the keys in force, defaults included - the same list the Settings page shows.</summary>
    [JsonPropertyOrder(6)]
    public List<KeyBindingDto> KeyBindings { get; set; } = [];

    [JsonPropertyOrder(7)]
    public List<FolderTabExport> FolderTabs { get; set; } = [];
}

/// <summary>A locked folder tab as stored, not as restored - no availability check, since the file may be read elsewhere.</summary>
public class FolderTabExport
{
    [JsonPropertyOrder(1)]
    public string PanelId { get; set; } = string.Empty;

    [JsonPropertyOrder(2)]
    public string FolderPath { get; set; } = string.Empty;

    [JsonPropertyOrder(3)]
    public int Position { get; set; }
}

/// <summary>What the user has marked on files and folders on disk.</summary>
public class FileSystemExport
{
    [JsonPropertyOrder(1)]
    public List<string> FlaggedPaths { get; set; } = [];

    /// <summary>Rating by path, 1 to 10. Unrated paths have no entry.</summary>
    [JsonPropertyOrder(2)]
    public Dictionary<string, int> Ratings { get; set; } = [];

    [JsonPropertyOrder(3)]
    public List<TagDto> Tags { get; set; } = [];

    /// <summary>Tag ids by path, for the paths that carry any.</summary>
    [JsonPropertyOrder(4)]
    public Dictionary<string, List<Ulid>> TagIdsByPath { get; set; } = [];
}

/// <summary>Every snapshot with its whole tree, and the labels they are filed under.</summary>
public class SnapshotsExport
{
    [JsonPropertyOrder(1)]
    public List<LabelDto> Labels { get; set; } = [];

    [JsonPropertyOrder(2)]
    public List<SnapshotDto> Snapshots { get; set; } = [];
}
