using System.Text.RegularExpressions;
using Ufo.Abstractions;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.DataTransferObjects;
using Ufo.Abstractions.Requests;
using Ufo.Server.Mappers;

namespace Ufo.Server.Services;

public interface ITagsService
{
    /// <summary>
    /// The user's tags and which paths carry them, with anything the server may
    /// no longer read left out.
    /// </summary>
    Task<FsItemTagsDto> GetFsItemTagsAsync(Ulid userId, CancellationToken cancellationToken);

    /// <summary>Creates a tag, or hands back the existing one of that name.</summary>
    Task<TagDto?> CreateTagAsync(CreateTagRequest request, Ulid userId, CancellationToken cancellationToken);

    /// <summary>Puts one tag on, or takes it off, a set of files and folders.</summary>
    Task<ServerResult> SetFsItemTagAsync(
        SetFsItemTagRequest request,
        Ulid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The tags on each path, for the snapshot walk to freeze as it goes.
    /// </summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<TagEntity>>> GetTagsByPathAsync(
        Ulid userId,
        CancellationToken cancellationToken);
}

public partial class TagsService : ITagsService
{
    /// <summary>
    /// A CSS hex colour and nothing else.
    /// </summary>
    /// <remarks>
    /// Checked because this string is written straight into a style attribute.
    /// Anything that is not six hex digits behind a hash has no business being
    /// there, whatever it might otherwise do once the browser reads it.
    /// </remarks>
    [GeneratedRegex("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorHexPattern { get; }

    /// <summary>As many paths as one request may name - enough for a whole listing.</summary>
    private const int MaximumPathsPerRequest = 1000;

    /// <summary>Longest path a row will hold; the column is plain TEXT.</summary>
    private const int MaximumPathLength = 4096;

    /// <summary>As many tags as one user may define. Generous, and finite.</summary>
    private const int MaximumTagsPerUser = 200;

    private readonly ITagsRepository _tagsRepository;
    private readonly IPathGuard _pathGuard;
    private readonly ILogger<TagsService> _logger;

    public TagsService(ITagsRepository tagsRepository, IPathGuard pathGuard, ILogger<TagsService> logger)
    {
        _tagsRepository = tagsRepository ?? throw new ArgumentNullException(nameof(tagsRepository));
        _pathGuard = pathGuard ?? throw new ArgumentNullException(nameof(pathGuard));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<FsItemTagsDto> GetFsItemTagsAsync(Ulid userId, CancellationToken cancellationToken)
    {
        var tags = await _tagsRepository.GetTagsAsync(userId, cancellationToken);
        var assignments = await _tagsRepository.GetFsItemTagsAsync(userId, cancellationToken);

        var result = new FsItemTagsDto { Tags = tags.ToDtos() };

        foreach (var assignment in assignments)
        {
            // Re-checked on the way out as well as the way in: the allow-list is
            // configuration and can be tightened between sessions, and a tag put
            // on a path while the server was unrestricted must not name one it
            // may no longer open.
            if (!_pathGuard.TryResolveQuietly(assignment.FullPath, out _))
            {
                continue;
            }

            if (!result.TagIdsByPath.TryGetValue(assignment.FullPath, out var tagIds))
            {
                tagIds = [];
                result.TagIdsByPath[assignment.FullPath] = tagIds;
            }

            tagIds.Add(assignment.TagId);
        }

        return result;
    }

    public async Task<TagDto?> CreateTagAsync(
        CreateTagRequest request,
        Ulid userId,
        CancellationToken cancellationToken)
    {
        var name = request?.Name?.Trim() ?? string.Empty;

        if (name.Length == 0)
        {
            return null;
        }

        if (!ColorHexPattern.IsMatch(request!.ColorHex ?? string.Empty))
        {
            return null;
        }

        var existingTags = await _tagsRepository.GetTagsAsync(userId, cancellationToken);

        // Only a new name counts against the limit; asking again for one that
        // exists is not another tag.
        if (existingTags.Count >= MaximumTagsPerUser
            && !existingTags.Any(tag => string.Equals(tag.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var storedTag = await _tagsRepository.GetOrCreateTagAsync(
            new TagEntity { Name = name, ColorHex = request.ColorHex!, UserId = userId },
            cancellationToken);

        _logger.LogInformation("CreateTagAsync - UserId: {UserId}, Name: {Name}", userId, name);

        return storedTag.ToDto();
    }

    public async Task<ServerResult> SetFsItemTagAsync(
        SetFsItemTagRequest request,
        Ulid userId,
        CancellationToken cancellationToken)
    {
        if (request?.FullPaths is not { Count: > 0 })
        {
            return Rejected("No files or folders were given.");
        }

        if (request.FullPaths.Count > MaximumPathsPerRequest)
        {
            return Rejected($"At most {MaximumPathsPerRequest} items can be tagged in one go.");
        }

        // The tag has to be one of this user's, or a caller could hang another
        // user's tag on their own files and read its name and colour back.
        var tags = await _tagsRepository.GetTagsAsync(userId, cancellationToken);

        if (tags.All(tag => tag.Id != request.TagId))
        {
            return Rejected("That is not one of your tags.");
        }

        var pathsToWrite = new List<string>();

        foreach (var requestedPath in request.FullPaths)
        {
            if (string.IsNullOrWhiteSpace(requestedPath) || requestedPath.Length > MaximumPathLength)
            {
                return Rejected("A path was empty or longer than this server will store.");
            }

            if (!request.IsApplied)
            {
                // Taking a tag off skips the guard, as clearing a flag or a
                // rating does: a path that has left the allow-list still has
                // rows, and refusing would leave tags that cannot be removed.
                pathsToWrite.Add(requestedPath);

                if (_pathGuard.TryResolveQuietly(requestedPath, out var resolvedForRemoval)
                    && resolvedForRemoval != requestedPath)
                {
                    pathsToWrite.Add(resolvedForRemoval);
                }

                continue;
            }

            // The guard authorises; it does not decide the key. Its answer is
            // trimmed and link-resolved, and nothing else in the application
            // resolves anything - a row keyed on it could never be found again.
            if (!_pathGuard.TryResolve(requestedPath, out _))
            {
                return Rejected($"'{requestedPath}' is not something this server is allowed to open.");
            }

            pathsToWrite.Add(requestedPath);
        }

        return await _tagsRepository.SetFsItemTagAsync(
            request.TagId,
            pathsToWrite,
            request.IsApplied,
            cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<TagEntity>>> GetTagsByPathAsync(
        Ulid userId,
        CancellationToken cancellationToken)
    {
        var tags = await _tagsRepository.GetTagsAsync(userId, cancellationToken);
        var assignments = await _tagsRepository.GetFsItemTagsAsync(userId, cancellationToken);

        var tagsById = tags.ToDictionary(tag => tag.Id);
        var byPath = new Dictionary<string, List<TagEntity>>(PathComparer);

        foreach (var assignment in assignments)
        {
            if (!tagsById.TryGetValue(assignment.TagId, out var tag))
            {
                continue;
            }

            if (!byPath.TryGetValue(assignment.FullPath, out var tagsForPath))
            {
                tagsForPath = [];
                byPath[assignment.FullPath] = tagsForPath;
            }

            tagsForPath.Add(tag);
        }

        return byPath.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<TagEntity>)entry.Value,
            PathComparer);
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    private static ServerResult Rejected(string message) =>
        new()
        {
            ActionName = "Setting Tags.",
            Result = Result.Error,
            Priority = ActionPriority.Highest,
            Message = message
        };
}
