using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ufo.Server.Extensions;
using Ufo.Server.Services;

namespace Ufo.FunctionalTests.VersionController;

#region Test WebApplication factory

/// <summary>
/// Boots the production host for <c>GET /api/version</c>. No database is set up:
/// the endpoint reads assembly metadata, and the bearer middleware validates the
/// token without a lookup, so a schema here would only hide that.
/// </summary>
public class VersionApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(HostEnvironmentExtensions.FunctionalTesting);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source=test-{Guid.NewGuid():N};Mode=Memory;Cache=Shared",
                ["JWT:Key"] = VersionTestConstants.JwtKey,
                ["JWT:Issuer"] = VersionTestConstants.JwtIssuer,
                ["JWT:Audience"] = VersionTestConstants.JwtAudience,
                ["Kestrel:Endpoints:App:Url"] = "http://localhost:0"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.AddLogging(lb => lb.SetMinimumLevel(LogLevel.Warning));

            // The host reads JWT options before ConfigureAppConfiguration runs, so
            // the bearer middleware would otherwise still hold the appsettings key.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(VersionTestConstants.JwtKey)),
                    ValidateIssuer = true,
                    ValidIssuer = VersionTestConstants.JwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = VersionTestConstants.JwtAudience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            });
        });
    }

    /// <summary>A client carrying a signed-in user's bearer token.</summary>
    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", VersionJwtTestHelper.GenerateToken(Ulid.NewUlid()));

        return client;
    }

    /// <summary>Returns an HTTP client with NO authorization header.</summary>
    public HttpClient CreateUnauthenticatedClient() => CreateClient();
}

#endregion

#region Constants & helpers

internal static class VersionTestConstants
{
    public const string JwtKey = "super-secret-test-key-that-is-long-enough-256bits!!";
    public const string JwtIssuer = "ufo-test-issuer";
    public const string JwtAudience = "ufo-test-audience";
    public const string ApiBase = "/api/version";
}

internal static class VersionJwtTestHelper
{
    public static string GenerateToken(Ulid userId, string userName = "testuser", int expiryMinutes = 60)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(VersionTestConstants.JwtKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, userName),
            new Claim("role", "user"),
        };

        var token = new JwtSecurityToken(
            issuer: VersionTestConstants.JwtIssuer,
            audience: VersionTestConstants.JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

internal static class VersionJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<T?> ReadAsync<T>(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<T>(Options);
}

#endregion

#region GET /api/version

public class VersionController_GetTests : IDisposable
{
    private static readonly Regex ThreeSegmentVersion = new(@"^\d+\.\d+\.\d+$");

    private readonly VersionApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task GetVersion_WithAToken_Returns200()
    {
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync(VersionTestConstants.ApiBase);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetVersion_WithAToken_AnswersThreeSegments()
    {
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync(VersionTestConstants.ApiBase);
        var body = await VersionJson.ReadAsync<VersionPayload>(response);

        Assert.NotNull(body);
        Assert.Matches(ThreeSegmentVersion, body.Version);
    }

    [Fact]
    public async Task GetVersion_AnswersWhatTheBuildStamped()
    {
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync(VersionTestConstants.ApiBase);
        var body = await VersionJson.ReadAsync<VersionPayload>(response);

        Assert.Equal(ApplicationVersionService.Current, body!.Version);
    }

    [Fact]
    public async Task GetVersion_UsesACamelCasePropertyName()
    {
        // The Angular client reads response.version; the host's camelCase policy
        // is what makes that true.
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync(VersionTestConstants.ApiBase);
        var jsonString = await response.Content.ReadAsStringAsync();

        Assert.Contains("\"version\"", jsonString);
    }

    [Fact]
    public async Task GetVersion_WithoutAToken_Returns401()
    {
        // Deliberately authenticated: the exact build of a self-hosted server is
        // not something to hand to an unauthenticated caller.
        using var client = _factory.CreateUnauthenticatedClient();

        var response = await client.GetAsync(VersionTestConstants.ApiBase);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

#endregion

// ---------------------------------------------------------------------------
// Response DTO
// ---------------------------------------------------------------------------

internal record VersionPayload(string Version);
