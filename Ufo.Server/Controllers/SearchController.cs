using Microsoft.AspNetCore.Mvc;
using Ufo.Abstractions.Requests;
using Ufo.Database.Repositories;

namespace Ufo.Server.Controllers;

[ApiController]
[Route("api/[controller]")]

public class SearchController : ControllerBase
{
    private readonly ILogger<SearchController> _logger;
    private readonly SearchRepository _searchRepository;

    public SearchController(SearchRepository searchRepository, ILogger<SearchController> logger)
    {
        _searchRepository = searchRepository ?? throw new ArgumentNullException(nameof(searchRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpPost]
    public async Task<IActionResult> SearchAsync(SearchRequest searchRequest, CancellationToken cancellationToken)
    {
        _logger.LogInformation("SearchAsync");

        var result = await _searchRepository.SearchAsync(searchRequest, cancellationToken);
        if (result.Files.Count > 0 || result.Folders.Count > 0)
        {
            return Ok(result);
        }

        return NoContent();
    }
}
