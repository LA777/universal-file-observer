# Universal File Observer (UFO) - Project Analysis

## Project Overview
The Universal File Observer is a .NET 10 application that monitors and tracks file system changes across multiple PCs and storage drives. It creates snapshots of the file system hierarchy and stores metadata about files, folders, volumes, and storage devices.

**Technology Stack:**
- .NET 10
- C# 14.0
- SQLite Database
- Dapper ORM (for data access)
- SQLiteNetExtensions (for ORM relationships)

---

## Database Entities

| Entity | Table Name | Purpose | Key Properties |
|--------|-----------|---------|-----------------|
| **Pc** | `Pcs` | Represents a personal computer | Id (PK), Name, DeviceId |
| **StorageDrive** | `StorageDrives` | Represents a physical storage device | Id (PK), Name, DeviceId, SerialNumber, TotalSize, MediaType, InterfaceType, Description |
| **Volume** | `Volumes` | Represents a partition on a storage drive | Id (PK), DriveLetter, VolumeName, VolumeSize, VolumeSerialNumber, StorageDriveId (FK), Description |
| **VolumeInfo** | `VolumeInfos` | Tracks volume state at a specific point in time | Id (PK), FreeSpace, DriveStatus, VolumeId (FK), SnapshotId (FK) |
| **Snapshot** | `Snapshots` | Point-in-time record of file system state | Id (PK), Timestamp |
| **FsFolder** | `Folders` | Represents a directory in the file system | Id (PK), Name, Size, Sha256Hash |
| **FsFile** | `Files` | Represents a file in the file system | Id (PK), Name, Size, Sha256Hash, FileExtension |
| **PcsToStorageDrives** | `PcsToStorageDrives` | Junction table linking Pcs, StorageDrives, and Snapshots (Many-to-Many) | PcId (FK), StorageDriveId (FK), SnapshotId (FK) |
| **FoldersToFolders** | `FoldersToFolders` | Junction table for folder hierarchy (Parent-Child relationships) | SnapshotId (FK), ParentFolderId (FK), ChildFolderId (FK) |
| **FilesToFolders** | `FilesToFolders` | Junction table linking files to their parent folders | SnapshotId (FK), FolderId (FK), FileId (FK) |

---

## Entity Relationships Diagram

```
???????????????????????????????????????????????????????????????????????????
?                     UNIVERSAL FILE OBSERVER DATABASE                    ?
???????????????????????????????????????????????????????????????????????????

                              ????????????????
                              ?      PC      ?
                              ?   (Pcs)      ?
                              ????????????????
                              ? Id (PK)      ?
                              ? Name         ?
                              ? DeviceId     ?
                              ????????????????
                                     ?
                                     ? (Many-to-Many)
                                     ?
                        ????????????????????????????
                        ?  PcsToStorageDrives     ?
                        ?  (Junction Table)       ?
                        ??????????????????????????
                        ? PcId (FK)              ?
                        ? StorageDriveId (FK)    ?
                        ? SnapshotId (FK)        ?
                        ????????????????????????????
                                     ?
                ??????????????????????????????????????????
                ?                    ?                   ?
    ????????????????????????  ??????????????????  ?????????????????????
    ?  StorageDrive        ?  ?   Snapshot     ?  ?   (1-1 relation)  ?
    ?  (StorageDrives)     ?  ?   (Snapshots)  ?  ?   shown in PC      ?
    ????????????????????????  ??????????????????  ?????????????????????
    ? Id (PK)              ?  ? Id (PK)        ?
    ? Name                 ?  ? Timestamp      ?
    ? DeviceId             ?  ? RootFolder (FK)?
    ? SerialNumber         ?  ? VolumeInfo (FK)?
    ? TotalSize            ?  ??????????????????
    ? Description          ?           ?
    ? MediaType            ?           ? (One-to-Many)
    ? InterfaceType        ?           ?
    ????????????????????????           ?
               ?                       ?
               ? (One-to-Many)    ???????????????????????
               ?                  ?   VolumeInfo        ?
    ???????????????????????????   ?   (VolumeInfos)     ?
    ?  Volume                 ?   ???????????????????????
    ?  (Volumes)              ?   ? Id (PK)             ?
    ???????????????????????????   ? FreeSpace           ?
    ? Id (PK)                 ?   ? DriveStatus         ?
    ? DriveLetter             ?   ? VolumeId (FK)       ?
    ? VolumeName              ?   ? SnapshotId (FK)     ?
    ? Description             ?   ???????????????????????
    ? VolumeSerialNumber      ?
    ? VolumeSize              ?
    ? StorageDriveId (FK)     ?
    ???????????????????????????


                      FILE SYSTEM HIERARCHY
                      ?????????????????????
                        
                        ????????????????????
                        ?   FsFolder       ?
                        ?   (Folders)      ?
                        ????????????????????
                        ? Id (PK)          ?
                        ? Name             ?
                        ? Size             ?
                        ? Sha256Hash       ?
                        ????????????????????
                                 ?
                ???????????????????????????????????
                ?                ?                ?
        (Many-to-Many)    (Many-to-Many)  (Many-to-Many)
                ?                ?                ?
    ???????????????????????   ?        ????????????????????????
    ? FoldersToFolders    ?   ?        ?  FilesToFolders     ?
    ? (Junction)          ?   ?        ?  (Junction)         ?
    ???????????????????????   ?        ???????????????????????
    ? SnapshotId (FK)     ?   ?        ? SnapshotId (FK)     ?
    ? ParentFolderId (FK) ?   ?        ? FolderId (FK)       ?
    ? ChildFolderId (FK)  ?   ?        ? FileId (FK)         ?
    ???????????????????????   ?        ???????????????????????
                              ?
                    ????????????????????
                    ?   FsFile         ?
                    ?   (Files)        ?
                    ????????????????????
                    ? Id (PK)          ?
                    ? Name             ?
                    ? Size             ?
                    ? Sha256Hash       ?
                    ? FileExtension    ?
                    ????????????????????
```

---

## SQLite Database Schema Summary

### Core Storage Structure
- **Pcs**: Stores computer information for multi-PC support
- **StorageDrives**: Physical storage devices connected to PCs (includes device metadata like serial number, media type, interface)
- **Volumes**: Partitions on storage drives (drive letter, volume name, size)
- **VolumeInfos**: Point-in-time snapshots of volume state (tracks free space and drive status at specific timestamps)

### File System Hierarchy Structure
- **Folders**: Directory entries with associated metadata (size, SHA256 hash for integrity verification)
- **Files**: File entries with metadata (size, extension, SHA256 hash)
- **FoldersToFolders**: Hierarchical relationships between folders enabling parent-child directory trees
- **FilesToFolders**: Relationships between files and their parent directories

### Temporal & Relationship Tables
- **Snapshots**: Point-in-time captures of the entire file system state with timestamps
- **PcsToStorageDrives**: Links PCs to their storage drives within a snapshot context
  - Composite PK: (PcId, StorageDriveId, SnapshotId)
  - Enables tracking which drives were connected to which PC at which time
- **VolumeInfos**: Captures volume state (free space, status) at each snapshot
  - Allows historical analysis of storage usage patterns
- **FoldersToFolders & FilesToFolders**: Both include SnapshotId to track file system state across time
  - Enables comparison of file system state between different snapshots

---

## Key Design Patterns

### 1. Temporal Snapshots
- All file system and volume data is snapshot-aware
- Allows historical tracking and comparison of file system states
- Each snapshot represents a complete state capture at a specific point in time

### 2. Hierarchical File System Storage
- Folders can have parent and child relationships for representing directory trees
- FoldersToFolders junction table supports unlimited directory depth
- ParentFolderId is nullable to support root folders

### 3. Many-to-Many Relationships
- Uses junction tables for complex relationships:
  - PcsToStorageDrives: Multiple PCs can have multiple storage drives across multiple snapshots
  - FoldersToFolders: Supports complex folder hierarchies
  - FilesToFolders: Files linked to folders within snapshots

### 4. Entity Base Class Pattern
- Common inheritance pattern with `EntityBase` providing:
  - Unique identifier (Id)
  - Common name property
- Inherited by PcEntity, StorageDriveEntity, FsFolderEntity, FsFileEntity

### 5. Content Hashing for Integrity
- SHA256 hashes for both files and folders
- Enables duplicate detection and integrity verification
- Useful for identifying unchanged content across snapshots

### 6. ULID Identifiers
- Uses ULID (Universally Unique Lexicographically Sortable Identifier) instead of GUID
- Provides sortable unique identifiers with timestamp information
- Improves database index efficiency compared to random GUIDs

---

## Foreign Key Relationships & Cascading Delete Strategy

| Foreign Key | Table | References | Delete Rule | Rationale |
|-------------|-------|-----------|------------|-----------|
| FK_Volumes_StorageDrives | Volumes | StorageDrives | ON DELETE CASCADE | Volumes depend on storage drives |
| FK_VolumeInfos_Volumes | VolumeInfos | Volumes | ON DELETE CASCADE | Volume info tied to volumes |
| FK_VolumeInfos_Snapshots | VolumeInfos | Snapshots | ON DELETE NO ACTION | Preserves snapshot history |
| FK_PcsToStorageDrives_Pcs | PcsToStorageDrives | Pcs | ON DELETE NO ACTION | Maintains historical relationships |
| FK_PcsToStorageDrives_StorageDrives | PcsToStorageDrives | StorageDrives | ON DELETE NO ACTION | Maintains historical relationships |
| FK_PcsToStorageDrives_Snapshots | PcsToStorageDrives | Snapshots | ON DELETE NO ACTION | Preserves snapshot associations |
| FK_FoldersToFolders_Snapshots | FoldersToFolders | Snapshots | ON DELETE NO ACTION | Preserves snapshot history |
| FK_FoldersToFolders_Folders | FoldersToFolders | Folders | ON DELETE NO ACTION | Maintains folder relationships |
| FK_FilesToFolders_Folders | FilesToFolders | Folders | ON DELETE NO ACTION | Maintains file-folder associations |
| FK_FilesToFolders_Files | FilesToFolders | Files | ON DELETE NO ACTION | Maintains file references |
| FK_FilesToFolders_Snapshots | FilesToFolders | Snapshots | ON DELETE NO ACTION | Preserves snapshot history |

**Strategy Notes:**
- Cascade deletes used only for dependent data (Volumes ? StorageDrives, VolumeInfos ? Volumes)
- NO ACTION used for snapshots to preserve complete historical records
- Prevents accidental data loss when snapshots need to be queried later

---

## Data Flow & Usage Scenarios

```
???????????????????????????????????????????????????????????????
?              CAPTURE WORKFLOW                               ?
???????????????????????????????????????????????????????????????
?                                                             ?
?  1. System Scan                                             ?
?     ??? Enumerate PCs ? StorageDrives ? Volumes           ?
?                                                             ?
?  2. Create Snapshot                                         ?
?     ??? Record Timestamp & VolumeInfo                       ?
?         (FreeSpace, DriveStatus)                            ?
?                                                             ?
?  3. Build File System Hierarchy                             ?
?     ??? Recursively scan folders & files                    ?
?         ? Calculate SHA256 hashes                           ?
?         ? Store in Folders & Files tables                   ?
?         ? Create FoldersToFolders & FilesToFolders links    ?
?                                                             ?
?  4. Associate with Snapshot                                 ?
?     ??? Link PCs to StorageDrives via                       ?
?         PcsToStorageDrives junction table                   ?
?                                                             ?
???????????????????????????????????????????????????????????????

???????????????????????????????????????????????????????????????
?              QUERY SCENARIOS                                ?
???????????????????????????????????????????????????????????????
?                                                             ?
?  • Get all files in a folder for a specific snapshot       ?
?    ? Query FilesToFolders WHERE SnapshotId & FolderId      ?
?                                                             ?
?  • Track storage usage over time                           ?
?    ? Query VolumeInfos with FreeSpace trends               ?
?                                                             ?
?  • Find all PCs with a specific storage drive              ?
?    ? Query PcsToStorageDrives WHERE StorageDriveId         ?
?                                                             ?
?  • Compare folder contents between snapshots               ?
?    ? Query FoldersToFolders for both snapshots             ?
?                                                             ?
?  • Identify duplicate files by hash                        ?
?    ? Query Files WHERE Sha256Hash matches                  ?
?                                                             ?
???????????????????????????????????????????????????????????????
```

---

## Strengths of the Design

? **Complete Historical Tracking**
- Maintains complete file system history through snapshots
- Enables time-series analysis of storage and file system changes
- Supports audit trails and compliance reporting

? **Multi-PC & Multi-Drive Support**
- Designed to track multiple PCs with multiple storage drives
- Flexible enough for enterprise environments
- Scales to multiple snapshots over time

? **Efficient Data Organization**
- Proper indexing with primary/foreign keys for query optimization
- Composite primary keys prevent duplicate entries
- Normalized schema reduces data redundancy

? **Rich Metadata Tracking**
- Tracks both structural data (hierarchy) and metadata (size, hash, status)
- SHA256 hashes enable integrity verification and duplicate detection
- Status tracking for volumes (healthy, warning, error states)

? **Snapshot-Based Approach**
- Point-in-time captures enable consistent state representation
- Allows comparison and analytics across multiple time periods
- Supports branching analysis (what changed between snapshot X and Y)

? **Flexible Folder Hierarchy**
- Supports unlimited directory depth
- Nullable ParentFolderId allows root folder representation
- Enables querying folder trees at any level

---

## Design Considerations & Potential Optimizations

?? **Data Volume at Scale**
- **Issue**: Large file systems can result in significant data volume
  - One entry per file/folder per snapshot
  - Example: 1M files × 52 snapshots/year = 52M file records
- **Mitigation**: Consider archiving old snapshots or implementing incremental snapshots

?? **Query Performance on Deep Hierarchies**
- **Issue**: Deep folder hierarchies require multiple joins
- **Optimization**: Consider materialized path or nested set model for ancestor queries
- **Current Approach**: Works well with indexed ForeignKeys but may need optimization for large hierarchies

?? **Snapshot Deletion Constraints**
- **Issue**: Foreign key constraints with ON DELETE NO ACTION prevent cascading deletes
- **Benefit**: Preserves data integrity and historical records
- **Note**: Requires explicit data cleanup procedures if snapshots must be deleted

?? **Storage Drive Updates**
- **Issue**: StorageDrive table doesn't include timestamps
- **Consideration**: Cannot track when drive properties changed (e.g., serial number updates)
- **Potential Fix**: Add CreatedAt, UpdatedAt timestamps to Pcs, StorageDrives

---

## Projects in Solution

| Project | Purpose |
|---------|---------|
| **Ufo.Abstractions** | Contains entity definitions, interfaces, and DTOs |
| **Ufo.Database** | Database context (Dapper), repositories, and migrations |
| **Ufo.Server** | ASP.NET Core API server with controllers and data providers |
| **Ufo.UnitTests** | Unit tests for repositories and services |

---

## Related Files & Components

### Database Layer
- `Ufo.Database/Contexts/DapperDataContext.cs` - Schema initialization
- `Ufo.Database/Repositories/FileSystemSqLiteRepository.cs` - Data access logic
- Entity definitions in `Ufo.Abstractions/Database/Entities/`

### API Layer
- `Ufo.Server/Controllers/SnapshotController.cs` - Snapshot endpoints
- `Ufo.Server/DataProviders/SystemInfoProvider.cs` - System data collection

---

## Technology Integration

- **SQLite** via `Microsoft.Data.Sqlite` for embedded database
- **Dapper** for lightweight ORM and query execution
- **SQLiteNetExtensions** for navigation properties and relationship management
- **ULID** for sortable unique identifiers
- **SHA256** for content hashing and integrity verification

---

*Analysis Date: 2024*  
*Project: Universal File Observer (UFO)*  
*Framework: .NET 10 with C# 14.0*
