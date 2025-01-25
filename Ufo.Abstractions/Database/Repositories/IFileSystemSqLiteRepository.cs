using Ufo.Abstractions.Database.Entities;

namespace Ufo.Abstractions.Database.Repositories
{
    public interface IFileSystemSqLiteRepository
    {
        Task<int> AddDataAsync(SnapshotEntity snapshotEntity, CancellationToken cancellationToken = default);
        Task InitiateDatabase();
        Task DropDataInTables();
        Task<IEnumerable<FsFileEntity>> GetFilesByNameAndExtensionAsync(string name, string extension, CancellationToken cancellationToken = default);
        Task<IEnumerable<FsFolderEntity>> GetFoldersByNameAsync(string name, CancellationToken cancellationToken = default);
        Task<SnapshotEntity> GetLatestSnapshotWithAllEntitiesAsync(CancellationToken cancellationToken = default);
        Task<SnapshotEntity> GetSnapshotByGuidAsync(Guid snapshotGuid, CancellationToken cancellationToken = default);
        Task<IList<SnapshotEntity>> GetSnapshotsAsync(CancellationToken cancellationToken = default);
    }
}
