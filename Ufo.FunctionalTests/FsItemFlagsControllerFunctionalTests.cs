using System.Net;
using System.Net.Http.Json;
using Ufo.Abstractions;
using Ufo.Abstractions.Requests;

namespace Ufo.FunctionalTests.FsItemMarkers;

/// <summary>
/// Covers <c>/api/fsitemflags</c>: what a flag request accepts, what a read
/// gives back, and the allow-list on both sides of the table.
/// </summary>
public class FsItemFlagsControllerFunctionalTests : IDisposable
{
    private const string Endpoint = "/api/fsitemflags";

    private readonly MarkerTestTree _tree = new();

    public void Dispose() => _tree.Dispose();

    private static FsItemFlagsRequest Flag(params string[] fullPaths) =>
        new() { FullPaths = fullPaths, IsFlagEnabled = true };

    private static FsItemFlagsRequest Unflag(params string[] fullPaths) =>
        new() { FullPaths = fullPaths, IsFlagEnabled = false };

    private static async Task<List<string>> GetFlaggedPathsAsync(HttpClient client)
    {
        var response = await client.GetAsync(Endpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<List<string>>())!;
    }

    private static async Task<ServerResult> ReadServerResultAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ServerResult>())!;

    #region Reading and writing

    [Fact]
    public async Task GetFlags_WhenNothingIsFlagged_ReturnsAnEmptyList()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        Assert.Empty(await GetFlaggedPathsAsync(client));
    }

    [Fact]
    public async Task SetFlags_ThenGet_ReturnsEveryPathFlagged()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(Endpoint, Flag(_tree.FirstFile, _tree.Folder));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var serverResult = await ReadServerResultAsync(response);
        Assert.Equal(Result.Success, serverResult.Result);

        var flaggedPaths = await GetFlaggedPathsAsync(client);
        Assert.Equal(2, flaggedPaths.Count);
        Assert.Contains(_tree.FirstFile, flaggedPaths);
        Assert.Contains(_tree.Folder, flaggedPaths);
    }

    [Fact]
    public async Task SetFlags_OnAPathAlreadyFlagged_KeepsOneRow()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        await client.PostAsJsonAsync(Endpoint, Flag(_tree.FirstFile));
        var response = await client.PostAsJsonAsync(Endpoint, Flag(_tree.FirstFile));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(await GetFlaggedPathsAsync(client));
        Assert.Equal(1, await factory.CountRowsAsync("SELECT COUNT(*) FROM FsItemFlags"));
    }

    [Fact]
    public async Task SetFlags_StoresThePathAsItWasSent()
    {
        // The guard authorises but does not decide the key: the listings never
        // resolve anything, so a resolved spelling could never be matched again.
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var unresolvedSpelling = Path.Combine(_tree.AllowedRoot, ".", "notes.txt");
        await client.PostAsJsonAsync(Endpoint, Flag(unresolvedSpelling));

        Assert.Equal([unresolvedSpelling], await GetFlaggedPathsAsync(client));
    }

    [Fact]
    public async Task ClearFlags_RemovesOnlyThePathsNamed()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        await client.PostAsJsonAsync(Endpoint, Flag(_tree.FirstFile, _tree.SecondFile, _tree.Folder));
        var response = await client.PostAsJsonAsync(Endpoint, Unflag(_tree.SecondFile));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var flaggedPaths = await GetFlaggedPathsAsync(client);
        Assert.Equal(2, flaggedPaths.Count);
        Assert.DoesNotContain(_tree.SecondFile, flaggedPaths);
    }

    [Fact]
    public async Task ClearFlags_OnAPathThatWasNeverFlagged_Succeeds()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(Endpoint, Unflag(_tree.FirstFile));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await GetFlaggedPathsAsync(client));
    }

    [Fact]
    public async Task SetFlags_WhenUnrestricted_AcceptsAPathThatDoesNotExist()
    {
        // The desktop browses the whole machine, and a flag is a note about a
        // path rather than a read of it - an unplugged drive is still flaggable.
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var missingPath = Path.Combine(_tree.AllowedRoot, "not-there.txt");
        var response = await client.PostAsJsonAsync(Endpoint, Flag(missingPath));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([missingPath], await GetFlaggedPathsAsync(client));
    }

    #endregion

    #region Validation

    [Fact]
    public async Task SetFlags_WithNoPaths_IsRefused()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(Endpoint, Flag());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var serverResult = await ReadServerResultAsync(response);
        Assert.Equal(Result.Error, serverResult.Result);
        Assert.Contains("No files or folders", serverResult.Message);
    }

    [Fact]
    public async Task SetFlags_WithoutTheListAtAll_IsRefused()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(Endpoint, new { isFlagEnabled = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SetFlags_WithMoreThanAThousandPaths_IsRefused()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var tooMany = Enumerable.Range(0, 1001)
            .Select(index => Path.Combine(_tree.AllowedRoot, $"file-{index}.txt"))
            .ToArray();

        var response = await client.PostAsJsonAsync(Endpoint, Flag(tooMany));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("At most 1000", (await ReadServerResultAsync(response)).Message);
        Assert.Equal(0, await factory.CountRowsAsync("SELECT COUNT(*) FROM FsItemFlags"));
    }

    [Fact]
    public async Task SetFlags_WithExactlyAThousandPaths_IsAccepted()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var justEnough = Enumerable.Range(0, 1000)
            .Select(index => Path.Combine(_tree.AllowedRoot, $"file-{index}.txt"))
            .ToArray();

        var response = await client.PostAsJsonAsync(Endpoint, Flag(justEnough));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1000, await factory.CountRowsAsync("SELECT COUNT(*) FROM FsItemFlags"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SetFlags_WithABlankPath_IsRefusedAndWritesNothing(string blankPath)
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(Endpoint, Flag(_tree.FirstFile, blankPath));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("empty or longer", (await ReadServerResultAsync(response)).Message);
        // Rejected as a whole: the good path beside the bad one is not flagged either.
        Assert.Empty(await GetFlaggedPathsAsync(client));
    }

    [Fact]
    public async Task SetFlags_WithAPathLongerThanTheServerStores_IsRefused()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var overlongPath = Path.Combine(_tree.AllowedRoot, new string('a', 4097));
        var response = await client.PostAsJsonAsync(Endpoint, Flag(overlongPath));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("empty or longer", (await ReadServerResultAsync(response)).Message);
    }

    #endregion

    #region Allow-list

    [Fact]
    public async Task SetFlags_WhenRestricted_AcceptsAPathInsideTheAllowedRoot()
    {
        using var factory = new FsItemMarkersApiFactory(_tree.AllowedRoot);
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(Endpoint, Flag(_tree.SecondFile));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([_tree.SecondFile], await GetFlaggedPathsAsync(client));
    }

    [Fact]
    public async Task SetFlags_WhenRestricted_RefusesAPathOutsideTheAllowedRoot()
    {
        using var factory = new FsItemMarkersApiFactory(_tree.AllowedRoot);
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(Endpoint, Flag(_tree.FirstFile, _tree.OutsideFile));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("not something this server is allowed to open", (await ReadServerResultAsync(response)).Message);
        Assert.Equal(0, await factory.CountRowsAsync("SELECT COUNT(*) FROM FsItemFlags"));
    }

    [Fact]
    public async Task SetFlags_WhenRestricted_RefusesAPathThatClimbsOutOfTheAllowedRoot()
    {
        using var factory = new FsItemMarkersApiFactory(_tree.AllowedRoot);
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var climbingPath = Path.Combine(_tree.AllowedRoot, "..", "elsewhere", "secret.txt");
        var response = await client.PostAsJsonAsync(Endpoint, Flag(climbingPath));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetFlags_WhenRestricted_LeavesOutAPathTheServerMayNoLongerOpen()
    {
        // Written while the server was unrestricted, then the allow-list was
        // tightened: the row is still there, but it must not be handed back.
        using var factory = new FsItemMarkersApiFactory(_tree.AllowedRoot);
        var (client, userId) = await factory.CreateAuthenticatedClientAsync();

        await factory.ExecuteSqlAsync(
            "INSERT INTO FsItemFlags (Id, FullPath, UserId) VALUES (@Id, @FullPath, @UserId)",
            ("@Id", Ulid.NewUlid().ToString()),
            ("@FullPath", _tree.OutsideFile),
            ("@UserId", userId.ToString()));
        await client.PostAsJsonAsync(Endpoint, Flag(_tree.FirstFile));

        Assert.Equal([_tree.FirstFile], await GetFlaggedPathsAsync(client));
        Assert.Equal(2, await factory.CountRowsAsync("SELECT COUNT(*) FROM FsItemFlags"));
    }

    [Fact]
    public async Task ClearFlags_WhenRestricted_StillClearsAPathOutsideTheAllowedRoot()
    {
        // Clearing skips the guard on purpose: refusing would leave the user a
        // flag they can neither see nor remove.
        using var factory = new FsItemMarkersApiFactory(_tree.AllowedRoot);
        var (client, userId) = await factory.CreateAuthenticatedClientAsync();

        await factory.ExecuteSqlAsync(
            "INSERT INTO FsItemFlags (Id, FullPath, UserId) VALUES (@Id, @FullPath, @UserId)",
            ("@Id", Ulid.NewUlid().ToString()),
            ("@FullPath", _tree.OutsideFile),
            ("@UserId", userId.ToString()));

        var response = await client.PostAsJsonAsync(Endpoint, Unflag(_tree.OutsideFile));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, await factory.CountRowsAsync("SELECT COUNT(*) FROM FsItemFlags"));
    }

    #endregion

    #region Accounts and authentication

    [Fact]
    public async Task Flags_AreKeptApartBetweenAccounts()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (firstClient, _) = await factory.CreateAuthenticatedClientAsync();
        var (secondClient, _) = await factory.CreateAuthenticatedClientAsync();

        await firstClient.PostAsJsonAsync(Endpoint, Flag(_tree.FirstFile));

        Assert.Empty(await GetFlaggedPathsAsync(secondClient));

        // Nor can the second account clear what the first one flagged.
        await secondClient.PostAsJsonAsync(Endpoint, Unflag(_tree.FirstFile));

        Assert.Equal([_tree.FirstFile], await GetFlaggedPathsAsync(firstClient));
    }

    [Fact]
    public async Task Flags_RequireAuthentication()
    {
        using var factory = new FsItemMarkersApiFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Endpoint)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync(Endpoint, Flag(_tree.FirstFile))).StatusCode);
    }

    #endregion
}
