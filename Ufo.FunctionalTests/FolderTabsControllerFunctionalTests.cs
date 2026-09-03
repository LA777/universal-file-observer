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
using Ufo.Abstractions.Database;
using Ufo.Abstractions.DataTransferObjects;
using Ufo.Abstractions.Options;
using Ufo.Abstractions.Requests;
using Ufo.Database.Contexts;
using Ufo.FunctionalTests.Extensions;
using Ufo.Server.Extensions;

namespace Ufo.FunctionalTests.FolderTabs;

#region Test WebApplication factory

/// <summary>
/// Boots the application against an in-memory database and a temporary folder
/// tree, so tabs can be locked to folders that genuinely exist.
/// </summary>
public class FolderTabsApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"test-foldertabs-{Guid.NewGuid():N}";
    private SqliteConnection? _sqLiteConnection;

    public string ConnectionString => $"Data Source={_dbName};Mode=Memory;Cache=Shared";

    public const string JwtKey = "super-secret-test-key-that-is-long-enough-256bits!!";
    public const string JwtIssuer = "ufo-test-issuer";
    public const string JwtAudience = "ufo-test-audience";

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
        _sqLiteConnection ??= new SqliteConnection(ConnectionString);

        if (_sqLiteConnection.State != System.Data.ConnectionState.Open)
        {
            await _sqLiteConnection.OpenAsync();
            await DapperDataContext.InitiateDatabaseAsync(_sqLiteConnection);
        }

        var userId = Ulid.NewUlid();
        var userName = $"tabs-user-{userId}";

        using var command = _sqLiteConnection.CreateCommand();
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
}

#endregion

#region Functional tests

public class FolderTabsControllerFunctionalTests : IAsyncLifetime
{
    private const string Endpoint = "/api/foldertabs";

    private string _testRoot = null!;
    private string _firstFolder = null!;
    private string _secondFolder = null!;

    public Task InitializeAsync()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"ufo-tabs-functional-{Guid.NewGuid():N}");
        _firstFolder = Path.Combine(_testRoot, "documents");
        _secondFolder = Path.Combine(_testRoot, "backup");

        Directory.CreateDirectory(_firstFolder);
        Directory.CreateDirectory(_secondFolder);

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static FolderTabsRequest RequestFor(string panelId, params string[] folderPaths) =>
        new() { PanelId = panelId, FolderPaths = folderPaths };

    [Fact]
    public async Task GetFolderTabs_WhenNoneAreLocked_ReturnsNothing()
    {
        using var factory = new FolderTabsApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(Endpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var folderTabs = await response.Content.ReadFromJsonAsync<List<FolderTabDto>>();
        Assert.NotNull(folderTabs);
        Assert.Empty(folderTabs!);
    }

    [Fact]
    public async Task PutFolderTabs_ThenGet_ReturnsThemInOrder()
    {
        using var factory = new FolderTabsApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var saveResponse = await client.PutAsJsonAsync(
            Endpoint,
            RequestFor("left", _secondFolder, _firstFolder));

        Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);

        var folderTabs = await (await client.GetAsync(Endpoint)).Content.ReadFromJsonAsync<List<FolderTabDto>>();

        Assert.Equal(2, folderTabs!.Count);
        Assert.Equal(_secondFolder, folderTabs[0].FolderPath);
        Assert.Equal(_firstFolder, folderTabs[1].FolderPath);
        Assert.All(folderTabs, folderTab => Assert.Equal("left", folderTab.PanelId));
    }

    [Fact]
    public async Task PutFolderTabs_ReplacesOnlyTheNamedPanel()
    {
        using var factory = new FolderTabsApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        await client.PutAsJsonAsync(Endpoint, RequestFor("left", _firstFolder));
        await client.PutAsJsonAsync(Endpoint, RequestFor("right", _secondFolder));

        // The panes save independently. Were the save account-wide, the second
        // call would have deleted the first pane's tab.
        var folderTabs = await (await client.GetAsync(Endpoint)).Content.ReadFromJsonAsync<List<FolderTabDto>>();

        Assert.Equal(2, folderTabs!.Count);
        Assert.Equal(_firstFolder, folderTabs.Single(tab => tab.PanelId == "left").FolderPath);
        Assert.Equal(_secondFolder, folderTabs.Single(tab => tab.PanelId == "right").FolderPath);
    }

    [Fact]
    public async Task PutFolderTabs_WithAnEmptyList_UnlocksThePanelsLastTab()
    {
        using var factory = new FolderTabsApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        await client.PutAsJsonAsync(Endpoint, RequestFor("left", _firstFolder));
        var response = await client.PutAsJsonAsync(Endpoint, RequestFor("left"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var folderTabs = await (await client.GetAsync(Endpoint)).Content.ReadFromJsonAsync<List<FolderTabDto>>();
        Assert.Empty(folderTabs!);
    }

    [Fact]
    public async Task PutFolderTabs_WithAFolderThatIsNotThere_IsRefused()
    {
        using var factory = new FolderTabsApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            Endpoint,
            RequestFor("left", Path.Combine(_testRoot, "never-existed")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutFolderTabs_WithAPanelThatDoesNotExist_IsRefused()
    {
        using var factory = new FolderTabsApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(Endpoint, RequestFor("middle", _firstFolder));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FolderTabs_AreKeptApartBetweenAccounts()
    {
        using var factory = new FolderTabsApiFactory();
        var (firstClient, _) = await factory.CreateAuthenticatedClientAsync();
        var (secondClient, _) = await factory.CreateAuthenticatedClientAsync();

        await firstClient.PutAsJsonAsync(Endpoint, RequestFor("left", _firstFolder));

        var secondUsersTabs = await (await secondClient.GetAsync(Endpoint))
            .Content.ReadFromJsonAsync<List<FolderTabDto>>();

        Assert.Empty(secondUsersTabs!);
    }

    [Fact]
    public async Task FolderTabs_RequireAuthentication()
    {
        using var factory = new FolderTabsApiFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Endpoint)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PutAsJsonAsync(Endpoint, RequestFor("left", _firstFolder))).StatusCode);
    }
}

#endregion
