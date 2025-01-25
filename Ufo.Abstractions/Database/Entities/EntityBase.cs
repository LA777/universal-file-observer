using Newtonsoft.Json;
using SQLite;

namespace Ufo.Abstractions.Database.Entities
{
    public abstract class EntityBase
    {
        [PrimaryKey]
        public Guid Guid { get; set; } = Guid.NewGuid();

        [NotNull]
        [MaxLength(256)]
        public string Name { get; set; }
    }
}
