using System.Diagnostics;

namespace Ufo.Server.Hosting;

/// <summary>
/// Opens the application URL in the user's default browser.
/// </summary>
public static class BrowserLauncher
{
    /// <summary>
    /// Attempts to open <paramref name="url"/> and reports whether it worked.
    /// </summary>
    /// <remarks>
    /// Never throws. The previous implementation threw for unknown platforms and
    /// let a failed <see cref="Process.Start(ProcessStartInfo)"/> propagate, which
    /// killed startup in any environment without a browser - notably a container,
    /// where "xdg-open" is not installed.
    /// </remarks>
    public static bool TryOpen(string url, ILogger? logger = null)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", url);
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start("xdg-open", url);
            }
            else
            {
                logger?.LogWarning("Cannot open a browser on this platform; navigate to {Url} manually.", url);
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "Could not open a browser; navigate to {Url} manually.", url);
            return false;
        }
    }
}
