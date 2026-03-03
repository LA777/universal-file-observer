using Ufo.Abstractions.Database.Entities;

namespace Ufo.Abstractions.Database.Repositories;

public interface ILabelsRepository
{
    public Task<IList<ServerResult>> AddLabelAsync(LabelEntity labelEntity, Ulid userId, CancellationToken cancellationToken = default);
    public Task<IList<LabelEntity>> GetAllLabelsAsync(Ulid userId, CancellationToken cancellationToken = default);
    public Task<IList<LabelEntity>> GetLabelsBySnapshotIdAsync(Ulid snapshotId, Ulid userId, CancellationToken cancellationToken = default);
    public Task<ServerResult> UpdateLabelAsync(LabelEntity labelEntity, Ulid userId, CancellationToken cancellationToken = default);
    public Task<ServerResult> AddLabelToSnapshotAsync(Ulid labelId, Ulid snapshotId, Ulid userId, CancellationToken cancellationToken = default);
    public Task<ServerResult> RemoveLabelFromSnapshotAsync(Ulid labelId, Ulid snapshotId, Ulid userId, CancellationToken cancellationToken = default);
    public Task<ServerResult> DeleteLabelByIdAsync(Ulid labelId, Ulid userId, CancellationToken cancellationToken = default);
}
