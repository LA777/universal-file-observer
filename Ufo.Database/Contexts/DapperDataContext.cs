using Dapper;
using Microsoft.Data.Sqlite;
using Ufo.Database.Handlers;

namespace Ufo.Database.Contexts;

public static class DapperDataContext
{
    private const string Sql = @"
        CREATE TABLE IF NOT EXISTS Pcs (
            Id                        TEXT NOT NULL UNIQUE CONSTRAINT PK_Pcs PRIMARY KEY,
            DeviceId                  TEXT NOT NULL,
            Name                      TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS StorageDrives (
            Id                        TEXT NOT NULL UNIQUE CONSTRAINT PK_StorageDrives PRIMARY KEY,
            Name                      TEXT NOT NULL,
            DeviceId                  TEXT NOT NULL,
            SerialNumber              TEXT NOT NULL,
            TotalSize                 REAL NOT NULL,
            Description               TEXT NOT NULL,
            MediaType                 TEXT NOT NULL,
            InterfaceType             TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS Volumes (
            Id                        TEXT NOT NULL UNIQUE CONSTRAINT PK_Volumes PRIMARY KEY,
            DriveLetter               TEXT NOT NULL,
            VolumeName                TEXT NOT NULL,
            Description               TEXT NOT NULL,
            VolumeSerialNumber        TEXT NOT NULL,
            VolumeSize                REAL NOT NULL,
            StorageDriveId            TEXT NOT NULL,

            CONSTRAINT FK_Volumes_StorageDrives_StorageDriveId  FOREIGN KEY (StorageDriveId)  REFERENCES StorageDrives (Id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS VolumeInfos (
            Id                        TEXT NOT NULL UNIQUE CONSTRAINT PK_VolumeInfos PRIMARY KEY,
            FreeSpace                 REAL NOT NULL,
            DriveStatus               TEXT NOT NULL,
            VolumeId                  TEXT NOT NULL,
            SnapshotId                TEXT NOT NULL,

            CONSTRAINT FK_VolumeInfos_Volumes_VolumeId     FOREIGN KEY (VolumeId)      REFERENCES Volumes (Id)   ON DELETE CASCADE,
            CONSTRAINT FK_VolumeInfos_Snapshots_SnapshotId FOREIGN KEY (SnapshotId)    REFERENCES Snapshots (Id) ON DELETE NO ACTION
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
            Description               TEXT
        );

        CREATE TABLE IF NOT EXISTS Folders (
            Id                        TEXT NOT NULL UNIQUE CONSTRAINT PK_Folders PRIMARY KEY,
            Name                      TEXT NOT NULL,
            Size                      REAL NOT NULL,
            Sha256Hash                TEXT NOT NULL
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
            Sha256Hash                TEXT NOT NULL
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
    ";

    public static async Task InitiateDatabaseAsync(string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new ArgumentNullException(nameof(connectionString));
        }

        SqlMapper.AddTypeHandler(new SqlUlidTypeHandler());
        SqlMapper.AddTypeHandler(new SqlNullableUlidTypeHandler());
        SqlMapper.RemoveTypeMap(typeof(Ulid));
        SqlMapper.RemoveTypeMap(typeof(Ulid?));
        SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());

        await using var sqLiteConnection = new SqliteConnection(connectionString);
        await sqLiteConnection.ExecuteAsync(Sql);
    }
}
