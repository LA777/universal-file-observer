using Ufo.Abstractions.Database.Entities;

namespace Ufo.Abstractions.Database.Repositories;

public interface IFileSystemRepository
{
    public Task<int> AddSnapshotAsync(SnapshotEntity snapshotEntity, CancellationToken cancellationToken = default);
    //Task InitiateDatabase();
    public Task DropDataInTables();
    public Task<IEnumerable<FsFileEntity>> GetFilesByNameAndExtensionAsync(string name, string extension, CancellationToken cancellationToken = default);
    public Task<IEnumerable<FsFolderEntity>> GetFoldersByNameAsync(string name, CancellationToken cancellationToken = default);
    public Task<SnapshotEntity> GetLatestSnapshotWithAllEntitiesAsync(CancellationToken cancellationToken = default);
    public Task<SnapshotEntity> GetSnapshotByIdAsync(Ulid snapshotId, CancellationToken cancellationToken = default);
    public Task<IList<SnapshotEntity>> GetAllSnapshotsAsync(CancellationToken cancellationToken = default);
    public Task<DeleteResult> DeleteSnapshotByIdAsync(Ulid snapshotId, CancellationToken cancellationToken = default);
}
