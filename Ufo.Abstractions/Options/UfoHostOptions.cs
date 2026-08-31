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
    /// Serves TLS using the certificate stored in the database, generating a
    /// self-signed one on first run when none has been uploaded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Independent of <see cref="EnableHttpsRedirection"/>: this decides whether
    /// the server holds a certificate at all, that decides what happens to plain
    /// HTTP requests. A deployment behind a reverse proxy that terminates TLS
    /// upstream should turn this off, so no certificate is generated or stored
    /// for a listener that will only ever see decrypted traffic.
    /// </para>
    /// <para>
    /// Turning it off does not close an HTTPS endpoint declared under
    /// <c>Kestrel:Endpoints</c>; it only stops this application supplying the
    /// certificate for one, which would leave Kestrel to fall back to its own
    /// configuration.
    /// </para>
    /// </remarks>
    public bool EnableHttps { get; set; }

    /// <summary>
    /// Extra host names and IP addresses to name in the generated self-signed
    /// certificate, on top of the ones the machine can see for itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Needed in a container, which cannot enumerate the host's addresses: it
    /// sees only its own loopback, its container id and its bridge address, so a
    /// certificate generated purely from what it can observe would not name the
    /// LAN address people actually browse to, and every client would report a
    /// host-name mismatch rather than a merely untrusted issuer.
    /// </para>
    /// <para>
    /// Entries that parse as an IP address are recorded as such; everything else
    /// is recorded as a DNS name. Only affects certificates this application
    /// generates - an uploaded one is used exactly as supplied.
    /// </para>
    /// </remarks>
    public IList<string> CertificateSubjectAlternativeNames { get; set; } = [];

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
    /// <para>
    /// An installed desktop application has nobody to set a secret for it, and a
    /// key shipped inside the executable would be shared by every installation -
    /// one extracted copy would forge tokens against all of them.
    /// </para>
    /// <para>
    /// Off by default, and deliberately not set by <see cref="ForDesktop"/>: the
    /// headless entry point falls back to those same defaults for anything that
    /// is not a container, so enabling it there would let a published server
    /// behind a reverse proxy quietly invent a key next to its binary - which an
    /// upgrade would then replace, signing out every user. Only the installed
    /// tray application, which owns a per-user writable data directory, opts in.
    /// </para>
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
        // Serves https://localhost:55000 with a certificate of its own rather
        // than the ASP.NET Core development certificate, which an installed
        // application has no reason to expect on the machine.
        EnableHttps = true,
        AllowedRoots = []
    };

    /// <summary>
    /// Defaults for the container image: headless, logs to stdout, serving TLS
    /// from its own stored certificate, and restricted to whatever is mounted in
    /// and listed in <c>Ufo__AllowedRoots</c>. A deployment that terminates TLS
    /// upstream instead should set <c>Ufo__EnableHttps=false</c>.
    /// </summary>
    public static UfoHostOptions ForContainer() => new()
    {
        OpenBrowserOnStartup = false,
        // Nothing to redirect: the container serves a single HTTPS endpoint and
        // opens no plaintext listener. A deployment that terminates TLS upstream
        // swaps that endpoint for an http one, and redirecting there would bounce
        // requests to a port that is not listening.
        EnableHttpsRedirection = false,
        EnableFileLogging = false,
        EnableHttps = true,
        // Network-reachable, and its data directory is a volume: JWT__Key must be
        // supplied, and a missing one has to be fatal rather than invented.
        GenerateJwtKeyWhenMissing = false,
        DataDirectory = "/data",
        // Left empty here and defaulted after configuration binding - see
        // RequireAllowedRoots.
        AllowedRoots = [],
        RequireAllowedRoots = true
    };
}
