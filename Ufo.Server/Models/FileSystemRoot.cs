using Ufo.Abstractions.Database.Entities;

namespace Ufo.Server.Models
{
    public class FileSystemRoot
    {
        public IList<string> Drives { get; set; } = new List<string>();
        public FsFolder? Folder { get; set; }
    }
}
