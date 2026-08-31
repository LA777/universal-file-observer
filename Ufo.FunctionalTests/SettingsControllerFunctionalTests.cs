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

namespace Ufo.FunctionalTests.SettingsController;

#region Test WebApplication factory

/// <summary>
/// Boots the production host against a per-factory in-memory SQLite database, so
/// each test exercises the whole stack:
///   HTTP pipeline → JWT middleware → SettingsController → repository → SQLite.
/// </summary>
public class SettingsApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"test-{Guid.NewGuid():N}";

    // One connection is held open for the factory's lifetime; the in-memory
    // database is dropped the moment the last connection to it closes.
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
                ["JWT:Key"] = TestConstants.JwtKey,
                ["JWT:Issuer"] = TestConstants.JwtIssuer,
                ["JWT:Audience"] = TestConstants.JwtAudience,
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

            // The host reads JWT options before ConfigureAppConfiguration runs, so
            // the bearer middleware would otherwise still hold the appsettings key.
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
    /// Creates the schema, seeds a user, and returns a client carrying that
    /// user's bearer token.
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
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", JwtTestHelper.GenerateToken(userId, userName));

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
    public const string ApiBase = "/api/settings";
}

internal static class JwtTestHelper
{
    public static string GenerateToken(Ulid userId, string userName = "testuser", int expiryMinutes = 60)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestConstants.JwtKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
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
}

internal static class Json
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new UlidJsonConverter() }
    };

    public static async Task<T?> ReadAsync<T>(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<T>(Options);
}

#endregion

public class SettingsControllerFunctionalTests
{
    #region GET /api/settings

    [Fact]
    public async Task GetSettings_WhenNothingSaved_ReturnsTheDefaultTheme()
    {
        using var factory = new SettingsApiFactory();
        var (client, userId) = await factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(TestConstants.ApiBase);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = await Json.ReadAsync<UserSettingsDto>(response);
        Assert.NotNull(settings);
        Assert.Equal(UiThemes.Default, settings!.Theme);
        Assert.Equal(userId, settings.UserId);
    }

    [Fact]
    public async Task GetSettings_AfterSave_ReturnsTheSavedTheme()
    {
        using var factory = new SettingsApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var saveResponse = await client.PutAsJsonAsync(
            TestConstants.ApiBase,
            new UserSettingsRequest { Theme = UiThemes.Light });
        Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);

        var response = await client.GetAsync(TestConstants.ApiBase);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = await Json.ReadAsync<UserSettingsDto>(response);
        Assert.Equal(UiThemes.Light, settings!.Theme);
    }

    [Fact]
    public async Task GetSettings_WithoutAToken_IsUnauthorized()
    {
        using var factory = new SettingsApiFactory();
        var client = factory.CreateUnauthenticatedClient();

        var response = await client.GetAsync(TestConstants.ApiBase);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region PUT /api/settings

    [Fact]
    public async Task SaveSettings_WithASupportedTheme_Succeeds()
    {
        using var factory = new SettingsApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            TestConstants.ApiBase,
            new UserSettingsRequest { Theme = UiThemes.Light });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var serverResult = await Json.ReadAsync<ServerResult>(response);
        Assert.Equal(Result.Success, serverResult!.Result);
    }

    [Fact]
    public async Task SaveSettings_IsIdempotentAcrossRepeatedSaves()
    {
        using var factory = new SettingsApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        await client.PutAsJsonAsync(TestConstants.ApiBase, new UserSettingsRequest { Theme = UiThemes.Light });
        await client.PutAsJsonAsync(TestConstants.ApiBase, new UserSettingsRequest { Theme = UiThemes.Dark });
        var response = await client.PutAsJsonAsync(TestConstants.ApiBase, new UserSettingsRequest { Theme = UiThemes.Light });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var settings = await Json.ReadAsync<UserSettingsDto>(await client.GetAsync(TestConstants.ApiBase));
        Assert.Equal(UiThemes.Light, settings!.Theme);
    }

    [Fact]
    public async Task SaveSettings_WithAnUnknownTheme_IsRejected()
    {
        using var factory = new SettingsApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            TestConstants.ApiBase,
            new UserSettingsRequest { Theme = "solarized" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // The rejected write must not have changed anything.
        var settings = await Json.ReadAsync<UserSettingsDto>(await client.GetAsync(TestConstants.ApiBase));
        Assert.Equal(UiThemes.Default, settings!.Theme);
    }

    [Fact]
    public async Task SaveSettings_WithNoThemeInTheBody_IsRejectedAndChangesNothing()
    {
        using var factory = new SettingsApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        await client.PutAsJsonAsync(TestConstants.ApiBase, new UserSettingsRequest { Theme = UiThemes.Light });

        // A body with no theme must not read as "the default theme".
        var response = await client.PutAsJsonAsync(TestConstants.ApiBase, new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var settings = await Json.ReadAsync<UserSettingsDto>(await client.GetAsync(TestConstants.ApiBase));
        Assert.Equal(UiThemes.Light, settings!.Theme);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("DARK")]
    public async Task SaveSettings_WithADifferentlyCasedTheme_IsRejected(string theme)
    {
        using var factory = new SettingsApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        await client.PutAsJsonAsync(TestConstants.ApiBase, new UserSettingsRequest { Theme = UiThemes.Light });

        // The stored value is handed straight back to the client as a CSS class,
        // so a near miss has to be refused rather than normalised.
        var response = await client.PutAsJsonAsync(
            TestConstants.ApiBase, new UserSettingsRequest { Theme = theme });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var settings = await Json.ReadAsync<UserSettingsDto>(await client.GetAsync(TestConstants.ApiBase));
        Assert.Equal(UiThemes.Light, settings!.Theme);
    }

    [Fact]
    public async Task SaveSettings_WithAnOverlongTheme_IsRejectedByValidation()
    {
        using var factory = new SettingsApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            TestConstants.ApiBase,
            new UserSettingsRequest { Theme = new string('x', 64) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SaveSettings_WithoutAToken_IsUnauthorized()
    {
        using var factory = new SettingsApiFactory();
        var client = factory.CreateUnauthenticatedClient();

        var response = await client.PutAsJsonAsync(
            TestConstants.ApiBase,
            new UserSettingsRequest { Theme = UiThemes.Light });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SaveSettings_DoesNotLeakBetweenUsers()
    {
        using var factory = new SettingsApiFactory();
        var (firstClient, _) = await factory.CreateAuthenticatedClientAsync();
        var (secondClient, _) = await factory.CreateAuthenticatedClientAsync();

        await firstClient.PutAsJsonAsync(TestConstants.ApiBase, new UserSettingsRequest { Theme = UiThemes.Light });

        var secondUserSettings = await Json.ReadAsync<UserSettingsDto>(
            await secondClient.GetAsync(TestConstants.ApiBase));

        Assert.Equal(UiThemes.Default, secondUserSettings!.Theme);
    }

    #endregion
}
