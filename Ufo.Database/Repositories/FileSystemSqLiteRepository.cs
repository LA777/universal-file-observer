using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Data;
using System.Data.Common;
using Ufo.Abstractions;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.Options;

namespace Ufo.Database.Repositories;

public class FileSystemSqLiteRepository : IFileSystemSqLiteRepository
{
    private readonly ILogger<FileSystemSqLiteRepository> _logger;
    private readonly string _connectionString;
   
    public FileSystemSqLiteRepository(IOptionsMonitor<DatabaseOptions> databaseOptionsMonitor, ILogger<FileSystemSqLiteRepository>? logger)
    {
        _connectionString = databaseOptionsMonitor.CurrentValue.ConnectionString ?? throw new ArgumentNullException(nameof(databaseOptionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task DropDataInTables()
    {
        try
        {
            await using var sqLiteConnection = new SqliteConnection(_connectionString);
            await sqLiteConnection.QueryAsync<SnapshotEntity>(SqlScripts.ClearDataInTablesSql);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - DropDataInTables");
            throw;
        }
    }

    public async Task<IEnumerable<FsFileEntity>> GetFilesByNameAndExtensionAsync(string name, string extension, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var sqLiteConnection = new SqliteConnection(_connectionString);
            var fileEntities = await sqLiteConnection.QueryAsync<FsFileEntity>(SqlScripts.SelectFilesByNameAndExtensionSql, new { Name = name, FileExtension = extension });

            return fileEntities;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - GetFilesByNameAndExtensionAsync");
            throw;
        }
    }

    public async Task<IEnumerable<FsFolderEntity>> GetFoldersByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var sqLiteConnection = new SqliteConnection(_connectionString);
            var folderEntities = await sqLiteConnection.QueryAsync<FsFolderEntity>(SqlScripts.SelectFoldersByNameSql, new { Name = name });

            return folderEntities;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - GetFoldersByNameAsync");
            throw;
        }
    }

    public async Task<SnapshotEntity> GetSnapshotByIdAsync(Ulid snapshotId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var sqLiteConnection = new SqliteConnection(_connectionString);
            SnapshotEntity snapshotResult = null;

            await sqLiteConnection
            .QueryAsync<SnapshotEntity, VolumeInfoEntity, VolumeEntity, StorageDriveEntity, PcsToStorageDrivesEntity, PcEntity, SnapshotEntity>(
                SqlScripts.SelectSnapshotByIdSql,
                (snapshotEntity, volumeInfoEntity, volumeEntity, storageDriveEntity, _, pcEntity) =>
                {
                    if (snapshotResult == null)
                    {
                        snapshotResult = snapshotEntity;
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

                    return snapshotResult;
                },
                splitOn: "Id, Id, Id, PcId, Id",
                param: new { SnapshotId = snapshotId });

            if (snapshotResult == null)
            {
                throw new ArgumentNullException(nameof(snapshotResult));
            }

            FsFolderEntity? currentFolder = null;
            var folders = new Dictionary<Ulid, FsFolderEntity>();
            var childFolders = new Dictionary<Ulid, IList<FsFolderEntity>>();
            var processedFolderIds = new HashSet<Ulid>(); // Track folders already processed for relationships
            
            await sqLiteConnection
                .QueryAsync<FsFolderEntity, FoldersToFoldersEntity, FilesToFoldersEntity, FsFileEntity, FsFolderEntity>(
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
                                    childFolders.Add(foldersToFoldersEntity.ParentFolderId.Value, new List<FsFolderEntity> { fsFolderEntity });
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
                    param: new { SnapshotId = snapshotId },
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

    public async Task<SnapshotEntity> GetLatestSnapshotWithAllEntitiesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var sqLiteConnection = new SqliteConnection(_connectionString);
            SnapshotEntity? snapshotResult = null;

            await sqLiteConnection
            .QueryAsync<SnapshotEntity, VolumeInfoEntity, VolumeEntity, StorageDriveEntity, PcsToStorageDrivesEntity, PcEntity, SnapshotEntity>(
                SqlScripts.SelectLatestSnapshotWithSystemInfoSql,
                (snapshotEntity, volumeInfoEntity, volumeEntity, storageDriveEntity, _, pcEntity) =>
                {
                    if (snapshotResult == null)
                    {
                        snapshotResult = snapshotEntity;
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

                    return snapshotResult;
                },
                splitOn: "Id, Id, Id, PcId, Id");

            if (snapshotResult == null)
            {
                return null;
            }

            FsFolderEntity? currentFolder = null;
            var folders = new Dictionary<Ulid, FsFolderEntity>();
            var childFolders = new Dictionary<Ulid, IList<FsFolderEntity>>();
            var processedFolderIds = new HashSet<Ulid>(); // Track folders already processed for relationships
            
            await sqLiteConnection
                .QueryAsync<FsFolderEntity, FoldersToFoldersEntity, FilesToFoldersEntity, FsFileEntity, FsFolderEntity>(
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
                                    childFolders.Add(foldersToFoldersEntity.ParentFolderId.Value, new List<FsFolderEntity> { fsFolderEntity });
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
                    param: new { SnapshotId = snapshotResult.Id },
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

    public async Task<IList<SnapshotEntity>> GetAllSnapshotsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var sqLiteConnection = new SqliteConnection(_connectionString);

            var snapshots = new List<SnapshotEntity>();

            await sqLiteConnection
            .QueryAsync<SnapshotEntity, VolumeInfoEntity, VolumeEntity, StorageDriveEntity, PcEntity, FsFolderEntity, SnapshotEntity>(
                SqlScripts.SelectSnapshotsWithSystemInfoSql,
                (snapshotEntity, volumeInfoEntity, volumeEntity, storageDriveEntity, pcEntity, fsFolderEntity) =>
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
                    snapshotEntity.RootFolder = fsFolderEntity;

                    snapshots.Add(snapshotEntity);

                    return snapshotEntity;
                },
                splitOn: "Id, Id, Id, Id, Id");

            return snapshots;

        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - GetLatestSnapshotWithAllEntitiesAsync");
            throw;
        }
    }

    public async Task<DeleteResult> DeleteSnapshotByIdAsync(Ulid snapshotId, CancellationToken cancellationToken = default)
    {
        // TODO LA - Implement deletion


        await using var sqLiteConnection = new SqliteConnection(_connectionString);
        await sqLiteConnection.OpenAsync();
        using var transaction = await sqLiteConnection.BeginTransactionAsync();

        try
        {


            // Select PcsToStorageDrives by SnapshotId
            // Select PCs that have only one Snapshot that is deleting
            // Select StorageDrives that have only one Snapshot that is deleting
            // Select Volumes by StorageDriveId
            // Select VolumeInfoes by VolumeId and SnapshotId


            // Select FolderToFiles by SnapshotId
            // Delete Files that have only one Snapshot that is deleting

            // Select FolderToFolders by SnapshotId
            // Delete Folders that have only one Snapshot that is deleting


            // Dev snapshot         cc9785d5-7bb1-47e6-beb1-6c011c026fd9
            // PC Id              1bd5a3d1-4fa1-47d0-b723-2a483413aa54 - has other Snapshots
            // StorageDriveId     0c59da92-3826-49a2-8b74-772e5c3f1047 - has other Snapshots




            // 1. Delete related records in PcsToStorage
            //var deletedPcsToStorageDrives = await sqLiteConnection.ExecuteAsync(
            //    "DELETE FROM PcsToStorageDrives WHERE SnapshotId = @SnapshotId",
            //    new { SnapshotId = snapshotId },
            //    transaction
            //);

            // 2. Delete the snapshot from Fodlers
            //var deletedPcsToStorageDrives = await sqLiteConnection.ExecuteAsync(
            //    "DELETE FROM PcsToStorageDrives WHERE SnapshotId = @SnapshotId",
            //    new { SnapshotId = snapshotId },
            //    transaction
            //);


            // 3. Delete the snapshot from Snapshots
            //var deletedSnapshots = await sqLiteConnection.ExecuteAsync(
            //    "DELETE FROM Snapshots WHERE Id = @Id",
            //    new { Id = snapshotId },
            //    transaction
            //);

            await transaction.CommitAsync();

            return DeleteResult.Success;
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync();
            _logger.LogError(exception, "ERROR - DeleteSnapshotByIdAsync");
            throw;
        }
    }

    public async Task<int> AddSnapshotAsync(SnapshotEntity snapshotEntity, CancellationToken cancellationToken = default)
    {
        await using var sqLiteConnection = new SqliteConnection(_connectionString);
        await sqLiteConnection.OpenAsync(cancellationToken);
        await using var transaction = await sqLiteConnection.BeginTransactionAsync(cancellationToken);

        try
        {
            _logger.LogInformation($"Insert snapshot: {snapshotEntity.Id}");
            // add snapshot to DB
            await sqLiteConnection.ExecuteAsync(SqlScripts.InsertSnapshotSql, snapshotEntity, transaction);

            var volumeEntity = snapshotEntity.VolumeInfo!.Volume;
            var storageDriveEntity = volumeEntity!.StorageDrive;

            // Find same PC in DB
            var pcEntity = storageDriveEntity!.Pcs[0];
            var pcInDb = await sqLiteConnection.QuerySingleOrDefaultAsync<PcEntity>(SqlScripts.SelectPcSql, new { PcName = pcEntity.Name, DeviceId = pcEntity.DeviceId });
            if (pcInDb == null)
            {
                _logger.LogInformation($"Insert PC: {pcEntity.Id}");
                // add pc to DB
                await sqLiteConnection.ExecuteAsync(SqlScripts.InsertPcSql, pcEntity, transaction);
            }
            else
            {
                pcEntity.Id = pcInDb.Id;
            }

            var storageDriveInDb = await sqLiteConnection.QuerySingleOrDefaultAsync<StorageDriveEntity>(SqlScripts.SelectStorageDriveSql,
                new { storageDriveEntity.SerialNumber, storageDriveEntity.DeviceId, storageDriveEntity.Name });
            if (storageDriveInDb == null)
            {
                _logger.LogInformation($"Insert StorageDrive: {storageDriveEntity.Id}");
                // add StorageDrive to DB
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
                new { volumeEntity.VolumeSerialNumber });
            if (volumeInDb == null)
            {
                _logger.LogInformation($"Insert Volume: {volumeEntity.Id}");
                // add Volume to DB
                await sqLiteConnection.ExecuteAsync(SqlScripts.InsertVolumeSql, volumeEntity, transaction);
            }
            else
            {
                volumeEntity.Id = volumeInDb.Id;
                snapshotEntity.VolumeInfo.VolumeId = volumeInDb.Id;
            }

            _logger.LogInformation($"Insert VolumeInfo: {snapshotEntity.VolumeInfo.Id}");
            // add VolumeInfo to DB
            await sqLiteConnection.ExecuteAsync(SqlScripts.InsertVolumeInfoSql, snapshotEntity.VolumeInfo, transaction);

            // add Folder Tree to DB
            await AddFolderWithFilesRecursivelyAsync(sqLiteConnection, snapshotEntity.RootFolder!, null, snapshotEntity, transaction);

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

    private async Task AddFolderWithFilesRecursivelyAsync(IDbConnection sqLiteConnection, FsFolderEntity folderEntity, FsFolderEntity? parentFolderEntity, SnapshotEntity snapshotEntity, DbTransaction transaction)
    {
        try
        { // check if Folder in DB
            var folderInDb = await sqLiteConnection.QuerySingleOrDefaultAsync<FsFolderEntity>(SqlScripts.SelectFolderByNameAndParentFolderPathAndStorageDriveIdSql,
                new { folderEntity.Name, folderEntity.Size, folderEntity.Sha256Hash }, transaction);

            if (folderInDb == null) // Folder does not exist in DB
            {
                //_logger.LogInformation($"InsertFolderSql: {folderEntity.Id}");
                // add new Folder to DB
                await sqLiteConnection.ExecuteAsync(SqlScripts.InsertFolderSql, folderEntity, transaction);
            }
            else // Folder exists in DB
            {
                folderEntity.Id = folderInDb.Id;
            }

            await BindFolderWithFolderAndSnapshotAsync(sqLiteConnection, parentFolderEntity, folderEntity, snapshotEntity, transaction);

            // add child Folders (sorted by name)
            //var sortedChildFolders = folderEntity.ChildFolders.OrderBy(f => f.Name).ToList();
            foreach (var childFolder in folderEntity.ChildFolders)
            {
                await AddFolderWithFilesRecursivelyAsync(sqLiteConnection, childFolder, folderEntity, snapshotEntity, transaction);
            }

            // add Files (sorted by name)
            //var sortedFiles = folderEntity.Files.OrderBy(f => f.Name).ToList();
            foreach (var fileEntity in folderEntity.Files)
            { // check if File in DB
                var fileInDb = await sqLiteConnection.QuerySingleOrDefaultAsync<FsFileEntity>(SqlScripts.SelectFileByNameAndParentFolderPathAndStorageDriveIdSql,
                    new { fileEntity.Name, fileEntity.Size, fileEntity.FileExtension, fileEntity.Sha256Hash }, transaction);

                if (fileInDb == null) // File does not exist in DB
                {
                    //_logger.LogInformation($"InsertFileSql: {fileEntity.Id}");
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

    private async Task BindFolderWithFolderAndSnapshotAsync(IDbConnection sqLiteConnection, FsFolderEntity? parentFolderEntity, FsFolderEntity childFolderEntity, SnapshotEntity snapshotEntity, DbTransaction transaction)
    {
        try
        {
            // SelectFoldersToFoldersSql
            var folderToFolderInDb = await sqLiteConnection.QuerySingleOrDefaultAsync<FilesToFoldersEntity>(SqlScripts.SelectFoldersToFoldersSql,
                new { SnapshotId = snapshotEntity.Id, ParentFolderId = parentFolderEntity?.Id, ChildFolderId = childFolderEntity.Id }, transaction);

            if (folderToFolderInDb == null) // Item does not exist in DB
            {
                //_logger.LogInformation($"BindFolderWithFolderAndSnapshotAsync: {parentFolderEntity?.Id}, {childFolderEntity.Id}, {snapshotEntity.Id}");
                await sqLiteConnection.ExecuteAsync(SqlScripts.InsertFoldersToFoldersSql, new { ParentFolderId = parentFolderEntity?.Id, ChildFolderId = childFolderEntity.Id, SnapshotId = snapshotEntity.Id }, transaction);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - BindFolderWithFolderAndSnapshotAsync");
            throw;
        }
    }

    private async Task BindFileWithFolderAndSnapshotAsync(IDbConnection sqLiteConnection, FsFolderEntity folderEntity, FsFileEntity fileEntity, SnapshotEntity snapshotEntity, DbTransaction transaction)
    {
        try
        {
            // SelectFilesToFoldersSql
            var fileToFolderInDb = await sqLiteConnection.QuerySingleOrDefaultAsync<FilesToFoldersEntity>(SqlScripts.SelectFilesToFoldersSql,
                new { SnapshotId = snapshotEntity.Id, FolderId = folderEntity.Id, FileId = fileEntity.Id }, transaction);

            if (fileToFolderInDb == null) // Item does not exist in DB
            {
                //_logger.LogInformation($"BindFileWithFolderAndSnapshotAsync: {folderEntity.Id}, {folderEntity.Name}, {fileEntity.Id}, {fileEntity.Name}, {snapshotEntity.Id}");
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
                //_logger.LogInformation($"BindPcWithStorageDriveAndSnapshotAsync: {pcEntity.Id}, {storageDriveEntity.Id}, {snapshotEntity.Id}");
                await sqLiteConnection.ExecuteAsync(SqlScripts.InsertPcsToStorageDrivesSql, new { PcId = pcEntity.Id, StorageDriveId = storageDriveEntity.Id, SnapshotId = snapshotEntity.Id }, transaction);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - BindFileWithFolderAndSnapshotAsync");
            throw;
        }
    }

    private void SortFoldersAndFilesRecursively(FsFolderEntity folderEntity)
    {
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
}

public static class SqlScripts
{
    public const string SelectPcSql = "SELECT * FROM Pcs WHERE Name = @PcName AND DeviceId = @DeviceId;";
    public const string InsertPcSql = "INSERT INTO Pcs " +
                                        "(Id, Name, DeviceId) " +
                                        "VALUES " +
                                        "(@Id, @Name, @DeviceId)";
    public const string SelectStorageDriveSql = "SELECT * FROM StorageDrives WHERE SerialNumber = @SerialNumber AND DeviceId = @DeviceId AND Name = @Name;";
    public const string InsertStorageDriveSql = "INSERT INTO StorageDrives " +
                                                "(Id, Name, DeviceId, SerialNumber, TotalSize, Description, MediaType, InterfaceType) " +
                                                "VALUES " +
                                                "(@Id, @Name, @DeviceId, @SerialNumber, @TotalSize, @Description, @MediaType, @InterfaceType)";
    public const string SelectSnapshotsSql = "SELECT * FROM Snapshots WHERE StorageDriveId = @StorageDriveId;";
    public const string SelectLatestSnapshotWithSystemInfoSql = "SELECT * FROM Snapshots AS snapshot " +
                                                                    "LEFT JOIN VolumeInfos AS volinf ON volinf.SnapshotId == snapshot.Id " +
                                                                    "LEFT JOIN Volumes AS volume ON volinf.VolumeId == volume.Id " +
                                                                    "LEFT JOIN StorageDrives AS stdrv ON volume.StorageDriveId = stdrv.Id " +
                                                                    "LEFT JOIN PcsToStorageDrives AS pc2stdrv ON pc2stdrv.SnapshotId = snapshot.Id AND pc2stdrv.StorageDriveId = stdrv.Id " +
                                                                    "LEFT JOIN Pcs AS pc ON pc2stdrv.PcId = pc.Id " +
                                                                    "ORDER BY snapshot.Timestamp DESC LIMIT 1;";
    public const string SelectSnapshotByIdSql = "SELECT * FROM Snapshots AS snapshot " +
                                                    "LEFT JOIN VolumeInfos AS volinf ON volinf.SnapshotId == snapshot.Id " +
                                                    "LEFT JOIN Volumes AS volume ON volinf.VolumeId == volume.Id " +
                                                    "LEFT JOIN StorageDrives AS stdrv ON volume.StorageDriveId = stdrv.Id " +
                                                    "LEFT JOIN PcsToStorageDrives AS pc2stdrv ON pc2stdrv.SnapshotId = snapshot.Id AND pc2stdrv.StorageDriveId = stdrv.Id " +
                                                    "LEFT JOIN Pcs AS pc ON pc2stdrv.PcId = pc.Id " +
                                                    "WHERE snapshot.Id = @SnapshotId;";
    public const string SelectSnapshotsWithSystemInfoSql = "SELECT * FROM Snapshots AS snapshot " +
                                                            "LEFT JOIN VolumeInfos AS volinf ON volinf.SnapshotId == snapshot.Id " +
                                                            "LEFT JOIN Volumes AS volume ON volinf.VolumeId == volume.Id " +
                                                            "LEFT JOIN StorageDrives AS stdrv ON volume.StorageDriveId = stdrv.Id " +
                                                            "LEFT JOIN PcsToStorageDrives AS pc2stdrv ON pc2stdrv.SnapshotId = snapshot.Id AND pc2stdrv.StorageDriveId = stdrv.Id " +
                                                            "LEFT JOIN Pcs AS pc ON pc2stdrv.PcId = pc.Id " +
                                                            "LEFT JOIN FoldersToFolders AS fl2fl ON fl2fl.SnapshotId = snapshot.Id " +
                                                            "LEFT JOIN Folders AS folder ON folder.Id = fl2fl.ChildFolderId " +
                                                            "WHERE fl2fl.ParentFolderId is NULL " +
                                                            "ORDER BY snapshot.Timestamp DESC;";
    public const string SelectFoldersAndFilesBySnapshotSql = "SELECT * FROM Folders AS folder " +
                                                                "LEFT JOIN FoldersToFolders AS fl2fl ON fl2fl.ChildFolderId = folder.Id " +
                                                                "LEFT JOIN FilesToFolders AS fi2fl ON fi2fl.FolderId = folder.Id AND fi2fl.SnapshotId = @SnapshotId " +
                                                                "LEFT JOIN Files AS file ON fi2fl.FileId = file.Id " +
                                                                "WHERE fl2fl.SnapshotId = @SnapshotId;";
    public const string InsertSnapshotSql = "INSERT INTO Snapshots " +
                                            "(Id, Timestamp) " +
                                            "VALUES " +
                                            "(@Id, @Timestamp)";
    public const string SelectVolumeSql = "SELECT * FROM Volumes WHERE VolumeSerialNumber = @VolumeSerialNumber;";
    public const string InsertVolumeSql = "INSERT INTO Volumes " +
                                            "(Id, DriveLetter, VolumeName, Description, VolumeSerialNumber, VolumeSize, StorageDriveId) " +
                                            "VALUES " +
                                            "(@Id, @DriveLetter, @VolumeName, @Description, @VolumeSerialNumber, @VolumeSize, @StorageDriveId)";
    public const string SelectVolumeInfoSql = "SELECT * FROM VolumeInfos WHERE VolumeSerialNumber = @VolumeSerialNumber;";
    public const string InsertVolumeInfoSql = "INSERT INTO VolumeInfos " +
                                                "(Id, FreeSpace, DriveStatus, VolumeId, SnapshotId) " +
                                                "VALUES " +
                                                "(@Id, @FreeSpace, @DriveStatus, @VolumeId, @SnapshotId)";
    public const string SelectFolderByNameAndParentFolderPathAndStorageDriveIdSql = "SELECT * FROM Folders " +
                                                                                        "WHERE Name = @Name " +
                                                                                        "AND Size = @Size " +
                                                                                        "AND Sha256Hash = @Sha256Hash;";
    public const string SelectFoldersByNameSql = "SELECT * FROM Folders " +
                                                    "WHERE Name = @Name;";
    public const string InsertFolderSql = "INSERT INTO Folders " +
                                            "(Id, Name, Size, Sha256Hash) " +
                                            "VALUES " +
                                            "(@Id, @Name, @Size, @Sha256Hash)";
    public const string UpdateFolderHashSql = "UPDATE Folders " +
                                                "SET Sha256Hash = @Sha256Hash " +
                                                "WHERE Id = @Id;";
    public const string InsertFileSql = "INSERT INTO Files " +
                                            "(Id, Name, Size, FileExtension,Sha256Hash) " +
                                            "VALUES " +
                                            "(@Id, @Name, @Size, @FileExtension, @Sha256Hash)";
    public const string SelectFilesByNameAndExtensionSql = "SELECT * FROM Files " +
                                                                "WHERE Name = @Name " +
                                                                "AND FileExtension = @FileExtension;";
    public const string SelectFileByNameAndParentFolderPathAndStorageDriveIdSql = "SELECT * FROM Files " +
                                                                                    "WHERE Name = @Name " +
                                                                                    "AND Size = @Size " +
                                                                                    "AND FileExtension = @FileExtension " +
                                                                                    "AND Sha256Hash = @Sha256Hash;";
    public const string InsertFoldersToFoldersSql = "INSERT INTO FoldersToFolders " +
                                                    "(ParentFolderId, ChildFolderId, SnapshotId) " +
                                                    "VALUES " +
                                                    "(@ParentFolderId, @ChildFolderId, @SnapshotId)";
    public const string SelectFoldersToFoldersSql = "SELECT * FROM FoldersToFolders " +
                                                    "WHERE SnapshotId = @SnapshotId  " +
                                                    "AND ParentFolderId = @ParentFolderId " +
                                                    "AND ChildFolderId = @ChildFolderId;";
    public const string InsertFilesToFoldersSql = "INSERT INTO FilesToFolders " +
                                                    "(FolderId, FileId, SnapshotId) " +
                                                    "VALUES " +
                                                    "(@FolderId, @FileId, @SnapshotId)";
    public const string SelectFilesToFoldersSql = "SELECT * FROM FilesToFolders " +
                                                    "WHERE SnapshotId = @SnapshotId  " +
                                                    "AND FolderId = @FolderId " +
                                                    "AND FileId = @FileId;";
    public const string InsertPcsToStorageDrivesSql = "INSERT INTO PcsToStorageDrives " +
                                                        "(PcId, StorageDriveId, SnapshotId) " +
                                                        "VALUES " +
                                                        "(@PcId, @StorageDriveId, @SnapshotId)";
    public const string SelectPcsToStorageDrivesSql = "SELECT * FROM PcsToStorageDrives " +
                                                    "WHERE SnapshotId = @SnapshotId  " +
                                                    "AND PcId = @PcId " +
                                                    "AND StorageDriveId = @StorageDriveId;";
    public const string ClearDataInTablesSql = "PRAGMA foreign_keys = OFF;" +
                                                "DROP TABLE IF EXISTS PcsToStorageDrives;" +
                                                "DROP TABLE IF EXISTS FoldersToFolders;" +
                                                "DROP TABLE IF EXISTS FilesToFolders;" +
                                                "DROP TABLE IF EXISTS Pcs;" +
                                                "DROP TABLE IF EXISTS StorageDrives;" +
                                                "DROP TABLE IF EXISTS Volumes;" +
                                                "DROP TABLE IF EXISTS Folders;" +
                                                "DROP TABLE IF EXISTS Snapshots;" +
                                                "DROP TABLE IF EXISTS VolumeInfos;" +
                                                "DROP TABLE IF EXISTS Files;" +
                                                "PRAGMA foreign_keys = ON;";
}
