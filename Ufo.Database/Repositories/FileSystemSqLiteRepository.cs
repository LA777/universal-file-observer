using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Data;
using System.Data.Common;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.Options;
using Ufo.Database.Contexts;

namespace Ufo.Database.Repositories
{
    public class FileSystemSqLiteRepository : IFileSystemSqLiteRepository
    {
        private readonly ILogger<FileSystemSqLiteRepository> _logger;
        private readonly IOptionsMonitor<ApplicationSettings> _applicationSettings;

        public FileSystemSqLiteRepository(IOptionsMonitor<ApplicationSettings> applicationSettings, ILogger<FileSystemSqLiteRepository>? logger)
        {
            _applicationSettings = applicationSettings ?? throw new ArgumentNullException(nameof(applicationSettings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task InitiateDatabase()
        {
            try
            {
                await DapperDataContext.InitiateDatabaseAsync(_applicationSettings.CurrentValue.SqliteDbConnectionStrings);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "ERROR - InitiateDatabase");
                throw;
            }
        }

        public async Task DropDataInTables()
        {
            try
            {
                await using var sqLiteConnection = new SqliteConnection(_applicationSettings.CurrentValue.SqliteDbConnectionStrings);
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
                await InitiateDatabase();
                await using var sqLiteConnection = new SqliteConnection(_applicationSettings.CurrentValue.SqliteDbConnectionStrings);
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
                await InitiateDatabase();
                await using var sqLiteConnection = new SqliteConnection(_applicationSettings.CurrentValue.SqliteDbConnectionStrings);
                var folderEntities = await sqLiteConnection.QueryAsync<FsFolderEntity>(SqlScripts.SelectFoldersByNameSql, new { Name = name });

                return folderEntities;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "ERROR - GetFoldersByNameAsync");
                throw;
            }
        }

        public async Task<SnapshotEntity> GetSnapshotByGuidAsync(Guid snapshotGuid, CancellationToken cancellationToken = default)
        {
            try
            {
                await InitiateDatabase();
                await using var sqLiteConnection = new SqliteConnection(_applicationSettings.CurrentValue.SqliteDbConnectionStrings);
                SnapshotEntity snapshotResult = null;

                await sqLiteConnection
                .QueryAsync<SnapshotEntity, VolumeInfoEntity, VolumeEntity, StorageDriveEntity, PcsToStorageDrivesEntity, PcEntity, SnapshotEntity>(
                    SqlScripts.SelectSnapshotByGuidSql,
                    (snapshotEntity, volumeInfoEntity, volumeEntity, storageDriveEntity, _, pcEntity) =>
                    {
                        if (snapshotResult == null)
                        {
                            snapshotResult = snapshotEntity;
                            snapshotResult.VolumeInfo = volumeInfoEntity;
                            volumeInfoEntity.Snapshot = snapshotResult;
                            volumeInfoEntity.SnapshotGuid = snapshotResult.Guid;
                            volumeInfoEntity.Volume = volumeEntity;
                            volumeInfoEntity.VolumeGuid = volumeEntity.Guid;
                            volumeEntity.VolumeInfos.Add(volumeInfoEntity);
                            volumeEntity.StorageDrive = storageDriveEntity;
                            volumeEntity.StorageDriveGuid = storageDriveEntity.Guid;
                            storageDriveEntity.Snapshots.Add(snapshotResult);
                            storageDriveEntity.Volumes.Add(volumeEntity);
                            storageDriveEntity.Pcs.Add(pcEntity);
                            pcEntity.Snapshots.Add(snapshotResult);
                            pcEntity.StorageDrives.Add(storageDriveEntity);
                        }

                        return snapshotResult;
                    },
                    splitOn: "Guid, Guid, Guid, PcGuid, Guid",
                    param: new { SnapshotGuid = snapshotGuid });

                if (snapshotResult == null)
                {
                    throw new ArgumentNullException(nameof(snapshotResult));
                }

                FsFolderEntity currentFolder = null;
                var folders = new Dictionary<Guid, FsFolderEntity>();
                var childFolders = new Dictionary<Guid, IList<FsFolderEntity>>();
                await sqLiteConnection
                    .QueryAsync<FsFolderEntity, FoldersToFoldersEntity, FilesToFoldersEntity, FsFileEntity, FsFolderEntity>(
                        SqlScripts.SelectFoldersAndFilesBySnapshotSql,
                        (fsFolderEntity, foldersToFoldersEntity, filesToFoldersEntity, fsFileEntity) =>
                        {
                            folders.TryAdd(fsFolderEntity.Guid, fsFolderEntity);

                            // check if Folder already added
                            if (snapshotResult.RootFolder == null)
                            {
                                if (foldersToFoldersEntity.ParentFolderGuid == null)
                                {
                                    snapshotResult.RootFolder = fsFolderEntity;
                                    currentFolder = fsFolderEntity;
                                }
                                else
                                {
                                    throw new ApplicationException("Shit!");
                                }
                            }

                            var currentFolderParentFolder = currentFolder?.ParentFolders.FirstOrDefault();
                            var currentFolderParentFolderGuid = currentFolderParentFolder?.Guid;

                            if (currentFolder.Guid != fsFolderEntity.Guid || currentFolderParentFolderGuid != foldersToFoldersEntity.ParentFolderGuid)
                            {
                                // find ParentFolder
                                var parentFolderWasFound = folders.TryGetValue(foldersToFoldersEntity.ParentFolderGuid.Value, out var parentFolder);
                                if (parentFolderWasFound)
                                {
                                    parentFolder.ChildFolders.Add(fsFolderEntity);
                                    fsFolderEntity.ParentFolders.Add(parentFolder);
                                }
                                else
                                {
                                    var childFolderWasFound1 = childFolders.TryGetValue(foldersToFoldersEntity.ParentFolderGuid.Value, out var childFolderList);
                                    if (childFolderWasFound1)
                                    {
                                        childFolderList.Add(fsFolderEntity);
                                    }
                                    else
                                    {
                                        childFolders.Add(foldersToFoldersEntity.ParentFolderGuid.Value, new List<FsFolderEntity> { fsFolderEntity });
                                    }
                                }

                                // find ChildFolders
                                var childFoldersWasFound = childFolders.TryGetValue(fsFolderEntity.Guid, out var childFoldersList);
                                if (childFoldersWasFound)
                                {
                                    foreach (var childFolder in childFoldersList)
                                    {
                                        childFolder.ParentFolders.Add(fsFolderEntity);
                                        fsFolderEntity.ChildFolders.Add(childFolder);
                                    }

                                    childFolders.Remove(fsFolderEntity.Guid);
                                }

                                currentFolder = fsFolderEntity;
                            }

                            if (fsFileEntity != null)
                            {
                                fsFileEntity.Snapshots.Add(snapshotResult);
                                fsFileEntity.ParentFolders.Add(currentFolder);
                                currentFolder.Files.Add(fsFileEntity);
                            }

                            return currentFolder;
                        },
                        param: new { SnapshotGuid = snapshotGuid },
                        splitOn: "SnapshotGuid, SnapshotGuid, Guid");

                return snapshotResult;

            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "ERROR - GetSnapshotByGuidAsync");
                throw;
            }
        }

        public async Task<SnapshotEntity> GetLatestSnapshotWithAllEntitiesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await InitiateDatabase();
                await using var sqLiteConnection = new SqliteConnection(_applicationSettings.CurrentValue.SqliteDbConnectionStrings);
                SnapshotEntity snapshotResult = null;

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
                            volumeInfoEntity.SnapshotGuid = snapshotResult.Guid;
                            volumeInfoEntity.Volume = volumeEntity;
                            volumeInfoEntity.VolumeGuid = volumeEntity.Guid;
                            volumeEntity.VolumeInfos.Add(volumeInfoEntity);
                            volumeEntity.StorageDrive = storageDriveEntity;
                            volumeEntity.StorageDriveGuid = storageDriveEntity.Guid;
                            storageDriveEntity.Snapshots.Add(snapshotResult);
                            storageDriveEntity.Volumes.Add(volumeEntity);
                            storageDriveEntity.Pcs.Add(pcEntity);
                            pcEntity.Snapshots.Add(snapshotResult);
                            pcEntity.StorageDrives.Add(storageDriveEntity);
                        }

                        return snapshotResult;
                    },
                    splitOn: "Guid, Guid, Guid, PcGuid, Guid");

                if (snapshotResult == null)
                {
                    throw new ArgumentNullException(nameof(snapshotResult));
                }

                FsFolderEntity currentFolder = null;
                var folders = new Dictionary<Guid, FsFolderEntity>();
                var childFolders = new Dictionary<Guid, IList<FsFolderEntity>>();
                await sqLiteConnection
                    .QueryAsync<FsFolderEntity, FoldersToFoldersEntity, FilesToFoldersEntity, FsFileEntity, FsFolderEntity>(
                        SqlScripts.SelectFoldersAndFilesBySnapshotSql,
                        (fsFolderEntity, foldersToFoldersEntity, filesToFoldersEntity, fsFileEntity) =>
                        {
                            folders.TryAdd(fsFolderEntity.Guid, fsFolderEntity);

                            // check if Folder already added
                            if (snapshotResult.RootFolder == null)
                            {
                                if (foldersToFoldersEntity.ParentFolderGuid == null)
                                {
                                    snapshotResult.RootFolder = fsFolderEntity;
                                    currentFolder = fsFolderEntity;
                                }
                                else
                                {
                                    throw new ApplicationException("Shit!");
                                }
                            }

                            var currentFolderParentFolder = currentFolder?.ParentFolders.FirstOrDefault();
                            var currentFolderParentFolderGuid = currentFolderParentFolder?.Guid;

                            if (currentFolder.Guid != fsFolderEntity.Guid || currentFolderParentFolderGuid != foldersToFoldersEntity.ParentFolderGuid)
                            {
                                // find ParentFolder
                                var parentFolderWasFound = folders.TryGetValue(foldersToFoldersEntity.ParentFolderGuid.Value, out var parentFolder);
                                if (parentFolderWasFound)
                                {
                                    parentFolder.ChildFolders.Add(fsFolderEntity);
                                    fsFolderEntity.ParentFolders.Add(parentFolder);
                                }
                                else
                                {
                                    var childFolderWasFound1 = childFolders.TryGetValue(foldersToFoldersEntity.ParentFolderGuid.Value, out var childFolderList);
                                    if (childFolderWasFound1)
                                    {
                                        childFolderList.Add(fsFolderEntity);
                                    }
                                    else
                                    {
                                        childFolders.Add(foldersToFoldersEntity.ParentFolderGuid.Value, new List<FsFolderEntity> { fsFolderEntity });
                                    }
                                }

                                // find ChildFolders
                                var childFoldersWasFound = childFolders.TryGetValue(fsFolderEntity.Guid, out var childFoldersList);
                                if (childFoldersWasFound)
                                {
                                    foreach (var childFolder in childFoldersList)
                                    {
                                        childFolder.ParentFolders.Add(fsFolderEntity);
                                        fsFolderEntity.ChildFolders.Add(childFolder);
                                    }

                                    childFolders.Remove(fsFolderEntity.Guid);
                                }

                                currentFolder = fsFolderEntity;
                            }

                            if (fsFileEntity != null)
                            {
                                fsFileEntity.Snapshots.Add(snapshotResult);
                                fsFileEntity.ParentFolders.Add(currentFolder);
                                currentFolder.Files.Add(fsFileEntity);
                            }

                            return currentFolder;
                        },
                        param: new { SnapshotGuid = snapshotResult.Guid },
                        splitOn: "SnapshotGuid, SnapshotGuid, Guid");

                return snapshotResult;

            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "ERROR - GetLatestSnapshotWithAllEntitiesAsync");
                throw;
            }
        }

        public async Task<IList<SnapshotEntity>> GetSnapshotsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await InitiateDatabase();
                await using var sqLiteConnection = new SqliteConnection(_applicationSettings.CurrentValue.SqliteDbConnectionStrings);

                var snapshots = new List<SnapshotEntity>();

                await sqLiteConnection
                .QueryAsync<SnapshotEntity, VolumeInfoEntity, VolumeEntity, StorageDriveEntity, PcEntity, FsFolderEntity, SnapshotEntity>(
                    SqlScripts.SelectSnapshotsWithSystemInfoSql,
                    (snapshotEntity, volumeInfoEntity, volumeEntity, storageDriveEntity, pcEntity, fsFolderEntity) =>
                    {
                        snapshotEntity.VolumeInfo = volumeInfoEntity;
                        volumeInfoEntity.Snapshot = snapshotEntity;
                        volumeInfoEntity.SnapshotGuid = snapshotEntity.Guid;
                        volumeInfoEntity.Volume = volumeEntity;
                        volumeInfoEntity.VolumeGuid = volumeEntity.Guid;
                        volumeEntity.VolumeInfos.Add(volumeInfoEntity);
                        volumeEntity.StorageDrive = storageDriveEntity;
                        volumeEntity.StorageDriveGuid = storageDriveEntity.Guid;
                        storageDriveEntity.Snapshots.Add(snapshotEntity);
                        storageDriveEntity.Volumes.Add(volumeEntity);
                        storageDriveEntity.Pcs.Add(pcEntity);
                        pcEntity.Snapshots.Add(snapshotEntity);
                        pcEntity.StorageDrives.Add(storageDriveEntity);
                        snapshotEntity.RootFolder = fsFolderEntity;

                        snapshots.Add(snapshotEntity);

                        return snapshotEntity;
                    },
                    splitOn: "Guid, Guid, Guid, Guid, Guid");

                return snapshots;

            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "ERROR - GetLatestSnapshotWithAllEntitiesAsync");
                throw;
            }
        }

        public async Task<int> AddDataAsync(SnapshotEntity snapshotEntity, CancellationToken cancellationToken = default)
        {
            try
            {
                await InitiateDatabase();
                await using var sqLiteConnection = new SqliteConnection(_applicationSettings.CurrentValue.SqliteDbConnectionStrings);
                await sqLiteConnection.OpenAsync(cancellationToken);
                await using var transaction = await sqLiteConnection.BeginTransactionAsync(cancellationToken);

                try
                {
                    _logger.LogInformation($"Insert snapshot: {snapshotEntity.Guid}");
                    // add snapshot to DB
                    await sqLiteConnection.ExecuteAsync(SqlScripts.InsertSnapshotSql, snapshotEntity, transaction);

                    var volumeEntity = snapshotEntity.VolumeInfo.Volume;
                    var storageDriveEntity = volumeEntity.StorageDrive;

                    // Find same PC in DB
                    var pcEntity = storageDriveEntity.Pcs[0];
                    var pcInDb = await sqLiteConnection.QuerySingleOrDefaultAsync<PcEntity>(SqlScripts.SelectPcSql, new { PcName = pcEntity.Name });
                    if (pcInDb == null)
                    {
                        _logger.LogInformation($"Insert PC: {pcEntity.Guid}");
                        // add pc to DB
                        await sqLiteConnection.ExecuteAsync(SqlScripts.InsertPcSql, pcEntity, transaction);
                    }
                    else
                    {
                        pcEntity.Guid = pcInDb.Guid;
                    }

                    var storageDriveInDb = await sqLiteConnection.QuerySingleOrDefaultAsync<StorageDriveEntity>(SqlScripts.SelectStorageDriveSql,
                        new { storageDriveEntity.SerialNumber, storageDriveEntity.DeviceId, storageDriveEntity.Name });
                    if (storageDriveInDb == null)
                    {
                        _logger.LogInformation($"Insert StorageDrive: {storageDriveEntity.Guid}");
                        // add StorageDrive to DB
                        await sqLiteConnection.ExecuteAsync(SqlScripts.InsertStorageDriveSql, storageDriveEntity, transaction);
                    }
                    else
                    {
                        storageDriveEntity.Guid = storageDriveInDb.Guid;
                    }

                    // bind PC with StorageDrive
                    await BindPcWithStorageDriveAndSnapshotAsync(sqLiteConnection, pcEntity, storageDriveEntity, snapshotEntity, transaction);

                    // Check if Volume is in DB
                    var volumeInDb = await sqLiteConnection.QuerySingleOrDefaultAsync<VolumeEntity>(SqlScripts.SelectVolumeSql,
                        new { volumeEntity.VolumeSerialNumber });
                    if (volumeInDb == null)
                    {
                        _logger.LogInformation($"Insert Volume: {volumeEntity.Guid}");
                        // add Volume to DB
                        await sqLiteConnection.ExecuteAsync(SqlScripts.InsertVolumeSql, volumeEntity, transaction);
                    }
                    else
                    {
                        volumeEntity.Guid = volumeInDb.Guid;
                        snapshotEntity.VolumeInfo.VolumeGuid = volumeInDb.Guid;
                    }

                    _logger.LogInformation($"Insert VolumeInfo: {snapshotEntity.VolumeInfo.Guid}");
                    // add VolumeInfo to DB
                    await sqLiteConnection.ExecuteAsync(SqlScripts.InsertVolumeInfoSql, snapshotEntity.VolumeInfo, transaction);

                    // add Folder Tree to DB
                    await AddFolderWithFilesRecursivelyAsync(sqLiteConnection, snapshotEntity.RootFolder, null, snapshotEntity, transaction);

                    await transaction.CommitAsync(cancellationToken);
                }
                catch (Exception exception)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }

                return 1;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "ERROR - AddDataAsync");
                throw;
            }
        }

        private async Task AddFolderWithFilesRecursivelyAsync(IDbConnection sqLiteConnection, FsFolderEntity folderEntity, FsFolderEntity? parentFolderEntity, SnapshotEntity snapshotEntity, DbTransaction transaction)
        {
            try
            { // check if Folder in DB
                var folderInDb = await sqLiteConnection.QuerySingleOrDefaultAsync<FsFolderEntity>(SqlScripts.SelectFolderByNameAndParentFolderPathAndStorageDriveGuidSql,
                    new { folderEntity.Name, folderEntity.Size, folderEntity.Sha256Hash }, transaction);

                if (folderInDb == null) // Folder does not exist in DB
                {
                    //_logger.LogInformation($"InsertFolderSql: {folderEntity.Guid}");
                    // add new Folder to DB
                    await sqLiteConnection.ExecuteAsync(SqlScripts.InsertFolderSql, folderEntity, transaction);
                }
                else // Folder exists in DB
                {
                    folderEntity.Guid = folderInDb.Guid;
                }

                await BindFolderWithFolderAndSnapshotAsync(sqLiteConnection, parentFolderEntity, folderEntity, snapshotEntity, transaction);

                // add child Folders
                foreach (var childFolder in folderEntity.ChildFolders)
                {
                    await AddFolderWithFilesRecursivelyAsync(sqLiteConnection, childFolder, folderEntity, snapshotEntity, transaction);
                }

                // add Files
                foreach (var fileEntity in folderEntity.Files)
                { // check if File in DB
                    var fileInDb = await sqLiteConnection.QuerySingleOrDefaultAsync<FsFileEntity>(SqlScripts.SelectFileByNameAndParentFolderPathAndStorageDriveGuidSql,
                        new { fileEntity.Name, fileEntity.Size, fileEntity.FileExtension, fileEntity.Sha256Hash }, transaction);

                    if (fileInDb == null) // File does not exist in DB
                    {
                        //_logger.LogInformation($"InsertFileSql: {fileEntity.Guid}");
                        // add new File to DB
                        await sqLiteConnection.ExecuteAsync(SqlScripts.InsertFileSql, fileEntity, transaction);
                    }
                    else
                    {
                        fileEntity.Guid = fileInDb.Guid;
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
                    new { SnapshotGuid = snapshotEntity.Guid, ParentFolderGuid = parentFolderEntity?.Guid, ChildFolderGuid = childFolderEntity.Guid }, transaction);

                if (folderToFolderInDb == null) // Item does not exist in DB
                {
                    //_logger.LogInformation($"BindFolderWithFolderAndSnapshotAsync: {parentFolderEntity?.Guid}, {childFolderEntity.Guid}, {snapshotEntity.Guid}");
                    await sqLiteConnection.ExecuteAsync(SqlScripts.InsertFoldersToFoldersSql, new { ParentFolderGuid = parentFolderEntity?.Guid, ChildFolderGuid = childFolderEntity.Guid, SnapshotGuid = snapshotEntity.Guid }, transaction);
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
                    new { SnapshotGuid = snapshotEntity.Guid, FolderGuid = folderEntity.Guid, FileGuid = fileEntity.Guid }, transaction);

                if (fileToFolderInDb == null) // Item does not exist in DB
                {
                    //_logger.LogInformation($"BindFileWithFolderAndSnapshotAsync: {folderEntity.Guid}, {folderEntity.Name}, {fileEntity.Guid}, {fileEntity.Name}, {snapshotEntity.Guid}");
                    await sqLiteConnection.ExecuteAsync(SqlScripts.InsertFilesToFoldersSql, new { FolderGuid = folderEntity.Guid, FileGuid = fileEntity.Guid, SnapshotGuid = snapshotEntity.Guid }, transaction);
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
                    new { SnapshotGuid = snapshotEntity.Guid, PcGuid = pcEntity.Guid, StorageDriveGuid = storageDriveEntity.Guid }, transaction);

                if (pcToStorageDriveInDb == null) // Item does not exist in DB
                {
                    //_logger.LogInformation($"BindPcWithStorageDriveAndSnapshotAsync: {pcEntity.Guid}, {storageDriveEntity.Guid}, {snapshotEntity.Guid}");
                    await sqLiteConnection.ExecuteAsync(SqlScripts.InsertPcsToStorageDrivesSql, new { PcGuid = pcEntity.Guid, StorageDriveGuid = storageDriveEntity.Guid, SnapshotGuid = snapshotEntity.Guid }, transaction);
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "ERROR - BindFileWithFolderAndSnapshotAsync");
                throw;
            }
        }
    }

    public static class SqlScripts
    {
        public const string SelectPcSql = "SELECT * FROM Pcs WHERE Name = @PcName;";
        public const string InsertPcSql = "INSERT INTO Pcs " +
                                            "(Guid, Name) " +
                                            "VALUES " +
                                            "(@Guid, @Name)";
        public const string SelectStorageDriveSql = "SELECT * FROM StorageDrives WHERE SerialNumber = @SerialNumber AND DeviceId = @DeviceId AND Name = @Name;";
        public const string InsertStorageDriveSql = "INSERT INTO StorageDrives " +
                                                    "(Guid, Name, DeviceId, SerialNumber, TotalSize, Description, MediaType, InterfaceType) " +
                                                    "VALUES " +
                                                    "(@Guid, @Name, @DeviceId, @SerialNumber, @TotalSize, @Description, @MediaType, @InterfaceType)";
        public const string SelectSnapshotsSql = "SELECT * FROM Snapshots WHERE StorageDriveGuid = @StorageDriveGuid;";
        public const string SelectLatestSnapshotWithSystemInfoSql = "SELECT * FROM Snapshots AS snapshot " +
                                                                        "LEFT JOIN VolumeInfos AS volinf ON volinf.SnapshotGuid == snapshot.Guid " +
                                                                        "LEFT JOIN Volumes AS volume ON volinf.VolumeGuid == volume.Guid " +
                                                                        "LEFT JOIN StorageDrives AS stdrv ON volume.StorageDriveGuid = stdrv.Guid " +
                                                                        "LEFT JOIN PcsToStorageDrives AS pc2stdrv ON pc2stdrv.SnapshotGuid = snapshot.Guid AND pc2stdrv.StorageDriveGuid = stdrv.Guid " +
                                                                        "LEFT JOIN Pcs AS pc ON pc2stdrv.PcGuid = pc.Guid " +
                                                                        "ORDER BY snapshot.Timestamp DESC LIMIT 1;";
        public const string SelectSnapshotByGuidSql = "SELECT * FROM Snapshots AS snapshot " +
                                                        "LEFT JOIN VolumeInfos AS volinf ON volinf.SnapshotGuid == snapshot.Guid " +
                                                        "LEFT JOIN Volumes AS volume ON volinf.VolumeGuid == volume.Guid " +
                                                        "LEFT JOIN StorageDrives AS stdrv ON volume.StorageDriveGuid = stdrv.Guid " +
                                                        "LEFT JOIN PcsToStorageDrives AS pc2stdrv ON pc2stdrv.SnapshotGuid = snapshot.Guid AND pc2stdrv.StorageDriveGuid = stdrv.Guid " +
                                                        "LEFT JOIN Pcs AS pc ON pc2stdrv.PcGuid = pc.Guid " +
                                                        "WHERE snapshot.Guid = @SnapshotGuid;";
        public const string SelectSnapshotsWithSystemInfoSql = "SELECT * FROM Snapshots AS snapshot " +
                                                                "LEFT JOIN VolumeInfos AS volinf ON volinf.SnapshotGuid == snapshot.Guid " +
                                                                "LEFT JOIN Volumes AS volume ON volinf.VolumeGuid == volume.Guid " +
                                                                "LEFT JOIN StorageDrives AS stdrv ON volume.StorageDriveGuid = stdrv.Guid " +
                                                                "LEFT JOIN PcsToStorageDrives AS pc2stdrv ON pc2stdrv.SnapshotGuid = snapshot.Guid AND pc2stdrv.StorageDriveGuid = stdrv.Guid " +
                                                                "LEFT JOIN Pcs AS pc ON pc2stdrv.PcGuid = pc.Guid " +
                                                                "LEFT JOIN FoldersToFolders AS fl2fl ON fl2fl.SnapshotGuid = snapshot.Guid " +
                                                                "LEFT JOIN Folders AS folder ON folder.Guid = fl2fl.ChildFolderGuid " +
                                                                "WHERE fl2fl.ParentFolderGuid is NULL " +
                                                                "ORDER BY snapshot.Timestamp DESC;";
        public const string SelectFoldersAndFilesBySnapshotSql = "SELECT * FROM Folders AS folder " +
                                                                    "LEFT JOIN FoldersToFolders AS fl2fl ON fl2fl.ChildFolderGuid = folder.Guid " +
                                                                    "LEFT JOIN FilesToFolders AS fi2fl ON fi2fl.FolderGuid = folder.Guid AND fi2fl.SnapshotGuid = @SnapshotGuid " +
                                                                    "LEFT JOIN Files AS file ON fi2fl.FileGuid = file.Guid " +
                                                                    "WHERE fl2fl.SnapshotGuid = @SnapshotGuid;";
        public const string InsertSnapshotSql = "INSERT INTO Snapshots " +
                                                "(Guid, Timestamp) " +
                                                "VALUES " +
                                                "(@Guid, @Timestamp)";
        public const string SelectVolumeSql = "SELECT * FROM Volumes WHERE VolumeSerialNumber = @VolumeSerialNumber;";
        public const string InsertVolumeSql = "INSERT INTO Volumes " +
                                                "(Guid, DriveLetter, VolumeName, Description, VolumeSerialNumber, VolumeSize, StorageDriveGuid) " +
                                                "VALUES " +
                                                "(@Guid, @DriveLetter, @VolumeName, @Description, @VolumeSerialNumber, @VolumeSize, @StorageDriveGuid)";
        public const string SelectVolumeInfoSql = "SELECT * FROM VolumeInfos WHERE VolumeSerialNumber = @VolumeSerialNumber;";
        public const string InsertVolumeInfoSql = "INSERT INTO VolumeInfos " +
                                                    "(Guid, FreeSpace, DriveStatus, VolumeGuid, SnapshotGuid) " +
                                                    "VALUES " +
                                                    "(@Guid, @FreeSpace, @DriveStatus, @VolumeGuid, @SnapshotGuid)";
        public const string SelectFolderByNameAndParentFolderPathAndStorageDriveGuidSql = "SELECT * FROM Folders " +
                                                                                            "WHERE Name = @Name " +
                                                                                            "AND Size = @Size " +
                                                                                            "AND Sha256Hash = @Sha256Hash;";
        public const string SelectFoldersByNameSql = "SELECT * FROM Folders " +
                                                        "WHERE Name = @Name;";
        public const string InsertFolderSql = "INSERT INTO Folders " +
                                                "(Guid, Name, Size, Sha256Hash) " +
                                                "VALUES " +
                                                "(@Guid, @Name, @Size, @Sha256Hash)";
        public const string UpdateFolderHashSql = "UPDATE Folders " +
                                                    "SET Sha256Hash = @Sha256Hash " +
                                                    "WHERE Guid = @Guid;";
        public const string InsertFileSql = "INSERT INTO Files " +
                                                "(Guid, Name, Size, FileExtension,Sha256Hash) " +
                                                "VALUES " +
                                                "(@Guid, @Name, @Size, @FileExtension, @Sha256Hash)";
        public const string SelectFilesByNameAndExtensionSql = "SELECT * FROM Files " +
                                                                    "WHERE Name = @Name " +
                                                                    "AND FileExtension = @FileExtension;";
        public const string SelectFileByNameAndParentFolderPathAndStorageDriveGuidSql = "SELECT * FROM Files " +
                                                                                        "WHERE Name = @Name " +
                                                                                        "AND Size = @Size " +
                                                                                        "AND FileExtension = @FileExtension " +
                                                                                        "AND Sha256Hash = @Sha256Hash;";
        public const string InsertFoldersToFoldersSql = "INSERT INTO FoldersToFolders " +
                                                        "(ParentFolderGuid, ChildFolderGuid, SnapshotGuid) " +
                                                        "VALUES " +
                                                        "(@ParentFolderGuid, @ChildFolderGuid, @SnapshotGuid)";
        public const string SelectFoldersToFoldersSql = "SELECT * FROM FoldersToFolders " +
                                                        "WHERE SnapshotGuid = @SnapshotGuid  " +
                                                        "AND ParentFolderGuid = @ParentFolderGuid " +
                                                        "AND ChildFolderGuid = @ChildFolderGuid;";
        public const string InsertFilesToFoldersSql = "INSERT INTO FilesToFolders " +
                                                        "(FolderGuid, FileGuid, SnapshotGuid) " +
                                                        "VALUES " +
                                                        "(@FolderGuid, @FileGuid, @SnapshotGuid)";
        public const string SelectFilesToFoldersSql = "SELECT * FROM FilesToFolders " +
                                                        "WHERE SnapshotGuid = @SnapshotGuid  " +
                                                        "AND FolderGuid = @FolderGuid " +
                                                        "AND FileGuid = @FileGuid;";
        public const string InsertPcsToStorageDrivesSql = "INSERT INTO PcsToStorageDrives " +
                                                            "(PcGuid, StorageDriveGuid, SnapshotGuid) " +
                                                            "VALUES " +
                                                            "(@PcGuid, @StorageDriveGuid, @SnapshotGuid)";
        public const string SelectPcsToStorageDrivesSql = "SELECT * FROM PcsToStorageDrives " +
                                                        "WHERE SnapshotGuid = @SnapshotGuid  " +
                                                        "AND PcGuid = @PcGuid " +
                                                        "AND StorageDriveGuid = @StorageDriveGuid;";
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
}
