using Dapper;
using Microsoft.Data.Sqlite;

namespace Ufo.Database.Contexts
{
    public static class DapperDataContext
    {
        private const string Sql = @"
                CREATE TABLE IF NOT EXISTS Pcs (
                    Guid                      TEXT NOT NULL
                                              CONSTRAINT PK_Pcs PRIMARY KEY,
                    Name                      TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS StorageDrives (
                    Guid                      TEXT NOT NULL
                                              CONSTRAINT PK_StorageDrives PRIMARY KEY,
                    Name                      TEXT NOT NULL,
                    DeviceId                  TEXT NOT NULL,
                    SerialNumber              TEXT NOT NULL,
                    TotalSize                 REAL NOT NULL,
                    Description               TEXT NOT NULL,
                    MediaType                 TEXT NOT NULL,
                    InterfaceType             TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS Volumes (
                    Guid                      TEXT NOT NULL
                                              CONSTRAINT PK_Volumes PRIMARY KEY,
                    DriveLetter               TEXT NOT NULL,
                    VolumeName                TEXT NOT NULL,
                    Description               TEXT NOT NULL,
                    VolumeSerialNumber        TEXT NOT NULL,
                    VolumeSize                REAL NOT NULL,
                    StorageDriveGuid          TEXT NOT NULL,

                    CONSTRAINT FK_Volumes_StorageDrives_StorageDriveGuid  FOREIGN KEY (StorageDriveGuid)  REFERENCES StorageDrives (Guid)
                );

                CREATE TABLE IF NOT EXISTS Folders (
                    Guid                      TEXT NOT NULL
                                              CONSTRAINT PK_Folders PRIMARY KEY,
                    Name                      TEXT NOT NULL,
                    Size                      REAL NOT NULL,
                    Sha256Hash                TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS Snapshots (
                    Guid                      TEXT NOT NULL
                                              CONSTRAINT PK_Snapshots PRIMARY KEY,
                    Timestamp                 TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS VolumeInfos (
                    Guid                      TEXT NOT NULL
                                              CONSTRAINT PK_VolumeInfos PRIMARY KEY,
                    FreeSpace                 REAL NOT NULL,
                    DriveStatus               TEXT NOT NULL,
                    VolumeGuid                TEXT NOT NULL,
                    SnapshotGuid              TEXT NOT NULL,

                    CONSTRAINT FK_VolumeInfos_Volumes_VolumeGuid      FOREIGN KEY (VolumeGuid)      REFERENCES Volumes (Guid),
                    CONSTRAINT FK_VolumeInfos_Snapshots_SnapshotGuid  FOREIGN KEY (SnapshotGuid)    REFERENCES Snapshots (Guid)
                );

                CREATE TABLE IF NOT EXISTS PcsToStorageDrives (
                    SnapshotGuid              TEXT NOT NULL,
                    PcGuid                    TEXT NOT NULL,
                    StorageDriveGuid          TEXT NOT NULL,

                    CONSTRAINT PK_PcsToStorageDrives                                  PRIMARY KEY (PcGuid, StorageDriveGuid, SnapshotGuid),
                    CONSTRAINT FK_PcsToStorageDrives_Pcs_PcGuid                       FOREIGN KEY (PcGuid)            REFERENCES Pcs (Guid),
                    CONSTRAINT FK_PcsToStorageDrives_StorageDrives_StorageDriveGuid   FOREIGN KEY (StorageDriveGuid)  REFERENCES StorageDrives (Guid),
                    CONSTRAINT FK_PcsToStorageDrives_Snapshots_SnapshotGuid           FOREIGN KEY (SnapshotGuid)      REFERENCES Snapshots (Guid)
                );

                CREATE TABLE IF NOT EXISTS FoldersToFolders (
                    SnapshotGuid              TEXT NOT NULL,
                    ParentFolderGuid          TEXT,
                    ChildFolderGuid           TEXT NOT NULL,

                    CONSTRAINT PK_FoldersToFolders                           PRIMARY KEY (SnapshotGuid, ParentFolderGuid, ChildFolderGuid),
                    CONSTRAINT FK_FoldersToFolders_Snapshots_SnapshotGuid    FOREIGN KEY (SnapshotGuid)        REFERENCES Snapshots (Guid),
                    CONSTRAINT FK_FoldersToFolders_Folders_FolderGuid        FOREIGN KEY (ParentFolderGuid)    REFERENCES Folders (Guid),
                    CONSTRAINT FK_FoldersToFolders_Folders_FolderGuid        FOREIGN KEY (ChildFolderGuid)     REFERENCES Folders (Guid)
                );

                CREATE TABLE IF NOT EXISTS Files (
                    Guid                      TEXT NOT NULL
                                              CONSTRAINT PK_Files PRIMARY KEY,
                    Name                      TEXT NOT NULL,
                    Size                      REAL NOT NULL,
                    FileExtension             TEXT NOT NULL,
                    Sha256Hash                TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS FilesToFolders (
                    SnapshotGuid              TEXT NOT NULL,
                    FolderGuid                TEXT NOT NULL,
                    FileGuid                  TEXT NOT NULL,

                    CONSTRAINT PK_FilesToFolders                              PRIMARY KEY (FolderGuid, FileGuid, SnapshotGuid),
                    CONSTRAINT FK_FilesToFolders_Folders_FolderGuid           FOREIGN KEY (FolderGuid)    REFERENCES Folders (Guid),
                    CONSTRAINT FK_FilesToFolders_Files_FileGuid               FOREIGN KEY (FileGuid)      REFERENCES Files (Guid),
                    CONSTRAINT FK_FilesToFolders_Snapshots_SnapshotGuid       FOREIGN KEY (SnapshotGuid)  REFERENCES Snapshots (Guid)
                );
            ";

        public static async Task InitiateDatabaseAsync(string? connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentNullException(nameof(connectionString));
            }

            await using var sqLiteConnection = new SqliteConnection(connectionString);
            await sqLiteConnection.ExecuteAsync(Sql);
        }
    }
}
