using Ufo.Abstractions.Database.Entities;

namespace Ufo.Abstractions.Database.Repositories;

public interface ILabelsRepository
{
    public Task<int> AddLabelAsync(LabelEntity labelEntity, CancellationToken cancellationToken = default);
    public Task<IList<LabelEntity>> GetAllLabelsAsync(CancellationToken cancellationToken = default);
    public Task<IList<LabelEntity>> GetLabelsBySnapshotIdAsync(Ulid snapshotId, CancellationToken cancellationToken = default);
    public Task<int> UpdateLabelAsync(LabelEntity labelEntity, CancellationToken cancellationToken = default);
    public Task<int> AddLabelToSnapshotAsync(Ulid labelId, Ulid snapshotId, CancellationToken cancellationToken = default);
    public Task<int> RemoveLabelFromSnapshotAsync(Ulid labelId, Ulid snapshotId, CancellationToken cancellationToken = default);
    public Task<int> DeleteLabelByIdAsync(Ulid labelId, CancellationToken cancellationToken = default);
}
