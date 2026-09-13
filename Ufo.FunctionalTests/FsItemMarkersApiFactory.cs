using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Ufo.Abstractions.Database;
using Ufo.Abstractions.Options;
using Ufo.Database.Contexts;
using Ufo.FunctionalTests.Extensions;
using Ufo.Server.Extensions;
using Ufo.Server.Models;
using Ufo.Server.Services;

namespace Ufo.FunctionalTests.FsItemMarkers;

/// <summary>
/// Boots the application for the three features that mark a path - flags,
/// ratings and tags - against an in-memory database, unrestricted by default or
/// confined to one allowed root when the test is about the allow-list.
/// </summary>
/// <remarks>
/// One factory for the three because they are the same shape: a row keyed on a
/// path, written through the guard and read back through it again. Each test
/// gets its own database, so nothing here is shared between tests.
/// </remarks>
public class FsItemMarkersApiFactory : WebApplicationFactory<Program>
{
    public const string JwtKey = "super-secret-test-key-that-is-long-enough-256bits!!";
    public const string JwtIssuer = "ufo-test-issuer";
    public const string JwtAudience = "ufo-test-audience";

    private readonly string _dbName = $"test-fsitem-markers-{Guid.NewGuid():N}";
    private readonly string? _allowedRoot;
    private SqliteConnection? _sqLiteConnection;

    /// <param name="allowedRoot">
    /// A folder to confine the guard to, or null for the desktop's unrestricted
    /// behaviour, where any well-formed path is accepted whether or not it exists.
    /// </param>
    public FsItemMarkersApiFactory(string? allowedRoot = null)
    {
        _allowedRoot = allowedRoot;
    }

    public string ConnectionString => $"Data Source={_dbName};Mode=Memory;Cache=Shared";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(HostEnvironmentExtensions.FunctionalTesting);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = ConnectionString,
                ["JWT:Key"] = JwtKey,
                ["JWT:Issuer"] = JwtIssuer,
                ["JWT:Audience"] = JwtAudience,
                ["Kestrel:Endpoints:App:Url"] = "http://localhost:0"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            if (_allowedRoot is not null)
            {
                // Injected rather than configured, for the reason the restricted
                // file-system tests give: the host reads the Ufo section before a
                // test's configuration is applied.
                services.RemoveAll<IPathGuard>();
                services.AddSingleton<IPathGuard>(serviceProvider => new PathGuard(
                    serviceProvider.GetRequiredService<ILogger<PathGuard>>(),
                    Options.Create(new UfoHostOptions { AllowedRoots = [_allowedRoot] })));
            }

            services.RemoveAll<IDbConnectionFactory>();
            services.AddScoped<IDbConnectionFactory>(serviceProvider =>
                new SqliteConnectionFactory(
                    new DatabaseOptions { ConnectionString = ConnectionString }.ToOptionsMonitor(),
                    serviceProvider.GetRequiredService<ILogger<SqliteConnectionFactory>>()));

            services.AddLogging(loggingBuilder => loggingBuilder.SetMinimumLevel(LogLevel.Warning));

            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey)),
                    ValidateIssuer = true,
                    ValidIssuer = JwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = JwtAudience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            });
        });
    }

    /// <summary>A client already carrying a token, and the user it belongs to.</summary>
    public async Task<(HttpClient Client, Ulid UserId)> CreateAuthenticatedClientAsync()
    {
        var sqLiteConnection = await GetOpenConnectionAsync();

        var userId = Ulid.NewUlid();
        var userName = $"markers-user-{userId}";

        using var command = sqLiteConnection.CreateCommand();
        command.CommandText =
            "INSERT INTO Users (Id, Name, PasswordHash, CreatedAt) VALUES (@Id, @Name, @PasswordHash, @CreatedAt)";
        command.Parameters.AddWithValue("@Id", userId.ToString());
        command.Parameters.AddWithValue("@Name", userName);
        command.Parameters.AddWithValue("@PasswordHash", "not-a-real-hash");
        command.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow.ToString("o"));
        await command.ExecuteNonQueryAsync();

        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GenerateToken(userId, userName));

        return (client, userId);
    }

    /// <summary>
    /// Writes a row the way a past, less restricted server would have: straight
    /// into the table, past the guard.
    /// </summary>
    public async Task ExecuteSqlAsync(string sql, params (string Name, object Value)[] parameters)
    {
        var sqLiteConnection = await GetOpenConnectionAsync();

        using var command = sqLiteConnection.CreateCommand();
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    public async Task<long> CountRowsAsync(string countSql)
    {
        var sqLiteConnection = await GetOpenConnectionAsync();

        using var command = sqLiteConnection.CreateCommand();
        command.CommandText = countSql;

        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<SqliteConnection> GetOpenConnectionAsync()
    {
        _sqLiteConnection ??= new SqliteConnection(ConnectionString);

        if (_sqLiteConnection.State != System.Data.ConnectionState.Open)
        {
            await _sqLiteConnection.OpenAsync();
            await DapperDataContext.InitiateDatabaseAsync(_sqLiteConnection);
        }

        return _sqLiteConnection;
    }

    private static string GenerateToken(Ulid userId, string userName)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.NameId, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, userName),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            ],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public override ValueTask DisposeAsync()
    {
        _sqLiteConnection?.Dispose();

        return base.DisposeAsync();
    }

    /// <summary>
    /// The synchronous path matters as much as the asynchronous one: a test
    /// written as <c>using var factory = ...</c> disposes through here, and the
    /// shared in-memory database lives exactly as long as a connection to it.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sqLiteConnection?.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// A temporary folder tree for one test: a root, two files and a folder inside
/// it, and a file outside it for the allow-list tests to be refused on.
/// </summary>
public sealed class MarkerTestTree : IDisposable
{
    private readonly string _scratchRoot;

    public MarkerTestTree()
    {
        _scratchRoot = Path.Combine(Path.GetTempPath(), $"ufo-markers-functional-{Guid.NewGuid():N}");
        AllowedRoot = Path.Combine(_scratchRoot, "library");
        Folder = Path.Combine(AllowedRoot, "photos");
        FirstFile = Path.Combine(AllowedRoot, "notes.txt");
        SecondFile = Path.Combine(Folder, "holiday.jpg");
        OutsideFile = Path.Combine(_scratchRoot, "elsewhere", "secret.txt");

        Directory.CreateDirectory(Folder);
        Directory.CreateDirectory(Path.GetDirectoryName(OutsideFile)!);
        File.WriteAllText(FirstFile, "notes");
        File.WriteAllText(SecondFile, "jpeg");
        File.WriteAllText(OutsideFile, "secret");
    }

    public string AllowedRoot { get; }

    public string Folder { get; }

    public string FirstFile { get; }

    public string SecondFile { get; }

    /// <summary>A real file that sits outside <see cref="AllowedRoot"/>.</summary>
    public string OutsideFile { get; }

    public void Dispose()
    {
        if (Directory.Exists(_scratchRoot))
        {
            Directory.Delete(_scratchRoot, recursive: true);
        }
    }
}
