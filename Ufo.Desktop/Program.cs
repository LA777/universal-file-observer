using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Serilog;
using System.Reflection;
using Ufo.Abstractions.DataProviders;
using Ufo.Abstractions.Options;
using Ufo.DataProviders;
using Ufo.Platform.Windows;
using Ufo.Server.Hosting;

namespace Ufo.Desktop;

/// <summary>
/// Windows desktop entry point: runs the web host in the background and puts a
/// tray icon in the notification area.
/// </summary>
/// <remarks>
/// Shares its entire composition with the headless host through
/// <see cref="UfoHost.Build"/>; only the three things that genuinely differ live
/// here - the Windows system-info provider, the embedded front end and the tray
/// icon. See <c>_docs/AI_DUAL_TARGET_PLAN.md</c>.
/// </remarks>
internal static class Program
{
    private const string SingleInstanceMutexName = @"Global\UniversalFileObserver.Desktop";
    private const string EmbeddedWebRootNamespace = "wwwroot";

    [STAThread]
    private static void Main(string[] commandLineArguments)
    {
        using var singleInstanceMutex = TryAcquireSingleInstanceMutex(out var isFirstInstance);

        if (!isFirstInstance)
        {
            MessageBox.Show(
                "UFO is already running. Look for its icon in the notification area.",
                "Universal File Observer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();

        WebApplication? app = null;

        try
        {
            var hostOptions = CreateHostOptions();

            app = UfoHost.Build(
                commandLineArguments,
                hostOptions,
                ReplaceServicesWithWindowsImplementations,
                CreateEmbeddedWebRootFileProvider());

            app.StartAsync().GetAwaiter().GetResult();

            var applicationUrl = UfoHost.ResolveApplicationUrl(app.Configuration);

            using var trayApplicationContext = new TrayApplicationContext(applicationUrl, hostOptions.DataDirectory);
            Application.Run(trayApplicationContext);

            using var shutdownTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            app.StopAsync(shutdownTokenSource.Token).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Application terminated unexpectedly");

            // There is no console to read, so the failure has to be shown.
            MessageBox.Show(
                $"UFO could not start.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Universal File Observer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            (app as IDisposable)?.Dispose();
            Log.CloseAndFlush();
        }
    }

    /// <summary>
    /// Claims the global single-instance mutex.
    /// </summary>
    /// <remarks>
    /// A "Global\" mutex created by another user's session is not necessarily
    /// openable by this one, and the resulting exception has to be caught here:
    /// this is a WinExe with no console, so an unhandled throw at startup is a
    /// silent failure to the user. A mutex that cannot be claimed is treated as
    /// "not the first instance", which is the safe reading.
    /// </remarks>
    private static Mutex? TryAcquireSingleInstanceMutex(out bool isFirstInstance)
    {
        try
        {
            var singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out isFirstInstance);

            return singleInstanceMutex;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
        {
            isFirstInstance = false;
            return null;
        }
    }

    private static UfoHostOptions CreateHostOptions()
    {
        var hostOptions = UfoHostOptions.ForDesktop();

        // An installed executable cannot write beside itself under Program Files,
        // so the database, logs and generated machine id live in the user profile.
        hostOptions.DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UFO");

        // Opted into here rather than in ForDesktop, so that only an installation
        // with the per-user data directory set above generates its own signing
        // key. Nobody is around to set a secret for a tray application, and a key
        // shipped in the executable would be shared by every installation.
        hostOptions.GenerateJwtKeyWhenMissing = true;

        return hostOptions;
    }

    /// <summary>
    /// Swaps the POSIX system-info provider for the WMI-backed Windows one.
    /// </summary>
    private static void ReplaceServicesWithWindowsImplementations(IServiceCollection services)
    {
        services.RemoveAll<ISystemInfoProvider>();
        services.AddTransient<ISystemInfoProvider, WindowsSystemInfoProvider>();
    }

    /// <summary>
    /// Serves the Angular bundle out of the executable itself.
    /// </summary>
    /// <returns>
    /// The embedded provider, or <c>null</c> to fall back to a wwwroot directory
    /// on disk - which is what a plain <c>dotnet run</c> of this project does
    /// before the front end has been built.
    /// </returns>
    private static IFileProvider? CreateEmbeddedWebRootFileProvider()
    {
        var assembly = Assembly.GetExecutingAssembly();

        try
        {
            var embeddedFileProvider = new ManifestEmbeddedFileProvider(assembly, EmbeddedWebRootNamespace);

            return embeddedFileProvider.GetFileInfo("index.html").Exists ? embeddedFileProvider : null;
        }
        catch (InvalidOperationException)
        {
            // No embedded manifest - the front end was not built into this binary.
            return null;
        }
    }
}
