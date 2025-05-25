namespace Ufo.Abstractions.Responses;

public class FileSystemRoot
{
    public IList<string> Drives { get; set; } = [];
    public FsFolder? Folder { get; set; }
}
