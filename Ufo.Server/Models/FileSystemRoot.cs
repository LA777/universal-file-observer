namespace Ufo.Server.Models;

public class FileSystemRoot
{
    /// <summary>
    /// Top-level locations the user can jump to: drive letters on Windows, the
    /// configured allowed roots when the host restricts access, and "/" otherwise.
    /// </summary>
    public IList<string> Roots { get; set; } = [];

    public FsFolder? Folder { get; set; }
}
