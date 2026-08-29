using Microsoft.Extensions.Configuration;

namespace Ufo.Server.Hosting;

/// <summary>
/// Probes a running instance over HTTP and reports the result as a process exit
/// code, so a container HEALTHCHECK needs no extra tooling in the image.
/// </summary>
/// <remarks>
/// The alternative was installing curl or wget into the runtime image. Reusing
/// the runtime that is already there keeps the image smaller and means the probe
/// cannot drift from the endpoint it is checking.
/// </remarks>
public static class HealthCheckCommand
{
    public const string CommandLineSwitch = "--healthcheck";

    /// <summary>Endpoint that answers without authentication.</summary>
    private const string HealthEndpointPath = "/api/user/is-created";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    public static bool IsRequested(string[] commandLineArguments) =>
        commandLineArguments.Contains(CommandLineSwitch, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns 0 when the application answers successfully, 1 otherwise.
    /// </summary>
    public static async Task<int> RunAsync()
    {
        var healthUrl = ResolveHealthUrl();

        try
        {
            using var httpClient = new HttpClient { Timeout = RequestTimeout };
            using var response = await httpClient.GetAsync(healthUrl);

            if (response.IsSuccessStatusCode)
            {
                return 0;
            }

            await Console.Error.WriteLineAsync($"Health check failed: {healthUrl} returned {(int)response.StatusCode}.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            await Console.Error.WriteLineAsync($"Health check failed: {healthUrl} - {exception.Message}");
        }

        return 1;
    }

    private static Uri ResolveHealthUrl()
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        var configuredUrl = UfoHost.ResolveApplicationUrl(configuration);

        // The listener binds a wildcard address; the probe has to connect to a
        // real one from inside the container.
        var loopbackUrl = configuredUrl
            .Replace("0.0.0.0", "127.0.0.1", StringComparison.Ordinal)
            .Replace("[::]", "127.0.0.1", StringComparison.Ordinal)
            .Replace("://+", "://127.0.0.1", StringComparison.Ordinal)
            .Replace("://*", "://127.0.0.1", StringComparison.Ordinal);

        return new Uri(new Uri(loopbackUrl), HealthEndpointPath);
    }
}
