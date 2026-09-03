using Cysharp.Serialization.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ufo.Abstractions.Database;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.DataProviders;
using Ufo.Abstractions.Options;
using Ufo.Database.Contexts;
using Ufo.Database.Repositories;
using Ufo.DataProviders;
using Ufo.Server.Extensions;
using Ufo.Server.Security;
using Ufo.Server.Services;

namespace Ufo.Server.Hosting;

/// <summary>
/// Single composition root shared by both entry points - the headless
/// console/container host (<c>Ufo.Server</c>) and the Windows desktop tray
/// application (<c>Ufo.Desktop</c>).
/// </summary>
/// <remarks>
/// Everything that differs between the two hosts is expressed through
/// <see cref="UfoHostOptions"/>, never through a platform check inside a service.
/// See <c>_docs/AI_DUAL_TARGET_PLAN.md</c>.
/// </remarks>
public static class UfoHost
{
    public const string DefaultApplicationUrl = "https://localhost:55000";
    private const string ApplicationUrlConfigurationKey = "Kestrel:Endpoints:App:Url";

    /// <summary>
    /// HmacSha256 needs a 256-bit key. A shorter one survives startup and only
    /// fails when the first token is signed, so the length is checked up front.
    /// </summary>
    private const int MinimumJwtKeyLengthInBytes = 32;

    // A day. Past this a token is less a session than a standing credential, so
    // startup says so - without refusing, since only the deployment knows what it
    // is trading away.
    private const int LongJwtTokenLifetimeMinutes = 24 * 60;

    /// <summary>
    /// File in <see cref="UfoHostOptions.DataDirectory"/> holding the signing key
    /// generated for this installation. Sits beside the database and the machine
    /// id so that a backup of the data directory keeps sessions valid.
    /// </summary>
    private const string JwtSigningKeyFileName = "jwt-signing-key";

    /// <summary>
    /// Signing keys that were once committed to this repository, plus the
    /// placeholders someone might paste in their place. They are public
    /// knowledge, so a host configured with any of them would accept forged
    /// tokens from anyone; refusing to start is the only safe response.
    /// </summary>
    private static readonly HashSet<string> BurnedOrPlaceholderJwtKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "78D97475131A633F6975CA554DCEDDCBDC0EBE456EA270F5A7EA1C604787258A",
        "DEC1B33656A8AC7AF17822163FEFDA3E9A788EFFC71C27CAD8D2095A0B4EC579",
        "CHANGEME",
        "REPLACE_ME",
        "your-secret-key"
    };

    /// <summary>
    /// Builds the fully configured web application.
    /// </summary>
    /// <param name="commandLineArguments">Process arguments.</param>
    /// <param name="hostOptions">
    /// Entry-point defaults. The <c>Ufo</c> configuration section (and therefore
    /// <c>Ufo__*</c> environment variables) is bound over the top of these, so a
    /// deployment can override any of them without a rebuild.
    /// </param>
    /// <param name="configureServices">
    /// Runs last, after every default registration, so an entry point can replace
    /// a service - the desktop application swaps in the Windows
    /// <see cref="ISystemInfoProvider"/> this way.
    /// </param>
    /// <param name="webRootFileProvider">
    /// Serves the single-page application from somewhere other than a wwwroot
    /// directory on disk. The desktop application passes an embedded provider so
    /// that its published output really is a single executable.
    /// </param>
    public static WebApplication Build(
        string[] commandLineArguments,
        UfoHostOptions hostOptions,
        Action<IServiceCollection>? configureServices = null,
        IFileProvider? webRootFileProvider = null)
    {
        ArgumentNullException.ThrowIfNull(commandLineArguments);
        ArgumentNullException.ThrowIfNull(hostOptions);

        var builder = WebApplication.CreateBuilder(commandLineArguments);

        if (webRootFileProvider != null)
        {
            // Set before the static-file, default-file and fallback middleware are
            // wired up below, so all three read from it.
            builder.Environment.WebRootFileProvider = webRootFileProvider;
        }

        // No appsettings files are added here on purpose. WebApplication.CreateBuilder
        // has already loaded appsettings.json and appsettings.{Environment}.json,
        // and re-adding them would place them after the environment-variable
        // provider - which wins the last registration. Every Ufo__*, Kestrel__* and
        // ConnectionStrings__* override the container relies on would then be
        // silently beaten by the values baked into appsettings.json.
        var isFunctionalTesting = builder.Environment.IsFunctionalTesting();

        // Configuration wins over the entry point's defaults.
        builder.Configuration.GetSection(UfoHostOptions.SectionName).Bind(hostOptions);

        hostOptions.DataDirectory = ResolveDataDirectory(hostOptions.DataDirectory, builder.Environment.ContentRootPath);

        if (hostOptions.RequireAllowedRoots && hostOptions.AllowedRoots.Count == 0)
        {
            hostOptions.AllowedRoots = [UfoHostOptions.ContainerLibraryRoot];
        }

        if (isFunctionalTesting)
        {
            // Test hosts must not write log files or open a browser.
            hostOptions.EnableFileLogging = false;
            hostOptions.OpenBrowserOnStartup = false;
        }

        ConfigureSerilog(hostOptions, isFunctionalTesting);
        builder.Host.UseSerilog();

        var jwtOptions = builder.Configuration.GetSection("JWT").Get<JwtOptions>();
        if (jwtOptions == null)
        {
            throw new ArgumentNullException(nameof(JwtOptions), "JwtOptions is null.");
        }

        if (hostOptions.GenerateJwtKeyWhenMissing
            && !isFunctionalTesting
            && string.IsNullOrWhiteSpace(jwtOptions.Key))
        {
            jwtOptions.Key = ResolveInstallationJwtSigningKey(hostOptions.DataDirectory);

            // Added as its own source rather than assigned on the local object:
            // JwtTokenService resolves the key through IOptionsMonitor<JwtOptions>,
            // which binds from configuration, so signing and validation would
            // otherwise disagree. Registering last is safe here precisely because
            // this branch only runs when nothing else supplied a key.
            builder.Configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["JWT:Key"] = jwtOptions.Key });
        }

        if (isFunctionalTesting && string.IsNullOrWhiteSpace(jwtOptions.Key))
        {
            // A functional test host supplies its own key through
            // ConfigureAppConfiguration, which is applied after this point, and
            // replaces the bearer validation parameters outright. It only needs a
            // usable key here so that startup can complete, and an ephemeral one
            // keeps a real key out of the test sources.
            jwtOptions.Key = Convert.ToHexString(RandomNumberGenerator.GetBytes(MinimumJwtKeyLengthInBytes));
        }

        ValidateJwtSigningKey(jwtOptions.Key);
        ValidateJwtLifetimes(jwtOptions);

        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        }

        connectionString = ResolveConnectionString(connectionString, hostOptions.DataDirectory, isFunctionalTesting);

        builder.Services.Configure<DatabaseOptions>(options =>
        {
            options.ConnectionString = connectionString;
        });
        builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("JWT"));

        // Registered as the already-merged instance rather than re-bound from
        // configuration, so entry-point defaults survive alongside overrides.
        builder.Services.AddSingleton(hostOptions);
        builder.Services.AddSingleton<IOptions<UfoHostOptions>>(Options.Create(hostOptions));

        builder.Services.AddScoped<ILabelsService, LabelsService>();
        builder.Services.AddScoped<IUserService, UserService>();
        builder.Services.AddScoped<IUserSettingsService, UserSettingsService>();
        builder.Services.AddScoped<ISearchService, SearchService>();
        builder.Services.AddScoped<ISnapshotService, SnapshotService>();
        builder.Services.AddSingleton<IPathGuard, PathGuard>();
        // Both read the host's own rules once and hold no per-request state, so
        // they are shared for the same reason the path guard is.
        builder.Services.AddSingleton<IFileNameValidator, FileNameValidator>();
        builder.Services.AddSingleton<IFileSystemOperationService, FileSystemOperationService>();
        // The version is read out of the assembly metadata once and never changes
        // while the process lives, so there is nothing per-request about it.
        builder.Services.AddSingleton<IApplicationVersionService, ApplicationVersionService>();
        // Stateless, so it is shared rather than rebuilt per request.
        builder.Services.AddSingleton<IFolderTreeBuilder, FolderTreeBuilder>();

        builder.Services.AddScoped<IDbConnectionFactory, SqliteConnectionFactory>();
        builder.Services.AddTransient<ISystemInfoProvider, PosixSystemInfoProvider>();
        builder.Services.AddScoped<ISnapshotRepository, SnapshotRepository>();
        builder.Services.AddScoped<ILabelsRepository, LabelsRepository>();
        builder.Services.AddScoped<ISearchRepository, SearchRepository>();
        builder.Services.AddScoped<IUserRepository, UserRepository>();
        builder.Services.AddScoped<IUserSettingsRepository, UserSettingsRepository>();
        builder.Services.AddScoped<IServerSettingsRepository, ServerSettingsRepository>();
        builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

        // TLS certificate. The provider is a singleton because Kestrel reads it
        // on every handshake and it outlives any request scope; everything that
        // touches the database around it stays scoped.
        builder.Services.AddSingleton<IServerCertificateProvider, ServerCertificateProvider>();
        builder.Services.AddSingleton<ICertificateProtector, CertificateProtector>();
        builder.Services.AddSingleton<ISelfSignedCertificateFactory, SelfSignedCertificateFactory>();
        builder.Services.AddScoped<IServerCertificateService, ServerCertificateService>();

        // TODO LA - Get sqliteConnection and Init Database (refactor)
        if (!isFunctionalTesting)
        {
            var sqliteConnection = new SqliteConnection(connectionString);
            DapperDataContext.InitiateDatabaseAsync(sqliteConnection).GetAwaiter().GetResult();
        }

        builder.Services.AddTransient<IJwtTokenService, JwtTokenService>();
        builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        builder.Services.AddTransient<IJwtClaimsService, JwtClaimsService>();
        builder.Services.AddHttpContextAccessor();

        // Add CORS policy for Angular development server
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAngularDev", policyBuilder =>
            {
                policyBuilder
                    .WithOrigins("http://localhost:4200", "https://localhost:4200")
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials();
            });
        });

        // Add JWT Authentication
        JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
                ValidateIssuer = true,
                ValidIssuer = jwtOptions.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtOptions.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };
        });

        builder.Services.AddAuthorization();

        builder.Services.AddControllers(options =>
        {
            options.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider());
        })
            .AddJsonOptions((options) =>
            {
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.Converters.Add(new UlidJsonConverter());
            });

        builder.Services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, context, cancellationToken) =>
            {
                // Ensure instances exist
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

                // Add Bearer security scheme (Authorization Code flow only)
                document.Components.SecuritySchemes.Add("Bearer", new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    In = ParameterLocation.Header,
                    BearerFormat = "JWT",
                    Description = "Enter 'Bearer {token}'"
                });

                // Apply security requirement globally
                document.Security = [
                    new OpenApiSecurityRequirement
                    {
                        {
                            new OpenApiSecuritySchemeReference("Bearer", document),
                            []
                        }
                    }
                ];

                // Set the host document for all elements
                // including the security scheme references
                document.SetReferenceHostDocument();

                return Task.CompletedTask;
            });
        });

        if (!hostOptions.EnableHttps && !isFunctionalTesting)
        {
            // Kestrel fails an https:// endpoint that has no certificate, with an
            // error that says nothing about which of these two settings is wrong.
            GuardAgainstHttpsEndpointWithoutTls(builder.Configuration);
        }

        // Captured by the certificate selector below and assigned once the
        // container has been built. Safe to leave null until then: the selector
        // only runs on an accepted connection, which cannot happen before this
        // method has returned and the caller has started the host.
        IServerCertificateProvider? certificateProvider = null;

        if (hostOptions.EnableHttps && !isFunctionalTesting)
        {
            // Applies to every HTTPS endpoint Kestrel ends up with, whichever
            // configuration declared it - the desktop host's App endpoint and the
            // container's separate HTTPS one are both covered by this one hook.
            builder.WebHost.ConfigureKestrel(kestrelOptions =>
                kestrelOptions.ConfigureHttpsDefaults(httpsOptions =>
                {
                    // A selector rather than a fixed ServerCertificate: it is
                    // consulted per connection, so a certificate uploaded on the
                    // Settings page is served from the next connection onwards
                    // instead of after a restart.
                    httpsOptions.ServerCertificateSelector = (_, _) =>
                        certificateProvider?.Current;
                }));
        }

        configureServices?.Invoke(builder.Services);

        var app = builder.Build();

        if (hostOptions.EnableHttps && !isFunctionalTesting)
        {
            // Resolved after Build so the selector closure above has something to
            // read, and done here rather than in a hosted service: the certificate
            // has to be in place before Kestrel accepts its first connection, and
            // hosted services start after the server is already listening.
            certificateProvider = app.Services.GetRequiredService<IServerCertificateProvider>();

            InitialiseServerCertificate(app);
        }

        // HSTS belongs ahead of the endpoints, and is pointless where TLS is
        // terminated upstream.
        if (app.Environment.IsProduction() && hostOptions.EnableHttpsRedirection)
        {
            app.UseHsts();
        }

        app.UseDefaultFiles();
        app.UseStaticFiles();

        // maps to /openapi/v1.json
        app.MapOpenApi();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            // add Swagger UI and point to the OpenAPI document
            // also enable PKCE for OAuth2
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/openapi/v1.json", "v1");
            });
        }

        app.UseRouting();
        app.UseCors("AllowAngularDev");

        if (hostOptions.EnableHttpsRedirection)
        {
            app.UseHttpsRedirection();
        }

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapFallbackToFile("/index.html");

        return app;
    }

    /// <summary>
    /// The URL a user should be sent to. Falls back to the Kestrel "App" endpoint
    /// declared in configuration.
    /// </summary>
    public static string ResolveApplicationUrl(IConfiguration configuration) =>
        configuration[ApplicationUrlConfigurationKey] ?? DefaultApplicationUrl;

    /// <summary>
    /// Opens the browser when the host asked for it. Failures are logged, never
    /// fatal.
    /// </summary>
    public static void OpenBrowserIfRequested(WebApplication app, UfoHostOptions hostOptions)
    {
        if (!hostOptions.OpenBrowserOnStartup)
        {
            return;
        }

        BrowserLauncher.TryOpen(ResolveApplicationUrl(app.Configuration), app.Logger);
    }

    /// <summary>
    /// Returns this installation's signing key, generating and persisting one on
    /// first run. Keeping it in the data directory rather than in the shipped
    /// configuration means each installation signs with its own secret, and that
    /// users stay logged in across restarts and upgrades.
    /// </summary>
    /// <summary>
    /// Refuses to start when TLS is switched off but an HTTPS endpoint is still
    /// configured, naming both settings.
    /// </summary>
    /// <remarks>
    /// Nothing supplies a certificate with <c>Ufo:EnableHttps</c> off, so Kestrel
    /// would fail to bind such an endpoint anyway - but it reports only that it
    /// could not configure HTTPS, which sends people looking for a missing
    /// certificate file that was never meant to exist. Failing here says which
    /// pair of settings disagree.
    /// </remarks>
    private static void GuardAgainstHttpsEndpointWithoutTls(IConfiguration configuration)
    {
        // A blank Url counts as well as an https one. Blanking the variable is the
        // obvious way to try to switch the endpoint off, and it does not work:
        // the endpoint section still exists, and Kestrel rejects it for having no
        // Url. The variable has to be absent entirely.
        var unstartableEndpointNames = configuration.GetSection("Kestrel:Endpoints")
            .GetChildren()
            .Where(endpoint =>
                endpoint["Url"]?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) == true
                || string.IsNullOrWhiteSpace(endpoint["Url"]))
            .Select(endpoint => endpoint.Key)
            .ToList();

        if (unstartableEndpointNames.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Ufo:EnableHttps is false, but the Kestrel endpoint(s) {string.Join(", ", unstartableEndpointNames)} "
            + "still ask for https or have no Url. Nothing supplies a certificate with TLS switched off, so Kestrel "
            + "cannot start them. The endpoint has to be removed rather than blanked - blanking it leaves the "
            + "section in place with no Url, which fails the same way. With the container image, run it with the "
            + "supplied overlay: docker compose -f docker-compose.yml -f docker-compose.no-tls.yml up -d");
    }

    /// <summary>
    /// Publishes the stored TLS certificate before the host starts listening,
    /// generating and storing a self-signed one when there is nothing usable.
    /// </summary>
    /// <remarks>
    /// Blocking rather than async because <see cref="Build"/> is synchronous and
    /// its callers start the host immediately afterwards. A failure here is
    /// logged and swallowed: the HTTP endpoint and everything behind it still
    /// work, and taking the whole application down over TLS would turn a
    /// degraded deployment into an unreachable one.
    /// </remarks>
    private static void InitialiseServerCertificate(WebApplication app)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var certificateService = scope.ServiceProvider.GetRequiredService<IServerCertificateService>();

            certificateService.EnsureCertificateAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Log.Error(
                exception,
                "Could not prepare a TLS certificate. HTTPS endpoints will fail their handshake until this is resolved; "
                + "any plain HTTP endpoint is unaffected.");
        }
    }

    private static string ResolveInstallationJwtSigningKey(string dataDirectory)
    {
        var signingKeyFilePath = Path.Combine(dataDirectory, JwtSigningKeyFileName);

        if (File.Exists(signingKeyFilePath))
        {
            string persistedSigningKey;

            try
            {
                persistedSigningKey = File.ReadAllText(signingKeyFilePath).Trim();
            }
            catch (Exception exception)
            {
                // Deliberately not a warning-and-regenerate: a momentary lock from
                // a backup or a virus scanner would otherwise overwrite a perfectly
                // good key and sign every user out for good. Refusing to start is
                // recoverable on the next attempt; destroying the key is not.
                throw new InvalidOperationException(
                    $"The signing key file '{signingKeyFilePath}' exists but could not be read, and replacing it "
                    + "would invalidate every existing session. Start again once whatever holds the file has "
                    + "released it, correct its permissions, or delete it to have a new key generated.",
                    exception);
            }

            if (Encoding.UTF8.GetByteCount(persistedSigningKey) >= MinimumJwtKeyLengthInBytes)
            {
                return persistedSigningKey;
            }

            // Readable but unusable for signing, so there is no session to protect.
            Log.Warning(
                "Ignoring the signing key in {SigningKeyFilePath}: it is too short to sign with. Generating a replacement.",
                signingKeyFilePath);
        }

        var generatedSigningKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(MinimumJwtKeyLengthInBytes));

        try
        {
            File.WriteAllText(signingKeyFilePath, generatedSigningKey);
        }
        catch (Exception exception)
        {
            // Starting with an in-memory key beats refusing to start; the cost is
            // that everyone is signed out again on the next restart.
            Log.Warning(
                exception,
                "Could not persist a JWT signing key to {SigningKeyFilePath}. Using a key that lasts only for this run, so existing sessions will not survive a restart.",
                signingKeyFilePath);

            return generatedSigningKey;
        }

        if (!OperatingSystem.IsWindows())
        {
            // Attempted separately from the write above: a mount without Unix
            // modes fails here even though the key was persisted, which is a
            // weaker file mode to report, not a key that has been lost.
            // The Windows data directory is already under the user's profile.
            try
            {
                File.SetUnixFileMode(signingKeyFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (Exception exception)
            {
                Log.Warning(
                    exception,
                    "Persisted the JWT signing key to {SigningKeyFilePath} but could not restrict it to this user, so other users of this machine may be able to read it.",
                    signingKeyFilePath);
            }
        }

        Log.Information(
            "Generated a JWT signing key for this installation and persisted it to {SigningKeyFilePath}.",
            signingKeyFilePath);

        return generatedSigningKey;
    }

    /// <summary>
    /// Refuses to start unless the configured signing key is a unique secret of
    /// usable length. Without this an absent, truncated or placeholder key either
    /// fails deep inside the token handler or, worse, starts cleanly and leaves
    /// every deployment sharing a key that anyone can look up.
    /// </summary>
    private static void ValidateJwtSigningKey(string? signingKey)
    {
        const string remedy =
            "Supply JWT:Key as a unique secret of at least 32 bytes: "
            + "`dotnet user-secrets set \"JWT:Key\" \"<value>\" --project Ufo.Server` for development, "
            + "or the JWT__Key environment variable for a deployment. "
            + "Generate one with `openssl rand -hex 32`.";

        if (string.IsNullOrWhiteSpace(signingKey))
        {
            throw new InvalidOperationException($"JWT:Key is not configured. {remedy}");
        }

        if (BurnedOrPlaceholderJwtKeys.Contains(signingKey))
        {
            throw new InvalidOperationException(
                $"JWT:Key is a placeholder or a key that was published in this repository's history, "
                + $"so it cannot be trusted to sign tokens. {remedy}");
        }

        var signingKeyLengthInBytes = Encoding.UTF8.GetByteCount(signingKey);
        if (signingKeyLengthInBytes < MinimumJwtKeyLengthInBytes)
        {
            throw new InvalidOperationException(
                $"JWT:Key is {signingKeyLengthInBytes} bytes; HmacSha256 requires at least "
                + $"{MinimumJwtKeyLengthInBytes}. {remedy}");
        }
    }

    /// <summary>
    /// Refuses to start on a lifetime that cannot issue a usable token, and says
    /// so when an access token lives long enough to be worth a second thought.
    /// </summary>
    private static void ValidateJwtLifetimes(JwtOptions jwtOptions)
    {
        var tokenLifetimeMinutes = jwtOptions.TokenLifetimeMinutes;

        if (jwtOptions.RefreshTokenLifetimeDays <= 0)
        {
            throw new InvalidOperationException(
                $"JWT:RefreshTokenLifetimeDays is {jwtOptions.RefreshTokenLifetimeDays}; it must be greater than zero. "
                + "A refresh token issued with that lifetime is expired before the browser stores it, so every "
                + "session would end with its first access token. Leave it unset for the "
                + $"{JwtOptions.DefaultRefreshTokenLifetimeDays}-day default.");
        }

        if (jwtOptions.RefreshTokenAbsoluteLifetimeDays < jwtOptions.RefreshTokenLifetimeDays)
        {
            throw new InvalidOperationException(
                $"JWT:RefreshTokenAbsoluteLifetimeDays is {jwtOptions.RefreshTokenAbsoluteLifetimeDays}, which is less "
                + $"than JWT:RefreshTokenLifetimeDays ({jwtOptions.RefreshTokenLifetimeDays}). The absolute deadline "
                + "caps the sliding one, so the sliding window could never be reached and the setting would be a "
                + "misleading way of writing the shorter number.");
        }

        if (tokenLifetimeMinutes <= 0)
        {
            throw new InvalidOperationException(
                $"JWT:TokenLifetimeMinutes is {tokenLifetimeMinutes}; it must be greater than zero. "
                + "A token issued with that lifetime is expired before it reaches the client, so nobody "
                + "could stay signed in. Leave it unset for the "
                + $"{JwtOptions.DefaultTokenLifetimeMinutes}-minute default, or set "
                + "JWT:TokenLifetimeMinutes (JWT__TokenLifetimeMinutes for a deployment).");
        }

        // Allowed, because how long an access token should last is the
        // deployment's call - but said out loud, because an access token is the
        // one credential here that nothing can withdraw early. Revoking a session
        // ends its refresh token immediately; the access token already handed out
        // keeps working until it expires.
        if (tokenLifetimeMinutes > LongJwtTokenLifetimeMinutes)
        {
            Log.Warning(
                "JWT:TokenLifetimeMinutes is {TokenLifetimeMinutes} minutes. An access token cannot be revoked, "
                + "so one that leaks stays usable for that long even after the session behind it has been ended.",
                tokenLifetimeMinutes);
        }
    }

    /// <summary>
    /// Returns a writable, absolute directory for the database, logs and the
    /// generated machine id, creating it when necessary.
    /// </summary>
    private static string ResolveDataDirectory(string configuredDataDirectory, string contentRootPath)
    {
        var dataDirectory = string.IsNullOrWhiteSpace(configuredDataDirectory)
            ? contentRootPath
            : Path.GetFullPath(configuredDataDirectory);

        try
        {
            Directory.CreateDirectory(dataDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"The configured data directory '{dataDirectory}' is not writable. Set Ufo:DataDirectory (Ufo__DataDirectory) to a writable location.",
                exception);
        }

        return dataDirectory;
    }

    /// <summary>
    /// Rewrites a relative SQLite data source to sit inside the data directory.
    /// </summary>
    /// <remarks>
    /// Left untouched for in-memory databases and for functional test hosts, both
    /// of which supply a connection string that must be used verbatim. Without
    /// this rewrite the database file is created relative to the process working
    /// directory - inside the container's writable layer, where it is lost on
    /// recreation, and next to an installed executable on Windows, where it
    /// cannot be created at all.
    /// </remarks>
    private static string ResolveConnectionString(string connectionString, string dataDirectory, bool isFunctionalTesting)
    {
        if (isFunctionalTesting)
        {
            return connectionString;
        }

        SqliteConnectionStringBuilder connectionStringBuilder;
        try
        {
            connectionStringBuilder = new SqliteConnectionStringBuilder(connectionString);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            // Not a shape we understand; hand it through unchanged.
            return connectionString;
        }

        var dataSource = connectionStringBuilder.DataSource;

        if (connectionStringBuilder.Mode == SqliteOpenMode.Memory
            || string.IsNullOrWhiteSpace(dataSource)
            || dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase)
            || Path.IsPathRooted(dataSource))
        {
            return connectionString;
        }

        connectionStringBuilder.DataSource = Path.Combine(dataDirectory, dataSource);

        return connectionStringBuilder.ToString();
    }

    private static void ConfigureSerilog(UfoHostOptions hostOptions, bool isFunctionalTesting)
    {
        var loggerConfiguration = new LoggerConfiguration();

        // A functional test assembly boots one of these hosts per test - well over a
        // hundred of them, several at a time - and every one of them logs through the
        // same process-wide console. At Information level that is several lines per
        // request behind a single lock, which starved the hosts of the very threads they
        // needed to answer: request times ran into minutes and tests failed on their
        // client timeouts. Warnings and errors still come through, so a genuinely broken
        // test still says why.
        loggerConfiguration = isFunctionalTesting
            ? loggerConfiguration.MinimumLevel.Warning()
            : loggerConfiguration.MinimumLevel.Information();

        loggerConfiguration = loggerConfiguration.WriteTo.Console();

        if (hostOptions.EnableFileLogging)
        {
            loggerConfiguration = loggerConfiguration.WriteTo.File(
                path: Path.Combine(hostOptions.DataDirectory, "logs", "ufo-.txt"),
                rollingInterval: RollingInterval.Day,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}");
        }

        Log.Logger = loggerConfiguration.CreateLogger();
    }
}
