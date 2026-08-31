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
using Ufo.Abstractions;
using Ufo.Abstractions.Database;
using Ufo.Abstractions.DataTransferObjects;
using Ufo.Abstractions.Options;
using Ufo.Abstractions.Requests;
using Ufo.Database;
using Ufo.Database.Contexts;
using Ufo.FunctionalTests.Extensions;
using Ufo.Server.Extensions;

namespace Ufo.FunctionalTests.LabelController;

#region Test WebApplication factory

/// <summary>
/// Boots a real in-process ASP.NET Core host using the production Program entry
/// point, then swaps out the SQLite connection for a shared in-memory database
/// so every test is fully isolated yet exercises the complete stack:
///   HTTP pipeline → middleware → controller → repository → SQLite.
/// </summary>
public class LabelApiFactory : WebApplicationFactory<Program>
{
    // Each factory instance gets its own named in-memory database so tests
    // that run in parallel cannot interfere with each other.
    private readonly string _dbName = $"test-{Guid.NewGuid():N}";

    // We keep one open connection alive for the lifetime of the factory so
    // the in-memory database is not destroyed between requests.
    private SqliteConnection? _sqLiteConnection;

    public string ConnectionString => $"Data Source={_dbName};Mode=Memory;Cache=Shared";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(HostEnvironmentExtensions.FunctionalTesting);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Override the connection string so the app uses our in-memory DB.
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = ConnectionString,
                // Provide minimal JWT settings so authentication can be wired up.
                ["JWT:Key"] = TestConstants.JwtKey,
                ["JWT:Issuer"] = TestConstants.JwtIssuer,
                ["JWT:Audience"] = TestConstants.JwtAudience,
                // Suppress the OpenBrowser call – not relevant in tests.
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

            // Re-register JWT authentication using the test key.
            // Program.cs reads jwtOptions BEFORE ConfigureAppConfiguration callbacks fire,
            // so the middleware was initialized with the real appsettings.json key, not the test key.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(TestConstants.JwtKey)),
                    ValidateIssuer = true,
                    ValidIssuer = TestConstants.JwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = TestConstants.JwtAudience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            });
        });
    }

    /// <summary>
    /// Creates the schema and seeds a test user, returning a ready-to-use HTTP
    /// client with a valid JWT for that user pre-attached.
    /// </summary>
    public async Task<(HttpClient Client, Ulid UserId)> CreateAuthenticatedClientAsync()
    {
        // Open (and keep open) the anchor connection so the in-memory database
        // survives across the multiple connections the app will open internally.
        _sqLiteConnection = new SqliteConnection(ConnectionString);
        await _sqLiteConnection.OpenAsync();
        await DapperDataContext.InitiateDatabaseAsync(_sqLiteConnection);

        var userId = Ulid.NewUlid();
        var userName = $"testuser-{userId}";
        await _sqLiteConnection.ExecuteAsync(
            SqlScripts.InsertUserSql,
            new { Id = userId.ToString(), Name = userName, PasswordHash = "hash", IsAdmin = false });

        var token = JwtTestHelper.GenerateToken(userId, userName);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        return (client, userId);
    }

    /// <summary>Returns an HTTP client with NO authorization header.</summary>
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

internal static class TestConstants
{
    public const string JwtKey = "super-secret-test-key-that-is-long-enough-256bits!!";
    public const string JwtIssuer = "ufo-test-issuer";
    public const string JwtAudience = "ufo-test-audience";
    public const string ApiBase = "/api/label";
}

internal static class JwtTestHelper
{
    public static string GenerateToken(
        Ulid userId,
        string userName = "testuser",
        int expiryMinutes = 60,
        string? overrideSub = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestConstants.JwtKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, overrideSub ?? userId.ToString()),
            new Claim(ClaimTypes.Name, userName),
            new Claim("role", "user"),
        };

        var token = new JwtSecurityToken(
            issuer: TestConstants.JwtIssuer,
            audience: TestConstants.JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static string GenerateExpiredToken(Ulid userId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestConstants.JwtKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: TestConstants.JwtIssuer,
            audience: TestConstants.JwtAudience,
            claims: new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) },
            notBefore: DateTime.UtcNow.AddHours(-2),
            expires: DateTime.UtcNow.AddHours(-1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

internal static class RequestFactory
{
    public static LabelRequest NewLabel(string name = "TestLabel", string color = "#FF0000") =>
        new()
        {
            Id = Ulid.NewUlid(),
            Name = name,
            ColorHex = color
        };
}

#endregion

#region JSON deserialization helpers

internal static class Json
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new UlidJsonConverter() }
    };

    public static T? Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options);

    public static async Task<T?> ReadAsync<T>(HttpResponseMessage response) =>
        Deserialize<T>(await response.Content.ReadAsStringAsync());
}

#endregion

#region IOptionsMonitor adapter

/// <summary>
/// Minimal <see cref="IOptionsMonitor{TOptions}"/> implementation that wraps a
/// fixed value. Needed because <see cref="SqliteConnectionFactory"/> requires
/// <c>IOptionsMonitor</c> rather than the simpler <c>IOptions</c>.
/// </summary>
internal sealed class OptionsMonitorStub<T> : IOptionsMonitor<T> where T : class
{
    public OptionsMonitorStub(T value) => CurrentValue = value;

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}


#endregion

#region 1. Authentication & Authorization

public class LabelController_AuthTests : IAsyncLifetime
{
    private readonly LabelApiFactory _factory = new();
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
    public async Task GetAllLabels_WithoutToken_Returns401()
    {
        var client = _factory.CreateUnauthenticatedClient();
        var response = await client.GetAsync(TestConstants.ApiBase);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAllLabels_WithExpiredToken_Returns401()
    {
        var expiredToken = JwtTestHelper.GenerateExpiredToken(_userId);
        var client = _factory.CreateUnauthenticatedClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", expiredToken);

        var response = await client.GetAsync(TestConstants.ApiBase);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAllLabels_WithWrongSigningKey_Returns401()
    {
        // Sign with a different key – validation must fail.
        var badKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("completely-wrong-key-that-doesnt-match-anything!!"));
        var badCredentials = new SigningCredentials(badKey, SecurityAlgorithms.HmacSha256);
        var badToken = new JwtSecurityToken(
            TestConstants.JwtIssuer,
            TestConstants.JwtAudience,
            new[] { new Claim(ClaimTypes.NameIdentifier, _userId.ToString()) },
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: badCredentials);

        var client = _factory.CreateUnauthenticatedClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", new JwtSecurityTokenHandler().WriteToken(badToken));

        var response = await client.GetAsync(TestConstants.ApiBase);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAllLabels_WithValidToken_DoesNotReturn401Or403()
    {
        var response = await _authClient.GetAsync(TestConstants.ApiBase);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostLabel_WithoutToken_Returns401()
    {
        var client = _factory.CreateUnauthenticatedClient();
        var response = await client.PostAsJsonAsync(TestConstants.ApiBase, RequestFactory.NewLabel());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteLabel_WithoutToken_Returns401()
    {
        var client = _factory.CreateUnauthenticatedClient();
        var response = await client.DeleteAsync($"{TestConstants.ApiBase}/{Ulid.NewUlid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task JwtClaimsRequired_MissingSubClaim_Returns401()
    {
        // Token with no NameIdentifier claim – JwtClaimsRequiredAttribute should reject it.
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestConstants.JwtKey));
        var token = new JwtSecurityToken(
            TestConstants.JwtIssuer,
            TestConstants.JwtAudience,
            claims: new[] { new Claim(ClaimTypes.Name, "noSubUser") }, // no NameIdentifier — JwtClaimsRequiredAttribute will reject
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        var client = _factory.CreateUnauthenticatedClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", new JwtSecurityTokenHandler().WriteToken(token));

        var response = await client.GetAsync(TestConstants.ApiBase);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

#endregion

#region 2. POST /api/label – AddLabel

public class LabelController_AddLabelTests : IAsyncLifetime
{
    private readonly LabelApiFactory _factory = new();
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
    public async Task AddLabel_ValidRequest_Returns200WithSuccessResult()
    {
        var label = RequestFactory.NewLabel("Work");
        var response = await _client.PostAsJsonAsync(TestConstants.ApiBase, label);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var results = await Json.ReadAsync<List<ServerResult>>(response);
        Assert.NotNull(results);
        Assert.Contains(results, r => r.Result == Result.Success && r.Priority == ActionPriority.Highest);
    }

    [Fact]
    public async Task AddLabel_DuplicateName_Returns400WithError()
    {
        var label = RequestFactory.NewLabel("Duplicate");
        await _client.PostAsJsonAsync(TestConstants.ApiBase, label);

        // Try adding a second label with the same name (different Id).
        var duplicate = RequestFactory.NewLabel("Duplicate");
        var response = await _client.PostAsJsonAsync(TestConstants.ApiBase, duplicate);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var results = await Json.ReadAsync<List<ServerResult>>(response);
        Assert.NotNull(results);
        Assert.Contains(results, r => r.Result == Result.Error && r.Priority == ActionPriority.Highest);
    }

    [Fact]
    public async Task AddLabel_TwoDistinctUsers_SameNameAllowed()
    {
        // Create a second authenticated user/client.
        var (client2, _) = await _factory.CreateAuthenticatedClientAsync();

        const string sharedName = "SharedLabelName";
        var r1 = await _client.PostAsJsonAsync(TestConstants.ApiBase, RequestFactory.NewLabel(sharedName));
        var r2 = await client2.PostAsJsonAsync(TestConstants.ApiBase, RequestFactory.NewLabel(sharedName));

        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);

        client2.Dispose();
    }

    [Fact]
    public async Task AddLabel_PersistsInDatabase_CanBeRetrievedAfterwards()
    {
        var label = RequestFactory.NewLabel("PersistCheck");
        await _client.PostAsJsonAsync(TestConstants.ApiBase, label);

        var getResponse = await _client.GetAsync(TestConstants.ApiBase);
        var labels = await Json.ReadAsync<List<LabelDto>>(getResponse);

        Assert.NotNull(labels);
        Assert.Contains(labels, l => l.Name == "PersistCheck");
    }

    [Fact]
    public async Task AddLabel_StoresCorrectColorHex()
    {
        const string color = "#AABBCC";
        var label = RequestFactory.NewLabel("ColorTest", color);
        await _client.PostAsJsonAsync(TestConstants.ApiBase, label);

        var getResponse = await _client.GetAsync(TestConstants.ApiBase);
        var labels = await Json.ReadAsync<List<LabelDto>>(getResponse);

        Assert.NotNull(labels);
        Assert.Contains(labels, l => l.Name == "ColorTest" && l.ColorHex == color);
    }
}

#endregion

#region 3. GET /api/label – GetAllLabels

public class LabelController_GetAllLabelsTests : IAsyncLifetime
{
    private readonly LabelApiFactory _factory = new();
    private HttpClient _client = null!;

    public async Task InitializeAsync() =>
        (_client, _) = await _factory.CreateAuthenticatedClientAsync();

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetAllLabels_NoLabels_Returns404()
    {
        var response = await _client.GetAsync(TestConstants.ApiBase);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAllLabels_WithLabels_Returns200AndList()
    {
        await _client.PostAsJsonAsync(TestConstants.ApiBase, RequestFactory.NewLabel("Alpha"));
        await _client.PostAsJsonAsync(TestConstants.ApiBase, RequestFactory.NewLabel("Beta"));

        var response = await _client.GetAsync(TestConstants.ApiBase);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var labels = await Json.ReadAsync<List<LabelDto>>(response);
        Assert.NotNull(labels);
        Assert.Equal(2, labels.Count);
    }

    [Fact]
    public async Task GetAllLabels_OnlyReturnsOwnUsersLabels()
    {
        // User 1 adds a label.
        var ownLabel = RequestFactory.NewLabel("User1Label");
        await _client.PostAsJsonAsync(TestConstants.ApiBase, ownLabel);

        // User 2 adds a different label.
        var (client2, _) = await _factory.CreateAuthenticatedClientAsync();
        var otherUserLabel = RequestFactory.NewLabel("User2Label");
        await client2.PostAsJsonAsync(TestConstants.ApiBase, otherUserLabel);

        // User 1 should only see their own label.
        var response = await _client.GetAsync(TestConstants.ApiBase);
        var labels = await Json.ReadAsync<List<LabelDto>>(response);

        Assert.NotNull(labels);

        // Isolation is asserted by identity and by exact count, so an empty
        // result fails here instead of passing vacuously.
        var returnedLabel = Assert.Single(labels);
        Assert.Equal(ownLabel.Id, returnedLabel.Id);
        Assert.Equal("User1Label", returnedLabel.Name);

        // The other user's label must not leak into this response.
        Assert.DoesNotContain(labels, label => label.Id == otherUserLabel.Id);
        Assert.DoesNotContain(labels, label => label.Name == "User2Label");

        client2.Dispose();
    }
}

#endregion

#region 4. GET /api/label/snapshot/{snapshotId} – GetLabelsBySnapshotId

public class LabelController_GetLabelsBySnapshotTests : IAsyncLifetime
{
    private readonly LabelApiFactory _factory = new();
    private HttpClient _client = null!;
    private Ulid _userId;
    private SqliteConnection _db = null!;

    public async Task InitializeAsync()
    {
        (_client, _userId) = await _factory.CreateAuthenticatedClientAsync();
        _db = new SqliteConnection(_factory.ConnectionString);
        await _db.OpenAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<Ulid> SeedSnapshotAsync()
    {
        var snapshotId = Ulid.NewUlid();
        await _db.ExecuteAsync(
            SqlScripts.InsertSnapshotSql,
            new { Id = snapshotId.ToString(), Timestamp = DateTime.UtcNow.ToString("o"), Description = "test", UserId = _userId.ToString() });
        return snapshotId;
    }

    [Fact]
    public async Task GetLabelsBySnapshot_NoAssociations_Returns404()
    {
        var snapshotId = await SeedSnapshotAsync();
        var response = await _client.GetAsync($"{TestConstants.ApiBase}/snapshot/{snapshotId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetLabelsBySnapshot_AfterAssigning_Returns200WithLabel()
    {
        var snapshotId = await SeedSnapshotAsync();
        var label = RequestFactory.NewLabel("SnapshotLabel");
        await _client.PostAsJsonAsync(TestConstants.ApiBase, label);

        // Assign label to snapshot via the dedicated endpoint.
        await _client.PostAsync($"{TestConstants.ApiBase}/{label.Id}/snapshot/{snapshotId}", null);

        var response = await _client.GetAsync($"{TestConstants.ApiBase}/snapshot/{snapshotId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var labels = await Json.ReadAsync<List<LabelDto>>(response);
        Assert.NotNull(labels);
        Assert.Contains(labels, l => l.Name == "SnapshotLabel");
    }

    [Fact]
    public async Task GetLabelsBySnapshot_AfterRemoval_Returns404()
    {
        var snapshotId = await SeedSnapshotAsync();
        var label = RequestFactory.NewLabel("RemovableLabel");
        await _client.PostAsJsonAsync(TestConstants.ApiBase, label);
        await _client.PostAsync($"{TestConstants.ApiBase}/{label.Id}/snapshot/{snapshotId}", null);

        // Now remove it.
        await _client.DeleteAsync($"{TestConstants.ApiBase}/{label.Id}/snapshot/{snapshotId}");

        var response = await _client.GetAsync($"{TestConstants.ApiBase}/snapshot/{snapshotId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

#endregion

#region 5. PUT /api/label – UpdateLabel

public class LabelController_UpdateLabelTests : IAsyncLifetime
{
    private readonly LabelApiFactory _factory = new();
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
    public async Task UpdateLabel_ExistingLabel_Returns200WithSuccess()
    {
        var label = RequestFactory.NewLabel("Original");
        await _client.PostAsJsonAsync(TestConstants.ApiBase, label);

        var updated = label with { Name = "Updated", ColorHex = "#00FF00" };
        var response = await _client.PutAsJsonAsync(TestConstants.ApiBase, updated);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await Json.ReadAsync<ServerResult>(response);
        Assert.NotNull(result);
        Assert.Equal(Result.Success, result.Result);
    }

    [Fact]
    public async Task UpdateLabel_NonExistentLabel_Returns400()
    {
        var ghost = RequestFactory.NewLabel("Ghost"); // never inserted
        var response = await _client.PutAsJsonAsync(TestConstants.ApiBase, ghost);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateLabel_NameCollisionWithOtherLabel_Returns400()
    {
        await _client.PostAsJsonAsync(TestConstants.ApiBase, RequestFactory.NewLabel("First"));
        var second = RequestFactory.NewLabel("Second");
        await _client.PostAsJsonAsync(TestConstants.ApiBase, second);

        // Try to rename "Second" → "First" (collision).
        var colliding = second with { Name = "First" };
        var response = await _client.PutAsJsonAsync(TestConstants.ApiBase, colliding);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateLabel_SameNameOnSameLabel_IsAllowed()
    {
        var label = RequestFactory.NewLabel("SameName");
        await _client.PostAsJsonAsync(TestConstants.ApiBase, label);

        // Update but keep the same name – should succeed.
        var response = await _client.PutAsJsonAsync(TestConstants.ApiBase, label with { ColorHex = "#112233" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UpdateLabel_ChangesArePersisted()
    {
        var label = RequestFactory.NewLabel("BeforeUpdate");
        await _client.PostAsJsonAsync(TestConstants.ApiBase, label);
        await _client.PutAsJsonAsync(TestConstants.ApiBase, label with { Name = "AfterUpdate", ColorHex = "#FFFFFF" });

        var all = await Json.ReadAsync<List<LabelDto>>(
            await _client.GetAsync(TestConstants.ApiBase));

        Assert.NotNull(all);
        Assert.Contains(all, l => l.Name == "AfterUpdate" && l.ColorHex == "#FFFFFF");
        Assert.DoesNotContain(all, l => l.Name == "BeforeUpdate");
    }

    [Fact]
    public async Task UpdateLabel_CannotUpdateAnotherUsersLabel()
    {
        var (client2, _) = await _factory.CreateAuthenticatedClientAsync();

        var label = RequestFactory.NewLabel("User2ExclusiveLabel");
        await client2.PostAsJsonAsync(TestConstants.ApiBase, label);

        // User 1 tries to update user 2's label by Id.
        var response = await _client.PutAsJsonAsync(TestConstants.ApiBase, label with { Name = "HijackedLabel" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        client2.Dispose();
    }
}

#endregion

#region 6. POST /api/label/{labelId}/snapshot/{snapshotId} – AddLabelToSnapshot

public class LabelController_AddLabelToSnapshotTests : IAsyncLifetime
{
    private readonly LabelApiFactory _factory = new();
    private HttpClient _client = null!;
    private Ulid _userId;
    private SqliteConnection _db = null!;

    public async Task InitializeAsync()
    {
        (_client, _userId) = await _factory.CreateAuthenticatedClientAsync();
        _db = new SqliteConnection(_factory.ConnectionString);
        await _db.OpenAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<Ulid> SeedSnapshotAsync()
    {
        var id = Ulid.NewUlid();
        await _db.ExecuteAsync(
            SqlScripts.InsertSnapshotSql,
            new { Id = id.ToString(), Timestamp = DateTime.UtcNow.ToString("o"), Description = "snap", UserId = _userId.ToString() });
        return id;
    }

    [Fact]
    public async Task AddLabelToSnapshot_ValidIds_Returns200WithSuccess()
    {
        var label = RequestFactory.NewLabel("AssignMe");
        await _client.PostAsJsonAsync(TestConstants.ApiBase, label);
        var snapshotId = await SeedSnapshotAsync();

        var response = await _client.PostAsync(
            $"{TestConstants.ApiBase}/{label.Id}/snapshot/{snapshotId}", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await Json.ReadAsync<ServerResult>(response);
        Assert.Equal(Result.Success, result!.Result);
    }

    [Fact]
    public async Task AddLabelToSnapshot_NonExistentLabel_Returns400()
    {
        var snapshotId = await SeedSnapshotAsync();
        var response = await _client.PostAsync(
            $"{TestConstants.ApiBase}/{Ulid.NewUlid()}/snapshot/{snapshotId}", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddLabelToSnapshot_NonExistentSnapshot_Returns400()
    {
        var label = RequestFactory.NewLabel("NoSnapLabel");
        await _client.PostAsJsonAsync(TestConstants.ApiBase, label);

        var response = await _client.PostAsync(
            $"{TestConstants.ApiBase}/{label.Id}/snapshot/{Ulid.NewUlid()}", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddLabelToSnapshot_OtherUsersSnapshot_Returns400()
    {
        var (client2, userId2) = await _factory.CreateAuthenticatedClientAsync();
        var db2 = new SqliteConnection(_factory.ConnectionString);
        await db2.OpenAsync();

        // Snapshot belongs to user 2.
        var snapshotId = Ulid.NewUlid();
        await db2.ExecuteAsync(
            SqlScripts.InsertSnapshotSql,
            new { Id = snapshotId.ToString(), Timestamp = DateTime.UtcNow.ToString("o"), Description = "u2snap", UserId = userId2.ToString() });

        var label = RequestFactory.NewLabel("User1Label");
        await _client.PostAsJsonAsync(TestConstants.ApiBase, label);

        // User 1 tries to assign their label to user 2's snapshot.
        var response = await _client.PostAsync(
            $"{TestConstants.ApiBase}/{label.Id}/snapshot/{snapshotId}", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await db2.DisposeAsync();
        client2.Dispose();
    }
}

#endregion

#region 7. DELETE /api/label/{labelId}/snapshot/{snapshotId} – RemoveLabelFromSnapshot

public class LabelController_RemoveLabelFromSnapshotTests : IAsyncLifetime
{
    private readonly LabelApiFactory _factory = new();
    private HttpClient _client = null!;
    private Ulid _userId;
    private SqliteConnection _db = null!;

    public async Task InitializeAsync()
    {
        (_client, _userId) = await _factory.CreateAuthenticatedClientAsync();
        _db = new SqliteConnection(_factory.ConnectionString);
        await _db.OpenAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<Ulid> SeedSnapshotAsync()
    {
        var id = Ulid.NewUlid();
        await _db.ExecuteAsync(
            SqlScripts.InsertSnapshotSql,
            new { Id = id.ToString(), Timestamp = DateTime.UtcNow.ToString("o"), Description = "snap", UserId = _userId.ToString() });
        return id;
    }

    [Fact]
    public async Task RemoveLabelFromSnapshot_ExistingAssociation_Returns200()
    {
        var label = RequestFactory.NewLabel("RemoveLabel");
        await _client.PostAsJsonAsync(TestConstants.ApiBase, label);
        var snapshotId = await SeedSnapshotAsync();
        await _client.PostAsync($"{TestConstants.ApiBase}/{label.Id}/snapshot/{snapshotId}", null);

        var response = await _client.DeleteAsync(
            $"{TestConstants.ApiBase}/{label.Id}/snapshot/{snapshotId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RemoveLabelFromSnapshot_NonExistentLabel_Returns400()
    {
        var snapshotId = await SeedSnapshotAsync();
        var response = await _client.DeleteAsync(
            $"{TestConstants.ApiBase}/{Ulid.NewUlid()}/snapshot/{snapshotId}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RemoveLabelFromSnapshot_LabelNotAssignedToSnapshot_Returns400()
    {
        var label = RequestFactory.NewLabel("UnassignedLabel");
        await _client.PostAsJsonAsync(TestConstants.ApiBase, label);
        var snapshotId = await SeedSnapshotAsync();

        // Never assigned – removal should indicate not found.
        var response = await _client.DeleteAsync(
            $"{TestConstants.ApiBase}/{label.Id}/snapshot/{snapshotId}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

#endregion

#region 8. DELETE /api/label/{labelId} – DeleteLabelById

public class LabelController_DeleteLabelTests : IAsyncLifetime
{
    private readonly LabelApiFactory _factory = new();
    private HttpClient _client = null!;
    private Ulid _userId;
    private SqliteConnection _db = null!;

    public async Task InitializeAsync()
    {
        (_client, _userId) = await _factory.CreateAuthenticatedClientAsync();
        _db = new SqliteConnection(_factory.ConnectionString);
        await _db.OpenAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task DeleteLabel_ExistingLabel_Returns200()
    {
        var label = RequestFactory.NewLabel("ToDelete");
        await _client.PostAsJsonAsync(TestConstants.ApiBase, label);

        var response = await _client.DeleteAsync($"{TestConstants.ApiBase}/{label.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteLabel_NonExistentLabel_Returns404()
    {
        var response = await _client.DeleteAsync($"{TestConstants.ApiBase}/{Ulid.NewUlid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteLabel_RemovedFromGetAll()
    {
        var label = RequestFactory.NewLabel("WillBeGone");
        await _client.PostAsJsonAsync(TestConstants.ApiBase, label);
        await _client.DeleteAsync($"{TestConstants.ApiBase}/{label.Id}");

        var getResponse = await _client.GetAsync(TestConstants.ApiBase);
        // Either 404 (no labels) or 200 (other labels exist) – ours must not appear.
        if (getResponse.StatusCode == HttpStatusCode.OK)
        {
            var labels = await Json.ReadAsync<List<LabelDto>>(getResponse);
            Assert.DoesNotContain(labels!, l => l.Name == "WillBeGone");
        }
        else
        {
            Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        }
    }

    [Fact]
    public async Task DeleteLabel_AlsoCleansUpSnapshotAssociations()
    {
        var snapshotId = Ulid.NewUlid();
        await _db.ExecuteAsync(
            SqlScripts.InsertSnapshotSql,
            new { Id = snapshotId.ToString(), Timestamp = DateTime.UtcNow.ToString("o"), Description = "snap", UserId = _userId.ToString() });

        var label = RequestFactory.NewLabel("LabelWithAssoc");
        await _client.PostAsJsonAsync(TestConstants.ApiBase, label);
        await _client.PostAsync($"{TestConstants.ApiBase}/{label.Id}/snapshot/{snapshotId}", null);

        // Delete the label.
        await _client.DeleteAsync($"{TestConstants.ApiBase}/{label.Id}");

        // The LabelsToSnapshots row should be gone.
        var count = await _db.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM LabelsToSnapshots WHERE LabelId = @LabelId",
            new { LabelId = label.Id.ToString() });

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task DeleteLabel_CannotDeleteAnotherUsersLabel()
    {
        var (client2, _) = await _factory.CreateAuthenticatedClientAsync();
        var label = RequestFactory.NewLabel("User2Label");
        await client2.PostAsJsonAsync(TestConstants.ApiBase, label);

        // User 1 tries to delete.
        var response = await _client.DeleteAsync($"{TestConstants.ApiBase}/{label.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        client2.Dispose();
    }
}

#endregion

#region 9. POST /api/label/by-name – GetLabelByName

public class LabelController_GetLabelByNameTests : IAsyncLifetime
{
    private readonly LabelApiFactory _factory = new();
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
    public async Task GetLabelByName_ExistingLabel_Returns200WithLabel()
    {
        var label = RequestFactory.NewLabel("FindMe");
        await _client.PostAsJsonAsync(TestConstants.ApiBase, label);

        var response = await _client.PostAsJsonAsync(
            $"{TestConstants.ApiBase}/by-name",
            new LabelNameRequest { Name = "FindMe" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await Json.ReadAsync<LabelDto>(response);
        Assert.NotNull(result);
        Assert.Equal("FindMe", result.Name);
        Assert.Equal(label.Id, result.Id);
    }

    [Fact]
    public async Task GetLabelByName_NonExistentLabel_Returns404()
    {
        var response = await _client.PostAsJsonAsync(
            $"{TestConstants.ApiBase}/by-name",
            new LabelNameRequest { Name = "DoesNotExist" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetLabelByName_ReturnsCorrectColorHex()
    {
        var label = RequestFactory.NewLabel("ColorLabel", "#123456");
        await _client.PostAsJsonAsync(TestConstants.ApiBase, label);

        var response = await _client.PostAsJsonAsync(
            $"{TestConstants.ApiBase}/by-name",
            new LabelNameRequest { Name = "ColorLabel" });

        var result = await Json.ReadAsync<LabelDto>(response);
        Assert.NotNull(result);
        Assert.Equal("#123456", result.ColorHex);
    }

    [Fact]
    public async Task GetLabelByName_WithSpecialCharacters_Returns200()
    {
        const string specialName = "Label/With\\Special:Characters?And&More";
        var label = RequestFactory.NewLabel(specialName);
        await _client.PostAsJsonAsync(TestConstants.ApiBase, label);

        var response = await _client.PostAsJsonAsync(
            $"{TestConstants.ApiBase}/by-name",
            new LabelNameRequest { Name = specialName });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await Json.ReadAsync<LabelDto>(response);
        Assert.NotNull(result);
        Assert.Equal(specialName, result.Name);
    }

    [Fact]
    public async Task GetLabelByName_DoesNotReturnAnotherUsersLabel()
    {
        var (client2, _) = await _factory.CreateAuthenticatedClientAsync();
        await client2.PostAsJsonAsync(TestConstants.ApiBase, RequestFactory.NewLabel("User2Label"));

        // User 1 tries to find user 2's label by name.
        var response = await _client.PostAsJsonAsync(
            $"{TestConstants.ApiBase}/by-name",
            new LabelNameRequest { Name = "User2Label" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        client2.Dispose();
    }

    [Fact]
    public async Task GetLabelByName_WithoutToken_Returns401()
    {
        var client = _factory.CreateUnauthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"{TestConstants.ApiBase}/by-name",
            new LabelNameRequest { Name = "AnyLabel" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetLabelByName_AfterLabelDeleted_Returns404()
    {
        var label = RequestFactory.NewLabel("DeletedLabel");
        await _client.PostAsJsonAsync(TestConstants.ApiBase, label);
        await _client.DeleteAsync($"{TestConstants.ApiBase}/{label.Id}");

        var response = await _client.PostAsJsonAsync(
            $"{TestConstants.ApiBase}/by-name",
            new LabelNameRequest { Name = "DeletedLabel" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetLabelByName_AfterLabelRenamed_OldNameReturns404()
    {
        var label = RequestFactory.NewLabel("OldName");
        await _client.PostAsJsonAsync(TestConstants.ApiBase, label);

        await _client.PutAsJsonAsync(TestConstants.ApiBase,
            new LabelRequest { Id = label.Id, Name = "NewName", ColorHex = label.ColorHex });

        var response = await _client.PostAsJsonAsync(
            $"{TestConstants.ApiBase}/by-name",
            new LabelNameRequest { Name = "OldName" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetLabelByName_AfterLabelRenamed_NewNameReturns200()
    {
        var label = RequestFactory.NewLabel("BeforeRename");
        await _client.PostAsJsonAsync(TestConstants.ApiBase, label);

        await _client.PutAsJsonAsync(TestConstants.ApiBase,
            new LabelRequest { Id = label.Id, Name = "AfterRename", ColorHex = label.ColorHex });

        var response = await _client.PostAsJsonAsync(
            $"{TestConstants.ApiBase}/by-name",
            new LabelNameRequest { Name = "AfterRename" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await Json.ReadAsync<LabelDto>(response);
        Assert.NotNull(result);
        Assert.Equal(label.Id, result.Id);
        Assert.Equal("AfterRename", result.Name);
    }
}

#endregion