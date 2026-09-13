using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Ufo.Abstractions.Options;
using Ufo.Server.Extensions;
using Ufo.Server.Services;

namespace Ufo.FunctionalTests.VideoController;

#region Test WebApplication factory

/// <summary>
/// Boots the production host for <c>GET /api/video</c>. No database is set up:
/// the endpoint reads the disk through <see cref="IPathGuard"/> and nothing
/// else, and the bearer middleware validates the token without a lookup.
/// </summary>
/// <remarks>
/// The allow-list is injected rather than configured, for the reason the other
/// functional factories give: <c>UfoHost.Build</c> reads the <c>Ufo</c> section
/// before a test host's <c>ConfigureAppConfiguration</c> is applied. A
/// <c>null</c> root boots the host unrestricted, the way the desktop runs it.
/// </remarks>
public class VideoApiFactory : WebApplicationFactory<Program>
{
    private readonly string? _allowedRoot;

    public VideoApiFactory(string? allowedRoot = null)
    {
        _allowedRoot = allowedRoot;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(HostEnvironmentExtensions.FunctionalTesting);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source=test-video-{Guid.NewGuid():N};Mode=Memory;Cache=Shared",
                ["JWT:Key"] = VideoTestConstants.JwtKey,
                ["JWT:Issuer"] = VideoTestConstants.JwtIssuer,
                ["JWT:Audience"] = VideoTestConstants.JwtAudience,
                ["Kestrel:Endpoints:App:Url"] = "http://localhost:0"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.AddLogging(loggingBuilder => loggingBuilder.SetMinimumLevel(LogLevel.Warning));

            services.RemoveAll<IPathGuard>();
            services.AddSingleton<IPathGuard>(serviceProvider => new PathGuard(
                serviceProvider.GetRequiredService<ILogger<PathGuard>>(),
                Options.Create(new UfoHostOptions
                {
                    AllowedRoots = _allowedRoot is null ? [] : [_allowedRoot]
                })));

            // The host reads JWT options before ConfigureAppConfiguration runs, so
            // the bearer middleware would otherwise still hold the appsettings key.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(VideoTestConstants.JwtKey)),
                    ValidateIssuer = true,
                    ValidIssuer = VideoTestConstants.JwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = VideoTestConstants.JwtAudience,
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
            new AuthenticationHeaderValue("Bearer", VideoJwtTestHelper.GenerateToken(Ulid.NewUlid()));

        return client;
    }

    /// <summary>Returns an HTTP client with NO authorization header.</summary>
    public HttpClient CreateUnauthenticatedClient() => CreateClient();
}

#endregion

#region Constants & helpers

internal static class VideoTestConstants
{
    public const string JwtKey = "super-secret-test-key-that-is-long-enough-256bits!!";
    public const string JwtIssuer = "ufo-test-issuer";
    public const string JwtAudience = "ufo-test-audience";
    public const string ApiBase = "/api/video";

    public static string UrlFor(string filePath) =>
        $"{ApiBase}?filePath={Uri.EscapeDataString(filePath)}";
}

internal static class VideoJwtTestHelper
{
    public static string GenerateToken(Ulid userId, string userName = "testuser", int expiryMinutes = 60)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(VideoTestConstants.JwtKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, userName),
            new Claim("role", "user"),
        };

        var token = new JwtSecurityToken(
            issuer: VideoTestConstants.JwtIssuer,
            audience: VideoTestConstants.JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

/// <summary>
/// A temporary folder of "video" files. The bytes are arbitrary: the endpoint
/// never inspects content, only the extension and the path.
/// </summary>
internal sealed class VideoTestFolder : IDisposable
{
    public VideoTestFolder()
    {
        Root = Path.Combine(Path.GetTempPath(), $"ufo-video-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string WriteFile(string fileName, byte[] content)
    {
        var filePath = Path.Combine(Root, fileName);
        File.WriteAllBytes(filePath, content);
        return filePath;
    }

    public string WriteFile(string fileName, string content = "not really a video") =>
        WriteFile(fileName, Encoding.UTF8.GetBytes(content));

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}

#endregion

#region GET /api/video - unrestricted host

/// <summary>
/// The endpoint as the desktop runs it: no allow-list, so any existing file with
/// a video extension is streamed.
/// </summary>
public class VideoController_GetTests : IDisposable
{
    private readonly VideoApiFactory _factory = new();
    private readonly VideoTestFolder _folder = new();

    public void Dispose()
    {
        _factory.Dispose();
        _folder.Dispose();
    }

    [Fact]
    public async Task GetVideo_WithoutAToken_Returns401()
    {
        var filePath = _folder.WriteFile("clip.mp4");
        using var client = _factory.CreateUnauthenticatedClient();

        var response = await client.GetAsync(VideoTestConstants.UrlFor(filePath));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetVideo_ExistingMp4_Returns200WithTheFileBytes()
    {
        var content = new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 };
        var filePath = _folder.WriteFile("clip.mp4", content);
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync(VideoTestConstants.UrlFor(filePath));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(content, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task GetVideo_AnswersWithRangeSupport()
    {
        // The browser's <video> element seeks by asking for byte ranges; without
        // Accept-Ranges the scrub bar is decorative.
        var filePath = _folder.WriteFile("clip.mp4");
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync(VideoTestConstants.UrlFor(filePath));

        Assert.Contains("bytes", response.Headers.AcceptRanges);
    }

    [Fact]
    public async Task GetVideo_WithARangeHeader_Returns206AndOnlyThatSlice()
    {
        var content = Encoding.ASCII.GetBytes("0123456789");
        var filePath = _folder.WriteFile("clip.mp4", content);
        using var client = _factory.CreateAuthenticatedClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, VideoTestConstants.UrlFor(filePath));
        request.Headers.Range = new RangeHeaderValue(2, 5);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("2345", await response.Content.ReadAsStringAsync());
        Assert.Equal(content.Length, response.Content.Headers.ContentRange?.Length);
    }

    [Theory]
    [InlineData("clip.3gp", "video/3gp2")]
    [InlineData("clip.avi", "video/x-msvideo")]
    [InlineData("clip.mpg", "video/mpeg")]
    [InlineData("clip.mpeg", "video/mpeg")]
    [InlineData("clip.mp4", "video/mp4")]
    [InlineData("clip.m4v", "video/mp4")]
    [InlineData("clip.m4p", "video/mp4")]
    [InlineData("clip.ogv", "video/ogg")]
    [InlineData("clip.ogg", "video/ogg")]
    [InlineData("clip.mov", "video/quicktime")]
    [InlineData("clip.mkv", "video/webm")]
    [InlineData("clip.webm", "video/webm")]
    public async Task GetVideo_AnswersTheContentTypeForTheExtension(string fileName, string expectedContentType)
    {
        var filePath = _folder.WriteFile(fileName);
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync(VideoTestConstants.UrlFor(filePath));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedContentType, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetVideo_MatchesTheExtensionCaseInsensitively()
    {
        var filePath = _folder.WriteFile("CLIP.MP4");
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync(VideoTestConstants.UrlFor(filePath));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("video/mp4", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("photo.jpg")]
    [InlineData("archive.zip")]
    [InlineData("clip")]
    public async Task GetVideo_ExistingFileWithANonVideoExtension_Returns400(string fileName)
    {
        // The file is there and readable; it is the format that is refused, and
        // the caller should learn that rather than get an octet stream.
        var filePath = _folder.WriteFile(fileName);
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync(VideoTestConstants.UrlFor(filePath));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Unsupported video format", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetVideo_MissingFile_Returns404()
    {
        var filePath = Path.Combine(_folder.Root, "does-not-exist.mp4");
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync(VideoTestConstants.UrlFor(filePath));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetVideo_PathThatIsAFolder_Returns404()
    {
        // A folder is not a file, even one whose name ends in a video extension.
        var folderPath = Path.Combine(_folder.Root, "season.mp4");
        Directory.CreateDirectory(folderPath);
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync(VideoTestConstants.UrlFor(folderPath));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetVideo_BlankFilePath_Returns400(string filePath)
    {
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync(VideoTestConstants.UrlFor(filePath));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetVideo_WithoutTheFilePathParameter_Returns400()
    {
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync(VideoTestConstants.ApiBase);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetVideo_RelativeSegmentsInsidePath_AreCanonicalisedBeforeTheLookup()
    {
        var filePath = _folder.WriteFile("clip.mp4");
        var indirectPath = Path.Combine(_folder.Root, "somewhere", "..", "clip.mp4");
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync(VideoTestConstants.UrlFor(indirectPath));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(await File.ReadAllBytesAsync(filePath), await response.Content.ReadAsByteArrayAsync());
    }
}

#endregion

#region GET /api/video - restricted host

/// <summary>
/// The endpoint as a container runs it, with an allow-list in force. Before the
/// guard existed this endpoint read any video-named file on the machine.
/// </summary>
public class VideoController_RestrictedGetTests : IDisposable
{
    private readonly VideoTestFolder _testRoot = new();
    private readonly string _allowedRoot;
    private readonly string _forbiddenRoot;
    private readonly VideoApiFactory _factory;

    public VideoController_RestrictedGetTests()
    {
        _allowedRoot = Path.Combine(_testRoot.Root, "library");
        _forbiddenRoot = Path.Combine(_testRoot.Root, "secrets");
        Directory.CreateDirectory(_allowedRoot);
        Directory.CreateDirectory(_forbiddenRoot);

        _factory = new VideoApiFactory(_allowedRoot);
    }

    public void Dispose()
    {
        _factory.Dispose();
        _testRoot.Dispose();
    }

    private static string WriteFile(string folder, string fileName, string content = "not really a video")
    {
        var filePath = Path.Combine(folder, fileName);
        File.WriteAllText(filePath, content);
        return filePath;
    }

    private static bool TryCreateSymbolicLink(string linkPath, string targetPath, bool isDirectory)
    {
        try
        {
            if (isDirectory)
            {
                Directory.CreateSymbolicLink(linkPath, targetPath);
            }
            else
            {
                File.CreateSymbolicLink(linkPath, targetPath);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    [Fact]
    public async Task GetVideo_InsideTheAllowedRoot_Returns200()
    {
        var filePath = WriteFile(_allowedRoot, "clip.mp4", "allowed");
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync(VideoTestConstants.UrlFor(filePath));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("allowed", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetVideo_InANestedFolderOfTheAllowedRoot_Returns200()
    {
        var nestedFolder = Path.Combine(_allowedRoot, "season 1");
        Directory.CreateDirectory(nestedFolder);
        var filePath = WriteFile(nestedFolder, "episode.mkv");
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync(VideoTestConstants.UrlFor(filePath));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetVideo_OutsideTheAllowedRoot_Returns403()
    {
        var filePath = WriteFile(_forbiddenRoot, "clip.mp4", "secret");
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync(VideoTestConstants.UrlFor(filePath));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetVideo_OutsideTheAllowedRoot_IsRefusedBeforeTheFileIsLookedUp()
    {
        // 403 rather than 404: the guard answers first, so a forbidden path does
        // not reveal whether anything is there.
        var filePath = Path.Combine(_forbiddenRoot, "does-not-exist.mp4");
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync(VideoTestConstants.UrlFor(filePath));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetVideo_ClimbingOutOfTheAllowedRoot_Returns403()
    {
        WriteFile(_forbiddenRoot, "clip.mp4", "secret");
        var escapingPath = Path.Combine(_allowedRoot, "..", "secrets", "clip.mp4");
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync(VideoTestConstants.UrlFor(escapingPath));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetVideo_SiblingFolderSharingTheRootsPrefix_Returns403()
    {
        // "/library" must not admit "/library-secrets": a prefix match on the
        // string is not a containment check on the path.
        var siblingFolder = _allowedRoot + "-secrets";
        Directory.CreateDirectory(siblingFolder);
        var filePath = WriteFile(siblingFolder, "clip.mp4", "secret");
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync(VideoTestConstants.UrlFor(filePath));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetVideo_ThroughAFileLinkPointingOutside_Returns403()
    {
        var targetPath = WriteFile(_forbiddenRoot, "clip.mp4", "secret");
        var linkPath = Path.Combine(_allowedRoot, "innocent.mp4");
        if (!TryCreateSymbolicLink(linkPath, targetPath, isDirectory: false))
        {
            return; // The file system does not do links; nothing to defend against.
        }

        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync(VideoTestConstants.UrlFor(linkPath));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetVideo_ThroughAFolderLinkPointingOutside_Returns403()
    {
        // The link is a component in the middle of the path, not the file itself,
        // which is the case a last-component-only resolution misses.
        WriteFile(_forbiddenRoot, "clip.mp4", "secret");
        var linkPath = Path.Combine(_allowedRoot, "escape");
        if (!TryCreateSymbolicLink(linkPath, _forbiddenRoot, isDirectory: true))
        {
            return;
        }

        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync(VideoTestConstants.UrlFor(Path.Combine(linkPath, "clip.mp4")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetVideo_ThroughALinkStayingInsideTheRoot_Returns200()
    {
        // Links are resolved, not banned: one that lands inside the root is fine.
        var targetPath = WriteFile(_allowedRoot, "clip.mp4", "allowed");
        var linkPath = Path.Combine(_allowedRoot, "alias.mp4");
        if (!TryCreateSymbolicLink(linkPath, targetPath, isDirectory: false))
        {
            return;
        }

        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync(VideoTestConstants.UrlFor(linkPath));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("allowed", await response.Content.ReadAsStringAsync());
    }
}

#endregion
