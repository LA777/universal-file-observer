using Cysharp.Serialization.Json;
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
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Ufo.Abstractions;
using Ufo.Abstractions.Database;
using Ufo.Abstractions.DataTransferObjects;
using Ufo.Abstractions.Options;
using Ufo.Abstractions.Requests;
using Ufo.Database;
using Ufo.Database.Contexts;
using Ufo.FunctionalTests.Extensions;
using Ufo.Server.Extensions;

namespace Ufo.FunctionalTests.UserData;

#region Test WebApplication factory

/// <summary>
/// Boots the production host against a per-factory in-memory SQLite database, so
/// each test exercises the whole stack:
///   HTTP pipeline → JWT middleware → UserDataController → repository → SQLite.
/// The data being deleted is put there through the same public API, so what is
/// tested is what a user can actually do from the page.
/// </summary>
public class UserDataApiFactory : WebApplicationFactory<Program>
{
    public const string JwtKey = "super-secret-test-key-that-is-long-enough-256bits!!";
    public const string JwtIssuer = "ufo-test-issuer";
    public const string JwtAudience = "ufo-test-audience";

    private readonly string _dbName = $"test-userdata-{Guid.NewGuid():N}";
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

    /// <summary>
    /// Creates the schema on first use, seeds a user, and returns a client
    /// carrying that user's bearer token. Called twice for two users.
    /// </summary>
    public async Task<(HttpClient Client, Ulid UserId)> CreateAuthenticatedClientAsync()
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
            new { Id = userId.ToString(), Name = userName, PasswordHash = "hash", IsAdmin = false });

        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GenerateToken(userId, userName));

        return (client, userId);
    }

    public HttpClient CreateUnauthenticatedClient() => CreateClient();

    public async Task<long> CountRowsAsync(string table, Ulid userId) =>
        await _sqLiteConnection!.ExecuteScalarAsync<long>(
            $"SELECT COUNT(*) FROM {table} WHERE UserId = @UserId", new { UserId = userId.ToString() });

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
}

#endregion

public class UserDataControllerFunctionalTests : IDisposable
{
    private const string SnapshotsEndpoint = "/api/userdata/snapshots";
    private const string FileSystemEndpoint = "/api/userdata/filesystem";
    private const string SettingsEndpoint = "/api/userdata/settings";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new UlidJsonConverter() }
    };

    private readonly UserDataApiFactory _factory = new();
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), $"ufo-userdata-{Guid.NewGuid():N}");

    public UserDataControllerFunctionalTests()
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
    [InlineData(SnapshotsEndpoint)]
    [InlineData(FileSystemEndpoint)]
    [InlineData(SettingsEndpoint)]
    public async Task Delete_WithoutAToken_IsUnauthorized(string endpoint)
    {
        var client = _factory.CreateUnauthenticatedClient();

        var response = await client.DeleteAsync(endpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region DELETE /api/userdata/snapshots

    [Fact]
    public async Task DeleteSnapshots_RemovesEverySnapshotAndLabelOfTheCaller()
    {
        var (client, userId) = await _factory.CreateAuthenticatedClientAsync();
        await CreateSnapshotAsync(client);
        await CreateSnapshotAsync(client);
        await CreateLabelAsync(client, "keep");
        Assert.Equal(2, (await GetSnapshotSummariesAsync(client)).Count);

        var response = await client.DeleteAsync(SnapshotsEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var serverResult = await ReadAsync<ServerResult>(response);
        Assert.Equal(Result.Success, serverResult!.Result);
        Assert.Contains("2 snapshot", serverResult.Message);

        Assert.Empty(await GetSnapshotSummariesAsync(client));
        // The label list answers 404 rather than an empty list when there are none.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/label")).StatusCode);
        foreach (var table in new[] { "Snapshots", "Folders", "Files", "Pcs", "StorageDrives", "Volumes", "VolumeInfos", "Labels" })
        {
            Assert.Equal(0, await _factory.CountRowsAsync(table, userId));
        }
    }

    [Fact]
    public async Task DeleteSnapshots_LeavesAnotherUsersSnapshotsAlone()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();
        var (otherClient, _) = await _factory.CreateAuthenticatedClientAsync();
        await CreateSnapshotAsync(client);
        await CreateSnapshotAsync(otherClient);

        await client.DeleteAsync(SnapshotsEndpoint);

        Assert.Empty(await GetSnapshotSummariesAsync(client));
        var otherSnapshots = await GetSnapshotSummariesAsync(otherClient);
        Assert.Single(otherSnapshots);

        // Still readable as a whole tree, not merely still counted.
        var snapshot = await ReadAsync<SnapshotDto>(await otherClient.GetAsync($"/api/snapshot/{otherSnapshots[0].Id}"));
        Assert.NotNull(snapshot?.RootFolder);
        Assert.Equal(2, snapshot!.RootFolder!.Files.Count);
    }

    [Fact]
    public async Task DeleteSnapshots_LeavesFileSystemDataAndSettingsAlone()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();
        await CreateSnapshotAsync(client);
        await MarkFileSystemAsync(client);
        await ArrangeSettingsAsync(client);

        await client.DeleteAsync(SnapshotsEndpoint);

        Assert.Single((await ReadAsync<List<string>>(await client.GetAsync("/api/fsitemflags")))!);
        Assert.Single((await ReadAsync<Dictionary<string, int>>(await client.GetAsync("/api/fsitemratings")))!);
        Assert.Single((await ReadAsync<FsItemTagsDto>(await client.GetAsync("/api/tags")))!.Tags);
        Assert.Equal(UiThemes.Light, (await ReadAsync<UserSettingsDto>(await client.GetAsync("/api/settings")))!.Theme);
        Assert.Single((await ReadAsync<List<FolderTabDto>>(await client.GetAsync("/api/foldertabs")))!);
    }

    [Fact]
    public async Task DeleteSnapshots_WhenThereAreNone_StillSucceeds()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.DeleteAsync(SnapshotsEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Result.Success, (await ReadAsync<ServerResult>(response))!.Result);
    }

    #endregion

    #region DELETE /api/userdata/filesystem

    [Fact]
    public async Task DeleteFileSystemData_RemovesFlagsRatingsAndTagsOfTheCaller()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();
        await MarkFileSystemAsync(client);

        var response = await client.DeleteAsync(FileSystemEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Result.Success, (await ReadAsync<ServerResult>(response))!.Result);

        Assert.Empty((await ReadAsync<List<string>>(await client.GetAsync("/api/fsitemflags")))!);
        Assert.Empty((await ReadAsync<Dictionary<string, int>>(await client.GetAsync("/api/fsitemratings")))!);
        var tags = await ReadAsync<FsItemTagsDto>(await client.GetAsync("/api/tags"));
        Assert.Empty(tags!.Tags);
        Assert.Empty(tags.TagIdsByPath);
    }

    [Fact]
    public async Task DeleteFileSystemData_LeavesAnotherUsersMarksAlone()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();
        var (otherClient, _) = await _factory.CreateAuthenticatedClientAsync();
        await MarkFileSystemAsync(client);
        await MarkFileSystemAsync(otherClient);

        await client.DeleteAsync(FileSystemEndpoint);

        Assert.Single((await ReadAsync<List<string>>(await otherClient.GetAsync("/api/fsitemflags")))!);
        Assert.Single((await ReadAsync<Dictionary<string, int>>(await otherClient.GetAsync("/api/fsitemratings")))!);
        var otherTags = await ReadAsync<FsItemTagsDto>(await otherClient.GetAsync("/api/tags"));
        Assert.Single(otherTags!.Tags);
        Assert.Single(otherTags.TagIdsByPath);
    }

    [Fact]
    public async Task DeleteFileSystemData_LeavesSnapshotsAndSettingsAlone()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();
        await CreateSnapshotAsync(client);
        await MarkFileSystemAsync(client);
        await ArrangeSettingsAsync(client);

        await client.DeleteAsync(FileSystemEndpoint);

        Assert.Single(await GetSnapshotSummariesAsync(client));
        Assert.Equal(UiThemes.Light, (await ReadAsync<UserSettingsDto>(await client.GetAsync("/api/settings")))!.Theme);
        Assert.Single((await ReadAsync<List<FolderTabDto>>(await client.GetAsync("/api/foldertabs")))!);
    }

    #endregion

    #region DELETE /api/userdata/settings

    [Fact]
    public async Task DeleteSettings_PutsThemeShortcutsAndFolderTabsBackToTheDefaults()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();
        await ArrangeSettingsAsync(client);
        Assert.Equal("F9", CopyBinding(await GetShortcutsAsync(client)).PrimaryKey);

        var response = await client.DeleteAsync(SettingsEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Result.Success, (await ReadAsync<ServerResult>(response))!.Result);

        Assert.Equal(UiThemes.Default, (await ReadAsync<UserSettingsDto>(await client.GetAsync("/api/settings")))!.Theme);
        var copy = CopyBinding(await GetShortcutsAsync(client));
        Assert.Equal(copy.DefaultPrimaryKey, copy.PrimaryKey);
        Assert.Empty((await ReadAsync<List<FolderTabDto>>(await client.GetAsync("/api/foldertabs")))!);
    }

    [Fact]
    public async Task DeleteSettings_LeavesAnotherUsersSettingsAlone()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();
        var (otherClient, _) = await _factory.CreateAuthenticatedClientAsync();
        await ArrangeSettingsAsync(client);
        await ArrangeSettingsAsync(otherClient);

        await client.DeleteAsync(SettingsEndpoint);

        Assert.Equal(UiThemes.Light, (await ReadAsync<UserSettingsDto>(await otherClient.GetAsync("/api/settings")))!.Theme);
        Assert.Equal("F9", CopyBinding(await GetShortcutsAsync(otherClient)).PrimaryKey);
        Assert.Single((await ReadAsync<List<FolderTabDto>>(await otherClient.GetAsync("/api/foldertabs")))!);
    }

    [Fact]
    public async Task DeleteSettings_LeavesSnapshotsAndFileSystemDataAlone()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();
        await CreateSnapshotAsync(client);
        await MarkFileSystemAsync(client);
        await ArrangeSettingsAsync(client);

        await client.DeleteAsync(SettingsEndpoint);

        Assert.Single(await GetSnapshotSummariesAsync(client));
        Assert.Single((await ReadAsync<List<string>>(await client.GetAsync("/api/fsitemflags")))!);
        Assert.Single((await ReadAsync<FsItemTagsDto>(await client.GetAsync("/api/tags")))!.Tags);
    }

    #endregion

    #region Helpers

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

    private static async Task<List<SnapshotSummaryDto>> GetSnapshotSummariesAsync(HttpClient client) =>
        (await ReadAsync<List<SnapshotSummaryDto>>(await client.GetAsync("/api/snapshot/all/summary")))!;

    /// <summary>One flag, one rating, one tag applied to one path - through the public API.</summary>
    private async Task MarkFileSystemAsync(HttpClient client)
    {
        var fullPath = Path.Combine(_rootPath, "report.pdf");

        var flagResponse = await client.PostAsJsonAsync(
            "/api/fsitemflags", new FsItemFlagsRequest { FullPaths = [fullPath], IsFlagEnabled = true });
        Assert.Equal(HttpStatusCode.OK, flagResponse.StatusCode);

        var ratingResponse = await client.PostAsJsonAsync(
            "/api/fsitemratings", new FsItemRatingsRequest { FullPaths = [fullPath], Rating = 7 });
        Assert.Equal(HttpStatusCode.OK, ratingResponse.StatusCode);

        var tagResponse = await client.PostAsJsonAsync(
            "/api/tags", new CreateTagRequest { Name = "Important", ColorHex = "#00ff00" });
        Assert.Equal(HttpStatusCode.OK, tagResponse.StatusCode);
        var tag = await ReadAsync<TagDto>(tagResponse);

        var assignResponse = await client.PostAsJsonAsync(
            "/api/tags/assign", new SetFsItemTagRequest { TagId = tag!.Id, FullPaths = [fullPath], IsApplied = true });
        Assert.Equal(HttpStatusCode.OK, assignResponse.StatusCode);
    }

    /// <summary>The light theme, Copy rebound to F9, one folder tab locked.</summary>
    private async Task ArrangeSettingsAsync(HttpClient client)
    {
        var themeResponse = await client.PutAsJsonAsync("/api/settings", new UserSettingsRequest { Theme = UiThemes.Light });
        Assert.Equal(HttpStatusCode.OK, themeResponse.StatusCode);

        var shortcuts = await GetShortcutsAsync(client);
        var bindings = shortcuts.Select(binding => new KeyBindingRequest
        {
            ActionId = binding.ActionId,
            PrimaryKey = binding.ActionId == KeyBindingActions.Copy ? "F9" : binding.PrimaryKey,
            SecondaryKey = binding.SecondaryKey
        }).ToList();
        var shortcutsResponse = await client.PutAsJsonAsync("/api/settings/shortcuts", new KeyBindingsRequest { Bindings = bindings });
        Assert.Equal(HttpStatusCode.OK, shortcutsResponse.StatusCode);

        var tabResponse = await client.PostAsJsonAsync(
            "/api/foldertabs/lock", new FolderTabRequest { PanelId = "left", FolderPath = _rootPath });
        Assert.Equal(HttpStatusCode.OK, tabResponse.StatusCode);
    }

    private static async Task<List<KeyBindingDto>> GetShortcutsAsync(HttpClient client) =>
        (await ReadAsync<List<KeyBindingDto>>(await client.GetAsync("/api/settings/shortcuts")))!;

    private static KeyBindingDto CopyBinding(IEnumerable<KeyBindingDto> shortcuts) =>
        shortcuts.Single(binding => binding.ActionId == KeyBindingActions.Copy);

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions);
    }

    #endregion
}
