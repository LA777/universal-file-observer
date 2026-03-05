using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Requests;

namespace Ufo.Abstractions.Database.Repositories;

public interface ILabelsRepository
{
    public Task<IList<ServerResult>> AddLabelAsync(LabelRequest label, Ulid userId, CancellationToken cancellationToken = default);
    public Task<IList<LabelEntity>> GetAllLabelsAsync(Ulid userId, CancellationToken cancellationToken = default);
    public Task<IList<LabelEntity>> GetLabelsBySnapshotIdAsync(Ulid snapshotId, Ulid userId, CancellationToken cancellationToken = default);
    public Task<ServerResult> UpdateLabelAsync(LabelRequest label, Ulid userId, CancellationToken cancellationToken = default);
    public Task<ServerResult> AddLabelToSnapshotAsync(Ulid labelId, Ulid snapshotId, Ulid userId, CancellationToken cancellationToken = default);
    public Task<ServerResult> RemoveLabelFromSnapshotAsync(Ulid labelId, Ulid snapshotId, Ulid userId, CancellationToken cancellationToken = default);
    public Task<ServerResult> DeleteLabelByIdAsync(Ulid labelId, Ulid userId, CancellationToken cancellationToken = default);
    public Task<LabelEntity?> GetLabelByNameAsync(string labelName, Ulid userId, CancellationToken cancellationToken = default);
}
