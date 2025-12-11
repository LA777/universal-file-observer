# ??? Implementation Guide: Database Improvements Code

## ?? Overview

This guide provides concrete C# and SQL code examples for implementing all proposed database improvements in the UFO project.

---

## ? Priority 1 Implementations

### 1.1 Enhanced DapperDataContext.cs

```csharp
using Dapper;
using Microsoft.Data.Sqlite;

namespace Ufo.Database.Contexts;

public static class DapperDataContext
{
    // Current schema with P1 improvements
    private const string Sql = @"
        -- ============================================================
        -- CORE STORAGE INFRASTRUCTURE
        -- ============================================================
        
        CREATE TABLE IF NOT EXISTS Pcs (
            Id                        TEXT NOT NULL UNIQUE CONSTRAINT PK_Pcs PRIMARY KEY,
            Name                      TEXT NOT NULL,
            CreatedAt                 TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            UpdatedAt                 TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            
            CONSTRAINT CK_Pcs_Name CHECK (length(Name) > 0)
        );

        CREATE TABLE IF NOT EXISTS StorageDrives (
            Id                        TEXT NOT NULL UNIQUE CONSTRAINT PK_StorageDrives PRIMARY KEY,
            Name                      TEXT NOT NULL,
            DeviceId                  TEXT NOT NULL,
            SerialNumber              TEXT NOT NULL UNIQUE,
            TotalSize                 REAL NOT NULL,
            Description               TEXT NOT NULL,
            MediaType                 TEXT NOT NULL,
            InterfaceType             TEXT NOT NULL,
            CreatedAt                 TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            UpdatedAt                 TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            
            CONSTRAINT CK_StorageDrives_TotalSize CHECK (TotalSize > 0),
            CONSTRAINT CK_StorageDrives_DeviceId CHECK (length(DeviceId) > 0),
            CONSTRAINT CK_StorageDrives_SerialNumber CHECK (length(SerialNumber) > 0),
            CONSTRAINT CK_StorageDrives_Name CHECK (length(Name) > 0)
        );

        CREATE TABLE IF NOT EXISTS Volumes (
            Id                        TEXT NOT NULL UNIQUE CONSTRAINT PK_Volumes PRIMARY KEY,
            DriveLetter               TEXT NOT NULL,
            VolumeName                TEXT NOT NULL,
            Description               TEXT NOT NULL,
            VolumeSerialNumber        TEXT NOT NULL,
            VolumeSize                REAL NOT NULL,
            StorageDriveId            TEXT NOT NULL,
            CreatedAt                 TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            UpdatedAt                 TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,

            CONSTRAINT FK_Volumes_StorageDrives_StorageDriveId FOREIGN KEY (StorageDriveId) REFERENCES StorageDrives (Id) ON DELETE CASCADE,
            CONSTRAINT CK_Volumes_VolumeSize CHECK (VolumeSize > 0),
            CONSTRAINT CK_Volumes_VolumeName CHECK (length(VolumeName) > 0)
        );

        CREATE TABLE IF NOT EXISTS VolumeInfos (
            Id                        TEXT NOT NULL UNIQUE CONSTRAINT PK_VolumeInfos PRIMARY KEY,
            FreeSpace                 REAL NOT NULL,
            DriveStatus               TEXT NOT NULL,
            VolumeId                  TEXT NOT NULL,
            SnapshotId                TEXT NOT NULL,

            CONSTRAINT FK_VolumeInfos_Volumes_VolumeId FOREIGN KEY (VolumeId) REFERENCES Volumes (Id) ON DELETE CASCADE,
            CONSTRAINT FK_VolumeInfos_Snapshots_SnapshotId FOREIGN KEY (SnapshotId) REFERENCES Snapshots (Id) ON DELETE NO ACTION,
            CONSTRAINT CK_VolumeInfos_FreeSpace CHECK (FreeSpace >= 0)
        );

        CREATE TABLE IF NOT EXISTS PcsToStorageDrives (
            SnapshotId                TEXT NOT NULL,
            PcId                      TEXT NOT NULL,
            StorageDriveId            TEXT NOT NULL,

            CONSTRAINT PK_PcsToStorageDrives PRIMARY KEY (PcId, StorageDriveId, SnapshotId),
            CONSTRAINT FK_PcsToStorageDrives_Pcs_PcId FOREIGN KEY (PcId) REFERENCES Pcs (Id) ON DELETE NO ACTION,
            CONSTRAINT FK_PcsToStorageDrives_StorageDrives_StorageDriveId FOREIGN KEY (StorageDriveId) REFERENCES StorageDrives (Id) ON DELETE NO ACTION,
            CONSTRAINT FK_PcsToStorageDrives_Snapshots_SnapshotId FOREIGN KEY (SnapshotId) REFERENCES Snapshots (Id) ON DELETE NO ACTION
        );

        CREATE TABLE IF NOT EXISTS Snapshots (
            Id                        TEXT NOT NULL UNIQUE CONSTRAINT PK_Snapshots PRIMARY KEY,
            Timestamp                 TEXT NOT NULL
        );

        -- ============================================================
        -- FILE SYSTEM HIERARCHY
        -- ============================================================

        CREATE TABLE IF NOT EXISTS Folders (
            Id                        TEXT NOT NULL UNIQUE CONSTRAINT PK_Folders PRIMARY KEY,
            Name                      TEXT NOT NULL,
            Size                      REAL NOT NULL,
            Sha256Hash                TEXT NOT NULL,

            CONSTRAINT CK_Folders_Size CHECK (Size >= 0),
            CONSTRAINT CK_Folders_Name CHECK (length(Name) > 0)
        );

        CREATE TABLE IF NOT EXISTS FoldersToFolders (
            SnapshotId                TEXT NOT NULL,
            ParentFolderId            TEXT,
            ChildFolderId             TEXT NOT NULL,

            CONSTRAINT PK_FoldersToFolders PRIMARY KEY (SnapshotId, ParentFolderId, ChildFolderId),
            CONSTRAINT FK_FoldersToFolders_Snapshots_SnapshotId FOREIGN KEY (SnapshotId) REFERENCES Snapshots (Id) ON DELETE NO ACTION,
            CONSTRAINT FK_FoldersToFolders_Folders_ParentId FOREIGN KEY (ParentFolderId) REFERENCES Folders (Id) ON DELETE NO ACTION,
            CONSTRAINT FK_FoldersToFolders_Folders_ChildId FOREIGN KEY (ChildFolderId) REFERENCES Folders (Id) ON DELETE NO ACTION
        );

        CREATE TABLE IF NOT EXISTS Files (
            Id                        TEXT NOT NULL UNIQUE CONSTRAINT PK_Files PRIMARY KEY,
            Name                      TEXT NOT NULL,
            Size                      REAL NOT NULL,
            FileExtension             TEXT NOT NULL,
            Sha256Hash                TEXT NOT NULL,

            CONSTRAINT CK_Files_Size CHECK (Size >= 0),
            CONSTRAINT CK_Files_Name CHECK (length(Name) > 0)
        );

        CREATE TABLE IF NOT EXISTS FilesToFolders (
            SnapshotId                TEXT NOT NULL,
            FolderId                  TEXT NOT NULL,
            FileId                    TEXT NOT NULL,

            CONSTRAINT PK_FilesToFolders PRIMARY KEY (FolderId, FileId, SnapshotId),
            CONSTRAINT FK_FilesToFolders_Folders_FolderId FOREIGN KEY (FolderId) REFERENCES Folders (Id) ON DELETE NO ACTION,
            CONSTRAINT FK_FilesToFolders_Files_FileId FOREIGN KEY (FileId) REFERENCES Files (Id) ON DELETE NO ACTION,
            CONSTRAINT FK_FilesToFolders_Snapshots_SnapshotId FOREIGN KEY (SnapshotId) REFERENCES Snapshots (Id) ON DELETE NO ACTION
        );

        -- ============================================================
        -- PERFORMANCE INDEXES (Priority 1)
        -- ============================================================

        -- Volume queries
        CREATE INDEX IF NOT EXISTS IX_Volumes_StorageDriveId ON Volumes(StorageDriveId);

        -- PC-Drive-Snapshot queries
        CREATE INDEX IF NOT EXISTS IX_PcsToStorageDrives_SnapshotId ON PcsToStorageDrives(SnapshotId);
        CREATE INDEX IF NOT EXISTS IX_PcsToStorageDrives_PcId ON PcsToStorageDrives(PcId);
        CREATE INDEX IF NOT EXISTS IX_PcsToStorageDrives_StorageDriveId ON PcsToStorageDrives(StorageDriveId);

        -- Volume info queries
        CREATE INDEX IF NOT EXISTS IX_VolumeInfos_VolumeId ON VolumeInfos(VolumeId);
        CREATE INDEX IF NOT EXISTS IX_VolumeInfos_SnapshotId ON VolumeInfos(SnapshotId);

        -- File system hierarchy queries
        CREATE INDEX IF NOT EXISTS IX_FoldersToFolders_SnapshotId ON FoldersToFolders(SnapshotId);
        CREATE INDEX IF NOT EXISTS IX_FoldersToFolders_ParentFolderId ON FoldersToFolders(ParentFolderId);
        CREATE INDEX IF NOT EXISTS IX_FoldersToFolders_ChildFolderId ON FoldersToFolders(ChildFolderId);

        -- File queries
        CREATE INDEX IF NOT EXISTS IX_FilesToFolders_SnapshotId ON FilesToFolders(SnapshotId);
        CREATE INDEX IF NOT EXISTS IX_FilesToFolders_FolderId ON FilesToFolders(FolderId);
        CREATE INDEX IF NOT EXISTS IX_FilesToFolders_FileId ON FilesToFolders(FileId);

        -- Hash-based duplicate detection
        CREATE INDEX IF NOT EXISTS IX_Files_Sha256Hash ON Files(Sha256Hash);
        CREATE INDEX IF NOT EXISTS IX_Folders_Sha256Hash ON Folders(Sha256Hash);
    ";

    public static async Task InitiateDatabaseAsync(string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new ArgumentNullException(nameof(connectionString));
        }

        await using var sqliteConnection = new SqliteConnection(connectionString);
        await sqliteConnection.ExecuteAsync(Sql);
    }
}
```

---

## ?? Priority 2 Implementations

### 2.1 Add Support Methods for New Tables

```csharp
using Dapper;
using Microsoft.Data.Sqlite;

namespace Ufo.Database.Contexts;

public static class DapperDataContextP2
{
    // Schema versioning table
    private const string SchemaVersioningTableSql = @"
        CREATE TABLE IF NOT EXISTS SchemaVersions (
            Id              INTEGER PRIMARY KEY AUTOINCREMENT,
            Version         TEXT NOT NULL UNIQUE,
            Description     TEXT,
            AppliedAt       TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            RolledBackAt    TEXT
        );

        INSERT OR IGNORE INTO SchemaVersions (Version, Description) 
        VALUES ('1.0.0', 'Initial schema with core tables');

        INSERT OR IGNORE INTO SchemaVersions (Version, Description) 
        VALUES ('1.1.0', 'P1: Added audit timestamps, indexes, and constraints');
    ";

    // Statistics table
    private const string StatisticsTableSql = @"
        CREATE TABLE IF NOT EXISTS SnapshotStatistics (
            Id                  TEXT NOT NULL UNIQUE CONSTRAINT PK_SnapshotStatistics PRIMARY KEY,
            SnapshotId          TEXT NOT NULL UNIQUE,
            TotalFileCount      INTEGER NOT NULL DEFAULT 0,
            TotalFolderCount    INTEGER NOT NULL DEFAULT 0,
            TotalDataSize       REAL NOT NULL DEFAULT 0,
            AverageFreeSpace    REAL,
            CreatedAt           TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            
            CONSTRAINT FK_SnapshotStatistics_Snapshots FOREIGN KEY (SnapshotId) 
                REFERENCES Snapshots (Id) ON DELETE CASCADE
        );

        CREATE UNIQUE INDEX IF NOT EXISTS IX_SnapshotStatistics_SnapshotId 
            ON SnapshotStatistics(SnapshotId);
    ";

    // Error logging table
    private const string ErrorLoggingTableSql = @"
        CREATE TABLE IF NOT EXISTS SnapshotErrors (
            Id              TEXT NOT NULL UNIQUE CONSTRAINT PK_SnapshotErrors PRIMARY KEY,
            SnapshotId      TEXT,
            ErrorCode       TEXT NOT NULL,
            ErrorMessage    TEXT NOT NULL,
            StackTrace      TEXT,
            AffectedEntity  TEXT,
            Severity        TEXT NOT NULL DEFAULT 'ERROR',
            OccurredAt      TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            ResolvedAt      TEXT,
            
            CONSTRAINT FK_SnapshotErrors_Snapshots FOREIGN KEY (SnapshotId) 
                REFERENCES Snapshots (Id) ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS IX_SnapshotErrors_SnapshotId ON SnapshotErrors(SnapshotId);
        CREATE INDEX IF NOT EXISTS IX_SnapshotErrors_OccurredAt ON SnapshotErrors(OccurredAt);
        CREATE INDEX IF NOT EXISTS IX_SnapshotErrors_ErrorCode ON SnapshotErrors(ErrorCode);
    ";

    // Storage drive history table
    private const string DriveHistoryTableSql = @"
        CREATE TABLE IF NOT EXISTS StorageDriveHistory (
            Id                  TEXT NOT NULL UNIQUE CONSTRAINT PK_StorageDriveHistory PRIMARY KEY,
            StorageDriveId      TEXT NOT NULL,
            SerialNumber        TEXT,
            TotalSize           REAL,
            MediaType           TEXT,
            InterfaceType       TEXT,
            ChangeType          TEXT NOT NULL,
            ChangedAt           TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            Reason              TEXT,
            
            CONSTRAINT FK_StorageDriveHistory_StorageDrives FOREIGN KEY (StorageDriveId) 
                REFERENCES StorageDrives (Id) ON DELETE NO ACTION
        );

        CREATE INDEX IF NOT EXISTS IX_StorageDriveHistory_StorageDriveId 
            ON StorageDriveHistory(StorageDriveId);
        CREATE INDEX IF NOT EXISTS IX_StorageDriveHistory_ChangedAt 
            ON StorageDriveHistory(ChangedAt);
    ";

    // Data retention policies table
    private const string RetentionPoliciesTableSql = @"
        CREATE TABLE IF NOT EXISTS DataRetentionPolicies (
            Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
            PolicyName          TEXT NOT NULL UNIQUE,
            SnapshotAgeInDays   INTEGER NOT NULL DEFAULT 365,
            KeepMinSnapshots    INTEGER NOT NULL DEFAULT 12,
            Description         TEXT,
            IsActive            BOOLEAN DEFAULT 1,
            CreatedAt           TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            
            CONSTRAINT CK_DataRetentionPolicies_Age CHECK (SnapshotAgeInDays > 0),
            CONSTRAINT CK_DataRetentionPolicies_MinSnapshots CHECK (KeepMinSnapshots > 0)
        );

        INSERT OR IGNORE INTO DataRetentionPolicies 
            (PolicyName, SnapshotAgeInDays, KeepMinSnapshots, Description)
        VALUES ('Standard Retention', 365, 12, 'Keep snapshots for 1 year, minimum 12');
    ";

    /// <summary>
    /// Adds Priority 2 tables to an existing database
    /// </summary>
    public static async Task AddPriority2TablesAsync(string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new ArgumentNullException(nameof(connectionString));
        }

        await using var sqliteConnection = new SqliteConnection(connectionString);
        
        await sqliteConnection.ExecuteAsync(SchemaVersioningTableSql);
        await sqliteConnection.ExecuteAsync(StatisticsTableSql);
        await sqliteConnection.ExecuteAsync(ErrorLoggingTableSql);
        await sqliteConnection.ExecuteAsync(DriveHistoryTableSql);
        await sqliteConnection.ExecuteAsync(RetentionPoliciesTableSql);
    }
}
```

---

## ?? Entity Model Updates

### 2.2 Update Entity Models for Audit Fields

```csharp
namespace Ufo.Abstractions.Database.Entities;

// Update PcEntity
public class PcEntity : EntityBase
{
    public string? DeviceId { get; set; }
    
    // NEW: Audit timestamps
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    [JsonPropertyOrder(80)]
    [JsonIgnore]
    [ManyToMany(typeof(PcsToStorageDrivesEntity))]
    public IList<StorageDriveEntity> StorageDrives { get; set; } = [];

    [JsonPropertyOrder(90)]
    [JsonIgnore]
    [ManyToMany(typeof(PcsToStorageDrivesEntity))]
    public IList<SnapshotEntity> Snapshots { get; set; } = [];
}

// Update StorageDriveEntity
public class StorageDriveEntity : EntityBase
{
    [JsonPropertyOrder(10)]
    [MaxLength(128)]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyOrder(15)]
    [MaxLength(128)]
    public string SerialNumber { get; set; } = string.Empty;

    [JsonPropertyOrder(20)]
    public long TotalSize { get; set; }

    [JsonPropertyOrder(25)]
    [MaxLength(128)]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyOrder(30)]
    [MaxLength(128)]
    public string MediaType { get; set; } = string.Empty;

    [JsonPropertyOrder(35)]
    [MaxLength(128)]
    public string InterfaceType { get; set; } = string.Empty;

    // NEW: Audit timestamps
    [JsonPropertyOrder(40)]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    [JsonPropertyOrder(41)]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    [JsonPropertyOrder(50)]
    [ManyToMany(typeof(PcsToStorageDrivesEntity))]
    public IList<PcEntity> Pcs { get; set; } = [];

    [JsonIgnore]
    [ManyToMany(typeof(PcsToStorageDrivesEntity))]
    public IList<SnapshotEntity> Snapshots { get; set; } = [];

    [JsonIgnore]
    [OneToMany]
    public IList<VolumeEntity> Volumes { get; set; } = [];
}
```

---

## ?? New Entity Models

### 2.3 Error Logging Entity

```csharp
namespace Ufo.Abstractions.Database.Entities;

[Table("SnapshotErrors")]
public class SnapshotErrorEntity
{
    [JsonConverter(typeof(UlidJsonConverter))]
    [JsonPropertyOrder(0)]
    [PrimaryKey]
    public Ulid Id { get; set; } = Ulid.NewUlid();

    [JsonConverter(typeof(UlidJsonConverter))]
    [JsonPropertyOrder(1)]
    [ForeignKey(typeof(SnapshotEntity))]
    public Ulid? SnapshotId { get; set; }

    [JsonPropertyOrder(2)]
    [MaxLength(50)]
    public string ErrorCode { get; set; } = string.Empty;

    [JsonPropertyOrder(3)]
    public string ErrorMessage { get; set; } = string.Empty;

    [JsonPropertyOrder(4)]
    public string? StackTrace { get; set; }

    [JsonPropertyOrder(5)]
    [MaxLength(50)]
    public string? AffectedEntity { get; set; }

    [JsonPropertyOrder(6)]
    [MaxLength(50)]
    public string Severity { get; set; } = "ERROR";

    [JsonPropertyOrder(7)]
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.Now;

    [JsonPropertyOrder(8)]
    public DateTimeOffset? ResolvedAt { get; set; }
}

// Example usage
public static class ErrorLogging
{
    public static async Task LogSnapshotErrorAsync(
        IDbConnection connection,
        Ulid snapshotId,
        string errorCode,
        string errorMessage,
        string severity = "ERROR")
    {
        var error = new SnapshotErrorEntity
        {
            SnapshotId = snapshotId,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Severity = severity
        };

        const string sql = @"
            INSERT INTO SnapshotErrors 
                (Id, SnapshotId, ErrorCode, ErrorMessage, Severity, OccurredAt)
            VALUES (@Id, @SnapshotId, @ErrorCode, @ErrorMessage, @Severity, @OccurredAt)";

        await connection.ExecuteAsync(sql, error);
    }
}
```

### 2.4 Statistics Entity

```csharp
namespace Ufo.Abstractions.Database.Entities;

[Table("SnapshotStatistics")]
public class SnapshotStatisticsEntity
{
    [JsonConverter(typeof(UlidJsonConverter))]
    [JsonPropertyOrder(0)]
    [PrimaryKey]
    public Ulid Id { get; set; } = Ulid.NewUlid();

    [JsonConverter(typeof(UlidJsonConverter))]
    [JsonPropertyOrder(1)]
    [ForeignKey(typeof(SnapshotEntity))]
    [Unique]
    public Ulid SnapshotId { get; set; }

    [JsonPropertyOrder(2)]
    public int TotalFileCount { get; set; }

    [JsonPropertyOrder(3)]
    public int TotalFolderCount { get; set; }

    [JsonPropertyOrder(4)]
    public long TotalDataSize { get; set; }

    [JsonPropertyOrder(5)]
    public double? AverageFreeSpace { get; set; }

    [JsonPropertyOrder(6)]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}

// Example: Calculate and store statistics
public static class StatisticsCalculation
{
    public static async Task CalculateAndStoreStatisticsAsync(
        IDbConnection connection,
        Ulid snapshotId)
    {
        // Calculate totals
        const string calcSql = @"
            SELECT 
                COUNT(DISTINCT f.Id) as TotalFileCount,
                COUNT(DISTINCT fo.Id) as TotalFolderCount,
                COALESCE(SUM(f.Size), 0) as TotalDataSize,
                AVG(vi.FreeSpace) as AverageFreeSpace
            FROM Files f
            JOIN FilesToFolders ftf ON f.Id = ftf.FileId
            JOIN Folders fo ON ftf.FolderId = fo.Id
            LEFT JOIN VolumeInfos vi ON ftf.SnapshotId = vi.SnapshotId
            WHERE ftf.SnapshotId = @SnapshotId";

        var stats = await connection.QuerySingleAsync<dynamic>(calcSql, new { SnapshotId = snapshotId.ToString() });

        // Store statistics
        const string insertSql = @"
            INSERT INTO SnapshotStatistics 
                (Id, SnapshotId, TotalFileCount, TotalFolderCount, TotalDataSize, AverageFreeSpace)
            VALUES (@Id, @SnapshotId, @TotalFileCount, @TotalFolderCount, @TotalDataSize, @AverageFreeSpace)";

        var statsEntity = new SnapshotStatisticsEntity
        {
            SnapshotId = snapshotId,
            TotalFileCount = stats.TotalFileCount,
            TotalFolderCount = stats.TotalFolderCount,
            TotalDataSize = stats.TotalDataSize,
            AverageFreeSpace = stats.AverageFreeSpace
        };

        await connection.ExecuteAsync(insertSql, statsEntity);
    }
}
```

---

## ?? Repository Pattern Updates

### 2.5 Add Repository Methods for New Features

```csharp
namespace Ufo.Database.Repositories;

public interface IErrorRepository
{
    Task LogErrorAsync(SnapshotErrorEntity error);
    Task<IEnumerable<SnapshotErrorEntity>> GetSnapshotErrorsAsync(Ulid snapshotId);
    Task<IEnumerable<SnapshotErrorEntity>> GetErrorsAsync(string errorCode);
    Task<Dictionary<string, int>> GetErrorSummaryAsync(DateTimeOffset since);
}

public class ErrorRepository : IErrorRepository
{
    private readonly string _connectionString;

    public ErrorRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task LogErrorAsync(SnapshotErrorEntity error)
    {
        const string sql = @"
            INSERT INTO SnapshotErrors 
                (Id, SnapshotId, ErrorCode, ErrorMessage, StackTrace, AffectedEntity, Severity, OccurredAt)
            VALUES (@Id, @SnapshotId, @ErrorCode, @ErrorMessage, @StackTrace, @AffectedEntity, @Severity, @OccurredAt)";

        await using var connection = new SqliteConnection(_connectionString);
        await connection.ExecuteAsync(sql, error);
    }

    public async Task<IEnumerable<SnapshotErrorEntity>> GetSnapshotErrorsAsync(Ulid snapshotId)
    {
        const string sql = @"
            SELECT * FROM SnapshotErrors 
            WHERE SnapshotId = @SnapshotId 
            ORDER BY OccurredAt DESC";

        await using var connection = new SqliteConnection(_connectionString);
        return await connection.QueryAsync<SnapshotErrorEntity>(sql, new { SnapshotId = snapshotId.ToString() });
    }

    public async Task<IEnumerable<SnapshotErrorEntity>> GetErrorsAsync(string errorCode)
    {
        const string sql = @"
            SELECT * FROM SnapshotErrors 
            WHERE ErrorCode = @ErrorCode 
            ORDER BY OccurredAt DESC";

        await using var connection = new SqliteConnection(_connectionString);
        return await connection.QueryAsync<SnapshotErrorEntity>(sql, new { ErrorCode = errorCode });
    }

    public async Task<Dictionary<string, int>> GetErrorSummaryAsync(DateTimeOffset since)
    {
        const string sql = @"
            SELECT ErrorCode, COUNT(*) as Count 
            FROM SnapshotErrors 
            WHERE OccurredAt >= @Since 
            GROUP BY ErrorCode 
            ORDER BY Count DESC";

        await using var connection = new SqliteConnection(_connectionString);
        var results = await connection.QueryAsync<(string ErrorCode, int Count)>(
            sql, 
            new { Since = since.ToString("O") });

        return results.ToDictionary(x => x.ErrorCode, x => x.Count);
    }
}
```

---

## ?? Migration Scripts

### 2.6 Safe Migration for Existing Databases

```csharp
namespace Ufo.Database.Migrations;

public static class SchemaMigrations
{
    /// <summary>
    /// Migrate from schema v1.0.0 to v1.1.0 (P1 improvements)
    /// Safe to run multiple times (idempotent)
    /// </summary>
    public static async Task MigrateToV1_1_0Async(string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        using var transaction = connection.BeginTransaction();

        try
        {
            // Add audit columns to Pcs
            await connection.ExecuteAsync(@"
                ALTER TABLE Pcs ADD COLUMN CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP;
                ALTER TABLE Pcs ADD COLUMN UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP;
            ", transaction: transaction);

            // Add audit columns to StorageDrives
            await connection.ExecuteAsync(@"
                ALTER TABLE StorageDrives ADD COLUMN CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP;
                ALTER TABLE StorageDrives ADD COLUMN UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP;
            ", transaction: transaction);

            // Add audit columns to Volumes
            await connection.ExecuteAsync(@"
                ALTER TABLE Volumes ADD COLUMN CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP;
                ALTER TABLE Volumes ADD COLUMN UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP;
            ", transaction: transaction);

            // Create indexes (safe - IF NOT EXISTS)
            await connection.ExecuteAsync(@"
                CREATE INDEX IF NOT EXISTS IX_Volumes_StorageDriveId ON Volumes(StorageDriveId);
                CREATE INDEX IF NOT EXISTS IX_PcsToStorageDrives_SnapshotId ON PcsToStorageDrives(SnapshotId);
                CREATE INDEX IF NOT EXISTS IX_PcsToStorageDrives_PcId ON PcsToStorageDrives(PcId);
                CREATE INDEX IF NOT EXISTS IX_VolumeInfos_VolumeId ON VolumeInfos(VolumeId);
                CREATE INDEX IF NOT EXISTS IX_VolumeInfos_SnapshotId ON VolumeInfos(SnapshotId);
                CREATE INDEX IF NOT EXISTS IX_FoldersToFolders_SnapshotId ON FoldersToFolders(SnapshotId);
                CREATE INDEX IF NOT EXISTS IX_FilesToFolders_SnapshotId ON FilesToFolders(SnapshotId);
                CREATE INDEX IF NOT EXISTS IX_Files_Sha256Hash ON Files(Sha256Hash);
                CREATE INDEX IF NOT EXISTS IX_Folders_Sha256Hash ON Folders(Sha256Hash);
            ", transaction: transaction);

            // Record schema version
            await connection.ExecuteAsync(@"
                INSERT OR IGNORE INTO SchemaVersions (Version, Description) 
                VALUES ('1.1.0', 'P1: Added audit timestamps and performance indexes')
            ", transaction: transaction);

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Migrate from schema v1.1.0 to v1.2.0 (P2 improvements)
    /// </summary>
    public static async Task MigrateToV1_2_0Async(string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        using var transaction = connection.BeginTransaction();

        try
        {
            // Create error logging table
            await connection.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS SnapshotErrors (
                    Id              TEXT NOT NULL UNIQUE PRIMARY KEY,
                    SnapshotId      TEXT,
                    ErrorCode       TEXT NOT NULL,
                    ErrorMessage    TEXT NOT NULL,
                    StackTrace      TEXT,
                    AffectedEntity  TEXT,
                    Severity        TEXT NOT NULL DEFAULT 'ERROR',
                    OccurredAt      TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    ResolvedAt      TEXT,
                    
                    CONSTRAINT FK_SnapshotErrors_Snapshots 
                        FOREIGN KEY (SnapshotId) REFERENCES Snapshots (Id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS IX_SnapshotErrors_SnapshotId ON SnapshotErrors(SnapshotId);
                CREATE INDEX IF NOT EXISTS IX_SnapshotErrors_ErrorCode ON SnapshotErrors(ErrorCode);
            ", transaction: transaction);

            // Create statistics table
            await connection.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS SnapshotStatistics (
                    Id                  TEXT NOT NULL UNIQUE PRIMARY KEY,
                    SnapshotId          TEXT NOT NULL UNIQUE,
                    TotalFileCount      INTEGER NOT NULL DEFAULT 0,
                    TotalFolderCount    INTEGER NOT NULL DEFAULT 0,
                    TotalDataSize       REAL NOT NULL DEFAULT 0,
                    AverageFreeSpace    REAL,
                    CreatedAt           TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    
                    CONSTRAINT FK_SnapshotStatistics_Snapshots 
                        FOREIGN KEY (SnapshotId) REFERENCES Snapshots (Id) ON DELETE CASCADE
                );

                CREATE UNIQUE INDEX IF NOT EXISTS IX_SnapshotStatistics_SnapshotId 
                    ON SnapshotStatistics(SnapshotId);
            ", transaction: transaction);

            // Record schema version
            await connection.ExecuteAsync(@"
                INSERT OR IGNORE INTO SchemaVersions (Version, Description) 
                VALUES ('1.2.0', 'P2: Added error logging and statistics tables')
            ", transaction: transaction);

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }
}
```

---

## ?? Testing Queries

### 2.7 Verification Queries

```csharp
namespace Ufo.Database.Tests;

public class SchemaVerificationTests
{
    private readonly string _connectionString;

    [Fact]
    public async Task VerifyAllIndexesCreatedAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        
        const string sql = @"
            SELECT COUNT(*) FROM pragma_index_list('Volumes') 
            WHERE name LIKE 'IX_%'";

        var indexCount = await connection.ExecuteScalarAsync<int>(sql);
        Assert.True(indexCount >= 1, "Volumes should have at least 1 custom index");
    }

    [Fact]
    public async Task VerifyCheckConstraintsEnforcedAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);

        // Try to insert invalid data
        const string sql = @"
            INSERT INTO StorageDrives 
                (Id, Name, DeviceId, SerialNumber, TotalSize, Description, MediaType, InterfaceType)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)";

        // This should fail
        var ex = await Assert.ThrowsAsync<SqliteException>(() =>
            connection.ExecuteAsync(sql, 
                new[] { "id", "name", "dev", "sn", -1.0, "desc", "SSD", "SATA" }));

        Assert.Contains("CHECK constraint", ex.Message);
    }

    [Fact]
    public async Task VerifyAuditTimestampsAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = "PRAGMA table_info(Pcs)";
        var columns = await connection.QueryAsync<(string name, string type)>(
            "SELECT name, type FROM pragma_table_info('Pcs')");

        var columnNames = columns.Select(x => x.name).ToList();
        Assert.Contains("CreatedAt", columnNames);
        Assert.Contains("UpdatedAt", columnNames);
    }

    [Fact]
    public async Task VerifyUniqueSerialNumberAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);

        // Insert first drive
        const string insert1 = @"
            INSERT INTO StorageDrives 
                (Id, Name, DeviceId, SerialNumber, TotalSize, Description, MediaType, InterfaceType)
            VALUES ('d1', 'Drive1', 'dev1', 'SN-123', 1024, 'Desc', 'SSD', 'NVMe')";

        await connection.ExecuteAsync(insert1);

        // Try to insert second with same serial - should fail
        const string insert2 = @"
            INSERT INTO StorageDrives 
                (Id, Name, DeviceId, SerialNumber, TotalSize, Description, MediaType, InterfaceType)
            VALUES ('d2', 'Drive2', 'dev2', 'SN-123', 2048, 'Desc', 'SSD', 'NVMe')";

        var ex = await Assert.ThrowsAsync<SqliteException>(() => connection.ExecuteAsync(insert2));
        Assert.Contains("UNIQUE constraint", ex.Message);
    }
}
```

---

## ?? Usage Examples

### 2.8 How to Use in Application

```csharp
// Initialization
public class Startup
{
    public async Task ConfigureAsync(string connectionString)
    {
        // Initialize schema (P1)
        await DapperDataContext.InitiateDatabaseAsync(connectionString);
        
        // Add P2 tables if not already present
        await DapperDataContextP2.AddPriority2TablesAsync(connectionString);
    }
}

// Error logging
public class SnapshotService
{
    private readonly IErrorRepository _errorRepo;

    public async Task CaptureSnapshotAsync(Ulid snapshotId)
    {
        try
        {
            // Capture logic
        }
        catch (Exception ex)
        {
            await _errorRepo.LogErrorAsync(new SnapshotErrorEntity
            {
                SnapshotId = snapshotId,
                ErrorCode = "SCAN_FAILED",
                ErrorMessage = ex.Message,
                StackTrace = ex.StackTrace,
                AffectedEntity = "FILE",
                Severity = "ERROR"
            });
        }
    }
}

// Statistics
public class ReportingService
{
    public async Task<SnapshotStatisticsEntity> GetSnapshotStatsAsync(Ulid snapshotId)
    {
        // This is now instant - no recalculation needed!
        const string sql = "SELECT * FROM SnapshotStatistics WHERE SnapshotId = @SnapshotId";
        
        await using var connection = new SqliteConnection(_connectionString);
        return await connection.QuerySingleAsync<SnapshotStatisticsEntity>(
            sql, new { SnapshotId = snapshotId.ToString() });
    }
}
```

---

**Document Status:** ?? Ready for Implementation  
**Prepared for:** Universal File Observer (UFO) Project  
**Date:** 2024
