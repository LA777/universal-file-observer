using SQLite;
using Ufo.Abstractions.Database.Entities;

namespace Ufo.Server.Models;

public class FsFolder : FsItem
{
    public IList<FsFile> Files { get; set; } = [];

    public IList<FsFolder> ChildFolders { get; set; } = [];
}

public abstract class FsItem
{
    public string? Name { get; set; }

    public long? Size { get; set; }

    public string? Sha256Hash { get; set; }

    public string? FullPath { get; set; }

    public bool IsHidden { get; set; }

    public FsFolder? ParentFolder { get; set; }

    // Serialized for the client: the UI models expect hasParent/parentFolderPath
    // flags rather than the nested parentFolder object.
    public bool HasParent => ParentFolder != null;

    public string? ParentFolderPath => ParentFolder?.FullPath;
}

public class FsFile : FsItem
{
    [MaxLength(128)]
    public string? FileExtension { get; set; }

    public IList<SnapshotEntity> Snapshots { get; } = new List<SnapshotEntity>();
}
