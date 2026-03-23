using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.Requests;
using Ufo.Abstractions.Responses;
using Ufo.Server.Mappers;

namespace Ufo.Server.Services;

public interface ISearchService
{
    Task<SearchResponse> SearchAsync(SearchRequest searchRequest, Ulid userId, CancellationToken cancellationToken);
}

public class SearchService : ISearchService
{
    // TODO LA - Cover with Unit tests
    private readonly ISearchRepository _searchRepository;
    private readonly ILogger<SearchService> _logger;

    public SearchService(ISearchRepository searchRepository, ILogger<SearchService> logger)
    {
        _searchRepository = searchRepository ?? throw new ArgumentNullException(nameof(searchRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SearchResponse> SearchAsync(SearchRequest searchRequest, Ulid userId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("SearchAsync - Query: {Query}, UserId: {UserId}", searchRequest.Query, userId);
        var (folderEntities, filesEntities) = await _searchRepository.SearchAsync(searchRequest, userId, cancellationToken);

        var searchResponse = new SearchResponse
        {
            Files = filesEntities.ToDtoList(),          
            Folders = folderEntities.ToDtoList()          
        };

        return searchResponse;
    }
}