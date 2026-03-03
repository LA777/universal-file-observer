using Ufo.Abstractions.Requests;
using Ufo.Abstractions.Responses;

namespace Ufo.Abstractions.Database.Repositories;

public interface ISearchRepository
{
    public Task<SearchResponse> SearchAsync(SearchRequest request, Ulid userId, CancellationToken cancellationToken = default);
}
