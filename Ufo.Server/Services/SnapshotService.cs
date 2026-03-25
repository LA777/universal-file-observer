using Ufo.Abstractions;
using Ufo.Abstractions.DataTransferObjects;

namespace Ufo.Server.Services;

public interface ISnapshotService
{
    public Task<SnapshotDto> GetLatestSnapshotAsync(CancellationToken cancellationToken);

    public Task<SnapshotDto> GetSnapshotByIdAsync(Ulid snapshotId, CancellationToken cancellationToken);

    public Task<List<SnapshotSummaryDto>> GetAllSnapshotsAsync(CancellationToken cancellationToken);

    public Task<SnapshotSummaryDto> CreateSnapshotAsync(string folderPath, CancellationToken cancellationToken);

    public Task<DatabaseActionResult> DeleteSnapshotByIdAsync(Ulid snapshotId, CancellationToken cancellationToken);
}

public class SnapshotService : ISnapshotService
{
    public Task<SnapshotSummaryDto> CreateSnapshotAsync(string folderPath, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<DatabaseActionResult> DeleteSnapshotByIdAsync(Ulid snapshotId, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<List<SnapshotSummaryDto>> GetAllSnapshotsAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<SnapshotDto> GetLatestSnapshotAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<SnapshotDto> GetSnapshotByIdAsync(Ulid snapshotId, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
