using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ufo.Abstractions;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Database.Contexts;

namespace Ufo.Database.Repositories;

public class FileSystemEfCoreRepository : IFileSystemRepository
{
    private readonly ILogger<FileSystemEfCoreRepository> _logger;
    private readonly UfoDbContext _dbContext;

    public FileSystemEfCoreRepository(UfoDbContext dbContext, ILogger<FileSystemEfCoreRepository> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<int> AddSnapshotAsync(SnapshotEntity snapshotEntity, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            _logger.LogInformation($"Insert snapshot: {snapshotEntity.Id}");

            var volumeEntity = snapshotEntity.VolumeInfo!.Volume;
            var storageDriveEntity = volumeEntity!.StorageDrive;
            var pcEntity = storageDriveEntity!.Pcs[0];

            // Find or create PC
            var pcInDb = await _dbContext.Pcs
                .FirstOrDefaultAsync(p => p.Name == pcEntity.Name && p.DeviceId == pcEntity.DeviceId, cancellationToken);

            if (pcInDb == null)
            {
                _logger.LogInformation($"Insert PC: {pcEntity.Id}");
                _dbContext.Pcs.Add(pcEntity);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                pcEntity.Id = pcInDb.Id;
            }

            // Find or create StorageDrive
            var storageDriveInDb = await _dbContext.StorageDrives
                .FirstOrDefaultAsync(sd =>
                    sd.SerialNumber == storageDriveEntity.SerialNumber &&
                    sd.DeviceId == storageDriveEntity.DeviceId &&
                    sd.Name == storageDriveEntity.Name,
                    cancellationToken);

            if (storageDriveInDb == null)
            {
                _logger.LogInformation($"Insert StorageDrive: {storageDriveEntity.Id}");
                _dbContext.StorageDrives.Add(storageDriveEntity);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                storageDriveEntity.Id = storageDriveInDb.Id;
            }

            // Find or create Volume
            var volumeInDb = await _dbContext.Volumes
                .FirstOrDefaultAsync(v => v.VolumeSerialNumber == volumeEntity.VolumeSerialNumber, cancellationToken);

            if (volumeInDb == null)
            {
                _logger.LogInformation($"Insert Volume: {volumeEntity.Id}");
                volumeEntity.StorageDriveId = storageDriveEntity.Id;
                _dbContext.Volumes.Add(volumeEntity);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                volumeEntity.Id = volumeInDb.Id;
                snapshotEntity.VolumeInfo.VolumeId = volumeInDb.Id;
            }

            // Add snapshot
            snapshotEntity.VolumeInfo.Volume = volumeEntity;
            _dbContext.Snapshots.Add(snapshotEntity);
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Add VolumeInfo
            snapshotEntity.VolumeInfo.SnapshotId = snapshotEntity.Id;
            snapshotEntity.VolumeInfo.VolumeId = volumeEntity.Id;
            _dbContext.VolumeInfos.Add(snapshotEntity.VolumeInfo);

            // Bind PC with StorageDrive
            await BindPcWithStorageDriveAndSnapshotAsync(pcEntity, storageDriveEntity, snapshotEntity, cancellationToken);

            // Add folder tree recursively
            await AddFolderWithFilesRecursivelyAsync(snapshotEntity.RootFolder, null, snapshotEntity, cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return 1;
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "ERROR - AddSnapshotAsync");
            throw;
        }
    }

    private async Task AddFolderWithFilesRecursivelyAsync(
        FsFolderEntity folderEntity,
        FsFolderEntity? parentFolderEntity,
        SnapshotEntity snapshotEntity,
        CancellationToken cancellationToken)
    {
        try
        {
            // Check if folder exists in DB
            var folderInDb = await _dbContext.Folders
                .FirstOrDefaultAsync(f =>
                    f.Name == folderEntity.Name &&
                    f.Size == folderEntity.Size &&
                    f.Sha256Hash == folderEntity.Sha256Hash,
                    cancellationToken);

            if (folderInDb == null)
            {
                _logger.LogInformation($"Insert folder: {folderEntity.Id}");
                _dbContext.Folders.Add(folderEntity);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                folderEntity.Id = folderInDb.Id;
            }

            // Bind folder with parent folder and snapshot
            await BindFolderWithFolderAndSnapshotAsync(parentFolderEntity, folderEntity, snapshotEntity, cancellationToken);

            // Add child folders recursively
            foreach (var childFolder in folderEntity.ChildFolders)
            {
                await AddFolderWithFilesRecursivelyAsync(childFolder, folderEntity, snapshotEntity, cancellationToken);
            }

            // Add files
            foreach (var fileEntity in folderEntity.Files)
            {
                var fileInDb = await _dbContext.Files
                    .FirstOrDefaultAsync(f =>
                        f.Name == fileEntity.Name &&
                        f.Size == fileEntity.Size &&
                        f.FileExtension == fileEntity.FileExtension &&
                        f.Sha256Hash == fileEntity.Sha256Hash,
                        cancellationToken);

                if (fileInDb == null)
                {
                    _logger.LogInformation($"Insert file: {fileEntity.Id}");
                    _dbContext.Files.Add(fileEntity);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
                else
                {
                    fileEntity.Id = fileInDb.Id;
                }

                await BindFileWithFolderAndSnapshotAsync(folderEntity, fileEntity, snapshotEntity, cancellationToken);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - AddFolderWithFilesRecursivelyAsync");
            throw;
        }
    }

    private async Task BindFolderWithFolderAndSnapshotAsync(
        FsFolderEntity? parentFolderEntity,
        FsFolderEntity childFolderEntity,
        SnapshotEntity snapshotEntity,
        CancellationToken cancellationToken)
    {
        try
        {
            var parentFolderId = parentFolderEntity?.Id;
            var folderToFolderExists = await _dbContext.FoldersToFolders
                .AnyAsync(f =>
                    f.SnapshotId == snapshotEntity.Id &&
                    f.ParentFolderId == parentFolderId &&
                    f.ChildFolderId == childFolderEntity.Id,
                    cancellationToken);

            if (!folderToFolderExists)
            {
                _dbContext.FoldersToFolders.Add(new FoldersToFoldersEntity
                {
                    ParentFolderId = parentFolderEntity?.Id,
                    ChildFolderId = childFolderEntity.Id,
                    SnapshotId = snapshotEntity.Id
                });
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - BindFolderWithFolderAndSnapshotAsync");
            throw;
        }
    }

    private async Task BindFileWithFolderAndSnapshotAsync(
        FsFolderEntity folderEntity,
        FsFileEntity fileEntity,
        SnapshotEntity snapshotEntity,
        CancellationToken cancellationToken)
    {
        try
        {
            var fileToFolderExists = await _dbContext.FilesToFolders
                .AnyAsync(f =>
                    f.SnapshotId == snapshotEntity.Id &&
                    f.FolderId == folderEntity.Id &&
                    f.FileId == fileEntity.Id,
                    cancellationToken);

            if (!fileToFolderExists)
            {
                _dbContext.FilesToFolders.Add(new FilesToFoldersEntity
                {
                    FolderId = folderEntity.Id,
                    FileId = fileEntity.Id,
                    SnapshotId = snapshotEntity.Id
                });
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - BindFileWithFolderAndSnapshotAsync");
            throw;
        }
    }

    private async Task BindPcWithStorageDriveAndSnapshotAsync(
        PcEntity pcEntity,
        StorageDriveEntity storageDriveEntity,
        SnapshotEntity snapshotEntity,
        CancellationToken cancellationToken)
    {
        try
        {
            var pcToStorageDriveExists = await _dbContext.PcsToStorageDrives
                .AnyAsync(p =>
                    p.SnapshotId == snapshotEntity.Id &&
                    p.PcId == pcEntity.Id &&
                    p.StorageDriveId == storageDriveEntity.Id,
                    cancellationToken);

            if (!pcToStorageDriveExists)
            {
                _dbContext.PcsToStorageDrives.Add(new PcsToStorageDrivesEntity
                {
                    PcId = pcEntity.Id,
                    StorageDriveId = storageDriveEntity.Id,
                    SnapshotId = snapshotEntity.Id
                });
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - BindPcWithStorageDriveAndSnapshotAsync");
            throw;
        }
    }

    public Task DropDataInTables()
    {
        throw new NotImplementedException();
    }

    public Task<IEnumerable<FsFileEntity>> GetFilesByNameAndExtensionAsync(string name, string extension, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<IEnumerable<FsFolderEntity>> GetFoldersByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<SnapshotEntity> GetLatestSnapshotWithAllEntitiesAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public async Task<SnapshotEntity?> GetSnapshotByIdAsync(Ulid snapshotId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Snapshots
            .Include(s => s.VolumeInfo)
                .ThenInclude(vi => vi!.Volume)
                .ThenInclude(v => v!.StorageDrive)
            .Include(s => s.RootFolder)
            .Include(s => s.PcsToStorageDrives)
            .Include(s => s.FoldersToFolders)
            .Include(s => s.FilesToFolders)
            .FirstOrDefaultAsync(s => s.Id == snapshotId, cancellationToken);
    }

    public async Task<IList<SnapshotEntity>> GetAllSnapshotsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshots = await _dbContext.Snapshots
                .Include(s => s.VolumeInfo)
                    .ThenInclude(vi => vi!.Volume)
                    .ThenInclude(v => v!.StorageDrive)
                .Include(s => s.RootFolder)
                .Include(s => s.PcsToStorageDrives)
                .Include(s => s.FoldersToFolders)
                .Include(s => s.FilesToFolders)
                .OrderByDescending(s => s.Timestamp)
                .ToListAsync(cancellationToken);

            return snapshots;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - GetAllSnapshotsAsync");
            throw;
        }
    }

    public async Task<DeleteResult> DeleteSnapshotByIdAsync(Ulid snapshotId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var snapshot = await _dbContext.Snapshots.FindAsync([snapshotId], cancellationToken: cancellationToken);

            if (snapshot == null)
            {
                return DeleteResult.NotFound;
            }

            _logger.LogInformation($"Delete snapshot: {snapshotId}");

            // Get all file and folder links for this snapshot before deleting
            var filesToFoldersForSnapshot = await _dbContext.FilesToFolders
                .Where(f => f.SnapshotId == snapshotId)
                .ToListAsync(cancellationToken);

            var foldersToFoldersForSnapshot = await _dbContext.FoldersToFolders
                .Where(f => f.SnapshotId == snapshotId)
                .ToListAsync(cancellationToken);

            // Delete join entities that reference the snapshot
            _dbContext.FilesToFolders.RemoveRange(filesToFoldersForSnapshot);
            _dbContext.FoldersToFolders.RemoveRange(foldersToFoldersForSnapshot);
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Find and delete files that are only referenced by this snapshot
            var fileIdsInSnapshot = filesToFoldersForSnapshot.Select(f => f.FileId).Distinct().ToList();
            if (fileIdsInSnapshot.Count > 0)
            {
                var filesToDelete = new List<FsFileEntity>();
                foreach (var fileId in fileIdsInSnapshot)
                {
                    var fileHasOtherReferences = await _dbContext.FilesToFolders
                        .AnyAsync(f => f.FileId == fileId && f.SnapshotId != snapshotId, cancellationToken);

                    if (!fileHasOtherReferences)
                    {
                        var file = await _dbContext.Files.FindAsync([fileId], cancellationToken: cancellationToken);
                        if (file != null)
                        {
                            _logger.LogInformation($"Delete file: {fileId}");
                            filesToDelete.Add(file);
                        }
                    }
                }
                _dbContext.Files.RemoveRange(filesToDelete);
            }

            // Find and delete folders that are only referenced by this snapshot
            var folderIdsInSnapshot = foldersToFoldersForSnapshot
                .Select(f => new[] { f.ParentFolderId, f.ChildFolderId })
                .SelectMany(x => x)
                .Where(x => x.HasValue)
                .Select(x => x.Value)
                .Distinct()
                .ToList();

            if (folderIdsInSnapshot.Count > 0)
            {
                var foldersToDelete = new List<FsFolderEntity>();
                foreach (var folderId in folderIdsInSnapshot)
                {
                    var folderHasOtherReferences = await _dbContext.FoldersToFolders
                        .AnyAsync(f => (f.ParentFolderId == folderId || f.ChildFolderId == folderId) && f.SnapshotId != snapshotId, cancellationToken);

                    if (!folderHasOtherReferences)
                    {
                        var folder = await _dbContext.Folders.FindAsync([folderId], cancellationToken: cancellationToken);
                        if (folder != null)
                        {
                            _logger.LogInformation($"Delete folder: {folderId}");
                            foldersToDelete.Add(folder);
                        }
                    }
                }
                _dbContext.Folders.RemoveRange(foldersToDelete);
            }

            // Delete PcsToStorageDrives
            var pcsToStorageDrives = await _dbContext.PcsToStorageDrives
                .Where(p => p.SnapshotId == snapshotId)
                .ToListAsync(cancellationToken);
            _dbContext.PcsToStorageDrives.RemoveRange(pcsToStorageDrives);

            // Delete VolumeInfo
            var volumeInfo = await _dbContext.VolumeInfos
                .Where(vi => vi.SnapshotId == snapshotId)
                .FirstOrDefaultAsync(cancellationToken);
            if (volumeInfo != null)
            {
                _dbContext.VolumeInfos.Remove(volumeInfo);
            }

            // Delete the snapshot
            _dbContext.Snapshots.Remove(snapshot);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation($"Snapshot deleted successfully: {snapshotId}");
            return DeleteResult.Success;
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "ERROR - DeleteSnapshotByIdAsync");
            throw;
        }
    }
}