using System.Net;
using System.Net.Http.Json;
using Ufo.Abstractions;
using Ufo.Abstractions.DataTransferObjects;
using Ufo.Abstractions.Requests;

namespace Ufo.FunctionalTests.FsItemMarkers;

/// <summary>
/// Covers <c>/api/tags</c>: the vocabulary, putting a tag on and taking it off
/// a path, that a tag can only be one of your own, and the allow-list on both
/// sides of the assignment table.
/// </summary>
public class TagsControllerFunctionalTests : IDisposable
{
    private const string Endpoint = "/api/tags";
    private const string AssignEndpoint = "/api/tags/assign";

    private readonly MarkerTestTree _tree = new();

    public void Dispose() => _tree.Dispose();

    private static CreateTagRequest NewTag(string name, string colorHex = "#ff0000") =>
        new() { Name = name, ColorHex = colorHex };

    private static SetFsItemTagRequest Apply(Ulid tagId, params string[] fullPaths) =>
        new() { TagId = tagId, FullPaths = fullPaths, IsApplied = true };

    private static SetFsItemTagRequest Remove(Ulid tagId, params string[] fullPaths) =>
        new() { TagId = tagId, FullPaths = fullPaths, IsApplied = false };

    private static async Task<TagDto> CreateTagAsync(HttpClient client, string name, string colorHex = "#ff0000")
    {
        var response = await client.PostAsJsonAsync(Endpoint, NewTag(name, colorHex));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<TagDto>())!;
    }

    private static async Task<FsItemTagsDto> GetFsItemTagsAsync(HttpClient client)
    {
        var response = await client.GetAsync(Endpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<FsItemTagsDto>())!;
    }

    private static async Task<ServerResult> ReadServerResultAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ServerResult>())!;

    private async Task InsertAssignmentPastTheGuardAsync(FsItemMarkersApiFactory factory, Ulid tagId, string fullPath) =>
        await factory.ExecuteSqlAsync(
            "INSERT INTO FsItemTags (TagId, FullPath) VALUES (@TagId, @FullPath)",
            ("@TagId", tagId.ToString()),
            ("@FullPath", fullPath));

    #region The vocabulary

    [Fact]
    public async Task GetTags_WhenThereAreNone_ReturnsAnEmptyVocabularyAndNoAssignments()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var fsItemTags = await GetFsItemTagsAsync(client);

        Assert.Empty(fsItemTags.Tags);
        Assert.Empty(fsItemTags.TagIdsByPath);
    }

    [Fact]
    public async Task CreateTag_ReturnsTheTagWithAnIdAndListsItAfterwards()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var created = await CreateTagAsync(client, "Important", "#FF8800");

        Assert.NotEqual(Ulid.Empty, created.Id);
        Assert.Equal("Important", created.Name);
        Assert.Equal("#FF8800", created.ColorHex);

        var listed = Assert.Single((await GetFsItemTagsAsync(client)).Tags);
        Assert.Equal(created.Id, listed.Id);
        Assert.Equal("Important", listed.Name);
        Assert.Equal("#FF8800", listed.ColorHex);
    }

    [Fact]
    public async Task CreateTag_WithANameAlreadyTaken_ReturnsTheExistingTagUnchanged()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var first = await CreateTagAsync(client, "Important", "#ff0000");
        var second = await CreateTagAsync(client, "Important", "#00ff00");

        // The same tag, in its original colour: a name identifies a tag, and a
        // second request for it is a request for the one that exists.
        Assert.Equal(first.Id, second.Id);
        Assert.Equal("#ff0000", second.ColorHex);
        Assert.Single((await GetFsItemTagsAsync(client)).Tags);
    }

    [Fact]
    public async Task CreateTag_TrimsTheName()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var created = await CreateTagAsync(client, "  Holiday  ");

        Assert.Equal("Holiday", created.Name);

        // And the trimmed spelling is what a later request is matched against.
        var again = await CreateTagAsync(client, "Holiday");
        Assert.Equal(created.Id, again.Id);
    }

    [Fact]
    public async Task GetTags_ListsTheVocabularyByName()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        await CreateTagAsync(client, "Work");
        await CreateTagAsync(client, "Archive");
        await CreateTagAsync(client, "Holiday");

        var names = (await GetFsItemTagsAsync(client)).Tags.Select(tag => tag.Name).ToList();

        Assert.Equal(["Archive", "Holiday", "Work"], names);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateTag_WithABlankName_IsRefused(string blankName)
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(Endpoint, NewTag(blankName));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await factory.CountRowsAsync("SELECT COUNT(*) FROM Tags"));
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#fff")]
    [InlineData("#ff00000")]
    [InlineData("#gg0000")]
    [InlineData("ff0000")]
    [InlineData("#ff0000;")]
    [InlineData("")]
    public async Task CreateTag_WithAColourThatIsNotSixHexDigits_IsRefused(string colorHex)
    {
        // The colour is written into a style attribute, so nothing but #rrggbb
        // may get through.
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(Endpoint, NewTag("Important", colorHex));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await factory.CountRowsAsync("SELECT COUNT(*) FROM Tags"));
    }

    [Fact]
    public async Task CreateTag_WithoutABody_IsRefused()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(Endpoint, new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateTag_BeyondTwoHundred_IsRefusedButAnExistingNameStillAnswers()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        for (var tagNumber = 1; tagNumber <= 200; tagNumber++)
        {
            await CreateTagAsync(client, $"Tag {tagNumber:000}");
        }

        var oneTooMany = await client.PostAsJsonAsync(Endpoint, NewTag("Tag 201"));
        Assert.Equal(HttpStatusCode.BadRequest, oneTooMany.StatusCode);

        // Asking again for a name that exists is not another tag, so it is not
        // counted against the limit.
        var existing = await client.PostAsJsonAsync(Endpoint, NewTag("Tag 001"));
        Assert.Equal(HttpStatusCode.OK, existing.StatusCode);

        Assert.Equal(200, await factory.CountRowsAsync("SELECT COUNT(*) FROM Tags"));
    }

    #endregion

    #region Assigning

    [Fact]
    public async Task Assign_ThenGet_ListsTheTagAgainstEveryPathNamed()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var tag = await CreateTagAsync(client, "Important");

        var response = await client.PostAsJsonAsync(AssignEndpoint, Apply(tag.Id, _tree.FirstFile, _tree.Folder));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Result.Success, (await ReadServerResultAsync(response)).Result);

        var fsItemTags = await GetFsItemTagsAsync(client);
        Assert.Equal(2, fsItemTags.TagIdsByPath.Count);
        Assert.Equal([tag.Id], fsItemTags.TagIdsByPath[_tree.FirstFile]);
        Assert.Equal([tag.Id], fsItemTags.TagIdsByPath[_tree.Folder]);
    }

    [Fact]
    public async Task Assign_TwoTagsToOnePath_ListsBoth()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var important = await CreateTagAsync(client, "Important");
        var archive = await CreateTagAsync(client, "Archive", "#0000ff");

        await client.PostAsJsonAsync(AssignEndpoint, Apply(important.Id, _tree.FirstFile));
        await client.PostAsJsonAsync(AssignEndpoint, Apply(archive.Id, _tree.FirstFile));

        var tagIds = (await GetFsItemTagsAsync(client)).TagIdsByPath[_tree.FirstFile];
        Assert.Equal(2, tagIds.Count);
        Assert.Contains(important.Id, tagIds);
        Assert.Contains(archive.Id, tagIds);
    }

    [Fact]
    public async Task Assign_TheSameTagTwice_KeepsOneAssignment()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var tag = await CreateTagAsync(client, "Important");

        await client.PostAsJsonAsync(AssignEndpoint, Apply(tag.Id, _tree.FirstFile));
        var response = await client.PostAsJsonAsync(AssignEndpoint, Apply(tag.Id, _tree.FirstFile));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single((await GetFsItemTagsAsync(client)).TagIdsByPath[_tree.FirstFile]);
        Assert.Equal(1, await factory.CountRowsAsync("SELECT COUNT(*) FROM FsItemTags"));
    }

    [Fact]
    public async Task Remove_TakesOnlyThatTagOffOnlyThePathsNamed()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var important = await CreateTagAsync(client, "Important");
        var archive = await CreateTagAsync(client, "Archive", "#0000ff");

        await client.PostAsJsonAsync(AssignEndpoint, Apply(important.Id, _tree.FirstFile, _tree.SecondFile));
        await client.PostAsJsonAsync(AssignEndpoint, Apply(archive.Id, _tree.FirstFile));

        var response = await client.PostAsJsonAsync(AssignEndpoint, Remove(important.Id, _tree.FirstFile));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var fsItemTags = await GetFsItemTagsAsync(client);
        Assert.Equal([archive.Id], fsItemTags.TagIdsByPath[_tree.FirstFile]);
        Assert.Equal([important.Id], fsItemTags.TagIdsByPath[_tree.SecondFile]);
        // The vocabulary is untouched by taking a tag off a path.
        Assert.Equal(2, fsItemTags.Tags.Count);
    }

    [Fact]
    public async Task Remove_ALastAssignment_DropsThePathFromTheMap()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var tag = await CreateTagAsync(client, "Important");

        await client.PostAsJsonAsync(AssignEndpoint, Apply(tag.Id, _tree.FirstFile));
        await client.PostAsJsonAsync(AssignEndpoint, Remove(tag.Id, _tree.FirstFile));

        Assert.Empty((await GetFsItemTagsAsync(client)).TagIdsByPath);
    }

    [Fact]
    public async Task Assign_StoresThePathAsItWasSent()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var tag = await CreateTagAsync(client, "Important");

        var unresolvedSpelling = Path.Combine(_tree.AllowedRoot, ".", "notes.txt");
        await client.PostAsJsonAsync(AssignEndpoint, Apply(tag.Id, unresolvedSpelling));

        Assert.Equal([unresolvedSpelling], (await GetFsItemTagsAsync(client)).TagIdsByPath.Keys);
    }

    [Fact]
    public async Task Assign_WhenUnrestricted_AcceptsAPathThatDoesNotExist()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var tag = await CreateTagAsync(client, "Important");

        var missingPath = Path.Combine(_tree.AllowedRoot, "not-there.txt");
        var response = await client.PostAsJsonAsync(AssignEndpoint, Apply(tag.Id, missingPath));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([tag.Id], (await GetFsItemTagsAsync(client)).TagIdsByPath[missingPath]);
    }

    #endregion

    #region Assignment validation

    [Fact]
    public async Task Assign_WithATagThatDoesNotExist_IsRefused()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(AssignEndpoint, Apply(Ulid.NewUlid(), _tree.FirstFile));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("not one of your tags", (await ReadServerResultAsync(response)).Message);
    }

    [Fact]
    public async Task Assign_WithAnotherAccountsTag_IsRefused()
    {
        // Otherwise a caller could hang another user's tag on their own files and
        // read its name and colour back.
        using var factory = new FsItemMarkersApiFactory();
        var (owner, _) = await factory.CreateAuthenticatedClientAsync();
        var (intruder, _) = await factory.CreateAuthenticatedClientAsync();
        var ownersTag = await CreateTagAsync(owner, "Private");

        var response = await intruder.PostAsJsonAsync(AssignEndpoint, Apply(ownersTag.Id, _tree.FirstFile));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("not one of your tags", (await ReadServerResultAsync(response)).Message);
        Assert.Equal(0, await factory.CountRowsAsync("SELECT COUNT(*) FROM FsItemTags"));
    }

    [Fact]
    public async Task Remove_WithAnotherAccountsTag_IsRefusedAndChangesNothing()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (owner, _) = await factory.CreateAuthenticatedClientAsync();
        var (intruder, _) = await factory.CreateAuthenticatedClientAsync();
        var ownersTag = await CreateTagAsync(owner, "Private");
        await owner.PostAsJsonAsync(AssignEndpoint, Apply(ownersTag.Id, _tree.FirstFile));

        var response = await intruder.PostAsJsonAsync(AssignEndpoint, Remove(ownersTag.Id, _tree.FirstFile));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([ownersTag.Id], (await GetFsItemTagsAsync(owner)).TagIdsByPath[_tree.FirstFile]);
    }

    [Fact]
    public async Task Assign_WithNoPaths_IsRefused()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var tag = await CreateTagAsync(client, "Important");

        var response = await client.PostAsJsonAsync(AssignEndpoint, Apply(tag.Id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("No files or folders", (await ReadServerResultAsync(response)).Message);
    }

    [Fact]
    public async Task Assign_WithoutTheListAtAll_IsRefused()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var tag = await CreateTagAsync(client, "Important");

        var response = await client.PostAsJsonAsync(AssignEndpoint, new { tagId = tag.Id.ToString(), isApplied = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Assign_WithMoreThanAThousandPaths_IsRefused()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var tag = await CreateTagAsync(client, "Important");

        var tooMany = Enumerable.Range(0, 1001)
            .Select(index => Path.Combine(_tree.AllowedRoot, $"file-{index}.txt"))
            .ToArray();

        var response = await client.PostAsJsonAsync(AssignEndpoint, Apply(tag.Id, tooMany));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("At most 1000", (await ReadServerResultAsync(response)).Message);
        Assert.Equal(0, await factory.CountRowsAsync("SELECT COUNT(*) FROM FsItemTags"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Assign_WithABlankPath_IsRefusedWhetherApplyingOrRemoving(bool isApplied)
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var tag = await CreateTagAsync(client, "Important");

        var response = await client.PostAsJsonAsync(
            AssignEndpoint,
            new SetFsItemTagRequest { TagId = tag.Id, FullPaths = [_tree.FirstFile, "   "], IsApplied = isApplied });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("empty or longer", (await ReadServerResultAsync(response)).Message);
        Assert.Empty((await GetFsItemTagsAsync(client)).TagIdsByPath);
    }

    [Fact]
    public async Task Assign_WithAPathLongerThanTheServerStores_IsRefused()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var tag = await CreateTagAsync(client, "Important");

        var overlongPath = Path.Combine(_tree.AllowedRoot, new string('a', 4097));
        var response = await client.PostAsJsonAsync(AssignEndpoint, Apply(tag.Id, overlongPath));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("empty or longer", (await ReadServerResultAsync(response)).Message);
    }

    #endregion

    #region Allow-list

    [Fact]
    public async Task Assign_WhenRestricted_AcceptsAPathInsideTheAllowedRoot()
    {
        using var factory = new FsItemMarkersApiFactory(_tree.AllowedRoot);
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var tag = await CreateTagAsync(client, "Important");

        var response = await client.PostAsJsonAsync(AssignEndpoint, Apply(tag.Id, _tree.SecondFile));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([tag.Id], (await GetFsItemTagsAsync(client)).TagIdsByPath[_tree.SecondFile]);
    }

    [Fact]
    public async Task Assign_WhenRestricted_RefusesAPathOutsideTheAllowedRoot()
    {
        using var factory = new FsItemMarkersApiFactory(_tree.AllowedRoot);
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var tag = await CreateTagAsync(client, "Important");

        var response = await client.PostAsJsonAsync(AssignEndpoint, Apply(tag.Id, _tree.FirstFile, _tree.OutsideFile));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("not something this server is allowed to open", (await ReadServerResultAsync(response)).Message);
        Assert.Equal(0, await factory.CountRowsAsync("SELECT COUNT(*) FROM FsItemTags"));
    }

    [Fact]
    public async Task GetTags_WhenRestricted_LeavesOutAPathTheServerMayNoLongerOpenButKeepsTheTag()
    {
        using var factory = new FsItemMarkersApiFactory(_tree.AllowedRoot);
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var tag = await CreateTagAsync(client, "Important");

        await InsertAssignmentPastTheGuardAsync(factory, tag.Id, _tree.OutsideFile);
        await client.PostAsJsonAsync(AssignEndpoint, Apply(tag.Id, _tree.FirstFile));

        var fsItemTags = await GetFsItemTagsAsync(client);
        Assert.Equal([_tree.FirstFile], fsItemTags.TagIdsByPath.Keys);
        // The tag itself is not a path and is still the user's to see.
        Assert.Single(fsItemTags.Tags);
        Assert.Equal(2, await factory.CountRowsAsync("SELECT COUNT(*) FROM FsItemTags"));
    }

    [Fact]
    public async Task Remove_WhenRestricted_StillTakesTheTagOffAPathOutsideTheAllowedRoot()
    {
        using var factory = new FsItemMarkersApiFactory(_tree.AllowedRoot);
        var (client, _) = await factory.CreateAuthenticatedClientAsync();
        var tag = await CreateTagAsync(client, "Important");
        await InsertAssignmentPastTheGuardAsync(factory, tag.Id, _tree.OutsideFile);

        var response = await client.PostAsJsonAsync(AssignEndpoint, Remove(tag.Id, _tree.OutsideFile));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, await factory.CountRowsAsync("SELECT COUNT(*) FROM FsItemTags"));
    }

    #endregion

    #region Accounts and authentication

    [Fact]
    public async Task Tags_AreKeptApartBetweenAccounts()
    {
        using var factory = new FsItemMarkersApiFactory();
        var (firstClient, _) = await factory.CreateAuthenticatedClientAsync();
        var (secondClient, _) = await factory.CreateAuthenticatedClientAsync();

        var firstUsersTag = await CreateTagAsync(firstClient, "Important");
        await firstClient.PostAsJsonAsync(AssignEndpoint, Apply(firstUsersTag.Id, _tree.FirstFile));

        var secondUsersView = await GetFsItemTagsAsync(secondClient);
        Assert.Empty(secondUsersView.Tags);
        Assert.Empty(secondUsersView.TagIdsByPath);

        // The same name in another account is that account's own tag.
        var secondUsersTag = await CreateTagAsync(secondClient, "Important", "#0000ff");
        Assert.NotEqual(firstUsersTag.Id, secondUsersTag.Id);
        Assert.Equal("#0000ff", secondUsersTag.ColorHex);
    }

    [Fact]
    public async Task Tags_RequireAuthentication()
    {
        using var factory = new FsItemMarkersApiFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Endpoint)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync(Endpoint, NewTag("Important"))).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync(AssignEndpoint, Apply(Ulid.NewUlid(), _tree.FirstFile))).StatusCode);
    }

    #endregion
}
