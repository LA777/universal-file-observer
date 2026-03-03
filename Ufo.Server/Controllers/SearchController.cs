using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.Requests;
using Ufo.Extensions;
using Ufo.Server.Attributes;

namespace Ufo.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[JwtClaimsRequired]
public class SearchController : ControllerBase
{
    private readonly ILogger<SearchController> _logger;
    private readonly ISearchRepository _searchRepository;

    public SearchController(ISearchRepository searchRepository, ILogger<SearchController> logger)
    {
        _searchRepository = searchRepository ?? throw new ArgumentNullException(nameof(searchRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpPost]
    public async Task<IActionResult> SearchAsync(SearchRequest searchRequest, CancellationToken cancellationToken)
    {
        _logger.LogInformation("SearchAsync");
        var userId = HttpContext.GetUserIdAsUlid();

        var result = await _searchRepository.SearchAsync(searchRequest, userId, cancellationToken);
        if (result.Files.Count > 0 || result.Folders.Count > 0)
        {
            return Ok(result);
        }

        return NoContent();
    }
}
