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
using Ufo.Abstractions.Database;
using Ufo.Abstractions.Options;
using Ufo.Abstractions.Requests;
using Ufo.Database.Contexts;
using Ufo.FunctionalTests.Extensions;
using Ufo.Server.Extensions;

namespace Ufo.FunctionalTests.AuthController;

#region Test WebApplication factory

/// <summary>
/// Boots a real in-process ASP.NET Core host using the production Program entry
/// point, then swaps out the SQLite connection for a shared in-memory database
/// so every test is fully isolated yet exercises the complete stack:
///   HTTP pipeline → middleware → controller → repository → SQLite.
/// </summary>
public class AuthApiFactory : WebApplicationFactory<Program>
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
                    ["JWT:Key"] = AuthTestConstants.JwtKey,
                    ["JWT:Issuer"] = AuthTestConstants.JwtIssuer,
                    ["JWT:Audience"] = AuthTestConstants.JwtAudience,
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
                        Encoding.UTF8.GetBytes(AuthTestConstants.JwtKey)),
                    ValidateIssuer = true,
                    ValidIssuer = AuthTestConstants.JwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = AuthTestConstants.JwtAudience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            });
        });
    }

    /// <summary>
    /// Creates the schema, returning a ready-to-use HTTP client.
    /// </summary>
    public async Task<HttpClient> CreateClientAsync()
    {
        // Open (and keep open) the anchor connection so the in-memory database
        // survives across the multiple connections the app will open internally.
        _sqLiteConnection = new SqliteConnection(ConnectionString);
        await _sqLiteConnection.OpenAsync();
        await DapperDataContext.InitiateDatabaseAsync(_sqLiteConnection);

        return CreateClient();
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

#region Constants & helpers

internal static class AuthTestConstants
{
    public const string ApiBase = "/api/auth";
    public const string ValidPassword = "ValidPassword123!";
    public const string WeakPassword = "weak";
    public const string ValidUsername = "testuser";
    public const string JwtKey = "super-secret-test-key-that-is-long-enough-256bits!!";
    public const string JwtIssuer = "ufo-test-issuer";
    public const string JwtAudience = "ufo-test-audience";
}

internal static class AuthRequestFactory
{
    public static RegisterRequest NewRegister(
        string username = AuthTestConstants.ValidUsername,
        string password = AuthTestConstants.ValidPassword) =>
        new()
        {
            Username = username,
            Password = password
        };

    public static LoginRequest NewLogin(
        string username = AuthTestConstants.ValidUsername,
        string password = AuthTestConstants.ValidPassword) =>
        new()
        {
            Username = username,
            Password = password
        };
}

#endregion

/// <summary>
/// The refresh cookie, as seen from a test.
///
/// The cookie is written Secure, and the test host speaks http, so the handler's
/// own cookie container will not store or resend it - a browser would, over TLS.
/// These read it off the response and put it back on the next request by hand,
/// which also makes each test say out loud which token it is presenting.
/// </summary>
internal static class RefreshCookie
{
    public const string Name = "ufo_refresh_token";

    /// <summary>
    /// The cookie's value, empty when the server cleared it, or null when the
    /// response did not set it at all.
    /// </summary>
    public static string? Read(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders))
        {
            return null;
        }

        var cookieHeader = setCookieHeaders.LastOrDefault(header => header.StartsWith($"{Name}=", StringComparison.Ordinal));
        if (cookieHeader == null)
        {
            return null;
        }

        var value = cookieHeader[(Name.Length + 1)..];
        var end = value.IndexOf(';');

        return end < 0 ? value : value[..end];
    }

    public static string? ReadAttributes(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders)
            ? setCookieHeaders.LastOrDefault(header => header.StartsWith($"{Name}=", StringComparison.Ordinal))
            : null;

    public static Task<HttpResponseMessage> PostWithAsync(HttpClient client, string requestUri, string? refreshToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, requestUri);

        if (refreshToken != null)
        {
            request.Headers.Add("Cookie", $"{Name}={refreshToken}");
        }

        return client.SendAsync(request);
    }
}

#region JSON deserialization helpers

internal static class AuthJson
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

#endregion

#region 1. POST /api/auth/signup – SignupAsync

public class AuthController_SignupTests : IAsyncLifetime
{
    private readonly AuthApiFactory _factory = new();
    private HttpClient _client = null!;

    public async Task InitializeAsync() =>
        _client = await _factory.CreateClientAsync();

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Signup_ValidCredentials_Returns200WithSuccessMessage()
    {
        var request = AuthRequestFactory.NewRegister("newuser", AuthTestConstants.ValidPassword);
        var response = await _client.PostAsJsonAsync($"{AuthTestConstants.ApiBase}/signup", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("registered successfully", content);
    }

    [Fact]
    public async Task Signup_DuplicateUsername_Returns400()
    {
        var request = AuthRequestFactory.NewRegister("duplicate", AuthTestConstants.ValidPassword);

        // First signup should succeed
        var firstResponse = await _client.PostAsJsonAsync($"{AuthTestConstants.ApiBase}/signup", request);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        // Second signup with same username should fail
        var secondResponse = await _client.PostAsJsonAsync($"{AuthTestConstants.ApiBase}/signup", request);
        Assert.Equal(HttpStatusCode.BadRequest, secondResponse.StatusCode);

        var content = await secondResponse.Content.ReadAsStringAsync();
        Assert.Contains("already taken", content);
    }

    [Fact]
    public async Task Signup_FirstAccount_BecomesTheAdministrator()
    {
        var request = AuthRequestFactory.NewRegister("founder", AuthTestConstants.ValidPassword);

        var response = await _client.PostAsJsonAsync($"{AuthTestConstants.ApiBase}/signup", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The account that stands the installation up is the one that can change
        // the server certificate; nothing else in the product grants it.
        Assert.True(await IsAdministratorAsync("founder"));
    }

    [Fact]
    public async Task Signup_LaterAccounts_AreNotAdministrators()
    {
        await _client.PostAsJsonAsync(
            $"{AuthTestConstants.ApiBase}/signup",
            AuthRequestFactory.NewRegister("founder", AuthTestConstants.ValidPassword));

        await _client.PostAsJsonAsync(
            $"{AuthTestConstants.ApiBase}/signup",
            AuthRequestFactory.NewRegister("latecomer", AuthTestConstants.ValidPassword));

        Assert.True(await IsAdministratorAsync("founder"));
        // Otherwise anyone who can register could replace the TLS identity every
        // other user is served.
        Assert.False(await IsAdministratorAsync("latecomer"));
    }

    [Fact]
    public async Task Signup_AfterAFailedAttempt_StillGrantsAdministratorToTheFirstRealAccount()
    {
        // A rejected signup must not consume the administrator slot.
        var rejected = await _client.PostAsJsonAsync(
            $"{AuthTestConstants.ApiBase}/signup",
            AuthRequestFactory.NewRegister("weakling", AuthTestConstants.WeakPassword));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        await _client.PostAsJsonAsync(
            $"{AuthTestConstants.ApiBase}/signup",
            AuthRequestFactory.NewRegister("founder", AuthTestConstants.ValidPassword));

        Assert.True(await IsAdministratorAsync("founder"));
    }

    /// <summary>
    /// Read straight from the database: the flag is deliberately not exposed on
    /// any response, so there is no endpoint to assert against.
    /// </summary>
    private async Task<bool> IsAdministratorAsync(string userName)
    {
        await using var connection = new SqliteConnection(_factory.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT IsAdmin FROM Users WHERE Name = $name;";
        command.Parameters.AddWithValue("$name", userName);

        var isAdmin = await command.ExecuteScalarAsync();

        return Convert.ToInt64(isAdmin) == 1;
    }

    [Fact]
    public async Task Signup_WeakPassword_Returns400()
    {
        var request = AuthRequestFactory.NewRegister("weakpassworduser", AuthTestConstants.WeakPassword);
        var response = await _client.PostAsJsonAsync($"{AuthTestConstants.ApiBase}/signup", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Signup_EmptyUsername_Returns400()
    {
        var request = AuthRequestFactory.NewRegister("", AuthTestConstants.ValidPassword);
        var response = await _client.PostAsJsonAsync($"{AuthTestConstants.ApiBase}/signup", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Signup_EmptyPassword_Returns400()
    {
        var request = AuthRequestFactory.NewRegister("emptypassworduser", "");
        var response = await _client.PostAsJsonAsync($"{AuthTestConstants.ApiBase}/signup", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Signup_NullRequest_Returns400()
    {
        var response = await _client.PostAsJsonAsync($"{AuthTestConstants.ApiBase}/signup", (object?)null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Signup_CreatesUserInDatabase()
    {
        var request = AuthRequestFactory.NewRegister("databaseuser", AuthTestConstants.ValidPassword);
        var response = await _client.PostAsJsonAsync($"{AuthTestConstants.ApiBase}/signup", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // User should now be able to login
        var loginRequest = AuthRequestFactory.NewLogin("databaseuser", AuthTestConstants.ValidPassword);
        var loginResponse = await _client.PostAsJsonAsync($"{AuthTestConstants.ApiBase}/login", loginRequest);

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    }

    [Fact]
    public async Task Signup_MultipleValidUsers_AllSucceed()
    {
        var user1 = AuthRequestFactory.NewRegister("user1", AuthTestConstants.ValidPassword);
        var user2 = AuthRequestFactory.NewRegister("user2", AuthTestConstants.ValidPassword);
        var user3 = AuthRequestFactory.NewRegister("user3", AuthTestConstants.ValidPassword);

        var response1 = await _client.PostAsJsonAsync($"{AuthTestConstants.ApiBase}/signup", user1);
        var response2 = await _client.PostAsJsonAsync($"{AuthTestConstants.ApiBase}/signup", user2);
        var response3 = await _client.PostAsJsonAsync($"{AuthTestConstants.ApiBase}/signup", user3);

        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response3.StatusCode);
    }
}

#endregion

#region 2. POST /api/auth/login – LoginAsync

public class AuthController_LoginTests : IAsyncLifetime
{
    private readonly AuthApiFactory _factory = new();
    private HttpClient _client = null!;

    public async Task InitializeAsync() =>
        _client = await _factory.CreateClientAsync();

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task SignupUserAsync(string username = "loginuser", string password = AuthTestConstants.ValidPassword)
    {
        var request = AuthRequestFactory.NewRegister(username, password);
        await _client.PostAsJsonAsync($"{AuthTestConstants.ApiBase}/signup", request);
    }

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithToken()
    {
        await SignupUserAsync("validloginuser", AuthTestConstants.ValidPassword);

        var request = AuthRequestFactory.NewLogin("validloginuser", AuthTestConstants.ValidPassword);
        var response = await _client.PostAsJsonAsync($"{AuthTestConstants.ApiBase}/login", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var jsonDoc = JsonDocument.Parse(content);

        // Try with capital T first (as per AuthController)
        if (!jsonDoc.RootElement.TryGetProperty("Token", out var tokenElement))
        {
            // Try lowercase token
            jsonDoc.RootElement.TryGetProperty("token", out tokenElement);
        }
        var token = tokenElement.GetString();
        Assert.NotEmpty(token!);

        if (!jsonDoc.RootElement.TryGetProperty("Username", out var usernameElement))
        {
            jsonDoc.RootElement.TryGetProperty("username", out usernameElement);
        }
        Assert.Equal("validloginuser", usernameElement.GetString());
    }

    [Fact]
    public async Task Login_InvalidPassword_Returns401()
    {
        await SignupUserAsync("wrongpassworduser", AuthTestConstants.ValidPassword);

        var request = AuthRequestFactory.NewLogin("wrongpassworduser", "WrongPassword123!");
        var response = await _client.PostAsJsonAsync($"{AuthTestConstants.ApiBase}/login", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid username or password", content);
    }

    [Fact]
    public async Task Login_NonexistentUser_Returns401()
    {
        var request = AuthRequestFactory.NewLogin("nonexistent", AuthTestConstants.ValidPassword);
        var response = await _client.PostAsJsonAsync($"{AuthTestConstants.ApiBase}/login", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid username or password", content);
    }

    [Fact]
    public async Task Login_EmptyUsername_Returns400()
    {
        var request = AuthRequestFactory.NewLogin("", AuthTestConstants.ValidPassword);
        var response = await _client.PostAsJsonAsync($"{AuthTestConstants.ApiBase}/login", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_EmptyPassword_Returns400()
    {
        var request = AuthRequestFactory.NewLogin(AuthTestConstants.ValidUsername, "");
        var response = await _client.PostAsJsonAsync($"{AuthTestConstants.ApiBase}/login", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_NullRequest_Returns400()
    {
        var response = await _client.PostAsJsonAsync($"{AuthTestConstants.ApiBase}/login", (object?)null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_TokenIsValid()
    {
        await SignupUserAsync("tokenuser", AuthTestConstants.ValidPassword);

        var request = AuthRequestFactory.NewLogin("tokenuser", AuthTestConstants.ValidPassword);
        var response = await _client.PostAsJsonAsync($"{AuthTestConstants.ApiBase}/login", request);

        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);

        if (!jsonDoc.RootElement.TryGetProperty("Token", out var tokenElement))
        {
            jsonDoc.RootElement.TryGetProperty("token", out tokenElement);
        }
        var token = tokenElement.GetString();
        Assert.NotNull(token);

        // Verify token structure
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        Assert.NotNull(jwtToken);
        Assert.Equal(AuthTestConstants.JwtIssuer, jwtToken.Issuer);
        var audClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "aud");
        Assert.Equal(AuthTestConstants.JwtAudience, audClaim?.Value);
        // Check for nameid (short form of NameIdentifier)
        Assert.Contains(jwtToken.Claims, c => c.Type == "nameid" || c.Type == ClaimTypes.NameIdentifier);
        // Check for unique_name (short form of Name)
        Assert.Contains(jwtToken.Claims, c => (c.Type == "unique_name" || c.Type == ClaimTypes.Name) && c.Value == "tokenuser");
    }

    [Fact]
    public async Task Login_ReturnsTokenInHeader()
    {
        await SignupUserAsync("headeruser", AuthTestConstants.ValidPassword);

        var request = AuthRequestFactory.NewLogin("headeruser", AuthTestConstants.ValidPassword);
        var response = await _client.PostAsJsonAsync($"{AuthTestConstants.ApiBase}/login", request);

        Assert.True(response.Headers.Contains("X-Auth-Token"));
        var headerToken = response.Headers.GetValues("X-Auth-Token").FirstOrDefault();
        Assert.NotEmpty(headerToken);
    }

    [Fact]
    public async Task Login_TokenCanBeUsedForAuthenticatedRequests()
    {
        await SignupUserAsync("authuser", AuthTestConstants.ValidPassword);

        // Login and get token
        var loginRequest = AuthRequestFactory.NewLogin("authuser", AuthTestConstants.ValidPassword);
        var loginResponse = await _client.PostAsJsonAsync($"{AuthTestConstants.ApiBase}/login", loginRequest);

        var content = await loginResponse.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);
        if (!jsonDoc.RootElement.TryGetProperty("Token", out var tokenElement))
        {
            jsonDoc.RootElement.TryGetProperty("token", out tokenElement);
        }
        var token = tokenElement.GetString();

        // Use token to make an authenticated request (to label endpoint as it's protected)
        var labelClient = _factory.CreateClient();
        labelClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var labelResponse = await labelClient.GetAsync("/api/label");

        // Should not return 401 (may return 404 if no labels, but not 401)
        Assert.NotEqual(HttpStatusCode.Unauthorized, labelResponse.StatusCode);

        labelClient.Dispose();
    }

    [Fact]
    public async Task Login_CaseSensitiveUsername()
    {
        await SignupUserAsync("CaseSensitiveUser", AuthTestConstants.ValidPassword);

        // Try login with different case
        var request = AuthRequestFactory.NewLogin("casesensitiveuser", AuthTestConstants.ValidPassword);
        var response = await _client.PostAsJsonAsync($"{AuthTestConstants.ApiBase}/login", request);

        // Should fail due to case sensitivity
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_PasswordIsCaseSensitive()
    {
        await SignupUserAsync("casepwduser", "ValidPassword123!");

        var request = AuthRequestFactory.NewLogin("casepwduser", "validpassword123!");
        var response = await _client.PostAsJsonAsync($"{AuthTestConstants.ApiBase}/login", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

#endregion

#region 4. POST /api/auth/refresh and /api/auth/logout - the session's stored half

public class AuthController_RefreshTokenTests : IAsyncLifetime
{
    private const string Username = "refreshuser";

    private readonly AuthApiFactory _factory = new();
    private HttpClient _client = null!;

    public async Task InitializeAsync() =>
        _client = await _factory.CreateClientAsync();

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Signs a user up and in, returning the refresh cookie's value.</summary>
    private async Task<string> SignInAsync()
    {
        await _client.PostAsJsonAsync(
            $"{AuthTestConstants.ApiBase}/signup", AuthRequestFactory.NewRegister(Username));

        var response = await _client.PostAsJsonAsync(
            $"{AuthTestConstants.ApiBase}/login", AuthRequestFactory.NewLogin(Username));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var refreshToken = RefreshCookie.Read(response);
        Assert.False(string.IsNullOrEmpty(refreshToken));

        return refreshToken!;
    }

    private static async Task<string> ReadAccessTokenAsync(HttpResponseMessage response)
    {
        var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        if (!payload.RootElement.TryGetProperty("Token", out var token))
        {
            payload.RootElement.TryGetProperty("token", out token);
        }

        return token.GetString() ?? string.Empty;
    }

    [Fact]
    public async Task Login_SetsARefreshCookieThatScriptsCannotRead()
    {
        await _client.PostAsJsonAsync(
            $"{AuthTestConstants.ApiBase}/signup", AuthRequestFactory.NewRegister(Username));

        var response = await _client.PostAsJsonAsync(
            $"{AuthTestConstants.ApiBase}/login", AuthRequestFactory.NewLogin(Username));

        // The flags are the whole reason the refresh token lives in a cookie
        // rather than in the response body beside the access token.
        var cookie = RefreshCookie.ReadAttributes(response);
        Assert.NotNull(cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refresh_WithTheCookieFromLogin_ReturnsAFreshAccessTokenAndANewCookie()
    {
        var refreshToken = await SignInAsync();

        var response = await RefreshCookie.PostWithAsync(_client, $"{AuthTestConstants.ApiBase}/refresh", refreshToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var accessToken = await ReadAccessTokenAsync(response);
        Assert.False(string.IsNullOrEmpty(accessToken));

        // The access token is a real one for the same user.
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        Assert.Equal(Username, jwt.Claims.First(claim => claim.Type == "unique_name").Value);

        // Rotation: the cookie that comes back is not the one that went in.
        var rotatedToken = RefreshCookie.Read(response);
        Assert.False(string.IsNullOrEmpty(rotatedToken));
        Assert.NotEqual(refreshToken, rotatedToken);
    }

    [Fact]
    public async Task Refresh_WithNoCookie_Returns401()
    {
        var response = await RefreshCookie.PostWithAsync(_client, $"{AuthTestConstants.ApiBase}/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithATokenThatWasAlreadyRotated_IsRefusedWithoutTouchingTheLiveCookie()
    {
        var refreshToken = await SignInAsync();

        var firstRefresh = await RefreshCookie.PostWithAsync(_client, $"{AuthTestConstants.ApiBase}/refresh", refreshToken);
        Assert.Equal(HttpStatusCode.OK, firstRefresh.StatusCode);

        // Presenting the spent token again. Within the grace period this is read
        // as a retry rather than a theft, and it is refused either way - one
        // rotation, one successor.
        var secondRefresh = await RefreshCookie.PostWithAsync(_client, $"{AuthTestConstants.ApiBase}/refresh", refreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, secondRefresh.StatusCode);

        // But the cookie is left alone: the successor from the first refresh is
        // live and sitting in the browser, and a deletion here would take it with
        // it - two tabs refreshing together would end a session with nothing wrong
        // with it.
        Assert.Null(RefreshCookie.Read(secondRefresh));

        var successor = RefreshCookie.Read(firstRefresh);
        var afterTheRefusal = await RefreshCookie.PostWithAsync(_client, $"{AuthTestConstants.ApiBase}/refresh", successor);
        Assert.Equal(HttpStatusCode.OK, afterTheRefusal.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithAnUnknownToken_IsRefusedAndClearsTheCookie()
    {
        // A token this server never issued cannot become usable, so the cookie
        // carrying it is worth deleting - unlike the raced case above.
        var response = await RefreshCookie.PostWithAsync(
            _client, $"{AuthTestConstants.ApiBase}/refresh", "not-a-token-this-server-issued");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(string.Empty, RefreshCookie.Read(response));
    }

    [Fact]
    public async Task Refresh_WithTheSuccessorCookie_KeepsTheSessionGoing()
    {
        var refreshToken = await SignInAsync();

        var firstRefresh = await RefreshCookie.PostWithAsync(_client, $"{AuthTestConstants.ApiBase}/refresh", refreshToken);
        var successor = RefreshCookie.Read(firstRefresh);

        var secondRefresh = await RefreshCookie.PostWithAsync(_client, $"{AuthTestConstants.ApiBase}/refresh", successor);

        Assert.Equal(HttpStatusCode.OK, secondRefresh.StatusCode);
        Assert.NotEqual(successor, RefreshCookie.Read(secondRefresh));
    }

    [Fact]
    public async Task Logout_RevokesTheSessionSoItCannotBeRefreshedAgain()
    {
        var refreshToken = await SignInAsync();

        var logout = await RefreshCookie.PostWithAsync(_client, $"{AuthTestConstants.ApiBase}/logout", refreshToken);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(string.Empty, RefreshCookie.Read(logout));

        // The revocation is server-side: keeping a copy of the cookie buys nothing.
        var refresh = await RefreshCookie.PostWithAsync(_client, $"{AuthTestConstants.ApiBase}/refresh", refreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Logout_EndsTheSessionEvenWhenARefreshRotatedTheTokenFirst()
    {
        // A refresh that overlapped the sign-out has already rotated the cookie
        // the sign-out is carrying. Revoking only what was presented would leave
        // the successor live and the session resumable.
        var refreshToken = await SignInAsync();

        var refresh = await RefreshCookie.PostWithAsync(_client, $"{AuthTestConstants.ApiBase}/refresh", refreshToken);
        var successor = RefreshCookie.Read(refresh);

        // Signing out with the token the client had before that refresh landed.
        var logout = await RefreshCookie.PostWithAsync(_client, $"{AuthTestConstants.ApiBase}/logout", refreshToken);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var resumeAttempt = await RefreshCookie.PostWithAsync(_client, $"{AuthTestConstants.ApiBase}/refresh", successor);

        Assert.Equal(HttpStatusCode.Unauthorized, resumeAttempt.StatusCode);
    }

    [Fact]
    public async Task Logout_WithNoCookie_Succeeds()
    {
        // Signing out is not a place to tell a caller which tokens exist, and a
        // client whose access token has already expired still has to be able to.
        var response = await RefreshCookie.PostWithAsync(_client, $"{AuthTestConstants.ApiBase}/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}

#endregion
