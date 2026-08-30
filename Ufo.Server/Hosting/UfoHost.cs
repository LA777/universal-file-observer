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

        var applicationSettings = builder.Configuration.Get<ApplicationSettings>();
        if (applicationSettings == null)
        {
            throw new ArgumentNullException(nameof(ApplicationSettings), "ApplicationSettings is null.");
        }

        var jwtOptions = builder.Configuration.GetSection("JWT").Get<JwtOptions>();
        if (jwtOptions == null)
        {
            throw new ArgumentNullException(nameof(JwtOptions), "JwtOptions is null.");
        }

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
        builder.Services.Configure<ApplicationSettings>(builder.Configuration.GetSection("ApplicationSettings"));
        builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("JWT"));

        // Registered as the already-merged instance rather than re-bound from
        // configuration, so entry-point defaults survive alongside overrides.
        builder.Services.AddSingleton(hostOptions);
        builder.Services.AddSingleton<IOptions<UfoHostOptions>>(Options.Create(hostOptions));

        builder.Services.AddScoped<ILabelsService, LabelsService>();
        builder.Services.AddScoped<IUserService, UserService>();
        builder.Services.AddScoped<ISearchService, SearchService>();
        builder.Services.AddScoped<ISnapshotService, SnapshotService>();
        builder.Services.AddSingleton<IPathGuard, PathGuard>();
        // Stateless, so it is shared rather than rebuilt per request.
        builder.Services.AddSingleton<IFolderTreeBuilder, FolderTreeBuilder>();

        builder.Services.AddScoped<IDbConnectionFactory, SqliteConnectionFactory>();
        builder.Services.AddTransient<ISystemInfoProvider, PosixSystemInfoProvider>();
        builder.Services.AddScoped<ISnapshotRepository, SnapshotRepository>();
        builder.Services.AddScoped<ILabelsRepository, LabelsRepository>();
        builder.Services.AddScoped<ISearchRepository, SearchRepository>();
        builder.Services.AddScoped<IUserRepository, UserRepository>();

        // TODO LA - Get sqliteConnection and Init Database (refactor)
        if (!isFunctionalTesting)
        {
            var sqliteConnection = new SqliteConnection(connectionString);
            DapperDataContext.InitiateDatabaseAsync(sqliteConnection).GetAwaiter().GetResult();
        }

        builder.Services.AddTransient<IJwtTokenService, JwtTokenService>();
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

        configureServices?.Invoke(builder.Services);

        var app = builder.Build();

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
