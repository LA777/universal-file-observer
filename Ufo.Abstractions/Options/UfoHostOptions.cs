namespace Ufo.Abstractions.Options;

/// <summary>
/// Per-host capability flags. The application ships two entry points that share a
/// single web host: the Windows desktop tray application and the headless
/// container image. Every behavioural difference between them is expressed as a
/// named flag here rather than as a runtime platform check inside a service.
/// </summary>
/// <remarks>
/// Values set by the entry point are defaults; the <c>Ufo</c> configuration
/// section (and therefore <c>Ufo__*</c> environment variables) overrides them.
/// </remarks>
public class UfoHostOptions
{
    /// <summary>Configuration section these options are bound from.</summary>
    public const string SectionName = "Ufo";

    /// <summary>Where the container image expects indexable folders to be mounted.</summary>
    public const string ContainerLibraryRoot = "/library";

    /// <summary>
    /// Launches the default browser at the application URL once the host is up.
    /// Meaningful for the desktop application; must stay off in a container,
    /// where no browser exists and the launch attempt would throw during startup.
    /// </summary>
    public bool OpenBrowserOnStartup { get; set; }

    /// <summary>
    /// Writable directory holding the SQLite database, the rolling log files and
    /// the generated machine id. When empty, the content root is used, which is
    /// only appropriate for local development runs - an installed executable
    /// cannot write next to itself, and a container would lose the directory on
    /// recreation.
    /// </summary>
    public string DataDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Absolute paths the file-system APIs are allowed to read. An empty list
    /// means unrestricted access, which is the correct behaviour for a desktop
    /// application browsing its own machine. A network-reachable container must
    /// populate this, otherwise the browse and search endpoints amount to an
    /// arbitrary file read API.
    /// </summary>
    public IList<string> AllowedRoots { get; set; } = [];

    /// <summary>
    /// Redirects HTTP requests to HTTPS. Must stay off where TLS is terminated
    /// upstream (a container behind a reverse proxy), otherwise Kestrel issues
    /// redirects to a port that is not listening.
    /// </summary>
    public bool EnableHttpsRedirection { get; set; }

    /// <summary>
    /// Writes rolling log files into <see cref="DataDirectory"/>. Container hosts
    /// normally leave this off and collect the console sink instead.
    /// </summary>
    public bool EnableFileLogging { get; set; }

    /// <summary>
    /// Refuses to run unrestricted: when <see cref="AllowedRoots"/> is still empty
    /// after configuration is applied, it falls back to
    /// <see cref="ContainerLibraryRoot"/> rather than allowing the whole file
    /// system.
    /// </summary>
    /// <remarks>
    /// The container entry point is selected automatically from
    /// <c>DOTNET_RUNNING_IN_CONTAINER</c>, which Microsoft's base images already
    /// set, so an image built over the publish output without setting
    /// <c>Ufo__AllowedRoots__0</c> would otherwise expose an arbitrary file read
    /// API. The default cannot simply be pre-populated: the configuration binder
    /// <b>appends</b> to a non-empty list, so a deployment naming its own roots
    /// would silently keep this one too.
    /// </remarks>
    public bool RequireAllowedRoots { get; set; }

    /// <summary>
    /// Overrides the detected operating-system machine id. Containers should set
    /// this to the identity of the physical host, because a container's own
    /// <c>/etc/machine-id</c> is regenerated whenever the container is recreated
    /// and would fragment snapshot history across runs.
    /// </summary>
    public string MachineId { get; set; } = string.Empty;

    /// <summary>
    /// Generates a JWT signing key on first run and persists it in
    /// <see cref="DataDirectory"/> when <c>JWT:Key</c> is not configured.
    /// </summary>
    /// <remarks>
    /// An installed desktop application has nobody to set a secret for it, and a
    /// key shipped inside the executable would be shared by every installation -
    /// one extracted copy would forge tokens against all of them. A container is
    /// deliberately excluded: it is network-reachable and its data directory is a
    /// volume, so a key must be supplied through <c>JWT__Key</c> and the host
    /// should fail loudly rather than invent one.
    /// </remarks>
    public bool GenerateJwtKeyWhenMissing { get; set; }

    /// <summary>
    /// Defaults for the Windows desktop tray application: interactive, trusted
    /// with the whole machine, and serving HTTPS itself.
    /// </summary>
    public static UfoHostOptions ForDesktop() => new()
    {
        OpenBrowserOnStartup = true,
        EnableHttpsRedirection = true,
        EnableFileLogging = true,
        GenerateJwtKeyWhenMissing = true,
        AllowedRoots = []
    };

    /// <summary>
    /// Defaults for the container image: headless, logs to stdout, TLS terminated
    /// upstream, and restricted to whatever is mounted in and listed in
    /// <c>Ufo__AllowedRoots</c>.
    /// </summary>
    public static UfoHostOptions ForContainer() => new()
    {
        OpenBrowserOnStartup = false,
        EnableHttpsRedirection = false,
        EnableFileLogging = false,
        GenerateJwtKeyWhenMissing = false,
        DataDirectory = "/data",
        // Left empty here and defaulted after configuration binding - see
        // RequireAllowedRoots.
        AllowedRoots = [],
        RequireAllowedRoots = true
    };
}
