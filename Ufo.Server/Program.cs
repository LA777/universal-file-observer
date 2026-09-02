using Serilog;
using Ufo.Abstractions.Options;
using Ufo.Server.Hosting;
using Ufo.Server.Services;

// Container HEALTHCHECK path: probe a running instance and exit with its verdict
// instead of starting a second host.
if (HealthCheckCommand.IsRequested(args))
{
    return await HealthCheckCommand.RunAsync();
}

// The version comes from <Version> in Ufo.Server.csproj by way of the assembly
// metadata, so this line and GET /api/version can never disagree.
Console.WriteLine($"App started. Version: {ApplicationVersionService.Current}");

// Headless entry point: `dotnet run`, and the container image.
// The Windows desktop tray application has its own entry point in Ufo.Desktop and
// shares this host through UfoHost.Build. See _docs/AI_DUAL_TARGET_PLAN.md.
var isRunningInContainer = string.Equals(
    Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
    "true",
    StringComparison.OrdinalIgnoreCase);

var hostOptions = isRunningInContainer
    ? UfoHostOptions.ForContainer()
    : UfoHostOptions.ForDesktop();

var app = UfoHost.Build(args, hostOptions);

UfoHost.OpenBrowserIfRequested(app, hostOptions);

try
{
    await app.RunAsync();
}
catch (Exception exception)
{
    Log.Fatal(exception, "Application terminated unexpectedly");

    // A non-zero exit is what tells Docker restart policies and CI that the host
    // died; falling through to 0 reports a crash as a clean shutdown.
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

return 0;
