using SQLite;
using Ufo.Abstractions.Database.Entities;

namespace Ufo.Server.Models
{
    public class FsFolder : FsItem
    {
        public IList<FsFile> Files { get; set; } = new List<FsFile>();

        public IList<FsFolder> ChildFolders { get; set; } = new List<FsFolder>();
    }

    public abstract class FsItem
    {
        public string? Name { get; set; }

        public long? Size { get; set; }

        public string? Sha256Hash { get; set; }

        public string? FullPath { get; set; }

        public bool IsHidden { get; set; }

        public FsFolder? ParentFolder { get; set; }
    }

    public class FsFile : FsItem
    {
        [MaxLength(128)]
        public string? FileExtension { get; set; }

        public IList<SnapshotEntity> Snapshots { get; } = new List<SnapshotEntity>();
    }
}
