using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ufo.Abstractions.Requests;
using Ufo.Extensions;
using Ufo.Server.Attributes;
using Ufo.Server.Services;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[JwtClaimsRequired]
public class SearchController : ControllerBase
{
    private readonly ISearchService _searchService;
    private readonly ILogger<SearchController> _logger;

    public SearchController(ISearchService searchService, ILogger<SearchController> logger)
    {
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpPost]
    public async Task<IActionResult> SearchAsync(SearchRequest searchRequest, CancellationToken cancellationToken)
    {
        _logger.LogInformation("SearchAsync");
        var userId = HttpContext.GetUserIdAsUlid();

        var result = await _searchService.SearchAsync(searchRequest, userId, cancellationToken);
        if (result.Files.Count > 0 && result.Folders.Count > 0)
        {
            return Ok(result);
        }

        return NoContent();
    }
}