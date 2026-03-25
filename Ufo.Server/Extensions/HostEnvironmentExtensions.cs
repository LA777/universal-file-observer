namespace Ufo.Server.Extensions;

public static class HostEnvironmentExtensions
{
    public const string FunctionalTesting = "FunctionalTesting";

    /// <summary>
    /// Returns <c>true</c> when the application is running inside a functional
    /// test host (i.e. <c>ASPNETCORE_ENVIRONMENT == "FunctionalTesting"</c>).
    /// Use this to suppress side-effects that must not run during tests, such
    /// as opening a browser window.
    /// </summary>
    public static bool IsFunctionalTesting(this IHostEnvironment environment) =>
        environment.IsEnvironment(FunctionalTesting);
}
