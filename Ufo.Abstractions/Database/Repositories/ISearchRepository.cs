using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Requests;

namespace Ufo.Abstractions.Database.Repositories;

public interface ISearchRepository
{
    public Task<(List<FsFolderEntity>, List<FsFileEntity>)> SearchAsync(SearchRequest searchRequest, Ulid userId, CancellationToken cancellationToken = default);
}
