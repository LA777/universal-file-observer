namespace Ufo.Server.Models;

public class FileSystemRoot
{
    /// <summary>
    /// Top-level locations the user can jump to: drive letters on Windows, the
    /// configured allowed roots when the host restricts access, and "/" otherwise.
    /// </summary>
    public IList<string> Roots { get; set; } = [];

    public FsFolder? Folder { get; set; }

    /// <summary>
    /// What this host will accept as a name, so the client can reject a bad one
    /// while it is still being typed. Sent with the root because that is the one
    /// call every panel already makes before it can show anything.
    /// </summary>
    public FileNameRules NameRules { get; set; } = new();
}
