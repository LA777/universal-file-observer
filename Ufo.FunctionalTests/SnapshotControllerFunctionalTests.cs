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
using System.Net.Http.Json;
using System.Net;
using System.Security.Claims;
using System.Text;
using Ufo.Abstractions.Database;
using Ufo.Abstractions.DataTransferObjects;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Options;
using Ufo.Abstractions.Requests;
using Ufo.Database.Contexts;
using Ufo.FunctionalTests.Extensions;
using Ufo.Server.Extensions;

namespace Ufo.FunctionalTests.SnapshotController;

#region Test WebApplication factory

/// <summary>
/// Boots a real in-process ASP.NET Core host for Snapshot Controller tests.
/// Uses an in-memory SQLite database for complete isolation.
/// </summary>
public class SnapshotApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"test-snapshot-{Guid.NewGuid():N}";
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
                ["JWT:Key"] = SnapshotTestConstants.JwtKey,
                ["JWT:Issuer"] = SnapshotTestConstants.JwtIssuer,
                ["JWT:Audience"] = SnapshotTestConstants.JwtAudience,
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
                        Encoding.UTF8.GetBytes(SnapshotTestConstants.JwtKey)),
                    ValidateIssuer = true,
                    ValidIssuer = SnapshotTestConstants.JwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = SnapshotTestConstants.JwtAudience,
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

public static class SnapshotTestConstants
{
    public const string JwtKey = "super-secret-test-key-that-is-long-enough-256bits!!";
    public const string JwtIssuer = "ufo-test-issuer";
    public const string JwtAudience = "ufo-test-audience";

    public static readonly Ulid TestUserId = Ulid.NewUlid();
    public static readonly string TestUserName = "testuser";
    public static readonly string TestUserPasswordHash = BCrypt.Net.BCrypt.HashPassword("TestPassword123!");

    public static readonly Ulid TestUserId2 = Ulid.NewUlid();
    public static readonly string TestUserName2 = "testuser2";
    public static readonly string TestUserPasswordHash2 = BCrypt.Net.BCrypt.HashPassword("TestPassword456!");
}

#endregion

#region Test Helpers

public static class SnapshotRequestFactory
{
    public static PathRequest CreatePathRequest(string path)
    {
        return new PathRequest { Path = path };
    }
}

public static class SnapshotTestDataBuilder
{
    public static SnapshotEntity CreateSnapshotEntity(Ulid userId, UserEntity? user = null)
    {
        return new SnapshotEntity
        {
            Id = Ulid.NewUlid(),
            Timestamp = DateTimeOffset.UtcNow,
            Description = "Test Snapshot",
            UserId = userId,
            User = user ?? new UserEntity { Id = userId, Name = "Test User" }
        };
    }

    public static FolderEntity CreateRootFolderEntity(Ulid userId, UserEntity? user = null)
    {
        user ??= new UserEntity { Id = userId, Name = "Test User" };
        return new FolderEntity
        {
            Id = Ulid.NewUlid(),
            Name = "Root",
            Size = 0,
            Sha256Hash = "root-hash",
            CreatedAt = DateTime.UtcNow.ToString("o"),
            UpdatedAt = DateTime.UtcNow.ToString("o"),
            IsHidden = false,
            UserId = userId,
            User = user
        };
    }

    public static FileEntity CreateFileEntity(Ulid userId, string fileName = "test.txt", long size = 1024, UserEntity? user = null)
    {
        user ??= new UserEntity { Id = userId, Name = "Test User" };
        return new FileEntity
        {
            Id = Ulid.NewUlid(),
            Name = Path.GetFileNameWithoutExtension(fileName),
            Size = size,
            Sha256Hash = "test-file-hash",
            FileExtension = Path.GetExtension(fileName),
            CreatedAt = DateTime.UtcNow.ToString("o"),
            UpdatedAt = DateTime.UtcNow.ToString("o"),
            IsHidden = false,
            UserId = userId,
            User = user
        };
    }
}

#endregion

#region Functional Tests

public class SnapshotControllerFunctionalTests : IAsyncLifetime
{
    private SnapshotApiFactory _factory = null!;
    private HttpClient _client = null!;
    private SqliteConnection _connection = null!;
    private string _snapshotRootPath = null!;

    public async Task InitializeAsync()
    {
        _factory = new SnapshotApiFactory();
        _client = await _factory.CreateClientAsync();
        _connection = new SqliteConnection(_factory.ConnectionString);
        await _connection.OpenAsync();

        _snapshotRootPath = Path.Combine(Path.GetTempPath(), $"ufo-snapshot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_snapshotRootPath);

        // Register test users
        await RegisterTestUser(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName, SnapshotTestConstants.TestUserPasswordHash);
        await RegisterTestUser(SnapshotTestConstants.TestUserId2, SnapshotTestConstants.TestUserName2, SnapshotTestConstants.TestUserPasswordHash2);
    }

    public async Task DisposeAsync()
    {
        _connection?.Dispose();
        await _factory.DisposeAsync();

        if (_snapshotRootPath is not null && Directory.Exists(_snapshotRootPath))
        {
            Directory.Delete(_snapshotRootPath, recursive: true);
        }
    }

    private async Task RegisterTestUser(Ulid userId, string userName, string passwordHash)
    {
        // TODO LA - Move to a shared class for all tests that need users.
        const string sql = @"
            INSERT INTO Users (Id, Name, PasswordHash, CreatedAt) 
            VALUES (@Id, @Name, @PasswordHash, @CreatedAt)";

        using var connection = new SqliteConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@Id", userId.ToString());
        command.Parameters.AddWithValue("@Name", userName);
        command.Parameters.AddWithValue("@PasswordHash", passwordHash);
        command.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow.ToString("o"));
        await command.ExecuteNonQueryAsync();
    }

    private string GenerateToken(Ulid userId, string userName)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SnapshotTestConstants.JwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.NameId, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, userName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: SnapshotTestConstants.JwtIssuer,
            audience: SnapshotTestConstants.JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }


    #region CreateSnapshot Tests

    private string WriteSnapshotFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_snapshotRootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);

        return fullPath;
    }

    private async Task<HttpResponseMessage> PostCreateSnapshotAsync(Ulid userId, string userName)
    {
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GenerateToken(userId, userName));

        return await _client.PostAsJsonAsync(
            "api/snapshot/create",
            SnapshotRequestFactory.CreatePathRequest(_snapshotRootPath));
    }

    /// <summary>
    /// Flags or clears paths as the test user. The shared client carries no token
    /// by default, so it is set here exactly as PostCreateSnapshotAsync does.
    /// </summary>
    private async Task<HttpResponseMessage> PostSetFlagsAsync(bool isFlagEnabled, params string[] fullPaths)
    {
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            GenerateToken(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName));

        return await _client.PostAsJsonAsync(
            "api/fsitemflags",
            new FsItemFlagsRequest { FullPaths = fullPaths, IsFlagEnabled = isFlagEnabled });
    }

    /// <summary>Rates paths as the test user, or clears them with zero.</summary>
    private async Task<HttpResponseMessage> PostSetRatingsAsync(int rating, params string[] fullPaths)
    {
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            GenerateToken(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName));

        return await _client.PostAsJsonAsync(
            "api/fsitemratings",
            new FsItemRatingsRequest { FullPaths = fullPaths, Rating = rating });
    }

    /// <summary>Creates a tag as the test user and returns its id.</summary>
    private async Task<Ulid> PostCreateTagAsync(string name, string colorHex)
    {
        Authenticate();

        var response = await _client.PostAsJsonAsync(
            "api/tags",
            new CreateTagRequest { Name = name, ColorHex = colorHex });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<TagDto>())!.Id;
    }

    /// <summary>Puts a tag on, or takes it off, the given paths.</summary>
    private async Task<HttpResponseMessage> PostSetTagAsync(Ulid tagId, bool isApplied, params string[] fullPaths)
    {
        Authenticate();

        return await _client.PostAsJsonAsync(
            "api/tags/assign",
            new SetFsItemTagRequest { TagId = tagId, FullPaths = fullPaths, IsApplied = isApplied });
    }

    private void Authenticate() =>
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            GenerateToken(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName));

    private async Task<long> CountRowsAsync(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;

        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static IEnumerable<FolderDto> Flatten(FolderDto folder) =>
        [folder, .. folder.ChildFolders.SelectMany(Flatten)];

    [Fact]
    public async Task CreateSnapshot_WithNestedTree_PersistsEveryFolderAndFile()
    {
        WriteSnapshotFile("top.txt", "top-level");
        WriteSnapshotFile("documents/notes.md", "some notes");
        WriteSnapshotFile("documents/archive/old.log", "old log line");

        var createResponse = await PostCreateSnapshotAsync(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName);

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        var latestResponse = await _client.GetAsync("api/snapshot/latest");
        Assert.Equal(HttpStatusCode.OK, latestResponse.StatusCode);
        var snapshot = await latestResponse.Content.ReadFromJsonAsync<SnapshotDto>();

        var rootFolder = snapshot!.RootFolder;
        Assert.NotNull(rootFolder);
        Assert.Equal(Path.GetFileName(_snapshotRootPath), rootFolder.Name);

        var allFolders = Flatten(rootFolder).ToList();
        Assert.Equal(
            [Path.GetFileName(_snapshotRootPath), "archive", "documents"],
            allFolders.Select(folder => folder.Name).OrderBy(name => name != Path.GetFileName(_snapshotRootPath)).ThenBy(name => name).ToList());
        Assert.Equal(
            ["notes", "old", "top"],
            allFolders.SelectMany(folder => folder.Files).Select(file => file.Name).Order().ToList());

        // Every folder's own hash and its rolled-up size have to survive the round trip,
        // not just the file rows.
        Assert.All(allFolders, folder => Assert.NotEqual(string.Empty, folder.Sha256Hash));
        Assert.Equal("top-level".Length + "some notes".Length + "old log line".Length, rootFolder.Size);
    }

    [Fact]
    public async Task CreateSnapshot_WithIdenticalFilesInDifferentFolders_StoresOneFileRow()
    {
        WriteSnapshotFile("left/same.txt", "identical content");
        WriteSnapshotFile("right/same.txt", "identical content");

        var createResponse = await PostCreateSnapshotAsync(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName);

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        // De-duplication is by content, so the two copies share a single Files row and
        // are told apart by their two bindings.
        Assert.Equal(1, await CountRowsAsync("SELECT COUNT(*) FROM Files"));
        Assert.Equal(2, await CountRowsAsync("SELECT COUNT(*) FROM FilesToFolders"));
    }

    [Fact]
    public async Task CreateSnapshot_RecordsTheFlagAgainstTheBindingAndNotTheSharedFileRow()
    {
        // The reason the flag cannot live on Files. These two are byte-identical,
        // so they share one row - a flag column there would mark both, in every
        // snapshot they ever appear in.
        WriteSnapshotFile("left/same.txt", "identical content");
        WriteSnapshotFile("right/same.txt", "identical content");

        var flaggedPath = Path.Combine(_snapshotRootPath, "left", "same.txt");

        Assert.Equal(HttpStatusCode.OK, (await PostSetFlagsAsync(true, flaggedPath)).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await PostCreateSnapshotAsync(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName)).StatusCode);

        // One shared row, two bindings, and exactly one of them flagged.
        Assert.Equal(1, await CountRowsAsync("SELECT COUNT(*) FROM Files"));
        Assert.Equal(2, await CountRowsAsync("SELECT COUNT(*) FROM FilesToFolders"));
        Assert.Equal(1, await CountRowsAsync("SELECT COUNT(*) FROM FilesToFolders WHERE IsFlagEnabled = 1"));
    }

    [Fact]
    public async Task CreateSnapshot_KeepsTheFlagOfItsOwnMomentWhenTheLiveFlagChangesLater()
    {
        WriteSnapshotFile("data/report.txt", "report");
        var reportPath = Path.Combine(_snapshotRootPath, "data", "report.txt");

        Assert.Equal(HttpStatusCode.OK, (await PostSetFlagsAsync(true, reportPath)).StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await PostCreateSnapshotAsync(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName)).StatusCode);

        // Cleared afterwards. A snapshot records the moment it was taken, so this
        // must not reach back into one already made.
        Assert.Equal(HttpStatusCode.OK, (await PostSetFlagsAsync(false, reportPath)).StatusCode);

        Assert.Equal(1, await CountRowsAsync("SELECT COUNT(*) FROM FilesToFolders WHERE IsFlagEnabled = 1"));
        Assert.Equal(0, await CountRowsAsync("SELECT COUNT(*) FROM FsItemFlags"));
    }

    [Fact]
    public async Task CreateSnapshot_LeavesEverythingUnflaggedWhenNothingIs()
    {
        WriteSnapshotFile("data/one.txt", "one");

        Assert.Equal(
            HttpStatusCode.OK,
            (await PostCreateSnapshotAsync(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName)).StatusCode);

        // Flags are off by default, which is the ordinary case.
        Assert.Equal(0, await CountRowsAsync("SELECT COUNT(*) FROM FilesToFolders WHERE IsFlagEnabled = 1"));
        Assert.Equal(0, await CountRowsAsync("SELECT COUNT(*) FROM FoldersToFolders WHERE IsFlagEnabled = 1"));
    }

    [Fact]
    public async Task CreateSnapshot_RecordsTheRatingAgainstTheBindingAndNotTheSharedFileRow()
    {
        // The reason a rating cannot live on Files, exactly as for the flag: these
        // two are byte-identical and share one row, so a rating column there would
        // rate both, in every snapshot they appear in.
        WriteSnapshotFile("left/same.txt", "identical content");
        WriteSnapshotFile("right/same.txt", "identical content");

        var ratedPath = Path.Combine(_snapshotRootPath, "left", "same.txt");

        Assert.Equal(HttpStatusCode.OK, (await PostSetRatingsAsync(9, ratedPath)).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await PostCreateSnapshotAsync(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName)).StatusCode);

        // One shared row, two bindings, and the 9 on exactly one of them.
        Assert.Equal(1, await CountRowsAsync("SELECT COUNT(*) FROM Files"));
        Assert.Equal(2, await CountRowsAsync("SELECT COUNT(*) FROM FilesToFolders"));
        Assert.Equal(1, await CountRowsAsync("SELECT COUNT(*) FROM FilesToFolders WHERE Rating = 9"));
        Assert.Equal(1, await CountRowsAsync("SELECT COUNT(*) FROM FilesToFolders WHERE Rating = 0"));
    }

    [Fact]
    public async Task CreateSnapshot_KeepsTheRatingOfItsOwnMomentWhenTheLiveRatingChangesLater()
    {
        WriteSnapshotFile("data/report.txt", "report");
        var reportPath = Path.Combine(_snapshotRootPath, "data", "report.txt");

        Assert.Equal(HttpStatusCode.OK, (await PostSetRatingsAsync(6, reportPath)).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await PostCreateSnapshotAsync(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName)).StatusCode);

        // Changed afterwards. A snapshot records the moment it was taken, so this
        // must not reach back into one already made.
        Assert.Equal(HttpStatusCode.OK, (await PostSetRatingsAsync(2, reportPath)).StatusCode);

        Assert.Equal(1, await CountRowsAsync("SELECT COUNT(*) FROM FilesToFolders WHERE Rating = 6"));
        Assert.Equal(0, await CountRowsAsync("SELECT COUNT(*) FROM FilesToFolders WHERE Rating = 2"));
        Assert.Equal(1, await CountRowsAsync("SELECT COUNT(*) FROM FsItemRatings WHERE Rating = 2"));
    }

    [Fact]
    public async Task CreateSnapshot_RecordsARatedFolderWithoutRatingWhatIsInsideIt()
    {
        WriteSnapshotFile("data/one.txt", "one");
        var dataFolderPath = Path.Combine(_snapshotRootPath, "data");

        Assert.Equal(HttpStatusCode.OK, (await PostSetRatingsAsync(10, dataFolderPath)).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await PostCreateSnapshotAsync(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName)).StatusCode);

        // Rating a folder marks that folder only - the choice flags made too.
        Assert.Equal(1, await CountRowsAsync("SELECT COUNT(*) FROM FoldersToFolders WHERE Rating = 10"));
        Assert.Equal(0, await CountRowsAsync("SELECT COUNT(*) FROM FilesToFolders WHERE Rating > 0"));
    }

    [Fact]
    public async Task CreateSnapshot_LeavesEverythingUnratedWhenNothingIs()
    {
        WriteSnapshotFile("data/one.txt", "one");

        Assert.Equal(
            HttpStatusCode.OK,
            (await PostCreateSnapshotAsync(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName)).StatusCode);

        // Zero is unrated, which is the ordinary case.
        Assert.Equal(0, await CountRowsAsync("SELECT COUNT(*) FROM FilesToFolders WHERE Rating > 0"));
        Assert.Equal(0, await CountRowsAsync("SELECT COUNT(*) FROM FoldersToFolders WHERE Rating > 0"));
    }

    [Fact]
    public async Task CreateSnapshot_RecordsTagsAgainstTheBindingAndNotTheSharedFileRow()
    {
        // The reason tags cannot hang off Files, as with the flag and the rating:
        // these two are byte-identical and share one row.
        WriteSnapshotFile("left/same.txt", "identical content");
        WriteSnapshotFile("right/same.txt", "identical content");

        var importantId = await PostCreateTagAsync("Important", "#ff0000");
        var archiveId = await PostCreateTagAsync("Archive", "#0000ff");
        var taggedPath = Path.Combine(_snapshotRootPath, "left", "same.txt");

        // An item may carry several, which is what makes this a table rather
        // than another column on the association.
        Assert.Equal(HttpStatusCode.OK, (await PostSetTagAsync(importantId, true, taggedPath)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostSetTagAsync(archiveId, true, taggedPath)).StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await PostCreateSnapshotAsync(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName)).StatusCode);

        Assert.Equal(1, await CountRowsAsync("SELECT COUNT(*) FROM Files"));
        Assert.Equal(2, await CountRowsAsync("SELECT COUNT(*) FROM FilesToFolders"));
        // Two tags, on one of the two bindings.
        Assert.Equal(2, await CountRowsAsync("SELECT COUNT(*) FROM TagsToSnapshotFiles"));
        Assert.Equal(
            1,
            await CountRowsAsync("SELECT COUNT(DISTINCT FileId || FolderId) FROM TagsToSnapshotFiles"));
    }

    [Fact]
    public async Task CreateSnapshot_KeepsTheTagsOfItsOwnMomentWhenTheyChangeLater()
    {
        WriteSnapshotFile("data/report.txt", "report");
        var reportPath = Path.Combine(_snapshotRootPath, "data", "report.txt");

        var tagId = await PostCreateTagAsync("Important", "#ff0000");
        Assert.Equal(HttpStatusCode.OK, (await PostSetTagAsync(tagId, true, reportPath)).StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await PostCreateSnapshotAsync(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName)).StatusCode);

        // Taken off afterwards. A snapshot records the moment it was taken.
        Assert.Equal(HttpStatusCode.OK, (await PostSetTagAsync(tagId, false, reportPath)).StatusCode);

        Assert.Equal(1, await CountRowsAsync("SELECT COUNT(*) FROM TagsToSnapshotFiles"));
        Assert.Equal(0, await CountRowsAsync("SELECT COUNT(*) FROM FsItemTags"));
    }

    [Fact]
    public async Task GetSnapshot_ReadsBackTheTagsItWasTakenWith()
    {
        WriteSnapshotFile("data/report.txt", "report");
        var reportPath = Path.Combine(_snapshotRootPath, "data", "report.txt");

        var tagId = await PostCreateTagAsync("Important", "#ff0000");
        await PostSetTagAsync(tagId, true, reportPath);
        await PostCreateSnapshotAsync(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName);

        Authenticate();
        var snapshot = await (await _client.GetAsync("api/snapshot/latest"))
            .Content.ReadFromJsonAsync<SnapshotDto>();

        // The round trip that matters: written at capture against the binding,
        // read back separately, and stitched onto the right file in the tree.
        var taggedFile = Flatten(snapshot!.RootFolder!)
            .SelectMany(folder => folder.Files)
            .Single(file => file.Name == "report");

        Assert.Single(taggedFile.Tags);
        Assert.Equal("Important", taggedFile.Tags[0].Name);
        Assert.Equal("#ff0000", taggedFile.Tags[0].ColorHex);
    }

    [Fact]
    public async Task CreateSnapshot_OverTheSameTreeTwice_ReusesTheExistingRows()
    {
        WriteSnapshotFile("data/one.txt", "one");
        WriteSnapshotFile("data/two.txt", "two");

        Assert.Equal(
            HttpStatusCode.OK,
            (await PostCreateSnapshotAsync(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName)).StatusCode);

        var folderCountAfterFirst = await CountRowsAsync("SELECT COUNT(*) FROM Folders");
        var fileCountAfterFirst = await CountRowsAsync("SELECT COUNT(*) FROM Files");

        Assert.Equal(
            HttpStatusCode.OK,
            (await PostCreateSnapshotAsync(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName)).StatusCode);

        // Nothing on disk changed, so the second snapshot is entirely new bindings over
        // the rows the first one wrote.
        Assert.Equal(folderCountAfterFirst, await CountRowsAsync("SELECT COUNT(*) FROM Folders"));
        Assert.Equal(fileCountAfterFirst, await CountRowsAsync("SELECT COUNT(*) FROM Files"));
        Assert.Equal(2, await CountRowsAsync("SELECT COUNT(*) FROM Snapshots"));
    }

    [Fact]
    public async Task CreateSnapshot_WithMoreRowsThanFitInOneStatement_PersistsThemAll()
    {
        // Inserts are batched, so the interesting sizes are the ones that do not divide
        // evenly into a batch. 250 distinct files spread over 40 folders crosses the
        // boundary for both tables.
        const int fileCount = 250;
        const int folderCount = 40;

        for (var fileIndex = 0; fileIndex < fileCount; fileIndex++)
        {
            WriteSnapshotFile($"folder-{fileIndex % folderCount}/file-{fileIndex}.txt", $"unique-content-{fileIndex}");
        }

        var createResponse = await PostCreateSnapshotAsync(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName);

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        Assert.Equal(fileCount, await CountRowsAsync("SELECT COUNT(*) FROM Files"));
        Assert.Equal(fileCount, await CountRowsAsync("SELECT COUNT(*) FROM FilesToFolders"));
        Assert.Equal(folderCount + 1, await CountRowsAsync("SELECT COUNT(*) FROM Folders"));
        Assert.Equal(folderCount + 1, await CountRowsAsync("SELECT COUNT(*) FROM FoldersToFolders"));

        var latestResponse = await _client.GetAsync("api/snapshot/latest");
        var snapshot = await latestResponse.Content.ReadFromJsonAsync<SnapshotDto>();
        Assert.Equal(fileCount, Flatten(snapshot!.RootFolder!).SelectMany(folder => folder.Files).Count());
    }

    [Fact]
    public async Task CreateSnapshot_WithoutAuth_ReturnUnauthorized()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.PostAsJsonAsync(
            "api/snapshot/create",
            SnapshotRequestFactory.CreatePathRequest(_snapshotRootPath));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region GetLatestSnapshot Tests

    [Fact]
    public async Task GetLatestSnapshot_WithNoSnapshots_ReturnsNoContent()
    {
        // Arrange
        var token = GenerateToken(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        // Act
        var response = await _client.GetAsync("/api/snapshot/latest");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task GetLatestSnapshot_WithSnapshot_ReturnsOkWithSnapshot()
    {
        // Arrange
        var token = GenerateToken(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var user = new UserEntity { Id = SnapshotTestConstants.TestUserId, Name = SnapshotTestConstants.TestUserName };
        var snapshot = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId, user);

        await InsertSnapshot(snapshot);

        // Act
        var response = await _client.GetAsync("/api/snapshot/latest");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(content);
        Assert.Contains(snapshot.Id.ToString(), content);

        // TODO LA - Add more assertions to validate the returned snapshot content is correct (description, timestamp, etc.).
        // TODO LA - Add assertion for root folder and ensure it is returned correctly.
    }

    [Fact]
    public async Task GetLatestSnapshot_WithMultipleSnapshots_ReturnsLatestSnapshot()
    {
        // Arrange
        var token = GenerateToken(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var user = new UserEntity { Id = SnapshotTestConstants.TestUserId, Name = SnapshotTestConstants.TestUserName };

        var snapshot1 = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId, user);
        snapshot1.Timestamp = DateTimeOffset.UtcNow.AddHours(-2);

        await InsertSnapshot(snapshot1);

        var snapshot2 = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId, user);
        snapshot2.Timestamp = DateTimeOffset.UtcNow;

        await InsertSnapshot(snapshot2);

        // Act
        var response = await _client.GetAsync("/api/snapshot/latest");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(snapshot2.Id.ToString(), content);
    }

    [Fact]
    public async Task GetLatestSnapshot_WithoutAuth_ReturnUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/snapshot/latest");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetLatestSnapshot_WithMultipleUsers_ReturnsOnlyCurrentUserSnapshot()
    {
        // Arrange - Create and insert snapshots for both test users
        var user1 = new UserEntity { Id = SnapshotTestConstants.TestUserId, Name = SnapshotTestConstants.TestUserName };
        var user2 = new UserEntity { Id = SnapshotTestConstants.TestUserId2, Name = SnapshotTestConstants.TestUserName2 };

        // User 1 snapshots
        var user1Snapshot1 = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId, user1);
        user1Snapshot1.Timestamp = DateTimeOffset.UtcNow.AddHours(-3);
        user1Snapshot1.Description = "User 1 Old Snapshot";

        var user1Snapshot2 = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId, user1);
        user1Snapshot2.Timestamp = DateTimeOffset.UtcNow.AddHours(-1);
        user1Snapshot2.Description = "User 1 Latest Snapshot";

        // User 2 snapshots
        var user2Snapshot1 = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId2, user2);
        user2Snapshot1.Timestamp = DateTimeOffset.UtcNow;
        user2Snapshot1.Description = "User 2 Latest Snapshot";

        await InsertSnapshot(user1Snapshot1);
        await InsertSnapshot(user1Snapshot2);
        await InsertSnapshot(user2Snapshot1);

        // Act - Request as User 1
        var token1 = GenerateToken(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token1}");

        var response1 = await _client.GetAsync("/api/snapshot/latest");
        var content1 = await response1.Content.ReadAsStringAsync();

        // Assert - User 1 should only get their latest snapshot
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Contains(user1Snapshot2.Id.ToString(), content1);
        Assert.DoesNotContain(user1Snapshot1.Id.ToString(), content1);
        Assert.DoesNotContain(user2Snapshot1.Id.ToString(), content1);

        // Act - Request as User 2
        var token2 = GenerateToken(SnapshotTestConstants.TestUserId2, SnapshotTestConstants.TestUserName2);
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token2}");

        var response2 = await _client.GetAsync("/api/snapshot/latest");
        var content2 = await response2.Content.ReadAsStringAsync();

        // Assert - User 2 should only get their latest snapshot
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.Contains(user2Snapshot1.Id.ToString(), content2);
        Assert.DoesNotContain(user1Snapshot1.Id.ToString(), content2);
        Assert.DoesNotContain(user1Snapshot2.Id.ToString(), content2);
    }

    #endregion

    #region GetSnapshotById Tests

    [Fact]
    public async Task GetSnapshotById_WithValidSnapshot_ReturnsOk()
    {
        // Arrange
        var token = GenerateToken(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var user = new UserEntity { Id = SnapshotTestConstants.TestUserId, Name = SnapshotTestConstants.TestUserName };
        var snapshot = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId, user);

        await InsertSnapshot(snapshot);

        // Act
        var response = await _client.GetAsync($"/api/snapshot/{snapshot.Id}");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(snapshot.Id.ToString(), content);
    }

    [Fact]
    public async Task GetSnapshotById_WithNonExistentSnapshotId_ReturnsNotFound()
    {
        // Arrange
        var token = GenerateToken(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var nonExistentSnapshotId = Ulid.NewUlid();

        // Act
        var response = await _client.GetAsync($"/api/snapshot/{nonExistentSnapshotId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetSnapshotById_WithOtherUserSnapshot_ReturnsNotFound()
    {
        // Arrange
        var token1 = GenerateToken(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName);
        var user1 = new UserEntity { Id = SnapshotTestConstants.TestUserId, Name = SnapshotTestConstants.TestUserName };
        var snapshot = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId, user1);

        await InsertSnapshot(snapshot);

        // Act - Try to access with different user
        var token2 = GenerateToken(SnapshotTestConstants.TestUserId2, SnapshotTestConstants.TestUserName2);
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token2}");
        var response = await _client.GetAsync($"/api/snapshot/{snapshot.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetSnapshotById_WithoutAuth_ReturnUnauthorized()
    {
        // Act
        var response = await _client.GetAsync($"/api/snapshot/{Ulid.NewUlid()}");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region GetAllSnapshotsSummary Tests

    [Fact]
    public async Task GetAllSnapshotsSummary_WithNoSnapshots_ReturnsEmptyList()
    {
        // Arrange
        var token = GenerateToken(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        // Act
        var response = await _client.GetAsync("/api/snapshot/all/summary");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("[]", content);
    }

    [Fact]
    public async Task GetAllSnapshotsSummary_WithMultipleSnapshots_ReturnsAllUserSnapshots()
    {
        // Arrange
        var token = GenerateToken(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var user = new UserEntity { Id = SnapshotTestConstants.TestUserId, Name = SnapshotTestConstants.TestUserName };

        var snapshot1 = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId, user);

        await InsertSnapshot(snapshot1);

        var snapshot2 = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId, user);

        await InsertSnapshot(snapshot2);

        // Act
        var response = await _client.GetAsync("/api/snapshot/all/summary");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(snapshot1.Id.ToString(), content);
        Assert.Contains(snapshot2.Id.ToString(), content);
    }

    [Fact]
    public async Task GetAllSnapshotsSummary_WithUserIsolation_ReturnsOnlyUserSnapshots()
    {
        // Arrange
        var user1 = new UserEntity { Id = SnapshotTestConstants.TestUserId, Name = SnapshotTestConstants.TestUserName };
        var user2 = new UserEntity { Id = SnapshotTestConstants.TestUserId2, Name = SnapshotTestConstants.TestUserName2 };

        // Create snapshots for both users
        var snapshot1 = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId, user1);

        await InsertSnapshot(snapshot1);

        var snapshot2 = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId2, user2);

        await InsertSnapshot(snapshot2);

        // Act - Query with user 1 token
        var token = GenerateToken(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        var response = await _client.GetAsync("/api/snapshot/all/summary");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(snapshot1.Id.ToString(), content);
        Assert.DoesNotContain(snapshot2.Id.ToString(), content);
    }

    [Fact]
    public async Task GetAllSnapshotsSummary_WithoutAuth_ReturnUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/snapshot/all/summary");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region DeleteSnapshotByIdAsync Tests

    [Fact]
    public async Task DeleteSnapshotByIdAsync_WithValidSnapshot_ReturnsOkAndDeletes()
    {
        // Arrange
        var token = GenerateToken(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var user = new UserEntity { Id = SnapshotTestConstants.TestUserId, Name = SnapshotTestConstants.TestUserName };
        var snapshot = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId, user);

        await InsertSnapshot(snapshot);

        // Act
        var response = await _client.DeleteAsync($"/api/snapshot/delete/{snapshot.Id}");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("sucessfully deleted", content);
    }

    [Fact]
    public async Task DeleteSnapshotByIdAsync_WithInvalidSnapshot_ReturnsNotFound()
    {
        // Arrange
        var token = GenerateToken(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var nonExistentSnapshotId = Ulid.NewUlid();

        // Act
        var response = await _client.DeleteAsync($"/api/snapshot/delete/{nonExistentSnapshotId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteSnapshotByIdAsync_WithOtherUserSnapshot_ReturnsNotFound()
    {
        // Arrange
        var user1 = new UserEntity { Id = SnapshotTestConstants.TestUserId, Name = SnapshotTestConstants.TestUserName };
        var snapshot = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId, user1);

        await InsertSnapshot(snapshot);

        // Act - Try to delete with different user
        var token2 = GenerateToken(SnapshotTestConstants.TestUserId2, SnapshotTestConstants.TestUserName2);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token2}");
        var response = await _client.DeleteAsync($"/api/snapshot/delete/{snapshot.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteSnapshotByIdAsync_WithoutAuth_ReturnUnauthorized()
    {
        // Act
        var response = await _client.DeleteAsync($"/api/snapshot/delete/{Ulid.NewUlid()}");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteSnapshotByIdAsync_WithUserIsolation_UserCanOnlyDeleteOwnSnapshots()
    {
        // Arrange - Create snapshots for both users
        var user1 = new UserEntity { Id = SnapshotTestConstants.TestUserId, Name = SnapshotTestConstants.TestUserName };
        var user2 = new UserEntity { Id = SnapshotTestConstants.TestUserId2, Name = SnapshotTestConstants.TestUserName2 };

        var user1Snapshot = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId, user1);
        var user2Snapshot = SnapshotTestDataBuilder.CreateSnapshotEntity(SnapshotTestConstants.TestUserId2, user2);

        await InsertSnapshot(user1Snapshot);
        await InsertSnapshot(user2Snapshot);

        // Act & Assert - User 1 successfully deletes their own snapshot
        var token1 = GenerateToken(SnapshotTestConstants.TestUserId, SnapshotTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token1}");

        var deleteOwnResponse = await _client.DeleteAsync($"/api/snapshot/delete/{user1Snapshot.Id}");

        Assert.Equal(HttpStatusCode.OK, deleteOwnResponse.StatusCode);

        // Act & Assert - User 1 cannot delete User 2's snapshot
        var deleteOtherResponse = await _client.DeleteAsync($"/api/snapshot/delete/{user2Snapshot.Id}");

        Assert.Equal(HttpStatusCode.NotFound, deleteOtherResponse.StatusCode);

        // Act & Assert - User 2 can delete their own snapshot
        var token2 = GenerateToken(SnapshotTestConstants.TestUserId2, SnapshotTestConstants.TestUserName2);
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token2}");

        var user2DeleteOwnResponse = await _client.DeleteAsync($"/api/snapshot/delete/{user2Snapshot.Id}");

        Assert.Equal(HttpStatusCode.OK, user2DeleteOwnResponse.StatusCode);

        // Act & Assert - User 2 cannot delete already-deleted User 1 snapshot
        var user2DeleteDeletedResponse = await _client.DeleteAsync($"/api/snapshot/delete/{user1Snapshot.Id}");

        Assert.Equal(HttpStatusCode.NotFound, user2DeleteDeletedResponse.StatusCode);
    }

    #endregion

    #region Helper Methods

    private async Task InsertSnapshot(SnapshotEntity snapshot)
    {
        const string snapshotSql = @"
            INSERT INTO Snapshots (Id, Timestamp, Description, UserId)
            VALUES (@Id, @Timestamp, @Description, @UserId)";

        using var connection = new SqliteConnection(_factory.ConnectionString);
        await connection.OpenAsync();

        using var snapshotCommand = connection.CreateCommand();
        snapshotCommand.CommandText = snapshotSql;
        snapshotCommand.Parameters.AddWithValue("@Id", snapshot.Id.ToString());
        snapshotCommand.Parameters.AddWithValue("@Timestamp", snapshot.Timestamp.ToString("o"));
        snapshotCommand.Parameters.AddWithValue("@Description", snapshot.Description ?? "");
        snapshotCommand.Parameters.AddWithValue("@UserId", snapshot.UserId.ToString());
        await snapshotCommand.ExecuteNonQueryAsync();

        // Insert root folder if it exists
        if (snapshot.RootFolder != null)
        {
            const string folderSql = @"
                INSERT INTO Folders (Id, Name, Size, Sha256Hash, CreatedAt, UpdatedAt, IsHidden, UserId)
                VALUES (@Id, @Name, @Size, @Sha256Hash, @CreatedAt, @UpdatedAt, @IsHidden, @UserId)";

            using var folderCommand = connection.CreateCommand();
            folderCommand.CommandText = folderSql;
            folderCommand.Parameters.AddWithValue("@Id", snapshot.RootFolder.Id.ToString());
            folderCommand.Parameters.AddWithValue("@Name", snapshot.RootFolder.Name);
            folderCommand.Parameters.AddWithValue("@Size", snapshot.RootFolder.Size);
            folderCommand.Parameters.AddWithValue("@Sha256Hash", snapshot.RootFolder.Sha256Hash);
            folderCommand.Parameters.AddWithValue("@CreatedAt", snapshot.RootFolder.CreatedAt);
            folderCommand.Parameters.AddWithValue("@UpdatedAt", snapshot.RootFolder.UpdatedAt);
            folderCommand.Parameters.AddWithValue("@IsHidden", snapshot.RootFolder.IsHidden ? 1 : 0);
            folderCommand.Parameters.AddWithValue("@UserId", snapshot.RootFolder.UserId.ToString());
            await folderCommand.ExecuteNonQueryAsync();

            // Link folder to snapshot via FoldersToFolders
            const string folderToFolderSql = @"
                INSERT OR IGNORE INTO FoldersToFolders (SnapshotId, ParentFolderId, ChildFolderId)
                VALUES (@SnapshotId, @ParentFolderId, @ChildFolderId)";

            using var folderToFolderCommand = connection.CreateCommand();
            folderToFolderCommand.CommandText = folderToFolderSql;
            folderToFolderCommand.Parameters.AddWithValue("@SnapshotId", snapshot.Id.ToString());
            folderToFolderCommand.Parameters.AddWithValue("@ParentFolderId", DBNull.Value);
            folderToFolderCommand.Parameters.AddWithValue("@ChildFolderId", snapshot.RootFolder.Id.ToString());
            try
            {
                await folderToFolderCommand.ExecuteNonQueryAsync();
            }
            catch
            {
                // Ignore errors for test data insertion
            }
        }
    }

    private async Task InsertFolder(SnapshotEntity snapshot, FolderEntity folder, FolderEntity? parentFolder, SqliteConnection connection)
    {
        const string folderSql = @"
            INSERT INTO Folders (Id, Name, Size, Sha256Hash, CreatedAt, UpdatedAt, IsHidden, UserId)
            VALUES (@Id, @Name, @Size, @Sha256Hash, @CreatedAt, @UpdatedAt, @IsHidden, @UserId)";

        using var command = connection.CreateCommand();
        command.CommandText = folderSql;
        command.Parameters.AddWithValue("@Id", folder.Id.ToString());
        command.Parameters.AddWithValue("@Name", folder.Name);
        command.Parameters.AddWithValue("@Size", folder.Size);
        command.Parameters.AddWithValue("@Sha256Hash", folder.Sha256Hash);
        command.Parameters.AddWithValue("@CreatedAt", folder.CreatedAt);
        command.Parameters.AddWithValue("@UpdatedAt", folder.UpdatedAt);
        command.Parameters.AddWithValue("@IsHidden", folder.IsHidden ? 1 : 0);
        command.Parameters.AddWithValue("@UserId", folder.UserId.ToString());
        await command.ExecuteNonQueryAsync();

        // Link folder to snapshot
        using var folderSnapshotCommand = connection.CreateCommand();
        folderSnapshotCommand.CommandText = "INSERT OR IGNORE INTO FoldersToFolders (SnapshotId, ParentFolderId, ChildFolderId) VALUES (@SnapshotId, @ParentFolderId, @ChildFolderId)";
        folderSnapshotCommand.Parameters.AddWithValue("@SnapshotId", snapshot.Id.ToString());
        folderSnapshotCommand.Parameters.AddWithValue("@ParentFolderId", parentFolder?.Id.ToString() ?? (object)DBNull.Value);
        folderSnapshotCommand.Parameters.AddWithValue("@ChildFolderId", folder.Id.ToString());
        await folderSnapshotCommand.ExecuteNonQueryAsync();
    }

    #endregion
}

#endregion
