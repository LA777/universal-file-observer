using Dapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ufo.Abstractions;
using Ufo.Abstractions.Database;
using Ufo.Abstractions.DataTransferObjects;
using Ufo.Abstractions.Options;
using Ufo.Abstractions.Requests;
using Ufo.Database;
using Ufo.Database.Contexts;
using Ufo.FunctionalTests.Extensions;
using Ufo.Server.Extensions;
using Ufo.Server.Services;

namespace Ufo.FunctionalTests.Export;

#region Test WebApplication factory

/// <summary>
/// Boots the production host against a per-factory in-memory SQLite database:
///   HTTP pipeline → JWT middleware → ExportController → services → SQLite → zip.
/// The data exported is put there through the same public API a user has.
/// </summary>
public class ExportApiFactory : WebApplicationFactory<Program>
{
    public const string JwtKey = "super-secret-test-key-that-is-long-enough-256bits!!";
    public const string JwtIssuer = "ufo-test-issuer";
    public const string JwtAudience = "ufo-test-audience";

    /// <summary>The moment every export in these tests is stamped with.</summary>
    public static readonly DateTimeOffset ExportMoment = new(2026, 9, 13, 17, 3, 11, TimeSpan.Zero);

    private readonly string _dbName = $"test-export-{Guid.NewGuid():N}";
    private SqliteConnection? _sqLiteConnection;

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
            services.RemoveAll<IDbConnectionFactory>();
            services.AddScoped<IDbConnectionFactory>(sp =>
                new SqliteConnectionFactory(
                    new DatabaseOptions { ConnectionString = ConnectionString }.ToOptionsMonitor(),
                    sp.GetRequiredService<ILogger<SqliteConnectionFactory>>()));

            // A fixed clock, so the file name can be asserted exactly.
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(ExportMoment));

            services.AddLogging(lb => lb.SetMinimumLevel(LogLevel.Warning));

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

    public async Task<(HttpClient Client, Ulid UserId, string UserName)> CreateAuthenticatedClientAsync(bool isAdmin = false)
    {
        _sqLiteConnection ??= new SqliteConnection(ConnectionString);
        if (_sqLiteConnection.State != System.Data.ConnectionState.Open)
        {
            await _sqLiteConnection.OpenAsync();
            await DapperDataContext.InitiateDatabaseAsync(_sqLiteConnection);
        }

        var userId = Ulid.NewUlid();
        var userName = $"testuser-{userId}";
        await _sqLiteConnection.ExecuteAsync(
            SqlScripts.InsertUserSql,
            new { Id = userId.ToString(), Name = userName, PasswordHash = "hash", IsAdmin = isAdmin });

        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GenerateToken(userId, userName));

        return (client, userId, userName);
    }

    public HttpClient CreateUnauthenticatedClient() => CreateClient();

    private static string GenerateToken(Ulid userId, string userName)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, userName),
        };
        var token = new JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(60),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sqLiteConnection?.Close();
            _sqLiteConnection?.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

#endregion

public class ExportControllerFunctionalTests : IDisposable
{
    private const string UserEndpoint = "/api/export/user";
    private const string FileSystemEndpoint = "/api/export/filesystem";
    private const string SnapshotsEndpoint = "/api/export/snapshots";
    private const string AllEndpoint = "/api/export/all";

    private readonly ExportApiFactory _factory = new();
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), $"ufo-export-{Guid.NewGuid():N}");

    public ExportControllerFunctionalTests()
    {
        Directory.CreateDirectory(_rootPath);
        File.WriteAllText(Path.Combine(_rootPath, "report.pdf"), "report");
        File.WriteAllText(Path.Combine(_rootPath, "notes.txt"), "notes");
    }

    public void Dispose()
    {
        _factory.Dispose();
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    #region Authentication

    [Theory]
    [InlineData(UserEndpoint)]
    [InlineData(FileSystemEndpoint)]
    [InlineData(SnapshotsEndpoint)]
    [InlineData(AllEndpoint)]
    public async Task Export_WithoutAToken_IsUnauthorized(string endpoint)
    {
        var client = _factory.CreateUnauthenticatedClient();

        var response = await client.GetAsync(endpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region The archive

    [Theory]
    [InlineData(UserEndpoint, "user")]
    [InlineData(FileSystemEndpoint, "filesystem")]
    [InlineData(SnapshotsEndpoint, "snapshots")]
    [InlineData(AllEndpoint, "all")]
    public async Task Export_AnswersAZipNamedWithTheScopeAndTheMoment(string endpoint, string scope)
    {
        var (client, _, _) = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(endpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType!.MediaType);
        var expectedName = $"ufo-export-{scope}-20260913-170311";
        Assert.Equal($"{expectedName}.zip", response.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
        Assert.Matches(new Regex(@"^ufo-export-[a-z]+-\d{8}-\d{6}\.zip$"), response.Content.Headers.ContentDisposition.FileName!.Trim('"'));

        using var archive = new ZipArchive(await response.Content.ReadAsStreamAsync(), ZipArchiveMode.Read);
        var entry = Assert.Single(archive.Entries);
        Assert.Equal($"{expectedName}.json", entry.FullName);
    }

    [Fact]
    public async Task Export_StampsTheDocumentWithFormatScopeAndTheRunningVersion()
    {
        var (client, _, _) = await _factory.CreateAuthenticatedClientAsync();
        var version = (await client.GetFromJsonAsync<Dictionary<string, string>>("/api/version"))!["version"];

        var document = await ExportAsync(client, SnapshotsEndpoint);

        Assert.Equal(ExportDocument.FormatName, document.Format);
        Assert.Equal(ExportDocument.CurrentFormatVersion, document.FormatVersion);
        Assert.Equal("snapshots", document.Scope);
        Assert.Equal(version, document.ApplicationVersion);
        Assert.Equal(ExportApiFactory.ExportMoment, document.ExportedAt);
    }

    #endregion

    #region Scopes

    [Fact]
    public async Task ExportUser_CarriesTheAccountAndSettingsAndNothingElse()
    {
        var (client, userId, userName) = await _factory.CreateAuthenticatedClientAsync(isAdmin: true);
        await ArrangeSettingsAsync(client);
        await MarkFileSystemAsync(client);
        await CreateSnapshotAsync(client);

        var (document, json) = await ExportWithJsonAsync(client, UserEndpoint);

        Assert.NotNull(document.User);
        Assert.Equal(userId, document.User!.Id);
        Assert.Equal(userName, document.User.Name);
        Assert.True(document.User.IsAdmin);
        Assert.Equal(UiThemes.Light, document.User.Settings.Theme);
        Assert.Equal("F9", document.User.KeyBindings.Single(binding => binding.ActionId == KeyBindingActions.Copy).PrimaryKey);
        var tab = Assert.Single(document.User.FolderTabs);
        Assert.Equal("left", tab.PanelId);
        Assert.Equal(_rootPath, tab.FolderPath);
        Assert.Null(document.FileSystem);
        Assert.Null(document.Snapshots);
        Assert.DoesNotContain("passwordHash", json);
        Assert.DoesNotContain("\"hash\"", json);
    }

    [Fact]
    public async Task ExportFileSystem_CarriesFlagsRatingsAndTagsAndNothingElse()
    {
        var (client, _, _) = await _factory.CreateAuthenticatedClientAsync();
        await ArrangeSettingsAsync(client);
        await MarkFileSystemAsync(client);
        var fullPath = Path.Combine(_rootPath, "report.pdf");

        var document = await ExportAsync(client, FileSystemEndpoint);

        Assert.NotNull(document.FileSystem);
        Assert.Equal([fullPath], document.FileSystem!.FlaggedPaths);
        Assert.Equal(7, document.FileSystem.Ratings[fullPath]);
        var tag = Assert.Single(document.FileSystem.Tags);
        Assert.Equal("Important", tag.Name);
        Assert.Equal([tag.Id], document.FileSystem.TagIdsByPath[fullPath]);
        Assert.Null(document.User);
        Assert.Null(document.Snapshots);
    }

    [Fact]
    public async Task ExportSnapshots_CarriesEverySnapshotWithItsWholeTreeAndTheLabels()
    {
        var (client, _, _) = await _factory.CreateAuthenticatedClientAsync();
        await MarkFileSystemAsync(client);
        await CreateSnapshotAsync(client);
        await CreateSnapshotAsync(client);
        await CreateLabelAsync(client, "keep");

        var document = await ExportAsync(client, SnapshotsEndpoint);

        Assert.NotNull(document.Snapshots);
        Assert.Equal(2, document.Snapshots!.Snapshots.Count);
        Assert.All(document.Snapshots.Snapshots, snapshot =>
        {
            Assert.NotNull(snapshot.RootFolder);
            Assert.Equal(2, snapshot.RootFolder!.Files.Count);
            Assert.NotNull(snapshot.VolumeInfo);
        });
        // The flag was on report.pdf when the snapshot was taken, and the copy is in the tree.
        var report = document.Snapshots.Snapshots[0].RootFolder!.Files.Single(file => file.Name == "report");
        Assert.True(report.IsFlagEnabled);
        Assert.Equal("keep", Assert.Single(document.Snapshots.Labels).Name);
        Assert.Null(document.User);
        Assert.Null(document.FileSystem);
    }

    [Fact]
    public async Task ExportAll_CarriesEverySection()
    {
        var (client, _, _) = await _factory.CreateAuthenticatedClientAsync();
        await ArrangeSettingsAsync(client);
        await MarkFileSystemAsync(client);
        await CreateSnapshotAsync(client);

        var document = await ExportAsync(client, AllEndpoint);

        Assert.Equal("all", document.Scope);
        Assert.NotNull(document.User);
        Assert.NotNull(document.FileSystem);
        Assert.NotNull(document.Snapshots);
        Assert.Single(document.Snapshots!.Snapshots);
    }

    [Fact]
    public async Task Export_WithNothingToExport_StillAnswersAFileWithEmptySections()
    {
        var (client, _, _) = await _factory.CreateAuthenticatedClientAsync();

        var document = await ExportAsync(client, AllEndpoint);

        Assert.NotNull(document.FileSystem);
        Assert.Empty(document.FileSystem!.FlaggedPaths);
        Assert.NotNull(document.Snapshots);
        Assert.Empty(document.Snapshots!.Snapshots);
        Assert.Equal(UiThemes.Default, document.User!.Settings.Theme);
    }

    [Fact]
    public async Task Export_CarriesOnlyTheCallersData()
    {
        var (client, userId, _) = await _factory.CreateAuthenticatedClientAsync();
        var (otherClient, _, _) = await _factory.CreateAuthenticatedClientAsync();
        await MarkFileSystemAsync(otherClient);
        await CreateSnapshotAsync(otherClient);
        await CreateLabelAsync(otherClient, "theirs");

        var document = await ExportAsync(client, AllEndpoint);

        Assert.Equal(userId, document.User!.Id);
        Assert.Empty(document.FileSystem!.FlaggedPaths);
        Assert.Empty(document.FileSystem.Tags);
        Assert.Empty(document.Snapshots!.Snapshots);
        Assert.Empty(document.Snapshots.Labels);
    }

    #endregion

    #region Helpers

    private static async Task<ExportDocument> ExportAsync(HttpClient client, string endpoint) =>
        (await ExportWithJsonAsync(client, endpoint)).Document;

    private static async Task<(ExportDocument Document, string Json)> ExportWithJsonAsync(HttpClient client, string endpoint)
    {
        var response = await client.GetAsync(endpoint);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var archive = new ZipArchive(await response.Content.ReadAsStreamAsync(), ZipArchiveMode.Read);
        using var reader = new StreamReader(Assert.Single(archive.Entries).Open());
        var json = await reader.ReadToEndAsync();

        return (JsonSerializer.Deserialize<ExportDocument>(json, ExportArchiveWriter.JsonOptions)!, json);
    }

    private async Task CreateSnapshotAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/snapshot/create", new PathRequest { Path = _rootPath });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task CreateLabelAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/label", new LabelRequest { Name = name, ColorHex = "#ff0000" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>One flag, one rating, one tag applied to one path - through the public API.</summary>
    private async Task MarkFileSystemAsync(HttpClient client)
    {
        var fullPath = Path.Combine(_rootPath, "report.pdf");

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            "/api/fsitemflags", new FsItemFlagsRequest { FullPaths = [fullPath], IsFlagEnabled = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            "/api/fsitemratings", new FsItemRatingsRequest { FullPaths = [fullPath], Rating = 7 })).StatusCode);

        var tagResponse = await client.PostAsJsonAsync("/api/tags", new CreateTagRequest { Name = "Important", ColorHex = "#00ff00" });
        Assert.Equal(HttpStatusCode.OK, tagResponse.StatusCode);
        var tag = await tagResponse.Content.ReadFromJsonAsync<TagDto>(ExportArchiveWriter.JsonOptions);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            "/api/tags/assign", new SetFsItemTagRequest { TagId = tag!.Id, FullPaths = [fullPath], IsApplied = true })).StatusCode);
    }

    /// <summary>The light theme, Copy rebound to F9, one folder tab locked.</summary>
    private async Task ArrangeSettingsAsync(HttpClient client)
    {
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(
            "/api/settings", new UserSettingsRequest { Theme = UiThemes.Light })).StatusCode);

        var shortcuts = (await client.GetFromJsonAsync<List<KeyBindingDto>>("/api/settings/shortcuts"))!;
        var bindings = shortcuts.Select(binding => new KeyBindingRequest
        {
            ActionId = binding.ActionId,
            PrimaryKey = binding.ActionId == KeyBindingActions.Copy ? "F9" : binding.PrimaryKey,
            SecondaryKey = binding.SecondaryKey
        }).ToList();
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(
            "/api/settings/shortcuts", new KeyBindingsRequest { Bindings = bindings })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            "/api/foldertabs/lock", new FolderTabRequest { PanelId = "left", FolderPath = _rootPath })).StatusCode);
    }

    #endregion
}
