using Newtonsoft.Json;
using SQLite;

namespace Ufo.Abstractions.Database.Entities
{
    public abstract class FsItemEntity: EntityBase
    {
        public long? Size { get; set; }

        [NotNull]
        [MaxLength(128)]
        public string Sha256Hash { get; set; }

        [Ignore]
        public string FullPath { get; set; }

        [Ignore]
        //[JsonIgnore]
        public bool HasParent { get; set; }

        [Ignore]
        //[JsonIgnore]
        public bool IsHidden { get; set; }
    }
}
