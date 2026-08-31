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
using System.Net;
using System.Text;
using System.Text.Json;
using Ufo.Abstractions.Database;
using Ufo.Abstractions.Options;
using Ufo.Database;
using Ufo.Database.Contexts;
using Ufo.FunctionalTests.Extensions;
using Ufo.Server.Extensions;

namespace Ufo.FunctionalTests.UserController;

#region Test WebApplication factory

public class UserApiFactory : WebApplicationFactory<Program>
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
                ["JWT:Key"] = UserTestConstants.JwtKey,
                ["JWT:Issuer"] = UserTestConstants.JwtIssuer,
                ["JWT:Audience"] = UserTestConstants.JwtAudience,
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
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(UserTestConstants.JwtKey)),
                    ValidateIssuer = true,
                    ValidIssuer = UserTestConstants.JwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = UserTestConstants.JwtAudience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            });
        });
    }

    public async Task InitializeDatabaseAsync()
    {
        _sqLiteConnection = new SqliteConnection(ConnectionString);
        await _sqLiteConnection.OpenAsync();
        await DapperDataContext.InitiateDatabaseAsync(_sqLiteConnection);
    }

    public async Task SeedUserAsync(string? name = null)
    {
        var userId = Ulid.NewUlid();
        var userName = name ?? $"testuser-{userId}";
        await _sqLiteConnection!.ExecuteAsync(
            SqlScripts.InsertUserSql,
            new { Id = userId.ToString(), Name = userName, PasswordHash = "hash", IsAdmin = false });
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

#region Constants

internal static class UserTestConstants
{
    public const string JwtKey = "super-secret-test-key-that-is-long-enough-256bits!!";
    public const string JwtIssuer = "ufo-test-issuer";
    public const string JwtAudience = "ufo-test-audience";
    public const string ApiBase = "/api/user";
}

#endregion

#region Helpers

internal static class UserJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static T? Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options);

    public static async Task<T?> ReadAsync<T>(HttpResponseMessage response) =>
        Deserialize<T>(await response.Content.ReadAsStringAsync());
}

internal sealed class UserOptionsMonitorStub<T> : IOptionsMonitor<T> where T : class
{
    public UserOptionsMonitorStub(T value) => CurrentValue = value;
    public T CurrentValue { get; }
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

#endregion

#region 1. GET /api/user/is-created

public class UserController_IsCreatedTests : IAsyncLifetime
{
    private readonly UserApiFactory _factory = new();
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _factory.InitializeDatabaseAsync();
        _client = _factory.CreateClient();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task IsCreated_NoUsersInDatabase_Returns200WithIsFalse()
    {
        var response = await _client.GetAsync($"{UserTestConstants.ApiBase}/is-created");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await UserJson.ReadAsync<IsCreatedResponse>(response);
        Assert.NotNull(body);
        Assert.False(body.IsCreated);
    }

    [Fact]
    public async Task IsCreated_WithOneUser_Returns200WithIsTrue()
    {
        await _factory.SeedUserAsync();

        var response = await _client.GetAsync($"{UserTestConstants.ApiBase}/is-created");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await UserJson.ReadAsync<IsCreatedResponse>(response);
        Assert.NotNull(body);
        Assert.True(body.IsCreated);
    }

    [Fact]
    public async Task IsCreated_WithMultipleUsers_Returns200WithIsTrue()
    {
        await _factory.SeedUserAsync();
        await _factory.SeedUserAsync();
        await _factory.SeedUserAsync();

        var response = await _client.GetAsync($"{UserTestConstants.ApiBase}/is-created");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await UserJson.ReadAsync<IsCreatedResponse>(response);
        Assert.NotNull(body);
        Assert.True(body.IsCreated);
    }

    [Fact]
    public async Task IsCreated_IsAnonymous_DoesNotRequireToken()
    {
        // Endpoint is [AllowAnonymous] — must never return 401 or 403.
        var response = await _client.GetAsync($"{UserTestConstants.ApiBase}/is-created");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task IsCreated_ResponseBodyContainsIsCreatedProperty()
    {
        var response = await _client.GetAsync($"{UserTestConstants.ApiBase}/is-created");
        var jsonString = await response.Content.ReadAsStringAsync();

        // Verify the JSON shape contains the expected property.
        Assert.Contains("isCreated", jsonString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IsCreated_AfterUserSeeded_ThenDatabaseCleared_ReturnsFalse()
    {
        // Seed a user so IsCreated returns true.
        await _factory.SeedUserAsync();

        var firstResponse = await _client.GetAsync($"{UserTestConstants.ApiBase}/is-created");
        var firstBody = await UserJson.ReadAsync<IsCreatedResponse>(firstResponse);
        Assert.True(firstBody!.IsCreated);

        // Create a fresh factory with an empty database.
        await using var freshFactory = new UserApiFactory();
        await freshFactory.InitializeDatabaseAsync();
        using var freshClient = freshFactory.CreateClient();

        var secondResponse = await freshClient.GetAsync($"{UserTestConstants.ApiBase}/is-created");
        var secondBody = await UserJson.ReadAsync<IsCreatedResponse>(secondResponse);
        Assert.False(secondBody!.IsCreated);
    }
}

#endregion

// ---------------------------------------------------------------------------
// Response DTO
// ---------------------------------------------------------------------------

internal record IsCreatedResponse(bool IsCreated);