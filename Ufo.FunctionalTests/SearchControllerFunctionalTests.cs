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
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Ufo.Abstractions.Database;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Options;
using Ufo.Abstractions.Requests;
using Ufo.Abstractions.Responses;
using Ufo.Database;
using Ufo.Database.Contexts;
using Ufo.Server.Extensions;

namespace Ufo.FunctionalTests.SearchController;

#region Test WebApplication factory

public class SearchApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"test-{Guid.NewGuid():N}";
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
                ["JWT:Key"] = SearchTestConstants.JwtKey,
                ["JWT:Issuer"] = SearchTestConstants.JwtIssuer,
                ["JWT:Audience"] = SearchTestConstants.JwtAudience,
                ["Kestrel:Endpoints:App:Url"] = "http://localhost:0"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDbConnectionFactory>();
            services.AddScoped<IDbConnectionFactory>(sp =>
                new SqliteConnectionFactory(
                    new DatabaseOptions { ConnectionString = ConnectionString }.ToSearchOptionsMonitor(),
                    sp.GetRequiredService<ILogger<SqliteConnectionFactory>>()));

            services.AddLogging(lb => lb.SetMinimumLevel(LogLevel.Warning));

            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(SearchTestConstants.JwtKey)),
                    ValidateIssuer = true,
                    ValidIssuer = SearchTestConstants.JwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = SearchTestConstants.JwtAudience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            });
        });
    }

    public async Task<(HttpClient Client, Ulid UserId)> CreateAuthenticatedClientAsync()
    {
        _sqLiteConnection = new SqliteConnection(ConnectionString);
        await _sqLiteConnection.OpenAsync();
        await DapperDataContext.InitiateDatabaseAsync(_sqLiteConnection);

        var userId = Ulid.NewUlid();
        var userName = $"testuser-{userId}";
        await _sqLiteConnection.ExecuteAsync(
            SqlScripts.InsertUserSql,
            new { Id = userId.ToString(), Name = userName, PasswordHash = "hash" });

        var token = SearchJwtHelper.GenerateToken(userId, userName);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        return (client, userId);
    }

    public SqliteConnection Connection => _sqLiteConnection!;

    public HttpClient CreateUnauthenticatedClient() => CreateClient();

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

#region Constants & helpers

internal static class SearchTestConstants
{
    public const string JwtKey = "super-secret-test-key-that-is-long-enough-256bits!!";
    public const string JwtIssuer = "ufo-test-issuer";
    public const string JwtAudience = "ufo-test-audience";
    public const string ApiBase = "/api/search";
}

internal static class SearchJwtHelper
{
    public static string GenerateToken(Ulid userId, string userName = "testuser")
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SearchTestConstants.JwtKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, userName),
            new Claim("role", "user"),
        };

        var token = new JwtSecurityToken(
            issuer: SearchTestConstants.JwtIssuer,
            audience: SearchTestConstants.JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

internal static class SearchJsonHelper
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new UlidJsonConverter() }
    };

    public static async Task<T?> ReadAsync<T>(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync(), Options);
}

internal static class SearchOptionsMonitorExtensions
{
    public static IOptionsMonitor<T> ToSearchOptionsMonitor<T>(this T value) where T : class =>
        new SearchOptionsMonitorStub<T>(value);
}

internal sealed class SearchOptionsMonitorStub<T> : IOptionsMonitor<T> where T : class
{
    public SearchOptionsMonitorStub(T value) => CurrentValue = value;
    public T CurrentValue { get; }
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

#endregion

#region Database seeding helpers

/// <summary>
/// Seeds the minimum set of rows required for search queries to return results.
///
/// File search requires:   Files → FilesToFolders → Snapshots
/// Folder search requires: Folders → FoldersToFolders → Snapshots
///
/// Both queries LEFT JOIN LabelsToSnapshots → Labels, so labels are optional.
/// </summary>
internal static class SearchSeeder
{
    public static async Task<Ulid> SeedSnapshotAsync(SqliteConnection db, Ulid userId)
    {
        var snapshotId = Ulid.NewUlid();
        await db.ExecuteAsync(
            "INSERT INTO Snapshots (Id, Timestamp, Description, UserId) VALUES (@Id, @Timestamp, @Description, @UserId)",
            new { Id = snapshotId.ToString(), Timestamp = DateTime.UtcNow.ToString("o"), Description = "test", UserId = userId.ToString() });
        return snapshotId;
    }

    public static async Task<Ulid> SeedFolderAsync(SqliteConnection db, Ulid userId, string name)
    {
        var folderId = Ulid.NewUlid();
        await db.ExecuteAsync(SqlScripts.InsertFolderSql,
            new
            {
                Id = folderId.ToString(),
                Name = name,
                Size = 0.0,
                Sha256Hash = "abc123",
                UserId = userId.ToString(),
                CreatedAt = DateTime.UtcNow.AddMinutes(-10).ToString("o"),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-10).ToString("o"),
                IsHidden = false
            });
        return folderId;
    }

    /// <summary>
    /// Associates a folder with a snapshot via FoldersToFolders.
    /// ParentFolderId is NULL to represent a root folder.
    /// </summary>
    public static async Task LinkFolderToSnapshotAsync(SqliteConnection db, Ulid folderId, Ulid snapshotId)
    {
        await db.ExecuteAsync(
            "INSERT INTO FoldersToFolders (SnapshotId, ParentFolderId, ChildFolderId) VALUES (@SnapshotId, NULL, @ChildFolderId)",
            new { SnapshotId = snapshotId.ToString(), ChildFolderId = folderId.ToString() });
    }

    public static async Task<Ulid> SeedFileAsync(SqliteConnection db, Ulid userId, string name, string extension = ".txt")
    {
        var fileId = Ulid.NewUlid();
        await db.ExecuteAsync(SqlScripts.InsertFileSql,
            new
            {
                Id = fileId.ToString(),
                Name = name,
                Size = 100.0,
                FileExtension = extension,
                Sha256Hash = "def456",
                UserId = userId.ToString(),
                CreatedAt = DateTime.UtcNow.AddMinutes(-10).ToString("o"),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-10).ToString("o"),
                IsHidden = false
            });
        return fileId;
    }

    /// <summary>Associates a file with a folder and snapshot via FilesToFolders.</summary>
    public static async Task LinkFileToFolderAndSnapshotAsync(SqliteConnection db, Ulid fileId, Ulid folderId, Ulid snapshotId)
    {
        await db.ExecuteAsync(
            "INSERT INTO FilesToFolders (SnapshotId, FolderId, FileId) VALUES (@SnapshotId, @FolderId, @FileId)",
            new { SnapshotId = snapshotId.ToString(), FolderId = folderId.ToString(), FileId = fileId.ToString() });
    }

    /// <summary>
    /// Seeds a complete snapshot with one folder and one file, both matching the given name fragment.
    /// Returns the seeded snapshot, folder, and file IDs.
    /// </summary>
    public static async Task<(Ulid SnapshotId, Ulid FolderId, Ulid FileId)> SeedFullAsync(
        SqliteConnection db, Ulid userId, string name)
    {
        var snapshotId = await SeedSnapshotAsync(db, userId);
        var folderId = await SeedFolderAsync(db, userId, name);
        var fileId = await SeedFileAsync(db, userId, name);

        await LinkFolderToSnapshotAsync(db, folderId, snapshotId);
        await LinkFileToFolderAndSnapshotAsync(db, fileId, folderId, snapshotId);

        return (snapshotId, folderId, fileId);
    }
}

#endregion

#region 1. Authentication & Authorization

public class SearchController_AuthTests : IAsyncLifetime
{
    private readonly SearchApiFactory _factory = new();
    private HttpClient _authClient = null!;
    private Ulid _userId;

    public async Task InitializeAsync() =>
        (_authClient, _userId) = await _factory.CreateAuthenticatedClientAsync();

    public Task DisposeAsync()
    {
        _authClient.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Search_WithoutToken_Returns401()
    {
        var client = _factory.CreateUnauthenticatedClient();
        var response = await client.PostAsJsonAsync(SearchTestConstants.ApiBase,
            new SearchRequest { Query = "test" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Search_WithValidToken_DoesNotReturn401Or403()
    {
        // Query is >= 3 chars so model validation passes.
        var response = await _authClient.PostAsJsonAsync(SearchTestConstants.ApiBase,
            new SearchRequest { Query = "test" });
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Search_WithExpiredToken_Returns401()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SearchTestConstants.JwtKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiredToken = new JwtSecurityToken(
            issuer: SearchTestConstants.JwtIssuer,
            audience: SearchTestConstants.JwtAudience,
            claims: new[] { new Claim(ClaimTypes.NameIdentifier, _userId.ToString()) },
            notBefore: DateTime.UtcNow.AddHours(-2),
            expires: DateTime.UtcNow.AddHours(-1),
            signingCredentials: credentials);

        var client = _factory.CreateUnauthenticatedClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", new JwtSecurityTokenHandler().WriteToken(expiredToken));

        var response = await client.PostAsJsonAsync(SearchTestConstants.ApiBase,
            new SearchRequest { Query = "test" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

#endregion

#region 2. POST /api/search – query validation (MinLength 3)

public class SearchController_QueryValidationTests : IAsyncLifetime
{
    private readonly SearchApiFactory _factory = new();
    private HttpClient _client = null!;
    private Ulid _userId;

    public async Task InitializeAsync() =>
        (_client, _userId) = await _factory.CreateAuthenticatedClientAsync();

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Search_EmptyQuery_Returns400()
    {
        var response = await _client.PostAsJsonAsync(SearchTestConstants.ApiBase,
            new SearchRequest { Query = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_WhitespaceQuery_Returns400()
    {
        var response = await _client.PostAsJsonAsync(SearchTestConstants.ApiBase,
            new SearchRequest { Query = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_OneCharQuery_Returns400()
    {
        var response = await _client.PostAsJsonAsync(SearchTestConstants.ApiBase,
            new SearchRequest { Query = "a" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_TwoCharQuery_Returns400()
    {
        var response = await _client.PostAsJsonAsync(SearchTestConstants.ApiBase,
            new SearchRequest { Query = "ab" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_QueryExactly3Chars_DoesNotReturn400()
    {
        // Exactly 3 chars satisfies [MinLength(3)] — model validation passes.
        var response = await _client.PostAsJsonAsync(SearchTestConstants.ApiBase,
            new SearchRequest { Query = "abc" });
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_QueryLongerThan3Chars_DoesNotReturn400()
    {
        var response = await _client.PostAsJsonAsync(SearchTestConstants.ApiBase,
            new SearchRequest { Query = "longer_query" });
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_ValidQueryWithNoMatch_Returns204()
    {
        // Valid query (>= 3 chars) but no matching data → 204.
        var response = await _client.PostAsJsonAsync(SearchTestConstants.ApiBase,
            new SearchRequest { Query = "zzz_no_match_zzz" });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}

#endregion

#region 3. POST /api/search – files and folders results

public class SearchController_ResultTests : IAsyncLifetime
{
    private readonly SearchApiFactory _factory = new();
    private HttpClient _client = null!;
    private Ulid _userId;
    private SqliteConnection _db = null!;

    public async Task InitializeAsync()
    {
        (_client, _userId) = await _factory.CreateAuthenticatedClientAsync();
        _db = _factory.Connection;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Search_MatchingFilesAndFolders_Returns200()
    {
        await SearchSeeder.SeedFullAsync(_db, _userId, "invoice_2024");

        // IncludeFiles and IncludeFolders both default to true.
        var response = await _client.PostAsJsonAsync(SearchTestConstants.ApiBase,
            new SearchRequest { Query = "invoice" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Search_MatchingFilesAndFolders_ResponseContainsCorrectNames()
    {
        await SearchSeeder.SeedFullAsync(_db, _userId, "report_q3");

        var filesInDb = await _db.QueryAsync<FileEntity>("SELECT * FROM Files WHERE UserId = @UserId", new { UserId = _userId });

        var response = await _client.PostAsJsonAsync(SearchTestConstants.ApiBase,
            new SearchRequest { Query = "report" });

        var result = await SearchJsonHelper.ReadAsync<SearchResponse>(response);
        Assert.NotNull(result);
        Assert.Contains(result.Files, f => f.Name.Contains("report"));
        Assert.Contains(result.Folders, f => f.Name.Contains("report"));
    }

    [Fact]
    public async Task Search_OnlyFilesRequested_ReturnsMatchingFilesWithoutFolders()
    {
        // IncludeFolders = false → Folders list stays empty, but matching files
        // must still be returned with 200 OK.
        var snapshotId = await SearchSeeder.SeedSnapshotAsync(_db, _userId);
        var folderId = await SearchSeeder.SeedFolderAsync(_db, _userId, "container");
        await SearchSeeder.LinkFolderToSnapshotAsync(_db, folderId, snapshotId);

        var fileId = await SearchSeeder.SeedFileAsync(_db, _userId, "standalone_file");
        await SearchSeeder.LinkFileToFolderAndSnapshotAsync(_db, fileId, folderId, snapshotId);

        // IncludeFiles defaults to true.
        var response = await _client.PostAsJsonAsync(SearchTestConstants.ApiBase,
            new SearchRequest { Query = "standalone", IncludeFolders = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await SearchJsonHelper.ReadAsync<SearchResponse>(response);
        Assert.NotNull(result);
        Assert.NotEmpty(result.Files);
        Assert.Empty(result.Folders);
    }

    [Fact]
    public async Task Search_OnlyFoldersRequested_ReturnsMatchingFoldersWithoutFiles()
    {
        // IncludeFiles = false → Files list stays empty, but matching folders
        // must still be returned with 200 OK.
        var snapshotId = await SearchSeeder.SeedSnapshotAsync(_db, _userId);
        var folderId = await SearchSeeder.SeedFolderAsync(_db, _userId, "orphan_folder");
        await SearchSeeder.LinkFolderToSnapshotAsync(_db, folderId, snapshotId);

        // IncludeFolders defaults to true.
        var response = await _client.PostAsJsonAsync(SearchTestConstants.ApiBase,
            new SearchRequest { Query = "orphan", IncludeFiles = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await SearchJsonHelper.ReadAsync<SearchResponse>(response);
        Assert.NotNull(result);
        Assert.NotEmpty(result.Folders);
        Assert.Empty(result.Files);
    }

    [Fact]
    public async Task Search_IsCaseInsensitive()
    {
        await SearchSeeder.SeedFullAsync(_db, _userId, "DocumentSummary");

        // SQLite LIKE is case-insensitive for ASCII by default.
        var response = await _client.PostAsJsonAsync(SearchTestConstants.ApiBase,
            new SearchRequest { Query = "documentsummary" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Search_PartialMatch_ReturnsResults()
    {
        await SearchSeeder.SeedFullAsync(_db, _userId, "annual_budget_2024");

        var response = await _client.PostAsJsonAsync(SearchTestConstants.ApiBase,
            new SearchRequest { Query = "budget" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await SearchJsonHelper.ReadAsync<SearchResponse>(response);
        Assert.NotNull(result);
        Assert.Contains(result.Files, f => f.Name.Contains("budget"));
        Assert.Contains(result.Folders, f => f.Name.Contains("budget"));
    }

    [Fact]
    public async Task Search_MultipleMatchingItems_ReturnsAll()
    {
        var snapshotId = await SearchSeeder.SeedSnapshotAsync(_db, _userId);

        var folder1 = await SearchSeeder.SeedFolderAsync(_db, _userId, "project_alpha");
        var folder2 = await SearchSeeder.SeedFolderAsync(_db, _userId, "project_beta");
        var file1 = await SearchSeeder.SeedFileAsync(_db, _userId, "project_alpha");
        var file2 = await SearchSeeder.SeedFileAsync(_db, _userId, "project_beta");

        await SearchSeeder.LinkFolderToSnapshotAsync(_db, folder1, snapshotId);
        await SearchSeeder.LinkFolderToSnapshotAsync(_db, folder2, snapshotId);
        await SearchSeeder.LinkFileToFolderAndSnapshotAsync(_db, file1, folder1, snapshotId);
        await SearchSeeder.LinkFileToFolderAndSnapshotAsync(_db, file2, folder2, snapshotId);

        var response = await _client.PostAsJsonAsync(SearchTestConstants.ApiBase,
            new SearchRequest { Query = "project" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await SearchJsonHelper.ReadAsync<SearchResponse>(response);
        Assert.NotNull(result);
        Assert.Equal(2, result.Files.Count);
        Assert.Equal(2, result.Folders.Count);
    }
}

#endregion

#region 4. POST /api/search – user isolation

public class SearchController_UserIsolationTests : IAsyncLifetime
{
    private readonly SearchApiFactory _factory = new();
    private HttpClient _client = null!;
    private Ulid _userId;
    private SqliteConnection _db = null!;

    public async Task InitializeAsync()
    {
        (_client, _userId) = await _factory.CreateAuthenticatedClientAsync();
        _db = _factory.Connection;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Search_DoesNotReturnAnotherUsersFiles()
    {
        var otherUserId = Ulid.NewUlid();
        await _db.ExecuteAsync(
            SqlScripts.InsertUserSql,
            new { Id = otherUserId.ToString(), Name = $"otheruser-{otherUserId}", PasswordHash = "hash" });

        await SearchSeeder.SeedFullAsync(_db, otherUserId, "confidential_data");

        // User 1 searches — must not see user 2's data.
        var response = await _client.PostAsJsonAsync(SearchTestConstants.ApiBase,
            new SearchRequest { Query = "confidential" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Search_OnlyReturnsCurrentUsersData()
    {
        var otherUserId = Ulid.NewUlid();
        await _db.ExecuteAsync(
            SqlScripts.InsertUserSql,
            new { Id = otherUserId.ToString(), Name = $"otheruser-{otherUserId}", PasswordHash = "hash" });

        // Both users have data matching the same query term.
        var ownSeed = await SearchSeeder.SeedFullAsync(_db, _userId, "shared_term");
        var otherUserSeed = await SearchSeeder.SeedFullAsync(_db, otherUserId, "shared_term");

        var response = await _client.PostAsJsonAsync(SearchTestConstants.ApiBase,
            new SearchRequest { Query = "shared_term" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await SearchJsonHelper.ReadAsync<SearchResponse>(response);
        Assert.NotNull(result);

        // Isolation is asserted by identity and by exact count, so an empty or
        // truncated result fails here instead of passing vacuously.
        Assert.Equal(ownSeed.FileId, Assert.Single(result.Files).Id);
        Assert.Equal(ownSeed.FolderId, Assert.Single(result.Folders).Id);

        // The other user's rows must not leak into this response.
        Assert.DoesNotContain(result.Files, file => file.Id == otherUserSeed.FileId);
        Assert.DoesNotContain(result.Folders, folder => folder.Id == otherUserSeed.FolderId);
    }
}

#endregion