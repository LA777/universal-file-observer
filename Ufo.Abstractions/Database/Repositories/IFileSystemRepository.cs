using Ufo.Abstractions.Database.Entities;

namespace Ufo.Abstractions.Database.Repositories;

public interface IFileSystemRepository
{
    public Task<int> AddSnapshotAsync(SnapshotEntity snapshotEntity, Ulid userId, CancellationToken cancellationToken = default);
    public Task DropDataInTables(CancellationToken cancellationToken = default);
    public Task<IEnumerable<FsFileEntity>> GetFilesByNameAndExtensionAsync(string name, string extension, Ulid userId, CancellationToken cancellationToken = default);
    public Task<IEnumerable<FsFolderEntity>> GetFoldersByNameAsync(string name, Ulid userId, CancellationToken cancellationToken = default);
    public Task<SnapshotEntity> GetLatestSnapshotWithAllEntitiesAsync(Ulid userId, CancellationToken cancellationToken = default);
    public Task<SnapshotEntity> GetSnapshotByIdAsync(Ulid snapshotId, Ulid userId, CancellationToken cancellationToken = default);
    public Task<IList<SnapshotEntity>> GetAllSnapshotsAsync(Ulid userId, CancellationToken cancellationToken = default);
    public Task<DatabaseActionResult> DeleteSnapshotByIdAsync(Ulid snapshotId, Ulid userId, CancellationToken cancellationToken = default);
}
