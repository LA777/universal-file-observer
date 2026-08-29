using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Ufo.Server.Hosting;

namespace Ufo.Desktop;

/// <summary>
/// The notification-area icon and its menu. Owns the Windows message loop for
/// the lifetime of the application; the web host runs alongside it.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string IconResourceName = "Ufo.Desktop.Resources.ufo.ico";

    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly string _applicationUrl;
    private readonly string _dataDirectory;

    public TrayApplicationContext(string applicationUrl, string dataDirectory)
    {
        _applicationUrl = applicationUrl;
        _dataDirectory = dataDirectory;

        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.Add("&Open UFO", null, (_, _) => OpenApplication());
        _contextMenu.Items.Add("&Copy address", null, (_, _) => CopyApplicationUrl());
        _contextMenu.Items.Add("Open &data folder", null, (_, _) => OpenDataDirectory());
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add("E&xit", null, (_, _) => ExitApplication());

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            // The tooltip is capped at 63 characters by the shell.
            Text = Truncate($"UFO - {_applicationUrl}", 63),
            Visible = true,
            ContextMenuStrip = _contextMenu
        };
        _notifyIcon.DoubleClick += (_, _) => OpenApplication();

        _notifyIcon.ShowBalloonTip(
            3000,
            "Universal File Observer",
            $"Running at {_applicationUrl}",
            ToolTipIcon.Info);
    }

    private void OpenApplication() => BrowserLauncher.TryOpen(_applicationUrl);

    private void CopyApplicationUrl()
    {
        try
        {
            Clipboard.SetText(_applicationUrl);
        }
        catch (ExternalException exception)
        {
            // Another process can hold the clipboard open; nothing worth
            // interrupting the user over.
            Debug.WriteLine(exception);
        }
    }

    private void OpenDataDirectory()
    {
        try
        {
            if (!Directory.Exists(_dataDirectory))
            {
                Directory.CreateDirectory(_dataDirectory);
            }

            Process.Start(new ProcessStartInfo(_dataDirectory) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            MessageBox.Show(
                $"Could not open {_dataDirectory}.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Universal File Observer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void ExitApplication()
    {
        _notifyIcon.Visible = false;
        ExitThread();
    }

    private static Icon LoadTrayIcon()
    {
        using var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(IconResourceName);

        return iconStream == null ? SystemIcons.Application : new Icon(iconStream);
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _contextMenu.Dispose();
        }

        base.Dispose(disposing);
    }
}
