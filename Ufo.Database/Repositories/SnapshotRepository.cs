using Dapper;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Data.Common;
using System.Text;
using Ufo.Abstractions;
using Ufo.Abstractions.Database;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;

namespace Ufo.Database.Repositories;

public class SnapshotRepository : ISnapshotRepository
{
    private readonly ILogger<SnapshotRepository> _logger;
    private readonly IDbConnectionFactory _dbConnectionFactory;

    /// <summary>
    /// How many content hashes go into one existence lookup. Chosen by measurement
    /// rather than by the parameter limit: the cost of preparing the IN list grows
    /// faster than the round trips it saves, so throughput peaks well short of what
    /// SQLite would allow and falls back to per-row speed by around 900.
    /// </summary>
    private const int HashesPerLookupStatement = 100;

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
            await AddFolderTreeAsync(sqLiteConnection, userId, snapshotEntity.RootFolder!, snapshotEntity, transaction);

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

    /// <summary>
    /// Writes a snapshot's folder tree. The tree is flattened first and then written a
    /// table at a time rather than a node at a time, which is what lets the existence
    /// lookups be answered in hash batches and the bindings be written without a lookup
    /// at all: four statements per file become one insert, one binding, and a hundredth
    /// of a lookup.
    /// </summary>
    private async Task AddFolderTreeAsync(IDbConnection sqLiteConnection, Ulid userId, FolderEntity rootFolderEntity, SnapshotEntity snapshotEntity, DbTransaction transaction)
    {
        try
        {
            var treeContents = FlattenFolderTree(rootFolderEntity);

            _logger.LogInformation(
                "AddFolderTreeAsync - {FolderCount} folders, {FileCount} files for SnapshotId: {SnapshotId}",
                treeContents.Folders.Count,
                treeContents.Files.Count,
                snapshotEntity.Id);

            // Rows before bindings: the join tables reference both sides by id, and the
            // ids are only final once the de-duplication below has run.
            await ResolveAndInsertFoldersAsync(sqLiteConnection, userId, treeContents.Folders, transaction);
            await ResolveAndInsertFilesAsync(sqLiteConnection, userId, treeContents.Files, transaction);

            await InsertFolderBindingsAsync(sqLiteConnection, treeContents.FolderBindings, snapshotEntity, transaction);
            await InsertFileBindingsAsync(sqLiteConnection, treeContents.FileBindings, snapshotEntity, transaction);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - AddFolderTreeAsync");
            throw;
        }
    }

    /// <summary>
    /// Flattens the tree into the four lists the write path needs. Breadth-first and
    /// iterative rather than recursive, so a deep tree is not limited by the stack.
    /// </summary>
    private static FolderTreeContents FlattenFolderTree(FolderEntity rootFolderEntity)
    {
        var folders = new List<FolderEntity>();
        var files = new List<FileEntity>();
        var folderBindings = new List<FolderBinding>();
        var fileBindings = new List<FileBinding>();

        var expandedFolders = new HashSet<FolderEntity>(ReferenceEqualityComparer.Instance);
        var seenFiles = new HashSet<FileEntity>(ReferenceEqualityComparer.Instance);

        var queue = new Queue<FolderBinding>();
        queue.Enqueue(new FolderBinding(null, rootFolderEntity));

        while (queue.Count > 0)
        {
            var folderBinding = queue.Dequeue();
            folderBindings.Add(folderBinding);

            var folderEntity = folderBinding.ChildFolder;

            // A folder reachable through more than one parent still needs a binding per
            // parent, but its contents only have to be walked once.
            if (!expandedFolders.Add(folderEntity))
            {
                continue;
            }

            folders.Add(folderEntity);

            foreach (var childFolderEntity in folderEntity.ChildFolders)
            {
                queue.Enqueue(new FolderBinding(folderEntity, childFolderEntity));
            }

            foreach (var fileEntity in folderEntity.Files)
            {
                fileBindings.Add(new FileBinding(folderEntity, fileEntity));

                if (seenFiles.Add(fileEntity))
                {
                    files.Add(fileEntity);
                }
            }
        }

        return new FolderTreeContents(folders, folderBindings, files, fileBindings);
    }

    /// <summary>
    /// Points every folder that already exists for this user at the row that is already
    /// there, and inserts the rest.
    /// </summary>
    private async Task ResolveAndInsertFoldersAsync(IDbConnection sqLiteConnection, Ulid userId, IReadOnlyList<FolderEntity> folderEntities, DbTransaction transaction)
    {
        if (folderEntities.Count == 0)
        {
            return;
        }

        var existingRows = await SelectByHashesAsync<FolderLookupRow>(
            sqLiteConnection,
            transaction,
            SqlScripts.SelectFoldersByHashesSqlPrefix,
            userId,
            [.. folderEntities.Select(folderEntity => folderEntity.Sha256Hash).Distinct()]);

        var knownFolderIds = new Dictionary<FolderDedupeKey, Ulid>();
        foreach (var existingRow in existingRows)
        {
            if (existingRow.Size is null)
            {
                continue;
            }

            knownFolderIds.TryAdd(new FolderDedupeKey(existingRow.Name, existingRow.Size.Value, existingRow.Sha256Hash), existingRow.Id);
        }

        var foldersToInsert = new List<FolderEntity>();

        foreach (var folderEntity in folderEntities)
        {
            // A null size can never match: SQL does not report NULL = NULL, so the
            // statement-per-folder version always inserted these too.
            if (folderEntity.Size is null)
            {
                foldersToInsert.Add(folderEntity);
                continue;
            }

            var dedupeKey = new FolderDedupeKey(folderEntity.Name, folderEntity.Size.Value, folderEntity.Sha256Hash);

            if (knownFolderIds.TryGetValue(dedupeKey, out var existingFolderId))
            {
                folderEntity.Id = existingFolderId;
                continue;
            }

            // Registering it before it is written is what makes an identical folder
            // later in the same tree reuse this row - the statement-per-folder version
            // got that for free by re-reading the transaction after every insert.
            knownFolderIds[dedupeKey] = folderEntity.Id;
            foldersToInsert.Add(folderEntity);
        }

        _logger.LogInformation("Inserting {NewFolderCount} new folders out of {FolderCount}", foldersToInsert.Count, folderEntities.Count);

        // One fixed statement for the whole list: Dapper rebinds and re-executes the
        // prepared command per row, which is what SQLite is fastest at.
        if (foldersToInsert.Count > 0)
        {
            await sqLiteConnection.ExecuteAsync(SqlScripts.InsertFolderSql, foldersToInsert, transaction);
        }
    }

    /// <summary>
    /// The file counterpart of <see cref="ResolveAndInsertFoldersAsync"/>; a file is the
    /// same file if its name, size, extension and hash all match.
    /// </summary>
    private async Task ResolveAndInsertFilesAsync(IDbConnection sqLiteConnection, Ulid userId, IReadOnlyList<FileEntity> fileEntities, DbTransaction transaction)
    {
        if (fileEntities.Count == 0)
        {
            return;
        }

        var existingRows = await SelectByHashesAsync<FileLookupRow>(
            sqLiteConnection,
            transaction,
            SqlScripts.SelectFilesByHashesSqlPrefix,
            userId,
            [.. fileEntities.Select(fileEntity => fileEntity.Sha256Hash).Distinct()]);

        var knownFileIds = new Dictionary<FileDedupeKey, Ulid>();
        foreach (var existingRow in existingRows)
        {
            if (existingRow.Size is null)
            {
                continue;
            }

            knownFileIds.TryAdd(new FileDedupeKey(existingRow.Name, existingRow.Size.Value, existingRow.FileExtension, existingRow.Sha256Hash), existingRow.Id);
        }

        var filesToInsert = new List<FileEntity>();

        foreach (var fileEntity in fileEntities)
        {
            if (fileEntity.Size is null)
            {
                filesToInsert.Add(fileEntity);
                continue;
            }

            var dedupeKey = new FileDedupeKey(fileEntity.Name, fileEntity.Size.Value, fileEntity.FileExtension, fileEntity.Sha256Hash);

            if (knownFileIds.TryGetValue(dedupeKey, out var existingFileId))
            {
                fileEntity.Id = existingFileId;
                continue;
            }

            knownFileIds[dedupeKey] = fileEntity.Id;
            filesToInsert.Add(fileEntity);
        }

        _logger.LogInformation("Inserting {NewFileCount} new files out of {FileCount}", filesToInsert.Count, fileEntities.Count);

        if (filesToInsert.Count > 0)
        {
            await sqLiteConnection.ExecuteAsync(SqlScripts.InsertFileSql, filesToInsert, transaction);
        }
    }

    /// <summary>
    /// Writes the parent-to-child bindings. Where the recursive version asked whether
    /// each binding was already there and then inserted it, the primary key answers that
    /// on its own, so this is one statement per binding instead of two.
    /// </summary>
    private static async Task InsertFolderBindingsAsync(IDbConnection sqLiteConnection, IReadOnlyList<FolderBinding> folderBindings, SnapshotEntity snapshotEntity, DbTransaction transaction)
    {
        if (folderBindings.Count == 0)
        {
            return;
        }

        var bindingRows = folderBindings
            .Select(folderBinding => new
            {
                // Null for the root folder, which is how the read side finds it.
                ParentFolderId = folderBinding.ParentFolder?.Id,
                ChildFolderId = folderBinding.ChildFolder.Id,
                SnapshotId = snapshotEntity.Id
            })
            .ToList();

        await sqLiteConnection.ExecuteAsync(SqlScripts.InsertFoldersToFoldersIfMissingSql, bindingRows, transaction);
    }

    private static async Task InsertFileBindingsAsync(IDbConnection sqLiteConnection, IReadOnlyList<FileBinding> fileBindings, SnapshotEntity snapshotEntity, DbTransaction transaction)
    {
        if (fileBindings.Count == 0)
        {
            return;
        }

        var bindingRows = fileBindings
            .Select(fileBinding => new
            {
                FolderId = fileBinding.ParentFolder.Id,
                FileId = fileBinding.File.Id,
                SnapshotId = snapshotEntity.Id
            })
            .ToList();

        await sqLiteConnection.ExecuteAsync(SqlScripts.InsertFilesToFoldersIfMissingSql, bindingRows, transaction);
    }

    /// <summary>
    /// Reads back the rows that could match any of <paramref name="hashes"/>, a batch of
    /// hashes at a time. Both callers then narrow the result in memory, on the columns
    /// the hash alone does not cover.
    /// </summary>
    private static async Task<List<TRow>> SelectByHashesAsync<TRow>(
        IDbConnection sqLiteConnection,
        DbTransaction transaction,
        string selectSqlPrefix,
        Ulid userId,
        IReadOnlyList<string> hashes)
    {
        var rows = new List<TRow>();

        for (var batchStart = 0; batchStart < hashes.Count; batchStart += HashesPerLookupStatement)
        {
            var batchLength = Math.Min(HashesPerLookupStatement, hashes.Count - batchStart);
            var parameters = new DynamicParameters();
            parameters.Add("UserId", userId);

            var placeholders = new StringBuilder("(");
            for (var rowIndex = 0; rowIndex < batchLength; rowIndex++)
            {
                if (rowIndex > 0)
                {
                    placeholders.Append(',');
                }

                placeholders.Append("@Hash").Append(rowIndex);
                parameters.Add($"Hash{rowIndex}", hashes[batchStart + rowIndex]);
            }

            placeholders.Append(");");

            var batchRows = await sqLiteConnection.QueryAsync<TRow>(selectSqlPrefix + placeholders, parameters, transaction);
            rows.AddRange(batchRows);
        }

        return rows;
    }

    private readonly record struct FolderBinding(FolderEntity? ParentFolder, FolderEntity ChildFolder);

    private readonly record struct FileBinding(FolderEntity ParentFolder, FileEntity File);

    private readonly record struct FolderDedupeKey(string Name, long Size, string Sha256Hash);

    private readonly record struct FileDedupeKey(string Name, long Size, string FileExtension, string Sha256Hash);

    private sealed record FolderTreeContents(
        IReadOnlyList<FolderEntity> Folders,
        IReadOnlyList<FolderBinding> FolderBindings,
        IReadOnlyList<FileEntity> Files,
        IReadOnlyList<FileBinding> FileBindings);

    private sealed class FolderLookupRow
    {
        public Ulid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public long? Size { get; set; }

        public string Sha256Hash { get; set; } = string.Empty;
    }

    private sealed class FileLookupRow
    {
        public Ulid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public long? Size { get; set; }

        public string FileExtension { get; set; } = string.Empty;

        public string Sha256Hash { get; set; } = string.Empty;
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
