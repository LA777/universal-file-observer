using Cysharp.Serialization.Json;
using SQLite;
using SQLiteNetExtensions.Attributes;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.Database.Entities;

/// <summary>
/// A tag a file carried when a snapshot was taken.
/// </summary>
/// <remarks>
/// <para>
/// Keyed on the whole of <c>FilesToFolders</c>, because that association is the
/// only thing in the schema unique to one file, in one snapshot, under one
/// parent - the <c>Files</c> row itself is shared by every byte-identical copy.
/// </para>
/// <para>
/// A flag and a rating fit in a column on that association. A tag cannot: there
/// can be any number of them, which is what makes this a table of its own rather
/// than more columns.
/// </para>
/// </remarks>
[Table("TagsToSnapshotFiles")]
public class TagsToSnapshotFileEntity
{
    [JsonConverter(typeof(UlidJsonConverter))]
    [ForeignKey(typeof(SnapshotEntity))]
    public Ulid SnapshotId { get; set; }

    [JsonConverter(typeof(UlidJsonConverter))]
    [ForeignKey(typeof(FolderEntity))]
    public Ulid FolderId { get; set; }

    [JsonConverter(typeof(UlidJsonConverter))]
    [ForeignKey(typeof(FileEntity))]
    public Ulid FileId { get; set; }

    [JsonConverter(typeof(UlidJsonConverter))]
    [ForeignKey(typeof(TagEntity))]
    public Ulid TagId { get; set; }
}

/// <summary>
/// A tag a folder carried when a snapshot was taken. As above, keyed on the
/// whole of <c>FoldersToFolders</c>.
/// </summary>
[Table("TagsToSnapshotFolders")]
public class TagsToSnapshotFolderEntity
{
    [JsonConverter(typeof(UlidJsonConverter))]
    [ForeignKey(typeof(SnapshotEntity))]
    public Ulid SnapshotId { get; set; }

    /// <summary>Null for the root folder, exactly as on the association itself.</summary>
    [JsonConverter(typeof(UlidJsonConverter))]
    [ForeignKey(typeof(FolderEntity))]
    public Ulid? ParentFolderId { get; set; }

    [JsonConverter(typeof(UlidJsonConverter))]
    [ForeignKey(typeof(FolderEntity))]
    public Ulid ChildFolderId { get; set; }

    [JsonConverter(typeof(UlidJsonConverter))]
    [ForeignKey(typeof(TagEntity))]
    public Ulid TagId { get; set; }
}
