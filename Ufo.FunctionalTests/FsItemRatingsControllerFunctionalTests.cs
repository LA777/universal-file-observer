using System.Net;
using System.Net.Http.Json;
using Ufo.Abstractions;
using Ufo.Abstractions.Requests;

namespace Ufo.FunctionalTests.FsItemMarkers;

/// <summary>
/// Covers <c>/api/fsitemratings</c>: the 0 to 10 scale, zero as clearing, and
/// the allow-list on both sides of the table.
/// </summary>
public class FsItemRatingsControllerFunctionalTests : IDisposable
{
    private const string Endpoint = "/api/fsitemratings";

    private readonly MarkerTestTree _tree = new();

    public void Dispose() => _tree.Dispose();

    private static FsItemRatingsRequest Rate(int rating, params string[] fullPaths) =>
        new() { FullPaths = fullPaths, Rating = rating };

    private static async Task<Dictionary<string, int>> GetRatingsAsync(HttpClient client)
    {
        var response = await client.GetAsync(Endpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<Dictionary<string, int>>())!;
    }

    private static async Task<ServerResult> ReadServerResultAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ServerResult>())!;

    #region Reading and writing

    [Fact]
    public async Task GetRatings_WhenNothingIsRated_ReturnsAnEmptyMap()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        Assert.Empty(await GetRatingsAsync(client));
    }

    [Fact]
    public async Task SetRatings_ThenGet_ReturnsTheRatingByPath()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(Endpoint, Rate(7, _tree.FirstFile, _tree.Folder));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Result.Success, (await ReadServerResultAsync(response)).Result);

        var ratings = await GetRatingsAsync(client);
        Assert.Equal(2, ratings.Count);
        Assert.Equal(7, ratings[_tree.FirstFile]);
        Assert.Equal(7, ratings[_tree.Folder]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public async Task SetRatings_AcceptsBothEndsOfTheScale(int rating)
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(Endpoint, Rate(rating, _tree.FirstFile));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(rating, (await GetRatingsAsync(client))[_tree.FirstFile]);
    }

    [Fact]
    public async Task SetRatings_Again_ReplacesTheEarlierRating()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        await client.PostAsJsonAsync(Endpoint, Rate(8, _tree.FirstFile));
        var response = await client.PostAsJsonAsync(Endpoint, Rate(3, _tree.FirstFile));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, (await GetRatingsAsync(client))[_tree.FirstFile]);
        Assert.Equal(1, await factory.CountRowsAsync("SELECT COUNT(*) FROM FsItemRatings"));
    }

    [Fact]
    public async Task SetRatings_WithZero_ClearsTheRatingAndLeavesTheOthersAlone()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        await client.PostAsJsonAsync(Endpoint, Rate(5, _tree.FirstFile, _tree.SecondFile));
        var response = await client.PostAsJsonAsync(Endpoint, Rate(0, _tree.FirstFile));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var ratings = await GetRatingsAsync(client);
        Assert.Single(ratings);
        Assert.Equal(5, ratings[_tree.SecondFile]);
        // Unrated is no row, not a row holding zero.
        Assert.Equal(1, await factory.CountRowsAsync("SELECT COUNT(*) FROM FsItemRatings"));
    }

    [Fact]
    public async Task SetRatings_WithZero_OnAPathThatWasNeverRated_Succeeds()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(Endpoint, Rate(0, _tree.FirstFile));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await GetRatingsAsync(client));
    }

    [Fact]
    public async Task SetRatings_StoresThePathAsItWasSent()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var unresolvedSpelling = Path.Combine(_tree.AllowedRoot, ".", "notes.txt");
        await client.PostAsJsonAsync(Endpoint, Rate(4, unresolvedSpelling));

        var ratings = await GetRatingsAsync(client);
        Assert.Equal(4, ratings[unresolvedSpelling]);
    }

    [Fact]
    public async Task SetRatings_WhenUnrestricted_AcceptsAPathThatDoesNotExist()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var missingPath = Path.Combine(_tree.AllowedRoot, "not-there.txt");
        var response = await client.PostAsJsonAsync(Endpoint, Rate(2, missingPath));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await GetRatingsAsync(client))[missingPath]);
    }

    #endregion

    #region Validation

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public async Task SetRatings_OffTheScale_IsRefused(int rating)
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(Endpoint, Rate(rating, _tree.FirstFile));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await factory.CountRowsAsync("SELECT COUNT(*) FROM FsItemRatings"));
    }

    [Fact]
    public async Task SetRatings_WithNoPaths_IsRefused()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(Endpoint, Rate(5));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var serverResult = await ReadServerResultAsync(response);
        Assert.Equal(Result.Error, serverResult.Result);
        Assert.Contains("No files or folders", serverResult.Message);
    }

    [Fact]
    public async Task SetRatings_WithoutTheListAtAll_IsRefused()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(Endpoint, new { rating = 5 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SetRatings_WithMoreThanAThousandPaths_IsRefused()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var tooMany = Enumerable.Range(0, 1001)
            .Select(index => Path.Combine(_tree.AllowedRoot, $"file-{index}.txt"))
            .ToArray();

        var response = await client.PostAsJsonAsync(Endpoint, Rate(5, tooMany));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("At most 1000", (await ReadServerResultAsync(response)).Message);
        Assert.Equal(0, await factory.CountRowsAsync("SELECT COUNT(*) FROM FsItemRatings"));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(0)]
    public async Task SetRatings_WithABlankPath_IsRefusedWhetherRatingOrClearing(int rating)
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(Endpoint, Rate(rating, _tree.FirstFile, "   "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("empty or longer", (await ReadServerResultAsync(response)).Message);
        Assert.Empty(await GetRatingsAsync(client));
    }

    [Fact]
    public async Task SetRatings_WithAPathLongerThanTheServerStores_IsRefused()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var overlongPath = Path.Combine(_tree.AllowedRoot, new string('a', 4097));
        var response = await client.PostAsJsonAsync(Endpoint, Rate(5, overlongPath));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("empty or longer", (await ReadServerResultAsync(response)).Message);
    }

    #endregion

    #region Allow-list

    [Fact]
    public async Task SetRatings_WhenRestricted_AcceptsAPathInsideTheAllowedRoot()
    {
        using var factory = new FsItemMarkersApiFactory(_tree.AllowedRoot);
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(Endpoint, Rate(9, _tree.SecondFile));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(9, (await GetRatingsAsync(client))[_tree.SecondFile]);
    }

    [Fact]
    public async Task SetRatings_WhenRestricted_RefusesAPathOutsideTheAllowedRoot()
    {
        using var factory = new FsItemMarkersApiFactory(_tree.AllowedRoot);
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(Endpoint, Rate(9, _tree.FirstFile, _tree.OutsideFile));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("not something this server is allowed to open", (await ReadServerResultAsync(response)).Message);
        Assert.Equal(0, await factory.CountRowsAsync("SELECT COUNT(*) FROM FsItemRatings"));
    }

    [Fact]
    public async Task GetRatings_WhenRestricted_LeavesOutAPathTheServerMayNoLongerOpen()
    {
        using var factory = new FsItemMarkersApiFactory(_tree.AllowedRoot);
        var (client, userId) = await factory.CreateAuthenticatedClientAsync();

        await factory.ExecuteSqlAsync(
            "INSERT INTO FsItemRatings (Id, FullPath, Rating, UserId) VALUES (@Id, @FullPath, @Rating, @UserId)",
            ("@Id", Ulid.NewUlid().ToString()),
            ("@FullPath", _tree.OutsideFile),
            ("@Rating", 8),
            ("@UserId", userId.ToString()));
        await client.PostAsJsonAsync(Endpoint, Rate(6, _tree.FirstFile));

        var ratings = await GetRatingsAsync(client);
        Assert.Single(ratings);
        Assert.Equal(6, ratings[_tree.FirstFile]);
        Assert.Equal(2, await factory.CountRowsAsync("SELECT COUNT(*) FROM FsItemRatings"));
    }

    [Fact]
    public async Task SetRatings_WithZero_WhenRestricted_StillClearsAPathOutsideTheAllowedRoot()
    {
        using var factory = new FsItemMarkersApiFactory(_tree.AllowedRoot);
        var (client, userId) = await factory.CreateAuthenticatedClientAsync();

        await factory.ExecuteSqlAsync(
            "INSERT INTO FsItemRatings (Id, FullPath, Rating, UserId) VALUES (@Id, @FullPath, @Rating, @UserId)",
            ("@Id", Ulid.NewUlid().ToString()),
            ("@FullPath", _tree.OutsideFile),
            ("@Rating", 8),
            ("@UserId", userId.ToString()));

        var response = await client.PostAsJsonAsync(Endpoint, Rate(0, _tree.OutsideFile));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, await factory.CountRowsAsync("SELECT COUNT(*) FROM FsItemRatings"));
    }

    #endregion

    #region Accounts and authentication

    [Fact]
    public async Task Ratings_AreKeptApartBetweenAccounts()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (firstClient, _) = await factory.CreateAuthenticatedClientAsync();
        var (secondClient, _) = await factory.CreateAuthenticatedClientAsync();

        await firstClient.PostAsJsonAsync(Endpoint, Rate(7, _tree.FirstFile));

        Assert.Empty(await GetRatingsAsync(secondClient));

        // The second account rating the same path neither sees nor changes the first's.
        await secondClient.PostAsJsonAsync(Endpoint, Rate(2, _tree.FirstFile));

        Assert.Equal(7, (await GetRatingsAsync(firstClient))[_tree.FirstFile]);
        Assert.Equal(2, (await GetRatingsAsync(secondClient))[_tree.FirstFile]);
    }

    [Fact]
    public async Task Ratings_RequireAuthentication()
    {
        using var factory = new FsItemMarkersApiFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Endpoint)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync(Endpoint, Rate(5, _tree.FirstFile))).StatusCode);
    }

    #endregion
}
