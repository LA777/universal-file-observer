using Ufo.Abstractions.Database.Entities;

namespace Ufo.Abstractions.Database.Repositories;

public interface ISnapshotRepository
{
    public Task<int> AddSnapshotAsync(SnapshotEntity snapshotEntity, Ulid userId, CancellationToken cancellationToken = default);
    public Task<SnapshotEntity> GetLatestSnapshotWithAllEntitiesAsync(Ulid userId, CancellationToken cancellationToken = default);
    public Task<SnapshotEntity> GetSnapshotByIdAsync(Ulid snapshotId, Ulid userId, CancellationToken cancellationToken = default);
    public Task<IList<SnapshotEntity>> GetAllSnapshotsAsync(Ulid userId, CancellationToken cancellationToken = default);
    public Task<DatabaseActionResult> DeleteSnapshotByIdAsync(Ulid snapshotId, Ulid userId, CancellationToken cancellationToken = default);
}
