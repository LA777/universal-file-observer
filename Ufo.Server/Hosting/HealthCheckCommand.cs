using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Security;

namespace Ufo.Server.Hosting;

/// <summary>
/// Probes a running instance over whichever scheme its endpoint is configured
/// with, and reports the result as a process exit code, so a container
/// HEALTHCHECK needs no extra tooling in the image.
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
            using var httpClient = new HttpClient(CreateLoopbackHandler()) { Timeout = RequestTimeout };
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

    /// <summary>
    /// An HTTP handler that accepts the certificate on a loopback address without
    /// validating it.
    /// </summary>
    /// <remarks>
    /// The probe talks to this same container over 127.0.0.1, and the certificate
    /// it will be shown is normally one the application generated for itself -
    /// self-signed, and trusted by nothing. Validating it would make the health
    /// check fail on every default installation.
    /// <para>
    /// The exemption is deliberately limited to loopback. Nothing off-box is
    /// reachable at 127.0.0.1, so this cannot be turned into a way of ignoring a
    /// bad certificate on a real connection, and a non-loopback host falls through
    /// to normal validation.
    /// </para>
    /// </remarks>
    private static HttpClientHandler CreateLoopbackHandler() =>
        new()
        {
            ServerCertificateCustomValidationCallback = (request, _, _, sslPolicyErrors) =>
                sslPolicyErrors == SslPolicyErrors.None
                || IsLoopback(request.RequestUri)
        };

    private static bool IsLoopback(Uri? requestUri) =>
        requestUri is not null
        && IPAddress.TryParse(requestUri.Host, out var address)
        && IPAddress.IsLoopback(address);

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
