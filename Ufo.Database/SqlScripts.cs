namespace Ufo.Database;

public class SqlScripts
{
    public const string CreateDatabaseSqlScript = @"
        CREATE TABLE IF NOT EXISTS Users (
            Id           TEXT NOT NULL UNIQUE  CONSTRAINT PK_Users PRIMARY KEY,
            Name         TEXT NOT NULL UNIQUE,
            PasswordHash TEXT NOT NULL,
            CreatedAt    TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        ); 

        CREATE TABLE IF NOT EXISTS Pcs (
            Id                        TEXT NOT NULL UNIQUE CONSTRAINT PK_Pcs PRIMARY KEY,
            DeviceId                  TEXT NOT NULL,
            Name                      TEXT NOT NULL,
            UserId                    TEXT NOT NULL,

            CONSTRAINT FK_Pcs_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS StorageDrives (
            Id                        TEXT NOT NULL UNIQUE CONSTRAINT PK_StorageDrives PRIMARY KEY,
            Name                      TEXT NOT NULL,
            DeviceId                  TEXT NOT NULL,
            SerialNumber              TEXT NOT NULL,
            TotalSize                 REAL NOT NULL,
            Description               TEXT NOT NULL,
            MediaType                 TEXT NOT NULL,
            InterfaceType             TEXT NOT NULL,
            UserId                    TEXT NOT NULL,

            CONSTRAINT FK_StorageDrives_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS Volumes (
            Id                        TEXT NOT NULL UNIQUE CONSTRAINT PK_Volumes PRIMARY KEY,
            DriveLetter               TEXT NOT NULL,
            VolumeName                TEXT NOT NULL,
            Description               TEXT NOT NULL,
            VolumeSerialNumber        TEXT NOT NULL,
            VolumeSize                REAL NOT NULL,
            StorageDriveId            TEXT NOT NULL,
            UserId                    TEXT NOT NULL,

            CONSTRAINT FK_Volumes_StorageDrives_StorageDriveId  FOREIGN KEY (StorageDriveId)  REFERENCES StorageDrives (Id) ON DELETE CASCADE,
            CONSTRAINT FK_Volumes_Users_UserId                  FOREIGN KEY (UserId)          REFERENCES Users (Id)         ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS VolumeInfos (
            Id                        TEXT NOT NULL UNIQUE CONSTRAINT PK_VolumeInfos PRIMARY KEY,
            FreeSpace                 REAL NOT NULL,
            DriveStatus               TEXT NOT NULL,
            VolumeId                  TEXT NOT NULL,
            SnapshotId                TEXT NOT NULL,
            UserId                    TEXT NOT NULL,

            CONSTRAINT FK_VolumeInfos_Volumes_VolumeId     FOREIGN KEY (VolumeId)      REFERENCES Volumes (Id)   ON DELETE CASCADE,
            CONSTRAINT FK_VolumeInfos_Snapshots_SnapshotId FOREIGN KEY (SnapshotId)    REFERENCES Snapshots (Id) ON DELETE NO ACTION,
            CONSTRAINT FK_VolumeInfos_Users_UserId         FOREIGN KEY (UserId)        REFERENCES Users (Id)     ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS PcsToStorageDrives (
            SnapshotId                TEXT NOT NULL,
            PcId                      TEXT NOT NULL,
            StorageDriveId            TEXT NOT NULL,

            CONSTRAINT PK_PcsToStorageDrives                              PRIMARY KEY (PcId, StorageDriveId, SnapshotId),
            CONSTRAINT FK_PcsToStorageDrives_Pcs_PcId                     FOREIGN KEY (PcId)            REFERENCES Pcs (Id)           ON DELETE NO ACTION,
            CONSTRAINT FK_PcsToStorageDrives_StorageDrives_StorageDriveId FOREIGN KEY (StorageDriveId)  REFERENCES StorageDrives (Id) ON DELETE NO ACTION,
            CONSTRAINT FK_PcsToStorageDrives_Snapshots_SnapshotId         FOREIGN KEY (SnapshotId)      REFERENCES Snapshots (Id)     ON DELETE NO ACTION
        );

        CREATE TABLE IF NOT EXISTS Snapshots (
            Id                        TEXT NOT NULL UNIQUE CONSTRAINT PK_Snapshots PRIMARY KEY,
            Timestamp                 TEXT NOT NULL,
            Description               TEXT,
            UserId                    TEXT NOT NULL,

            CONSTRAINT FK_Snapshots_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS Folders (
            Id                        TEXT NOT NULL UNIQUE CONSTRAINT PK_Folders PRIMARY KEY,
            Name                      TEXT NOT NULL,
            Size                      REAL NOT NULL,
            Sha256Hash                TEXT NOT NULL,
            UserId                    TEXT NOT NULL,

            CONSTRAINT FK_Folders_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS FoldersToFolders (
            SnapshotId                TEXT NOT NULL,
            ParentFolderId            TEXT,
            ChildFolderId             TEXT NOT NULL,

            CONSTRAINT PK_FoldersToFolders                      PRIMARY KEY (SnapshotId, ParentFolderId, ChildFolderId),
            CONSTRAINT FK_FoldersToFolders_Snapshots_SnapshotId FOREIGN KEY (SnapshotId)     REFERENCES Snapshots (Id) ON DELETE NO ACTION,
            CONSTRAINT FK_FoldersToFolders_Folders_FolderId     FOREIGN KEY (ParentFolderId) REFERENCES Folders (Id)   ON DELETE NO ACTION,
            CONSTRAINT FK_FoldersToFolders_Folders_FolderId     FOREIGN KEY (ChildFolderId)  REFERENCES Folders (Id)   ON DELETE NO ACTION
        );

        CREATE TABLE IF NOT EXISTS Files (
            Id                        TEXT NOT NULL UNIQUE CONSTRAINT PK_Files PRIMARY KEY,
            Name                      TEXT NOT NULL,
            Size                      REAL NOT NULL,
            FileExtension             TEXT NOT NULL,
            Sha256Hash                TEXT NOT NULL,
            UserId                    TEXT NOT NULL,

            CONSTRAINT FK_Files_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS FilesToFolders (
            SnapshotId                TEXT NOT NULL,
            FolderId                  TEXT NOT NULL,
            FileId                    TEXT NOT NULL,

            CONSTRAINT PK_FilesToFolders                       PRIMARY KEY (FolderId, FileId, SnapshotId),
            CONSTRAINT FK_FilesToFolders_Folders_FolderId      FOREIGN KEY (FolderId)    REFERENCES Folders (Id)   ON DELETE NO ACTION,
            CONSTRAINT FK_FilesToFolders_Files_FileId          FOREIGN KEY (FileId)      REFERENCES Files (Id)     ON DELETE NO ACTION,
            CONSTRAINT FK_FilesToFolders_Snapshots_SnapshotId  FOREIGN KEY (SnapshotId)  REFERENCES Snapshots (Id) ON DELETE NO ACTION
        );

        CREATE TABLE IF NOT EXISTS Labels (
            Id                        TEXT NOT NULL UNIQUE CONSTRAINT PK_Labels PRIMARY KEY,
            Name                      TEXT NOT NULL,
            ColorHex                  TEXT NOT NULL,
            UserId                    TEXT NOT NULL,

            CONSTRAINT FK_Labels_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS LabelsToSnapshots (
            LabelId                   TEXT NOT NULL,
            SnapshotId                TEXT NOT NULL,

            CONSTRAINT PK_LabelsToSnapshots                       PRIMARY KEY (LabelId, SnapshotId),
            CONSTRAINT FK_LabelsToSnapshots_Labels_LabelId        FOREIGN KEY (LabelId)     REFERENCES Labels (Id)    ON DELETE NO ACTION,
            CONSTRAINT FK_LabelsToSnapshots_Snapshots_SnapshotId  FOREIGN KEY (SnapshotId)  REFERENCES Snapshots (Id) ON DELETE NO ACTION
        );  

        CREATE TABLE IF NOT EXISTS UsersToSnapshots (
            UserId                    TEXT NOT NULL,
            SnapshotId                TEXT NOT NULL,

            CONSTRAINT PK_UsersToSnapshots                       PRIMARY KEY (UserId, SnapshotId),
            CONSTRAINT FK_UsersToSnapshots_Users_UserId          FOREIGN KEY (UserId)      REFERENCES Users (Id)    ON DELETE NO ACTION,
            CONSTRAINT FK_UsersToSnapshots_Snapshots_SnapshotId  FOREIGN KEY (SnapshotId)  REFERENCES Snapshots (Id) ON DELETE NO ACTION
        );
    ";

    public const string SelectPcSql = "SELECT * FROM Pcs WHERE Name = @PcName AND DeviceId = @DeviceId AND UserId = @UserId;";
    public const string InsertPcSql = "INSERT INTO Pcs " +
                                        "(Id, Name, DeviceId, UserId) " +
                                        "VALUES " +
                                        "(@Id, @Name, @DeviceId, @UserId)";
    public const string SelectStorageDriveSql = "SELECT * FROM StorageDrives WHERE SerialNumber = @SerialNumber AND DeviceId = @DeviceId AND Name = @Name AND UserId = @UserId;";
    public const string InsertStorageDriveSql = "INSERT INTO StorageDrives " +
                                                "(Id, Name, DeviceId, SerialNumber, TotalSize, Description, MediaType, InterfaceType, UserId) " +
                                                "VALUES " +
                                                "(@Id, @Name, @DeviceId, @SerialNumber, @TotalSize, @Description, @MediaType, @InterfaceType, @UserId)";
    public const string SelectSnapshotsSql = "SELECT * FROM Snapshots WHERE StorageDriveId = @StorageDriveId AND UserId = @UserId;";
    public const string SelectSnapshotOnlyByIdSql = "SELECT * FROM Snapshots WHERE Id = @SnapshotId AND UserId = @UserId;";
    public const string SelectLatestSnapshotWithSystemInfoSql = "SELECT * FROM Snapshots AS snapshot " +
                                                                    "LEFT JOIN VolumeInfos AS volinf ON volinf.SnapshotId == snapshot.Id " +
                                                                    "LEFT JOIN Volumes AS volume ON volinf.VolumeId == volume.Id " +
                                                                    "LEFT JOIN StorageDrives AS stdrv ON volume.StorageDriveId = stdrv.Id " +
                                                                    "LEFT JOIN PcsToStorageDrives AS pc2stdrv ON pc2stdrv.SnapshotId = snapshot.Id AND pc2stdrv.StorageDriveId = stdrv.Id " +
                                                                    "LEFT JOIN Pcs AS pc ON pc2stdrv.PcId = pc.Id " +
                                                                    "WHERE snapshot.UserId = @UserId " +
                                                                    "ORDER BY snapshot.Timestamp DESC LIMIT 1;";
    public const string SelectSnapshotByIdSql = "SELECT * FROM Snapshots AS snapshot " +
                                                    "LEFT JOIN VolumeInfos AS volinf ON volinf.SnapshotId == snapshot.Id " +
                                                    "LEFT JOIN Volumes AS volume ON volinf.VolumeId == volume.Id " +
                                                    "LEFT JOIN StorageDrives AS stdrv ON volume.StorageDriveId = stdrv.Id " +
                                                    "LEFT JOIN PcsToStorageDrives AS pc2stdrv ON pc2stdrv.SnapshotId = snapshot.Id AND pc2stdrv.StorageDriveId = stdrv.Id " +
                                                    "LEFT JOIN Pcs AS pc ON pc2stdrv.PcId = pc.Id " +
                                                    "WHERE snapshot.Id = @SnapshotId AND snapshot.UserId = @UserId;";
    public const string SelectSnapshotsWithSystemInfoSql = "SELECT * FROM Snapshots AS snapshot " +
                                                            "LEFT JOIN VolumeInfos AS volinf ON volinf.SnapshotId == snapshot.Id " +
                                                            "LEFT JOIN Volumes AS volume ON volinf.VolumeId == volume.Id " +
                                                            "LEFT JOIN StorageDrives AS stdrv ON volume.StorageDriveId = stdrv.Id " +
                                                            "LEFT JOIN PcsToStorageDrives AS pc2stdrv ON pc2stdrv.SnapshotId = snapshot.Id AND pc2stdrv.StorageDriveId = stdrv.Id " +
                                                            "LEFT JOIN Pcs AS pc ON pc2stdrv.PcId = pc.Id " +
                                                            "LEFT JOIN FoldersToFolders AS fl2fl ON fl2fl.SnapshotId = snapshot.Id " +
                                                            "LEFT JOIN Folders AS folder ON folder.Id = fl2fl.ChildFolderId " +
                                                            "WHERE fl2fl.ParentFolderId is NULL AND snapshot.UserId = @UserId " +
                                                            "ORDER BY snapshot.Timestamp DESC;";
    public const string SelectFoldersAndFilesBySnapshotSql = "SELECT * FROM Folders AS folder " +
                                                                "LEFT JOIN FoldersToFolders AS fl2fl ON fl2fl.ChildFolderId = folder.Id " +
                                                                "LEFT JOIN FilesToFolders AS fi2fl ON fi2fl.FolderId = folder.Id AND fi2fl.SnapshotId = @SnapshotId " +
                                                                "LEFT JOIN Files AS file ON fi2fl.FileId = file.Id " +
                                                                "WHERE fl2fl.SnapshotId = @SnapshotId AND folder.UserId = @UserId;";
    public const string InsertSnapshotSql = "INSERT INTO Snapshots " +
                                            "(Id, Timestamp, Description, UserId) " +
                                            "VALUES " +
                                            "(@Id, @Timestamp, @Description, @UserId)";
    public const string SelectVolumeSql = "SELECT * FROM Volumes WHERE VolumeSerialNumber = @VolumeSerialNumber AND UserId = @UserId;";
    public const string InsertVolumeSql = "INSERT INTO Volumes " +
                                            "(Id, DriveLetter, VolumeName, Description, VolumeSerialNumber, VolumeSize, StorageDriveId, UserId) " +
                                            "VALUES " +
                                            "(@Id, @DriveLetter, @VolumeName, @Description, @VolumeSerialNumber, @VolumeSize, @StorageDriveId, @UserId)";
    public const string SelectVolumeInfoSql = "SELECT * FROM VolumeInfos WHERE VolumeSerialNumber = @VolumeSerialNumber AND UserId = @UserId;";
    public const string InsertVolumeInfoSql = "INSERT INTO VolumeInfos " +
                                                "(Id, FreeSpace, DriveStatus, VolumeId, SnapshotId, UserId) " +
                                                "VALUES " +
                                                "(@Id, @FreeSpace, @DriveStatus, @VolumeId, @SnapshotId, @UserId)";
    public const string SelectFolderByNameAndParentFolderPathAndStorageDriveIdSql = "SELECT * FROM Folders " +
                                                                                        "WHERE Name = @Name " +
                                                                                        "AND Size = @Size " +
                                                                                        "AND Sha256Hash = @Sha256Hash " +
                                                                                        "AND UserId = @UserId;";
    public const string SelectFoldersByNameSql = "SELECT * FROM Folders " +
                                                    "WHERE Name = @Name AND UserId = @UserId;";
    public const string InsertFolderSql = "INSERT INTO Folders " +
                                            "(Id, Name, Size, Sha256Hash, UserId) " +
                                            "VALUES " +
                                            "(@Id, @Name, @Size, @Sha256Hash, @UserId)";
    public const string UpdateFolderHashSql = "UPDATE Folders " +
                                                "SET Sha256Hash = @Sha256Hash " +
                                                "WHERE Id = @Id;";
    public const string InsertFileSql = "INSERT INTO Files " +
                                            "(Id, Name, Size, FileExtension,Sha256Hash, UserId) " +
                                            "VALUES " +
                                            "(@Id, @Name, @Size, @FileExtension, @Sha256Hash, @UserId)";
    public const string SelectFilesByNameAndExtensionSql = "SELECT * FROM Files " +
                                                                "WHERE Name = @Name " +
                                                                "AND FileExtension = @FileExtension " +
                                                                "AND UserId = @UserId;";
    public const string SelectFileByNameAndParentFolderPathAndStorageDriveIdSql = "SELECT * FROM Files " +
                                                                                    "WHERE Name = @Name " +
                                                                                    "AND Size = @Size " +
                                                                                    "AND FileExtension = @FileExtension " +
                                                                                    "AND Sha256Hash = @Sha256Hash " +
                                                                                    "AND UserId = @UserId;";
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

    // Delete SQL Scripts
    public const string DeleteFilesToFoldersBySnapshotSql = "DELETE FROM FilesToFolders WHERE SnapshotId = @SnapshotId;";
    public const string DeleteFilesWithoutSnapshotsSql = "DELETE FROM Files WHERE Id NOT IN " +
                                                          "(SELECT DISTINCT FileId FROM FilesToFolders);";
    public const string DeleteFoldersToFoldersBySnapshotSql = "DELETE FROM FoldersToFolders WHERE SnapshotId = @SnapshotId;";
    public const string DeleteFoldersWithoutSnapshotsSql = "DELETE FROM Folders WHERE Id NOT IN " +
                                                            "(SELECT DISTINCT ChildFolderId FROM FoldersToFolders);";
    public const string DeletePcsToStorageDrivesBySnapshotSql = "DELETE FROM PcsToStorageDrives WHERE SnapshotId = @SnapshotId;";
    public const string DeletePcsWithoutStorageDrivesSql = "DELETE FROM Pcs WHERE Id NOT IN " +
                                                            "(SELECT DISTINCT PcId FROM PcsToStorageDrives);";
    public const string DeleteStorageDrivesWithoutVolumesAndSnapshotsSql = "DELETE FROM StorageDrives WHERE Id NOT IN " +
                                                                            "(SELECT DISTINCT StorageDriveId FROM Volumes) " +
                                                                            "AND Id NOT IN " +
                                                                            "(SELECT DISTINCT StorageDriveId FROM PcsToStorageDrives) " +
                                                                            "AND UserId = @UserId;";
    public const string DeleteVolumesWithoutVolumeInfosSql = "DELETE FROM Volumes WHERE Id NOT IN " +
                                                              "(SELECT DISTINCT VolumeId FROM VolumeInfos);";
    public const string DeleteVolumeInfoBySnapshotSql = "DELETE FROM VolumeInfos WHERE SnapshotId = @SnapshotId AND UserId = @UserId;";
    public const string DeleteSnapshotByIdSql = "DELETE FROM Snapshots WHERE Id = @SnapshotId AND UserId = @UserId;";

    // Labels SQL Scripts
    public const string InsertLabelSql = "INSERT INTO Labels " +
                                           "(Id, Name, ColorHex, UserId) " +
                                           "VALUES " +
                                           "(@Id, @Name, @ColorHex, @UserId)";
    public const string SelectLabelByIdSql = "SELECT * FROM Labels WHERE Id = @LabelId AND UserId = @UserId;";
    public const string SelectLabelByNameSql = "SELECT * FROM Labels WHERE Name = @Name AND UserId = @UserId;";
    public const string InsertLabelsToSnapshotsSql = "INSERT INTO LabelsToSnapshots " +
                                                        "(LabelId, SnapshotId) " +
                                                        "VALUES " +
                                                        "(@LabelId, @SnapshotId);";
    public const string SelectAllLabelsSql = "SELECT * FROM Labels WHERE UserId = @UserId;";
    public const string SelectLabelsBySnapshotIdSql = "SELECT DISTINCT l.* FROM Labels AS l " +
                                                         "INNER JOIN LabelsToSnapshots AS l2s ON l2s.LabelId = l.Id " +
                                                         "WHERE l2s.SnapshotId = @SnapshotId AND UserId = @UserId;";
    public const string UpdateLabelSql = "UPDATE Labels " +
                                        "SET Name = @Name, ColorHex = @ColorHex " +
                                        "WHERE Id = @Id AND UserId = @UserId;";
    public const string DeleteLabelByIdSql = "DELETE FROM Labels WHERE Id = @LabelId AND UserId = @UserId;";
    public const string DeleteLabelsToSnapshotsBySnapshotIdSql = "DELETE FROM LabelsToSnapshots WHERE SnapshotId = @SnapshotId;";
    public const string DeleteLabelFromSnapshotSql = "DELETE FROM LabelsToSnapshots WHERE LabelId = @LabelId AND SnapshotId = @SnapshotId;";
    public const string DeleteLabelsToSnapshotsByLabelIdSql = "DELETE FROM LabelsToSnapshots WHERE LabelId = @LabelId;";

    // User SQL Scripts
    public const string SelectUserByIdSql = "SELECT * FROM Users WHERE Id = @UserId;";
    public const string SelectUserByNameSql = "SELECT * FROM Users WHERE Name = @Username;";
    public const string SelectSameUserByNameSql = "SELECT COUNT(1) FROM Users WHERE Name = @Username;";
    public const string InsertUserSql = "INSERT INTO Users (Id, Name, PasswordHash) " +
                                            "VALUES (@Id, @Name, @PasswordHash);";
    public const string SelectUserCountSql = "SELECT COUNT(1) FROM Users;";

    // Search SQL Scripts
    //public const string SearchFoldersByNameSql = "SELECT * FROM Folders " +
    //                                        "WHERE Name LIKE '%' || @Query || '%';";
    //public const string SearchFilesByNameSql = "SELECT * FROM Files " +
    //                                        "WHERE Name LIKE '%' || @Query || '%';";

    // Search Files + Snapshots + Labels
    //public const string SearchFilesByNameSql = @"
    //    SELECT 
    //        f.Id, f.Name, f.Size, f.Sha256Hash, f.FileExtension, -- File Columns
    //        s.Id, s.Timestamp, s.Description,                   -- Snapshot Columns
    //        l.Id, l.Name, l.ColorHex                            -- Label Columns
    //    FROM Files_FTS AS fts
    //    JOIN Files AS f ON fts.FileId = f.Id
    //    JOIN FilesToFolders AS ftf ON f.Id = ftf.FileId
    //    JOIN Snapshots AS s ON ftf.SnapshotId = s.Id
    //    LEFT JOIN LabelsToSnapshots AS lts ON s.Id = lts.SnapshotId
    //    LEFT JOIN Labels AS l ON lts.LabelId = l.Id
    //    WHERE Files_FTS MATCH @FtsQuery
    //    ORDER BY fts.rank;";

    // Search Folders + Snapshots + Labels
    //public const string SearchFoldersByNameSql = @"
    //    SELECT 
    //        fo.Id, fo.Name, fo.Size, fo.Sha256Hash,              -- Folder Columns
    //        s.Id, s.Timestamp, s.Description,                   -- Snapshot Columns
    //        l.Id, l.Name, l.ColorHex                            -- Label Columns
    //    FROM Folders_FTS AS fts
    //    JOIN Folders AS fo ON fts.FolderId = fo.Id
    //    JOIN FoldersToFolders AS ftf ON fo.Id = ftf.ChildFolderId
    //    JOIN Snapshots AS s ON ftf.SnapshotId = s.Id
    //    LEFT JOIN LabelsToSnapshots AS lts ON s.Id = lts.SnapshotId
    //    LEFT JOIN Labels AS l ON lts.LabelId = l.Id
    //    WHERE Folders_FTS MATCH @FtsQuery
    //    ORDER BY fts.rank;";

    public const string SearchFilesByNameSql = @"
        SELECT 
            f.Id, f.Name, f.Size, f.Sha256Hash, f.FileExtension, f.UserId,
            s.Id, s.Timestamp, s.Description, s.UserId,
            l.Id, l.Name, l.ColorHex, l.UserId
        FROM Files AS f
        JOIN FilesToFolders AS ftf ON f.Id = ftf.FileId
        JOIN Snapshots AS s ON ftf.SnapshotId = s.Id
        LEFT JOIN LabelsToSnapshots AS lts ON s.Id = lts.SnapshotId
        LEFT JOIN Labels AS l ON lts.LabelId = l.Id
        WHERE f.Name LIKE '%' || @Query || '%' AND f.UserId = @UserId
        ORDER BY f.Name ASC;";

    public const string SearchFoldersByNameSql = @"
        SELECT 
            fo.Id, fo.Name, fo.Size, fo.Sha256Hash, fo.UserId,
            s.Id, s.Timestamp, s.Description, s.UserId,
            l.Id, l.Name, l.ColorHex, l.UserId
        FROM Folders AS fo
        -- We join the mapping table directly instead of the FTS table
        JOIN FoldersToFolders AS ftf ON fo.Id = ftf.ChildFolderId
        JOIN Snapshots AS s ON ftf.SnapshotId = s.Id
        LEFT JOIN LabelsToSnapshots AS lts ON s.Id = lts.SnapshotId
        LEFT JOIN Labels AS l ON lts.LabelId = l.Id
        WHERE fo.Name LIKE '%' || @Query || '%' AND fo.UserId = @UserId
        ORDER BY fo.Name ASC;";
}
