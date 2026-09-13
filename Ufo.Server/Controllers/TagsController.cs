using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ufo.Abstractions;
using Ufo.Abstractions.DataTransferObjects;
using Ufo.Abstractions.Requests;
using Ufo.Extensions;
using Ufo.Server.Attributes;
using Ufo.Server.Services;

namespace Ufo.Server.Controllers;

/// <summary>
/// The user's tags, and which files and folders carry them.
/// </summary>
/// <remarks>
/// A tag is a property of a path, like a flag or a rating - and like them, a
/// snapshot keeps a copy of the tags as they stood when it was taken, written by
/// <see cref="SnapshotController"/> at capture time and never changed after.
/// Unlike them there can be any number per item, so the snapshot copy lives in
/// its own tables rather than a column.
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[JwtClaimsRequired]
public class TagsController : ControllerBase
{
    private readonly ILogger<TagsController> _logger;
    private readonly ITagsService _tagsService;

    public TagsController(ITagsService tagsService, ILogger<TagsController> logger)
    {
        _tagsService = tagsService ?? throw new ArgumentNullException(nameof(tagsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The tag vocabulary and the assignments together - an assignment is a tag
    /// id, and an id on its own cannot be drawn.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(FsItemTagsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFsItemTagsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetFsItemTagsAsync");
        var userId = HttpContext.GetUserIdAsUlid();

        return Ok(await _tagsService.GetFsItemTagsAsync(userId, cancellationToken));
    }

    /// <summary>
    /// Creates a tag. A name that is already taken returns the existing tag
    /// rather than a second one of the same name.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(TagDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateTagAsync(
        [FromBody] CreateTagRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("CreateTagAsync");
        var userId = HttpContext.GetUserIdAsUlid();

        var tag = await _tagsService.CreateTagAsync(request!, userId, cancellationToken);

        return tag is null
            ? BadRequest("A tag needs a name and a colour of the form #rrggbb.")
            : Ok(tag);
    }

    /// <summary>
    /// Puts one tag on, or takes it off, a set of files and folders. Only the
    /// paths named change.
    /// </summary>
    [HttpPost("assign")]
    [ProducesResponseType(typeof(ServerResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetFsItemTagAsync(
        [FromBody] SetFsItemTagRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("SetFsItemTagAsync");
        var userId = HttpContext.GetUserIdAsUlid();

        var serverResult = await _tagsService.SetFsItemTagAsync(request!, userId, cancellationToken);

        return serverResult.Result == Result.Success ? Ok(serverResult) : BadRequest(serverResult);
    }
}
