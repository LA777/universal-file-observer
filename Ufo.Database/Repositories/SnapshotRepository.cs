using Dapper;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Data.Common;
using Ufo.Abstractions;
using Ufo.Abstractions.Database;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;

namespace Ufo.Database.Repositories;

public class SnapshotRepository : ISnapshotRepository
{
    private readonly ILogger<SnapshotRepository> _logger;
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public SnapshotRepository(IDbConnectionFactory dbConnectionFactory, ILogger<SnapshotRepository>? logger)
    {
        _dbConnectionFactory = dbConnectionFactory ?? throw new ArgumentNullException(nameof(dbConnectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SnapshotEntity?> GetSnapshotByIdAsync(Ulid snapshotId, Ulid userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetSnapshotByIdAsync - SnapshotId: {SnapshotId}, UserId: {UserId}", snapshotId, userId);
        try
        {
            SnapshotEntity? snapshotResult = null;

            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);
            await sqLiteConnection
            .QueryAsync<SnapshotEntity, VolumeInfoEntity, VolumeEntity, StorageDriveEntity, PcsToStorageDrivesEntity, PcEntity, SnapshotEntity>(
                SqlScripts.SelectSnapshotByIdSql,
                (snapshotEntity, volumeInfoEntity, volumeEntity, storageDriveEntity, _, pcEntity) =>
                {
                    if (snapshotResult == null)
                    {
                        snapshotResult = snapshotEntity;

                        if (volumeInfoEntity != null && volumeEntity != null && storageDriveEntity != null)
                        {
                            snapshotResult.VolumeInfo = volumeInfoEntity;
                            volumeInfoEntity.Snapshot = snapshotResult;
                            volumeInfoEntity.SnapshotId = snapshotResult.Id;
                            volumeInfoEntity.Volume = volumeEntity;
                            volumeInfoEntity.VolumeId = volumeEntity.Id;
                            volumeEntity.VolumeInfos.Add(volumeInfoEntity);
                            volumeEntity.StorageDrive = storageDriveEntity;
                            volumeEntity.StorageDriveId = storageDriveEntity.Id;
                            storageDriveEntity.Snapshots.Add(snapshotResult);
                            storageDriveEntity.Volumes.Add(volumeEntity);
                            if (pcEntity != null)
                            {
                                storageDriveEntity.Pcs.Add(pcEntity);
                                pcEntity.Snapshots.Add(snapshotResult);
                                pcEntity.StorageDrives.Add(storageDriveEntity);
                            }
                        }
                    }

                    return snapshotResult;
                },
                splitOn: "Id, Id, Id, PcId, Id",
                param: new { SnapshotId = snapshotId, UserId = userId });

            if (snapshotResult == null)
            {
                return null;
            }

            FolderEntity? currentFolder = null;
            var folders = new Dictionary<Ulid, FolderEntity>();
            var childFolders = new Dictionary<Ulid, IList<FolderEntity>>();
            var processedFolderIds = new HashSet<Ulid>(); // Track folders already processed for relationships

            await sqLiteConnection
                .QueryAsync<FolderEntity, FoldersToFoldersEntity, FilesToFoldersEntity, FileEntity, FolderEntity>(
                    SqlScripts.SelectFoldersAndFilesBySnapshotSql,
                    (fsFolderEntity, foldersToFoldersEntity, filesToFoldersEntity, fsFileEntity) =>
                    {
                        folders.TryAdd(fsFolderEntity.Id, fsFolderEntity);

                        // check if Folder already added
                        if (snapshotResult.RootFolder == null)
                        {
                            if (foldersToFoldersEntity.ParentFolderId == null)
                            {
                                snapshotResult.RootFolder = fsFolderEntity;
                                currentFolder = fsFolderEntity;
                                processedFolderIds.Add(fsFolderEntity.Id);
                            }
                            else
                            {
                                throw new ApplicationException("Error!");
                            }
                        }

                        var currentFolderParentFolder = currentFolder?.ParentFolders.FirstOrDefault();
                        var currentFolderParentFolderId = currentFolderParentFolder?.Id;

                        // Only process folder relationships once per unique folder
                        if (!processedFolderIds.Contains(fsFolderEntity.Id) &&
                            (currentFolder!.Id != fsFolderEntity.Id || currentFolderParentFolderId != foldersToFoldersEntity.ParentFolderId))
                        {
                            processedFolderIds.Add(fsFolderEntity.Id);

                            // find ParentFolder
                            var parentFolderWasFound = folders.TryGetValue(foldersToFoldersEntity.ParentFolderId!.Value, out var parentFolder);
                            if (parentFolderWasFound)
                            {
                                // Only add if not already added
                                if (!parentFolder!.ChildFolders.Contains(fsFolderEntity))
                                {
                                    parentFolder.ChildFolders.Add(fsFolderEntity);
                                    fsFolderEntity.ParentFolders.Add(parentFolder);
                                }
                            }
                            else
                            {
                                var childFolderWasFound1 = childFolders.TryGetValue(foldersToFoldersEntity.ParentFolderId.Value, out var childFolderList);
                                if (childFolderWasFound1)
                                {
                                    if (!childFolderList!.Contains(fsFolderEntity))
                                    {
                                        childFolderList.Add(fsFolderEntity);
                                    }
                                }
                                else
                                {
                                    childFolders.Add(foldersToFoldersEntity.ParentFolderId.Value, new List<FolderEntity> { fsFolderEntity });
                                }
                            }

                            // find ChildFolders
                            var childFoldersWasFound = childFolders.TryGetValue(fsFolderEntity.Id, out var childFoldersList);
                            if (childFoldersWasFound)
                            {
                                foreach (var childFolder in childFoldersList!)
                                {
                                    if (!childFolder.ParentFolders.Contains(fsFolderEntity))
                                    {
                                        childFolder.ParentFolders.Add(fsFolderEntity);
                                        fsFolderEntity.ChildFolders.Add(childFolder);
                                    }
                                }

                                childFolders.Remove(fsFolderEntity.Id);
                            }

                            currentFolder = fsFolderEntity;
                        }

                        if (fsFileEntity != null)
                        {
                            // Only add file if not already added
                            if (!currentFolder!.Files.Contains(fsFileEntity))
                            {
                                fsFileEntity.Snapshots.Add(snapshotResult);
                                fsFileEntity.ParentFolders.Add(currentFolder);
                                currentFolder.Files.Add(fsFileEntity);
                            }
                        }

                        return currentFolder;
                    },
                    param: new { SnapshotId = snapshotId, UserId = userId },
                    splitOn: "SnapshotId, SnapshotId, Id");

            // Sort folders and files by name for consistent ordering
            SortFoldersAndFilesRecursively(snapshotResult.RootFolder!);

            return snapshotResult;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - GetSnapshotByIdAsync");
            throw;
        }
    }

    public async Task<SnapshotEntity?> GetLatestSnapshotWithAllEntitiesAsync(Ulid userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetLatestSnapshotWithAllEntitiesAsync - UserId: {UserId}", userId);
        try
        {
            SnapshotEntity? snapshotResult = null;

            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);

            await sqLiteConnection
            .QueryAsync<SnapshotEntity, VolumeInfoEntity, VolumeEntity, StorageDriveEntity, PcsToStorageDrivesEntity, PcEntity, SnapshotEntity>(
                SqlScripts.SelectLatestSnapshotWithSystemInfoSql,
                (snapshotEntity, volumeInfoEntity, volumeEntity, storageDriveEntity, _, pcEntity) =>
                {
                    if (snapshotResult == null)
                    {
                        snapshotResult = snapshotEntity;

                        if (volumeInfoEntity != null && volumeEntity != null && storageDriveEntity != null)
                        {
                            snapshotResult.VolumeInfo = volumeInfoEntity;
                            volumeInfoEntity.Snapshot = snapshotResult;
                            volumeInfoEntity.SnapshotId = snapshotResult.Id;
                            volumeInfoEntity.Volume = volumeEntity;
                            volumeInfoEntity.VolumeId = volumeEntity.Id;
                            volumeEntity.VolumeInfos.Add(volumeInfoEntity);
                            volumeEntity.StorageDrive = storageDriveEntity;
                            volumeEntity.StorageDriveId = storageDriveEntity.Id;
                            storageDriveEntity.Snapshots.Add(snapshotResult);
                            storageDriveEntity.Volumes.Add(volumeEntity);
                            if (pcEntity != null)
                            {
                                storageDriveEntity.Pcs.Add(pcEntity);
                                pcEntity.Snapshots.Add(snapshotResult);
                                pcEntity.StorageDrives.Add(storageDriveEntity);
                            }
                        }
                    }

                    return snapshotResult;
                },
                param: new { UserId = userId },
                splitOn: "Id, Id, Id, PcId, Id");

            if (snapshotResult == null)
            {
                return null;
            }

            FolderEntity? currentFolder = null;
            var folders = new Dictionary<Ulid, FolderEntity>();
            var childFolders = new Dictionary<Ulid, IList<FolderEntity>>();
            var processedFolderIds = new HashSet<Ulid>(); // Track folders already processed for relationships

            await sqLiteConnection
                .QueryAsync<FolderEntity, FoldersToFoldersEntity, FilesToFoldersEntity, FileEntity, FolderEntity>(
                    SqlScripts.SelectFoldersAndFilesBySnapshotSql,
                    (fsFolderEntity, foldersToFoldersEntity, filesToFoldersEntity, fsFileEntity) =>
                    {
                        folders.TryAdd(fsFolderEntity.Id, fsFolderEntity);

                        // check if Folder already added
                        if (snapshotResult.RootFolder == null)
                        {
                            if (foldersToFoldersEntity.ParentFolderId == null)
                            {
                                snapshotResult.RootFolder = fsFolderEntity;
                                currentFolder = fsFolderEntity;
                                processedFolderIds.Add(fsFolderEntity.Id);
                            }
                            else
                            {
                                throw new ApplicationException("Shit!");
                            }
                        }

                        var currentFolderParentFolder = currentFolder?.ParentFolders.FirstOrDefault();
                        var currentFolderParentFolderId = currentFolderParentFolder?.Id;

                        // Only process folder relationships once per unique folder
                        if (!processedFolderIds.Contains(fsFolderEntity.Id) &&
                            (currentFolder!.Id != fsFolderEntity.Id || currentFolderParentFolderId != foldersToFoldersEntity.ParentFolderId))
                        {
                            processedFolderIds.Add(fsFolderEntity.Id);

                            // find ParentFolder

                            if (foldersToFoldersEntity.ParentFolderId.HasValue)
                            {
                                var parentFolderWasFound = folders.TryGetValue(foldersToFoldersEntity.ParentFolderId.Value, out var parentFolder);

                                if (parentFolderWasFound)
                                {
                                    // Only add if not already added
                                    if (!parentFolder!.ChildFolders.Contains(fsFolderEntity))
                                    {
                                        parentFolder.ChildFolders.Add(fsFolderEntity);
                                        fsFolderEntity.ParentFolders.Add(parentFolder);
                                    }
                                }
                                else
                                {
                                    var childFolderWasFound1 = childFolders.TryGetValue(foldersToFoldersEntity.ParentFolderId.Value, out var childFolderList);
                                    if (childFolderWasFound1)
                                    {
                                        if (!childFolderList!.Contains(fsFolderEntity))
                                        {
                                            childFolderList.Add(fsFolderEntity);
                                        }
                                    }
                                    else
                                    {
                                        childFolders.Add(foldersToFoldersEntity.ParentFolderId.Value, new List<FolderEntity> { fsFolderEntity });
                                    }
                                }
                            }

                            // find ChildFolders
                            var childFoldersWasFound = childFolders.TryGetValue(fsFolderEntity.Id, out var childFoldersList);
                            if (childFoldersWasFound)
                            {
                                foreach (var childFolder in childFoldersList!)
                                {
                                    if (!childFolder.ParentFolders.Contains(fsFolderEntity))
                                    {
                                        childFolder.ParentFolders.Add(fsFolderEntity);
                                        fsFolderEntity.ChildFolders.Add(childFolder);
                                    }
                                }

                                childFolders.Remove(fsFolderEntity.Id);
                            }

                            currentFolder = fsFolderEntity;
                        }

                        if (fsFileEntity != null)
                        {
                            // Only add file if not already added
                            if (!currentFolder!.Files.Contains(fsFileEntity))
                            {
                                fsFileEntity.Snapshots.Add(snapshotResult);
                                fsFileEntity.ParentFolders.Add(currentFolder);
                                currentFolder.Files.Add(fsFileEntity);
                            }
                        }

                        return currentFolder;
                    },
                    param: new { SnapshotId = snapshotResult.Id, UserId = userId },
                    splitOn: "SnapshotId, SnapshotId, Id");

            // Sort folders and files by name for consistent ordering
            SortFoldersAndFilesRecursively(snapshotResult.RootFolder!);

            return snapshotResult;

        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - GetLatestSnapshotWithAllEntitiesAsync");
            throw;
        }
    }

    public async Task<IList<SnapshotEntity>> GetAllSnapshotsAsync(Ulid userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetAllSnapshotsAsync - UserId: {UserId}", userId);
        try
        {
            var snapshots = new List<SnapshotEntity>();

            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);
            await sqLiteConnection
            .QueryAsync<SnapshotEntity, VolumeInfoEntity, VolumeEntity, StorageDriveEntity, PcEntity, FolderEntity, SnapshotEntity>(
                SqlScripts.SelectSnapshotsWithSystemInfoSql,
                (snapshotEntity, volumeInfoEntity, volumeEntity, storageDriveEntity, pcEntity, fsFolderEntity) =>
                {
                    if (volumeInfoEntity != null && volumeEntity != null && storageDriveEntity != null && pcEntity != null)
                    {
                        snapshotEntity.VolumeInfo = volumeInfoEntity;
                        volumeInfoEntity.Snapshot = snapshotEntity;
                        volumeInfoEntity.SnapshotId = snapshotEntity.Id;
                        volumeInfoEntity.Volume = volumeEntity;
                        volumeInfoEntity.VolumeId = volumeEntity.Id;
                        volumeEntity.VolumeInfos.Add(volumeInfoEntity);
                        volumeEntity.StorageDrive = storageDriveEntity;
                        volumeEntity.StorageDriveId = storageDriveEntity.Id;
                        storageDriveEntity.Snapshots.Add(snapshotEntity);
                        storageDriveEntity.Volumes.Add(volumeEntity);
                        storageDriveEntity.Pcs.Add(pcEntity);
                        pcEntity.Snapshots.Add(snapshotEntity);
                        pcEntity.StorageDrives.Add(storageDriveEntity);
                    }

                    snapshotEntity.RootFolder = fsFolderEntity;

                    snapshots.Add(snapshotEntity);

                    return snapshotEntity;
                },
                param: new { UserId = userId },
                splitOn: "Id, Id, Id, Id, Id");

            // Attach labels so snapshot summaries can render them.
            var labelRows = await sqLiteConnection.QueryAsync<LabelForSnapshotRow>(
                SqlScripts.SelectLabelsForAllSnapshotsSql,
                new { UserId = userId });
            var snapshotsById = snapshots
                .GroupBy(x => x.Id)
                .ToDictionary(g => g.Key, g => g.First());
            foreach (var labelRow in labelRows)
            {
                if (snapshotsById.TryGetValue(labelRow.LinkedSnapshotId, out var labeledSnapshot))
                {
                    labeledSnapshot.Labels.Add(labelRow);
                }
            }

            return snapshots;

        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - GetLatestSnapshotWithAllEntitiesAsync");
            throw;
        }
    }

    /// <summary>Flat row for the labels-per-snapshot join query.</summary>
    private sealed class LabelForSnapshotRow : LabelEntity
    {
        public Ulid LinkedSnapshotId { get; set; }
    }

    #region DeleteSnapshotByIdAsync

    public async Task<DatabaseActionResult> DeleteSnapshotByIdAsync(Ulid snapshotId, Ulid userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("DeleteSnapshotByIdAsync - SnapshotId: {SnapshotId}, UserId: {UserId}", snapshotId, userId);
        var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);
        using var transaction = await sqLiteConnection.BeginTransactionAsync(cancellationToken);

        try
        {
            _logger.LogInformation($"Deleting snapshot: {snapshotId}");

            // Check if snapshot exists
            var snapshot = await sqLiteConnection.QuerySingleOrDefaultAsync<SnapshotEntity>(
                SqlScripts.SelectSnapshotOnlyByIdSql,
                new { SnapshotId = snapshotId, UserId = userId },
                transaction);

            if (snapshot == null)
            {
                return DatabaseActionResult.NotFound;
            }

            // 1. Delete FilesToFolders entries for this snapshot
            await sqLiteConnection.ExecuteAsync(
                SqlScripts.DeleteFilesToFoldersBySnapshotSql,
                new { SnapshotId = snapshotId },
                transaction);
            _logger.LogInformation($"Deleted FilesToFolders entries for snapshot: {snapshotId}");

            // 2. Delete Files that have no other snapshots
            await sqLiteConnection.ExecuteAsync(
                SqlScripts.DeleteFilesWithoutSnapshotsSql,
                null,
                transaction);
            _logger.LogInformation($"Deleted Files with no snapshots");

            // 3. Delete FoldersToFolders entries for this snapshot
            await sqLiteConnection.ExecuteAsync(
                SqlScripts.DeleteFoldersToFoldersBySnapshotSql,
                new { SnapshotId = snapshotId },
                transaction);
            _logger.LogInformation($"Deleted FoldersToFolders entries for snapshot: {snapshotId}");

            // 4. Delete Folders that have no other snapshots
            await sqLiteConnection.ExecuteAsync(
                SqlScripts.DeleteFoldersWithoutSnapshotsSql,
                null,
                transaction);
            _logger.LogInformation($"Deleted Folders with no snapshots");

            // 5. Delete PcsToStorageDrives entries for this snapshot
            await sqLiteConnection.ExecuteAsync(
                SqlScripts.DeletePcsToStorageDrivesBySnapshotSql,
                new { SnapshotId = snapshotId },
                transaction);
            _logger.LogInformation($"Deleted PcsToStorageDrives entries for snapshot: {snapshotId}");

            // 6. Delete Pcs that have no other storage drives
            await sqLiteConnection.ExecuteAsync(
                SqlScripts.DeletePcsWithoutStorageDrivesSql,
                null,
                transaction);
            _logger.LogInformation($"Deleted Pcs with no StorageDrives");

            // 7. Delete VolumeInfo entries for this snapshot
            await sqLiteConnection.ExecuteAsync(
                SqlScripts.DeleteVolumeInfoBySnapshotSql,
                new { SnapshotId = snapshotId, UserId = userId },
                transaction);
            _logger.LogInformation($"Deleted VolumeInfo for snapshot: {snapshotId}");

            // 8. Delete Volumes that have no other volume infos
            await sqLiteConnection.ExecuteAsync(
                SqlScripts.DeleteVolumesWithoutVolumeInfosSql,
                null,
                transaction);
            _logger.LogInformation($"Deleted Volumes with no VolumeInfos");

            // 9. Delete StorageDrives that have no other volumes and no other snapshots
            await sqLiteConnection.ExecuteAsync(
                SqlScripts.DeleteStorageDrivesWithoutVolumesAndSnapshotsSql,
                new { UserId = userId },
                transaction);
            _logger.LogInformation($"Deleted StorageDrives with no Volumes and Snapshots");

            // Delete association with labels. Labels should not be deleted.
            await sqLiteConnection.ExecuteAsync(
                SqlScripts.DeleteLabelsToSnapshotsBySnapshotIdSql,
                new { SnapshotId = snapshotId },
                transaction);

            // 10. Finally, delete the snapshot itself
            await sqLiteConnection.ExecuteAsync(
                SqlScripts.DeleteSnapshotByIdSql,
                new { SnapshotId = snapshotId, UserId = userId },
                transaction);
            _logger.LogInformation($"Deleted snapshot: {snapshotId}");

            await transaction.CommitAsync(cancellationToken);

            return DatabaseActionResult.Success;
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "ERROR - DeleteSnapshotByIdAsync");
            throw;
        }
    }

    #endregion

    #region AddSnapshotAsync

    public async Task<int> AddSnapshotAsync(SnapshotEntity snapshotEntity, Ulid userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("AddSnapshotAsync - SnapshotId: {SnapshotId}, UserId: {UserId}", snapshotEntity.Id, userId);
        var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);
        await using var transaction = await sqLiteConnection.BeginTransactionAsync(cancellationToken);

        try
        {
            _logger.LogInformation($"Insert snapshot: {snapshotEntity.Id} for UserId: {userId}");
            // add snapshot to DB
            snapshotEntity.UserId = userId;
            await sqLiteConnection.ExecuteAsync(SqlScripts.InsertSnapshotSql, snapshotEntity, transaction);

            var volumeEntity = snapshotEntity.VolumeInfo!.Volume;
            var storageDriveEntity = volumeEntity!.StorageDrive;

            // Find same PC in DB
            var pcEntity = storageDriveEntity!.Pcs[0];
            var pcInDb = await sqLiteConnection.QuerySingleOrDefaultAsync<PcEntity>(SqlScripts.SelectPcSql,
                new { pcEntity.HardwareUuid, pcEntity.HardwareSerialNumber, UserId = userId });
            if (pcInDb == null)
            {
                _logger.LogInformation($"Insert PC: {pcEntity.Id}");
                // add pc to DB
                pcEntity.UserId = userId;
                await sqLiteConnection.ExecuteAsync(SqlScripts.InsertPcSql, pcEntity, transaction);
            }
            else
            {
                pcEntity.Id = pcInDb.Id;
            }

            var storageDriveInDb = await sqLiteConnection.QuerySingleOrDefaultAsync<StorageDriveEntity>(SqlScripts.SelectStorageDriveSql,
                new { storageDriveEntity.SerialNumber, storageDriveEntity.DeviceId, storageDriveEntity.Name, UserId = userId.ToString() });
            if (storageDriveInDb == null)
            {
                _logger.LogInformation($"Insert StorageDrive: {storageDriveEntity.Id}");
                // add StorageDrive to DB
                storageDriveEntity.UserId = userId;
                await sqLiteConnection.ExecuteAsync(SqlScripts.InsertStorageDriveSql, storageDriveEntity, transaction);
            }
            else
            {
                storageDriveEntity.Id = storageDriveInDb.Id;
            }

            // bind PC with StorageDrive
            await BindPcWithStorageDriveAndSnapshotAsync(sqLiteConnection, pcEntity, storageDriveEntity, snapshotEntity, transaction);

            // Check if Volume is in DB
            var volumeInDb = await sqLiteConnection.QuerySingleOrDefaultAsync<VolumeEntity>(SqlScripts.SelectVolumeSql,
                new { volumeEntity.VolumeSerialNumber, UserId = userId.ToString() });
            if (volumeInDb == null)
            {
                _logger.LogInformation($"Insert Volume: {volumeEntity.Id}");
                // add Volume to DB
                volumeEntity.UserId = userId;
                await sqLiteConnection.ExecuteAsync(SqlScripts.InsertVolumeSql, volumeEntity, transaction);
            }
            else
            {
                volumeEntity.Id = volumeInDb.Id;
                snapshotEntity.VolumeInfo.VolumeId = volumeInDb.Id;
            }

            _logger.LogInformation($"Insert VolumeInfo: {snapshotEntity.VolumeInfo.Id}");
            // add VolumeInfo to DB
            snapshotEntity.VolumeInfo.UserId = userId;
            await sqLiteConnection.ExecuteAsync(SqlScripts.InsertVolumeInfoSql, snapshotEntity.VolumeInfo, transaction);

            // add Folder Tree to DB
            await AddFolderWithFilesRecursivelyAsync(sqLiteConnection, userId, snapshotEntity.RootFolder!, null, snapshotEntity, transaction);

            // Add Labels and associations
            await AddLabelsAndAssighnToSnapshotAsync(sqLiteConnection, userId, snapshotEntity, transaction);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "ERROR - AddDataAsync");
            throw;
        }

        return 1;
    }

    private async Task AddFolderWithFilesRecursivelyAsync(IDbConnection sqLiteConnection, Ulid userId, FolderEntity folderEntity, FolderEntity? parentFolderEntity, SnapshotEntity snapshotEntity, DbTransaction transaction)
    {
        try
        { // check if Folder in DB
            var folderInDb = await sqLiteConnection.QuerySingleOrDefaultAsync<FolderEntity>(SqlScripts.SelectFolderByNameAndParentFolderPathAndStorageDriveIdSql,
                new { folderEntity.Name, folderEntity.Size, folderEntity.Sha256Hash, UserId = userId }, transaction);

            if (folderInDb == null) // Folder does not exist in DB
            {
                _logger.LogInformation($"InsertFolderSql: {folderEntity.Id}");
                // add new Folder to DB
                await sqLiteConnection.ExecuteAsync(SqlScripts.InsertFolderSql, folderEntity, transaction);
            }
            else // Folder exists in DB
            {
                folderEntity.Id = folderInDb.Id;
            }

            await BindFolderWithFolderAndSnapshotAsync(sqLiteConnection, userId, parentFolderEntity, folderEntity, snapshotEntity, transaction);

            // add child Folders (sorted by name)
            //var sortedChildFolders = folderEntity.ChildFolders.OrderBy(f => f.Name).ToList();
            foreach (var childFolder in folderEntity.ChildFolders)
            {
                await AddFolderWithFilesRecursivelyAsync(sqLiteConnection, userId, childFolder, folderEntity, snapshotEntity, transaction);
            }

            // add Files (sorted by name)
            //var sortedFiles = folderEntity.Files.OrderBy(f => f.Name).ToList();
            foreach (var fileEntity in folderEntity.Files)
            { // check if File in DB
                var fileInDb = await sqLiteConnection.QuerySingleOrDefaultAsync<FileEntity>(SqlScripts.SelectFileByNameAndParentFolderPathAndStorageDriveIdSql,
                    new { fileEntity.Name, fileEntity.Size, fileEntity.FileExtension, fileEntity.Sha256Hash, UserId = userId }, transaction);

                if (fileInDb == null) // File does not exist in DB
                {
                    _logger.LogInformation($"InsertFileSql: {fileEntity.Id}");
                    // add new File to DB
                    await sqLiteConnection.ExecuteAsync(SqlScripts.InsertFileSql, fileEntity, transaction);
                }
                else
                {
                    fileEntity.Id = fileInDb.Id;
                }

                await BindFileWithFolderAndSnapshotAsync(sqLiteConnection, folderEntity, fileEntity, snapshotEntity, transaction);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - AddFolderWithFilesRecursivelyAsync");
            throw;
        }
    }

    private async Task BindFolderWithFolderAndSnapshotAsync(IDbConnection sqLiteConnection, Ulid userId, FolderEntity? parentFolderEntity, FolderEntity childFolderEntity, SnapshotEntity snapshotEntity, DbTransaction transaction)
    {
        try
        {
            // SelectFoldersToFoldersSql
            var folderToFolderInDb = await sqLiteConnection.QuerySingleOrDefaultAsync<FilesToFoldersEntity>(SqlScripts.SelectFoldersToFoldersSql,
                new { SnapshotId = snapshotEntity.Id, ParentFolderId = parentFolderEntity?.Id, ChildFolderId = childFolderEntity.Id }, transaction);

            if (folderToFolderInDb == null) // Item does not exist in DB
            {
                _logger.LogInformation($"BindFolderWithFolderAndSnapshotAsync: {parentFolderEntity?.Id}, {childFolderEntity.Id}, {snapshotEntity.Id}");
                await sqLiteConnection.ExecuteAsync(SqlScripts.InsertFoldersToFoldersSql, new { ParentFolderId = parentFolderEntity?.Id, ChildFolderId = childFolderEntity.Id, SnapshotId = snapshotEntity.Id }, transaction);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - BindFolderWithFolderAndSnapshotAsync");
            throw;
        }
    }

    private async Task BindFileWithFolderAndSnapshotAsync(IDbConnection sqLiteConnection, FolderEntity folderEntity, FileEntity fileEntity, SnapshotEntity snapshotEntity, DbTransaction transaction)
    {
        try
        {
            // SelectFilesToFoldersSql
            var fileToFolderInDb = await sqLiteConnection.QuerySingleOrDefaultAsync<FilesToFoldersEntity>(SqlScripts.SelectFilesToFoldersSql,
                new { SnapshotId = snapshotEntity.Id, FolderId = folderEntity.Id, FileId = fileEntity.Id }, transaction);

            if (fileToFolderInDb == null) // Item does not exist in DB
            {
                _logger.LogInformation($"BindFileWithFolderAndSnapshotAsync: {folderEntity.Id}, {folderEntity.Name}, {fileEntity.Id}, {fileEntity.Name}, {snapshotEntity.Id}");
                await sqLiteConnection.ExecuteAsync(SqlScripts.InsertFilesToFoldersSql, new { FolderId = folderEntity.Id, FileId = fileEntity.Id, SnapshotId = snapshotEntity.Id }, transaction);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - BindFileWithFolderAndSnapshotAsync");
            throw;
        }
    }

    private async Task BindPcWithStorageDriveAndSnapshotAsync(IDbConnection sqLiteConnection, PcEntity pcEntity, StorageDriveEntity storageDriveEntity, SnapshotEntity snapshotEntity, DbTransaction transaction)
    {
        try
        {
            // SelectPcsToStorageDrivesSql
            var pcToStorageDriveInDb = await sqLiteConnection.QuerySingleOrDefaultAsync<FilesToFoldersEntity>(SqlScripts.SelectPcsToStorageDrivesSql,
                new { SnapshotId = snapshotEntity.Id, PcId = pcEntity.Id, StorageDriveId = storageDriveEntity.Id }, transaction);

            if (pcToStorageDriveInDb == null) // Item does not exist in DB
            {
                _logger.LogInformation($"BindPcWithStorageDriveAndSnapshotAsync: {pcEntity.Id}, {storageDriveEntity.Id}, {snapshotEntity.Id}");
                await sqLiteConnection.ExecuteAsync(SqlScripts.InsertPcsToStorageDrivesSql, new { PcId = pcEntity.Id, StorageDriveId = storageDriveEntity.Id, SnapshotId = snapshotEntity.Id }, transaction);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - BindFileWithFolderAndSnapshotAsync");
            throw;
        }
    }

    private async Task AddLabelsAndAssighnToSnapshotAsync(IDbConnection sqLiteConnection, Ulid userId, SnapshotEntity snapshotEntity, DbTransaction transaction)
    {
        try
        {
            foreach (var labelEntity in snapshotEntity.Labels)
            {
                // Check if label exists
                var labelInDb = await sqLiteConnection.QuerySingleOrDefaultAsync<LabelEntity>(SqlScripts.SelectLabelByIdSql,
                    new { LabelId = labelEntity.Id, UserId = userId }, transaction);
                if (labelInDb == null) // Label does not exist in DB
                {
                    _logger.LogInformation($"Insert Label: {labelEntity.Id}");
                    // add new Label to DB
                    labelEntity.UserId = userId;
                    await sqLiteConnection.ExecuteAsync(SqlScripts.InsertLabelSql, labelEntity, transaction);
                }
                else
                {
                    labelEntity.Id = labelInDb.Id;
                }
                // Bind Label to Snapshot
                await sqLiteConnection.ExecuteAsync(SqlScripts.InsertLabelsToSnapshotsSql,
                    new { SnapshotId = snapshotEntity.Id, LabelId = labelEntity.Id }, transaction);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - AddLabelsAndAssighnToSnapshotAsync");
            throw;
        }
    }

    private void SortFoldersAndFilesRecursively(FolderEntity folderEntity)
    {
        if (folderEntity is null)
        {
            return;
        }

        // Sort child folders by name
        folderEntity.ChildFolders = folderEntity.ChildFolders.OrderBy(f => f.Name).ToList();

        // Sort files by name
        folderEntity.Files = folderEntity.Files.OrderBy(f => f.Name).ToList();

        // Recursively sort child folders
        foreach (var childFolder in folderEntity.ChildFolders)
        {
            SortFoldersAndFilesRecursively(childFolder);
        }
    }

    #endregion
}
