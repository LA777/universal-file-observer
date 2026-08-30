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
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Ufo.Abstractions.Database;
using Ufo.Abstractions.Options;
using Ufo.Abstractions.Requests;
using Ufo.Database.Contexts;
using Ufo.FunctionalTests.Extensions;
using Ufo.Server.Extensions;
using Ufo.Server.Models;
using Ufo.Server.Services;

namespace Ufo.FunctionalTests.RestrictedFileSystem;

#region Test WebApplication Factory

/// <summary>
/// Boots the application the way a container runs it: with a non-empty
/// <c>Ufo:AllowedRoots</c>, so the file-system endpoints are restricted.
/// </summary>
public class RestrictedFileSystemApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"test-restricted-{Guid.NewGuid():N}";

    public RestrictedFileSystemApiFactory(string allowedRoot)
    {
        AllowedRoot = allowedRoot;
    }

    public string AllowedRoot { get; }

    public string ConnectionString => $"Data Source={_dbName};Mode=Memory;Cache=Shared";

    private SqliteConnection? _sqLiteConnection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(HostEnvironmentExtensions.FunctionalTesting);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = ConnectionString,
                ["JWT:Key"] = RestrictedFileSystemTestConstants.JwtKey,
                ["JWT:Issuer"] = RestrictedFileSystemTestConstants.JwtIssuer,
                ["JWT:Audience"] = RestrictedFileSystemTestConstants.JwtAudience,
                ["Kestrel:Endpoints:App:Url"] = "http://localhost:0"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // The allow-list is injected rather than configured. A test host's
            // ConfigureAppConfiguration is applied after UfoHost.Build has already
            // read the Ufo section, which is why the other functional factories
            // also override services instead of relying on configuration. That
            // binding path is exercised by running the published server with real
            // Ufo__AllowedRoots__0 environment variables; what is under test here
            // is the enforcement.
            services.RemoveAll<IPathGuard>();
            services.AddSingleton<IPathGuard>(sp => new PathGuard(
                sp.GetRequiredService<ILogger<PathGuard>>(),
                Options.Create(new UfoHostOptions { AllowedRoots = [AllowedRoot] })));

            services.RemoveAll<IDbConnectionFactory>();
            services.AddScoped<IDbConnectionFactory>(sp =>
                new SqliteConnectionFactory(
                    new DatabaseOptions { ConnectionString = ConnectionString }.ToOptionsMonitor(),
                    sp.GetRequiredService<ILogger<SqliteConnectionFactory>>()));

            services.AddLogging(lb => lb.SetMinimumLevel(LogLevel.Warning));

            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(RestrictedFileSystemTestConstants.JwtKey)),
                    ValidateIssuer = true,
                    ValidIssuer = RestrictedFileSystemTestConstants.JwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = RestrictedFileSystemTestConstants.JwtAudience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            });
        });
    }

    public async Task<HttpClient> CreateClientAsync()
    {
        _sqLiteConnection = new SqliteConnection(ConnectionString);
        await _sqLiteConnection.OpenAsync();
        await DapperDataContext.InitiateDatabaseAsync(_sqLiteConnection);

        return CreateClient();
    }

    public override ValueTask DisposeAsync()
    {
        _sqLiteConnection?.Dispose();
        return base.DisposeAsync();
    }
}

#endregion

#region Test Constants

public static class RestrictedFileSystemTestConstants
{
    public const string JwtKey = "super-secret-test-key-that-is-long-enough-256bits!!";
    public const string JwtIssuer = "ufo-test-issuer";
    public const string JwtAudience = "ufo-test-audience";

    public static readonly Ulid TestUserId = Ulid.NewUlid();
    public const string TestUserName = "restricteduser";
}

#endregion

#region Functional Tests

/// <summary>
/// Covers the allow-list enforcement that a container depends on. Every one of
/// these paths was reachable before the guard existed.
/// </summary>
public class RestrictedFileSystemFunctionalTests : IAsyncLifetime
{
    private RestrictedFileSystemApiFactory _factory = null!;
    private HttpClient _client = null!;
    private string _testRoot = null!;
    private string _allowedRoot = null!;
    private string _forbiddenRoot = null!;
    private bool _symbolicLinksSupported;

    public async Task InitializeAsync()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"ufo-restricted-{Guid.NewGuid():N}");
        _allowedRoot = Path.Combine(_testRoot, "library");
        _forbiddenRoot = Path.Combine(_testRoot, "secrets");

        Directory.CreateDirectory(_allowedRoot);
        Directory.CreateDirectory(_forbiddenRoot);

        // Distinctive names so a leak is unambiguous in an assertion.
        File.WriteAllText(Path.Combine(_allowedRoot, "public-marker.txt"), "public");
        File.WriteAllText(Path.Combine(_forbiddenRoot, "public-marker-secret.txt"), "secret");

        // A folder one level below the link target, so a request can address a
        // path that passes *through* the link rather than ending at it.
        Directory.CreateDirectory(Path.Combine(_forbiddenRoot, "nested"));
        File.WriteAllText(Path.Combine(_forbiddenRoot, "nested", "public-marker-nested.txt"), "secret");

        _symbolicLinksSupported = TryCreateDirectorySymbolicLink(
            Path.Combine(_allowedRoot, "escape"),
            _forbiddenRoot);

        _factory = new RestrictedFileSystemApiFactory(_allowedRoot);
        _client = await _factory.CreateClientAsync();

        await RegisterTestUser();
        _client.DefaultRequestHeaders.Add(
            "Authorization",
            $"Bearer {GenerateToken(RestrictedFileSystemTestConstants.TestUserId, RestrictedFileSystemTestConstants.TestUserName)}");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();

        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    private static bool TryCreateDirectorySymbolicLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            // Unprivileged Windows without developer mode; the link-specific
            // assertions below are skipped rather than failed.
            return false;
        }
    }

    [Fact]
    public async Task GetFileSystemRoot_ReturnsOnlyTheAllowedRoot()
    {
        var response = await _client.GetAsync("/api/filesystem/root");
        var fileSystemRoot = await response.Content.ReadFromJsonAsync<FileSystemRoot>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(fileSystemRoot);
        Assert.Equal([_allowedRoot], fileSystemRoot!.Roots);
        // The home folder is outside the allow-list, so browsing must start at the
        // root instead of failing or leaking a path.
        Assert.Equal(_allowedRoot, fileSystemRoot.Folder?.FullPath);
    }

    [Fact]
    public async Task GetFolderInfo_OutsideTheAllowedRoot_IsForbidden()
    {
        var response = await _client.PostAsJsonAsync("/api/filesystem/folder", new PathRequest { Path = _forbiddenRoot });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetFolderInfo_ThroughASymlinkedDirectory_IsForbidden()
    {
        if (!_symbolicLinksSupported)
        {
            return;
        }

        var response = await _client.PostAsJsonAsync(
            "/api/filesystem/folder",
            new PathRequest { Path = Path.Combine(_allowedRoot, "escape") });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetFolderInfo_ThroughASymlinkedDirectoryToANestedFolder_IsForbidden()
    {
        if (!_symbolicLinksSupported)
        {
            return;
        }

        // The deep case: "nested" is not itself a link, so resolving only the final
        // path component sees nothing to follow and the folder is served from
        // outside the allow-list.
        var response = await _client.PostAsJsonAsync(
            "/api/filesystem/folder",
            new PathRequest { Path = Path.Combine(_allowedRoot, "escape", "nested") });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetVideo_ThroughASymlinkedDirectory_IsForbidden()
    {
        if (!_symbolicLinksSupported)
        {
            return;
        }

        var pathThroughLink = Path.Combine(_allowedRoot, "escape", "nested", "public-marker-nested.txt");

        var response = await _client.GetAsync($"/api/video?filePath={Uri.EscapeDataString(pathThroughLink)}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SearchFileSystem_InsideTheAllowedRoot_ReturnsResults()
    {
        var response = await _client.PostAsJsonAsync("/api/filesystem/search", new FileSystemSearchRequest
        {
            Path = _allowedRoot,
            Query = "public-marker",
            IncludeFiles = true,
            IncludeFolders = true,
            MaxResults = 100
        });

        var results = await response.Content.ReadFromJsonAsync<List<FsSearchResult>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(results);
        Assert.Contains(results!, result => result.Name == "public-marker");
    }

    [Fact]
    public async Task SearchFileSystem_DoesNotFollowASymlinkOutOfTheAllowedRoot()
    {
        if (!_symbolicLinksSupported)
        {
            return;
        }

        // The walk descends into every subdirectory it finds. Guarding only the
        // starting path let it step through "escape" and report files from
        // outside the allow-list.
        var response = await _client.PostAsJsonAsync("/api/filesystem/search", new FileSystemSearchRequest
        {
            Path = _allowedRoot,
            Query = "public-marker",
            IncludeFiles = true,
            IncludeFolders = true,
            MaxResults = 100
        });

        var results = await response.Content.ReadFromJsonAsync<List<FsSearchResult>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(results);
        Assert.DoesNotContain(results!, result => result.FullPath.StartsWith(_forbiddenRoot, StringComparison.Ordinal));
        Assert.DoesNotContain(results!, result => result.Name == "public-marker-secret");
    }

    [Fact]
    public async Task SearchFileSystem_OutsideTheAllowedRoot_IsForbidden()
    {
        var response = await _client.PostAsJsonAsync("/api/filesystem/search", new FileSystemSearchRequest
        {
            Path = _forbiddenRoot,
            Query = "public-marker",
            IncludeFiles = true,
            MaxResults = 100
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SearchFileSystem_TerminatesOnASymlinkCycle()
    {
        var cycleRoot = Path.Combine(_allowedRoot, "cycle");
        Directory.CreateDirectory(cycleRoot);

        if (!TryCreateDirectorySymbolicLink(Path.Combine(cycleRoot, "self"), cycleRoot))
        {
            return;
        }

        // A query matching nothing means the max-results bound never trips, so
        // only cycle detection can end this walk.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var response = await _client.PostAsJsonAsync("/api/filesystem/search", new FileSystemSearchRequest
        {
            Path = _allowedRoot,
            Query = "a-name-that-matches-nothing",
            IncludeFiles = true,
            IncludeFolders = true,
            MaxResults = 2000
        }, timeout.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetVideo_OutsideTheAllowedRoot_IsForbidden()
    {
        var forbiddenFilePath = Path.Combine(_forbiddenRoot, "public-marker-secret.txt");

        var response = await _client.GetAsync($"/api/video?filePath={Uri.EscapeDataString(forbiddenFilePath)}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateSnapshot_OutsideTheAllowedRoot_IsForbidden()
    {
        var response = await _client.PostAsJsonAsync("/api/snapshot/create", new PathRequest { Path = _forbiddenRoot });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateSnapshot_DoesNotIndexThroughASymlinkOutOfTheAllowedRoot()
    {
        if (!_symbolicLinksSupported)
        {
            return;
        }

        // Forbidding the forbidden root outright is the easy half. This is the other
        // half, and the damaging one: the snapshot walk descends into whatever it
        // enumerates, so "escape" inside the allowed root led it out - recording the
        // name, size and SHA-256 of every file below, and leaving all of it browsable
        // through the snapshot endpoints long after the walk finished.
        var createResponse = await _client.PostAsJsonAsync("/api/snapshot/create", new PathRequest { Path = _allowedRoot });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        var snapshotResponse = await _client.GetAsync("/api/snapshot/latest");
        Assert.Equal(HttpStatusCode.OK, snapshotResponse.StatusCode);

        var snapshotJson = await snapshotResponse.Content.ReadAsStringAsync();

        Assert.DoesNotContain("public-marker-secret", snapshotJson, StringComparison.Ordinal);
        Assert.DoesNotContain("public-marker-nested", snapshotJson, StringComparison.Ordinal);
        Assert.DoesNotContain("escape", snapshotJson, StringComparison.Ordinal);

        // The allowed content is still indexed, so the guard is not simply refusing
        // everything.
        Assert.Contains("public-marker", snapshotJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetFolderInfo_DoesNotListASymlinkOutOfTheAllowedRoot()
    {
        if (!_symbolicLinksSupported)
        {
            return;
        }

        var response = await _client.PostAsJsonAsync("/api/filesystem/folder", new PathRequest { Path = _allowedRoot });

        var folder = await response.Content.ReadFromJsonAsync<FsFolder>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(folder);
        Assert.DoesNotContain(folder!.ChildFolders, childFolder => childFolder.Name == "escape");
        Assert.Contains(folder.Files, file => file.Name == "public-marker");
    }

    [Fact]
    public async Task GetFolderInfo_AtTheAllowedRoot_OffersNoParentToNavigateTo()
    {
        var response = await _client.PostAsJsonAsync("/api/filesystem/folder", new PathRequest { Path = _allowedRoot });

        var folder = await response.Content.ReadFromJsonAsync<FsFolder>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(folder);
        // The parent of the allowed root is outside it, so advertising it would hand the
        // UI a path that only ever answers 403.
        Assert.Null(folder!.ParentFolder);
    }

    private async Task RegisterTestUser()
    {
        const string sql = @"
            INSERT INTO Users (Id, Name, PasswordHash, CreatedAt)
            VALUES (@Id, @Name, @PasswordHash, @CreatedAt)";

        using var connection = new SqliteConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@Id", RestrictedFileSystemTestConstants.TestUserId.ToString());
        command.Parameters.AddWithValue("@Name", RestrictedFileSystemTestConstants.TestUserName);
        command.Parameters.AddWithValue("@PasswordHash", BCrypt.Net.BCrypt.HashPassword("TestPassword123!"));
        command.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow.ToString("o"));
        await command.ExecuteNonQueryAsync();
    }

    private static string GenerateToken(Ulid userId, string userName)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(RestrictedFileSystemTestConstants.JwtKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.NameId, userId.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, userName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: RestrictedFileSystemTestConstants.JwtIssuer,
            audience: RestrictedFileSystemTestConstants.JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

#endregion
