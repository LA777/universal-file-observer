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
using Ufo.Abstractions.Options;
using Ufo.Abstractions.Requests;
using Ufo.Database.Contexts;
using Ufo.FunctionalTests.Extensions;
using Ufo.Server.Extensions;
using Ufo.Server.Models;

namespace Ufo.FunctionalTests.FileSystemController;

#region Test WebApplication Factory

/// <summary>
/// Boots a real in-process ASP.NET Core host for FileSystem Controller tests.
/// Uses an in-memory SQLite database for complete isolation.
/// </summary>
public class FileSystemApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"test-filesystem-{Guid.NewGuid():N}";
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
                ["JWT:Key"] = FileSystemTestConstants.JwtKey,
                ["JWT:Issuer"] = FileSystemTestConstants.JwtIssuer,
                ["JWT:Audience"] = FileSystemTestConstants.JwtAudience,
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
                        Encoding.UTF8.GetBytes(FileSystemTestConstants.JwtKey)),
                    ValidateIssuer = true,
                    ValidIssuer = FileSystemTestConstants.JwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = FileSystemTestConstants.JwtAudience,
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

public static class FileSystemTestConstants
{
    public const string JwtKey = "super-secret-test-key-that-is-long-enough-256bits!!";
    public const string JwtIssuer = "ufo-test-issuer";
    public const string JwtAudience = "ufo-test-audience";

    public static readonly Ulid TestUserId = Ulid.NewUlid();
    public static readonly string TestUserName = "testuser";
    public static readonly string TestUserPasswordHash = BCrypt.Net.BCrypt.HashPassword("TestPassword123!");
}

#endregion

#region Functional Tests

public class FileSystemControllerFunctionalTests : IAsyncLifetime
{
    private FileSystemApiFactory _factory = null!;
    private HttpClient _client = null!;
    private SqliteConnection _connection = null!;
    private string _testDir = null!;
    private string _testFile = null!;
    private string _nestedDir = null!;

    public async Task InitializeAsync()
    {
        _factory = new FileSystemApiFactory();
        _client = await _factory.CreateClientAsync();
        _connection = new SqliteConnection(_factory.ConnectionString);
        await _connection.OpenAsync();

        // Register test user
        await RegisterTestUser(FileSystemTestConstants.TestUserId, FileSystemTestConstants.TestUserName, FileSystemTestConstants.TestUserPasswordHash);

        // Setup test directory structure
        _testDir = Path.Combine(Path.GetTempPath(), $"ufo-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);

        _testFile = Path.Combine(_testDir, "test-file.txt");
        File.WriteAllText(_testFile, "Test content");

        _nestedDir = Path.Combine(_testDir, "nested-folder");
        Directory.CreateDirectory(_nestedDir);
        File.WriteAllText(Path.Combine(_nestedDir, "nested-file.txt"), "Nested content");
    }

    public async Task DisposeAsync()
    {
        _connection?.Dispose();
        await _factory.DisposeAsync();

        // Cleanup test directories
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    private async Task RegisterTestUser(Ulid userId, string userName, string passwordHash)
    {
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
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(FileSystemTestConstants.JwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.NameId, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, userName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: FileSystemTestConstants.JwtIssuer,
            audience: FileSystemTestConstants.JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    #region GetFileSystemRoot Tests

    [Fact]
    public async Task GetFileSystemRoot_WithValidToken_ReturnsOk()
    {
        // Arrange
        var token = GenerateToken(FileSystemTestConstants.TestUserId, FileSystemTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        // Act
        var response = await _client.GetAsync("/api/filesystem/root");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.NotNull(content);
        Assert.NotEmpty(content);
    }

    [Fact]
    public async Task GetFileSystemRoot_WithoutAuth_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/filesystem/root");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetFileSystemRoot_ReturnsJsonResponse()
    {
        // Arrange
        var token = GenerateToken(FileSystemTestConstants.TestUserId, FileSystemTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        // Act
        var response = await _client.GetAsync("/api/filesystem/root");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"folder\"", content);
        Assert.Contains("\"roots\"", content);
    }

    #endregion

    #region GetFolderInfo Tests

    [Fact]
    public async Task GetFolderInfo_WithValidPath_ReturnsOk()
    {
        // Arrange
        var token = GenerateToken(FileSystemTestConstants.TestUserId, FileSystemTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var request = new PathRequest { Path = _testDir };

        // Act
        var response = await _client.PostAsJsonAsync("/api/filesystem/folder", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.NotNull(content);
        Assert.NotEmpty(content);
    }

    [Fact]
    public async Task GetFolderInfo_WithValidPath_ReturnsJsonWithFiles()
    {
        // Arrange
        var token = GenerateToken(FileSystemTestConstants.TestUserId, FileSystemTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var request = new PathRequest { Path = _testDir };

        // Act
        var response = await _client.PostAsJsonAsync("/api/filesystem/folder", request);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"files\"", content);
        Assert.Contains("\"childFolders\"", content);
        // Verify the test file is referenced in response
        Assert.Contains("test-file", content);
    }

    [Fact]
    public async Task GetFolderInfo_WithValidPath_ReturnsJsonWithFolders()
    {
        // Arrange
        var token = GenerateToken(FileSystemTestConstants.TestUserId, FileSystemTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var request = new PathRequest { Path = _testDir };

        // Act
        var response = await _client.PostAsJsonAsync("/api/filesystem/folder", request);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Verify nested folder is in response
        Assert.Contains("nested-folder", content);
    }

    [Fact]
    public async Task GetFolderInfo_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var request = new PathRequest { Path = _testDir };

        // Act
        var response = await _client.PostAsJsonAsync("/api/filesystem/folder", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetFolderInfo_WithNonExistentPath_ReturnsNotFoundWithAnExplanation()
    {
        // Arrange
        var token = GenerateToken(FileSystemTestConstants.TestUserId, FileSystemTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var nonExistentPath = Path.Combine(_testDir, "does-not-exist-" + Guid.NewGuid());
        var request = new PathRequest { Path = nonExistentPath };

        // Act
        var response = await _client.PostAsJsonAsync("/api/filesystem/folder", request);

        // Assert
        // This used to escape as an unhandled DirectoryNotFoundException, which reached
        // the user as an error popup with nothing readable in it.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains(nonExistentPath, content);
        Assert.Contains("does not exist", content);
    }

    [Fact]
    public async Task GetFolderInfo_InsideAFolderThatCannotBeTraversed_ReportsThePermissionRatherThanAbsence()
    {
        // Arrange
        var lockedDir = Path.Combine(_testDir, "locked");
        var insideLockedDir = Path.Combine(lockedDir, "inside");
        Directory.CreateDirectory(insideLockedDir);

        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        File.SetUnixFileMode(lockedDir, UnixFileMode.None);

        try
        {
            // Running as root ignores the mode, so there is nothing to assert then.
            if (Directory.Exists(insideLockedDir))
            {
                return;
            }

            var token = GenerateToken(FileSystemTestConstants.TestUserId, FileSystemTestConstants.TestUserName);
            _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

            // Act
            var response = await _client.PostAsJsonAsync(
                "/api/filesystem/folder",
                new PathRequest { Path = insideLockedDir });

            // Assert
            // Directory.Exists answers false here just as it does for a deleted folder, so
            // without the attribute probe this came back as "does not exist".
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("does not have permission", content);
        }
        finally
        {
            File.SetUnixFileMode(
                lockedDir,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public async Task GetFolderInfo_WithAPathThatIsAFile_ReturnsBadRequestWithAnExplanation()
    {
        // Arrange
        var token = GenerateToken(FileSystemTestConstants.TestUserId, FileSystemTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var filePath = Path.Combine(_testDir, "not-a-folder.txt");
        await File.WriteAllTextAsync(filePath, "content");

        var request = new PathRequest { Path = filePath };

        // Act
        var response = await _client.PostAsJsonAsync("/api/filesystem/folder", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("is a file, not a folder", content);
    }

    [Fact]
    public async Task GetFolderInfo_WithBlankPath_ReturnsBadRequestNamingTheField()
    {
        // Arrange
        var token = GenerateToken(FileSystemTestConstants.TestUserId, FileSystemTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var request = new PathRequest { Path = "   " };

        // Act
        var response = await _client.PostAsJsonAsync("/api/filesystem/folder", request);

        // Assert
        // [ApiController] model validation rejects a blank Path before the action runs,
        // so this arrives as a ProblemDetails rather than the controller's own sentence.
        // The client renders either one, so what matters here is that a body arrives at all.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Path", content);
    }

    [Fact]
    public async Task GetFolderInfo_WithNestedFolder_ReturnsNestedContent()
    {
        // Arrange
        var token = GenerateToken(FileSystemTestConstants.TestUserId, FileSystemTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var request = new PathRequest { Path = _nestedDir };

        // Act
        var response = await _client.PostAsJsonAsync("/api/filesystem/folder", request);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(content);
        // Verify nested file is in response
        Assert.Contains("nested-file", content);
    }

    [Fact]
    public async Task GetFolderInfo_WithFileSizes_ReturnsFileSizeInfo()
    {
        // Arrange
        var token = GenerateToken(FileSystemTestConstants.TestUserId, FileSystemTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var request = new PathRequest { Path = _testDir };

        // Act
        var response = await _client.PostAsJsonAsync("/api/filesystem/folder", request);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Verify size information is included
        Assert.Contains("\"size\"", content);
    }

    [Fact]
    public async Task GetFolderInfo_WithCancellationToken_CompletesRequest()
    {
        // Arrange
        var token = GenerateToken(FileSystemTestConstants.TestUserId, FileSystemTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        // Create a directory with many items to test cancellation handling
        var largeDir = Path.Combine(_testDir, "large-dir");
        Directory.CreateDirectory(largeDir);
        for (int i = 0; i < 50; i++)
        {
            File.WriteAllText(Path.Combine(largeDir, $"file-{i}.txt"), $"Content {i}");
        }

        var request = new PathRequest { Path = largeDir };

        // Act
        var response = await _client.PostAsJsonAsync("/api/filesystem/folder", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(content);
    }

    [Fact]
    public async Task GetFolderInfo_WithEmptyDirectory_ReturnsEmptyCollections()
    {
        // Arrange
        var token = GenerateToken(FileSystemTestConstants.TestUserId, FileSystemTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var emptyDir = Path.Combine(_testDir, "empty-folder");
        Directory.CreateDirectory(emptyDir);

        var request = new PathRequest { Path = emptyDir };

        // Act
        var response = await _client.PostAsJsonAsync("/api/filesystem/folder", request);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"files\":[]", content);
        Assert.Contains("\"childFolders\":[]", content);
    }

    #endregion

    #region GetParentFolderInfo Tests

    [Fact]
    public async Task GetParentFolderInfo_WithValidPath_ReturnsOk()
    {
        // Arrange
        var token = GenerateToken(FileSystemTestConstants.TestUserId, FileSystemTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var request = new PathRequest { Path = _nestedDir };

        // Act
        var response = await _client.PostAsJsonAsync("/api/filesystem/parent", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.NotNull(content);
        Assert.NotEmpty(content);
    }

    [Fact]
    public async Task GetParentFolderInfo_WithValidPath_ReturnsParentFolder()
    {
        // Arrange
        var token = GenerateToken(FileSystemTestConstants.TestUserId, FileSystemTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var request = new PathRequest { Path = _nestedDir };

        // Act
        var response = await _client.PostAsJsonAsync("/api/filesystem/parent", request);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(content);
        // Parent folder response should contain folder structure
        Assert.Contains("\"files\"", content);
        Assert.Contains("\"childFolders\"", content);
    }

    [Fact]
    public async Task GetParentFolderInfo_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var request = new PathRequest { Path = _nestedDir };

        // Act
        var response = await _client.PostAsJsonAsync("/api/filesystem/parent", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetParentFolderInfo_WithRootPath_ReturnsNotFound()
    {
        // Arrange
        var token = GenerateToken(FileSystemTestConstants.TestUserId, FileSystemTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        // Use root path which has no parent
        string rootPath = OperatingSystem.IsWindows() ? "C:\\" : "/";
        var request = new PathRequest { Path = rootPath };

        // Act
        var response = await _client.PostAsJsonAsync("/api/filesystem/parent", request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetParentFolderInfo_WithMultipleLevels_ReturnsParentPath()
    {
        // Arrange
        var token = GenerateToken(FileSystemTestConstants.TestUserId, FileSystemTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        // Create deeper nested structure
        var deeperDir = Path.Combine(_nestedDir, "deeper-folder");
        Directory.CreateDirectory(deeperDir);

        var request = new PathRequest { Path = deeperDir };

        // Act
        var response = await _client.PostAsJsonAsync("/api/filesystem/parent", request);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(content);
        // Should return info about nested-folder (the parent)
        Assert.Contains("\"name\"", content);
    }

    [Fact]
    public async Task GetParentFolderInfo_ReturnsChildFolderInfo()
    {
        // Arrange
        var token = GenerateToken(FileSystemTestConstants.TestUserId, FileSystemTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var request = new PathRequest { Path = _nestedDir };

        // Act
        var response = await _client.PostAsJsonAsync("/api/filesystem/parent", request);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Parent folder should include the nested folder as a child
        Assert.Contains("nested-folder", content);
    }

    #endregion

    #region Write Operation Tests

    /// <summary>
    /// Authenticates the shared client. The write tests all need a token, and
    /// adding the same header in each of them is how one of them ends up testing
    /// the unauthenticated path by accident.
    /// </summary>
    private void Authenticate()
    {
        var token = GenerateToken(FileSystemTestConstants.TestUserId, FileSystemTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Remove("Authorization");
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
    }

    [Fact]
    public async Task GetFileSystemRoot_PublishesTheHostsNamingRules()
    {
        Authenticate();

        var response = await _client.GetAsync("/api/filesystem/root");
        var fileSystemRoot = await response.Content.ReadFromJsonAsync<FileSystemRoot>();

        // The client applies these while the user types, so a root response
        // without them leaves the name box guessing.
        Assert.NotNull(fileSystemRoot);
        Assert.Equal(255, fileSystemRoot!.NameRules.MaximumLength);
        Assert.Contains('/', fileSystemRoot.NameRules.InvalidCharacters);
    }

    [Fact]
    public async Task CreateEntry_CreatesAnEmptyFile()
    {
        Authenticate();

        var response = await _client.PostAsJsonAsync(
            "/api/filesystem/create",
            new FileSystemCreateRequest { ParentPath = _testDir, Name = "created.txt", IsFile = true });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var createdPath = Path.Combine(_testDir, "created.txt");
        Assert.True(File.Exists(createdPath));
        Assert.Equal(0, new FileInfo(createdPath).Length);
    }

    [Fact]
    public async Task CreateEntry_CreatesAFolder()
    {
        Authenticate();

        var response = await _client.PostAsJsonAsync(
            "/api/filesystem/create",
            new FileSystemCreateRequest { ParentPath = _testDir, Name = "created-folder", IsFile = false });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(Directory.Exists(Path.Combine(_testDir, "created-folder")));
    }

    [Fact]
    public async Task CreateEntry_RejectsANameThatWouldEscapeTheFolder()
    {
        Authenticate();

        var response = await _client.PostAsJsonAsync(
            "/api/filesystem/create",
            new FileSystemCreateRequest { ParentPath = _nestedDir, Name = "../escaped.txt", IsFile = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(File.Exists(Path.Combine(_testDir, "escaped.txt")));
    }

    [Fact]
    public async Task CreateEntry_ReportsANameAlreadyTakenAsAConflict()
    {
        Authenticate();

        var response = await _client.PostAsJsonAsync(
            "/api/filesystem/create",
            new FileSystemCreateRequest { ParentPath = _testDir, Name = "test-file.txt", IsFile = true });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        // A create is never a truncation of what was already there.
        Assert.Equal("Test content", await File.ReadAllTextAsync(_testFile));
    }

    [Fact]
    public async Task RenameEntry_ChangesTheNameAndKeepsTheContents()
    {
        Authenticate();

        var response = await _client.PostAsJsonAsync(
            "/api/filesystem/rename",
            new FileSystemRenameRequest { Path = _testFile, NewName = "renamed.txt" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(File.Exists(_testFile));
        Assert.Equal("Test content", await File.ReadAllTextAsync(Path.Combine(_testDir, "renamed.txt")));
    }

    [Fact]
    public async Task RenameEntry_ReportsACollisionAsAConflict()
    {
        Authenticate();
        var otherFilePath = Path.Combine(_testDir, "taken.txt");
        await File.WriteAllTextAsync(otherFilePath, "theirs");

        var response = await _client.PostAsJsonAsync(
            "/api/filesystem/rename",
            new FileSystemRenameRequest { Path = _testFile, NewName = "taken.txt" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("theirs", await File.ReadAllTextAsync(otherFilePath));
        Assert.True(File.Exists(_testFile));
    }

    [Fact]
    public async Task RenameEntry_ReportsAnEntryThatIsNoLongerThere()
    {
        Authenticate();

        var response = await _client.PostAsJsonAsync(
            "/api/filesystem/rename",
            new FileSystemRenameRequest { Path = Path.Combine(_testDir, "gone.txt"), NewName = "after.txt" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CopyEntries_LeavesTheSourceWhereItIs()
    {
        Authenticate();

        var response = await _client.PostAsJsonAsync(
            "/api/filesystem/copy",
            new FileSystemTransferRequest { Paths = [_testFile], DestinationFolderPath = _nestedDir });
        var result = await response.Content.ReadFromJsonAsync<FileSystemBatchResult>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, result!.SucceededCount);
        Assert.Empty(result.Failures);
        Assert.True(File.Exists(_testFile));
        Assert.Equal("Test content", await File.ReadAllTextAsync(Path.Combine(_nestedDir, "test-file.txt")));
    }

    [Fact]
    public async Task CopyEntries_ReportsAPartialFailureWithoutFailingTheRequest()
    {
        Authenticate();

        var response = await _client.PostAsJsonAsync(
            "/api/filesystem/copy",
            new FileSystemTransferRequest
            {
                Paths = [Path.Combine(_testDir, "gone.txt"), _testFile],
                DestinationFolderPath = _nestedDir
            });
        var result = await response.Content.ReadFromJsonAsync<FileSystemBatchResult>();

        // One bad path among many is not a bad request: the good ones went
        // through, and the answer has to be able to say both things at once.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, result!.SucceededCount);
        Assert.Equal("gone.txt", Assert.Single(result.Failures).Name);
    }

    [Fact]
    public async Task CopyEntries_ReportsACollisionAsSomethingTheUserCanAnswer()
    {
        Authenticate();
        var destinationFilePath = Path.Combine(_nestedDir, "test-file.txt");
        await File.WriteAllTextAsync(destinationFilePath, "theirs");

        var response = await _client.PostAsJsonAsync(
            "/api/filesystem/copy",
            new FileSystemTransferRequest { Paths = [_testFile], DestinationFolderPath = _nestedDir });
        var result = await response.Content.ReadFromJsonAsync<FileSystemBatchResult>();

        Assert.Equal(0, result!.SucceededCount);
        Assert.True(Assert.Single(result.Failures).IsConflict);
        Assert.Equal("theirs", await File.ReadAllTextAsync(destinationFilePath));
    }

    [Fact]
    public async Task CopyEntries_ReplacesWhatIsThereOnceOverwriteIsAsked()
    {
        Authenticate();
        var destinationFilePath = Path.Combine(_nestedDir, "test-file.txt");
        await File.WriteAllTextAsync(destinationFilePath, "theirs");

        var response = await _client.PostAsJsonAsync(
            "/api/filesystem/copy",
            new FileSystemTransferRequest
            {
                Paths = [_testFile],
                DestinationFolderPath = _nestedDir,
                Overwrite = true
            });
        var result = await response.Content.ReadFromJsonAsync<FileSystemBatchResult>();

        Assert.Equal(1, result!.SucceededCount);
        Assert.Equal("Test content", await File.ReadAllTextAsync(destinationFilePath));
    }

    [Fact]
    public async Task CopyEntries_RejectsARequestWithNothingToCopy()
    {
        Authenticate();

        var response = await _client.PostAsJsonAsync(
            "/api/filesystem/copy",
            new FileSystemTransferRequest { Paths = [], DestinationFolderPath = _nestedDir });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MoveEntries_TakesTheEntryOutOfTheSourceFolder()
    {
        Authenticate();

        var response = await _client.PostAsJsonAsync(
            "/api/filesystem/move",
            new FileSystemTransferRequest { Paths = [_testFile], DestinationFolderPath = _nestedDir });
        var result = await response.Content.ReadFromJsonAsync<FileSystemBatchResult>();

        Assert.Equal(1, result!.SucceededCount);
        Assert.False(File.Exists(_testFile));
        Assert.Equal("Test content", await File.ReadAllTextAsync(Path.Combine(_nestedDir, "test-file.txt")));
    }

    [Fact]
    public async Task MoveEntries_TakesAFolderWithEverythingUnderIt()
    {
        Authenticate();
        var destinationFolder = Path.Combine(_testDir, "destination");
        Directory.CreateDirectory(destinationFolder);

        var response = await _client.PostAsJsonAsync(
            "/api/filesystem/move",
            new FileSystemTransferRequest { Paths = [_nestedDir], DestinationFolderPath = destinationFolder });
        var result = await response.Content.ReadFromJsonAsync<FileSystemBatchResult>();

        Assert.Equal(1, result!.SucceededCount);
        Assert.False(Directory.Exists(_nestedDir));
        Assert.Equal(
            "Nested content",
            await File.ReadAllTextAsync(Path.Combine(destinationFolder, "nested-folder", "nested-file.txt")));
    }

    [Fact]
    public async Task DeleteEntries_RemovesAFileForGood()
    {
        Authenticate();

        var response = await _client.PostAsJsonAsync(
            "/api/filesystem/delete",
            new FileSystemDeleteRequest { Paths = [_testFile] });
        var result = await response.Content.ReadFromJsonAsync<FileSystemBatchResult>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, result!.SucceededCount);
        Assert.False(File.Exists(_testFile));
    }

    [Fact]
    public async Task DeleteEntries_RemovesAFolderWithEverythingUnderIt()
    {
        Authenticate();

        var response = await _client.PostAsJsonAsync(
            "/api/filesystem/delete",
            new FileSystemDeleteRequest { Paths = [_nestedDir] });
        var result = await response.Content.ReadFromJsonAsync<FileSystemBatchResult>();

        Assert.Equal(1, result!.SucceededCount);
        Assert.False(Directory.Exists(_nestedDir));
    }

    [Fact]
    public async Task DeleteEntries_ReportsAnEntryThatIsAlreadyGone()
    {
        Authenticate();

        var response = await _client.PostAsJsonAsync(
            "/api/filesystem/delete",
            new FileSystemDeleteRequest { Paths = [Path.Combine(_testDir, "gone.txt")] });
        var result = await response.Content.ReadFromJsonAsync<FileSystemBatchResult>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, result!.SucceededCount);
        Assert.Single(result.Failures);
    }

    [Fact]
    public async Task DeleteEntries_RejectsARequestWithNothingToDelete()
    {
        Authenticate();

        var response = await _client.PostAsJsonAsync(
            "/api/filesystem/delete",
            new FileSystemDeleteRequest { Paths = [] });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region Authorization and Edge Cases

    [Fact]
    public async Task FileSystemController_AllEndpoints_RequireAuthentication()
    {
        // Test all endpoints without token

        var rootResponse = await _client.GetAsync("/api/filesystem/root");
        Assert.Equal(HttpStatusCode.Unauthorized, rootResponse.StatusCode);

        var folderRequest = new PathRequest { Path = _testDir };
        var folderResponse = await _client.PostAsJsonAsync("/api/filesystem/folder", folderRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, folderResponse.StatusCode);

        var parentRequest = new PathRequest { Path = _nestedDir };
        var parentResponse = await _client.PostAsJsonAsync("/api/filesystem/parent", parentRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, parentResponse.StatusCode);

        // The write endpoints matter most here: an unauthenticated read leaks a
        // listing, an unauthenticated delete takes the files with it.
        var createResponse = await _client.PostAsJsonAsync(
            "/api/filesystem/create",
            new FileSystemCreateRequest { ParentPath = _testDir, Name = "unauthenticated.txt", IsFile = true });
        Assert.Equal(HttpStatusCode.Unauthorized, createResponse.StatusCode);

        var renameResponse = await _client.PostAsJsonAsync(
            "/api/filesystem/rename",
            new FileSystemRenameRequest { Path = _testFile, NewName = "unauthenticated.txt" });
        Assert.Equal(HttpStatusCode.Unauthorized, renameResponse.StatusCode);

        var copyResponse = await _client.PostAsJsonAsync(
            "/api/filesystem/copy",
            new FileSystemTransferRequest { Paths = [_testFile], DestinationFolderPath = _nestedDir });
        Assert.Equal(HttpStatusCode.Unauthorized, copyResponse.StatusCode);

        var moveResponse = await _client.PostAsJsonAsync(
            "/api/filesystem/move",
            new FileSystemTransferRequest { Paths = [_testFile], DestinationFolderPath = _nestedDir });
        Assert.Equal(HttpStatusCode.Unauthorized, moveResponse.StatusCode);

        var deleteResponse = await _client.PostAsJsonAsync(
            "/api/filesystem/delete",
            new FileSystemDeleteRequest { Paths = [_testFile] });
        Assert.Equal(HttpStatusCode.Unauthorized, deleteResponse.StatusCode);

        // Nothing the sweep asked for actually happened.
        Assert.True(File.Exists(_testFile));
        Assert.False(File.Exists(Path.Combine(_testDir, "unauthenticated.txt")));
    }

    [Fact]
    public async Task GetFolderInfo_WithValidToken_AllRequestsSucceed()
    {
        // Arrange
        var token = GenerateToken(FileSystemTestConstants.TestUserId, FileSystemTestConstants.TestUserName);
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        // Act - Test multiple requests with same token
        var response1 = await _client.GetAsync("/api/filesystem/root");
        var response2 = await _client.PostAsJsonAsync("/api/filesystem/folder", new PathRequest { Path = _testDir });
        var response3 = await _client.PostAsJsonAsync("/api/filesystem/parent", new PathRequest { Path = _nestedDir });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response3.StatusCode);
    }

    #endregion
}

#endregion
