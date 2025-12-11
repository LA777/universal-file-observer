# 📊 Universal File Observer (UFO) - Project Analysis

## 🎯 Project Overview

The **Universal File Observer** is a comprehensive .NET 10 application designed to monitor and track file system changes across multiple PCs and storage drives. It creates point-in-time snapshots of the file system hierarchy and stores detailed metadata about files, folders, volumes, and storage devices.

**Key Capabilities:**
- 🖥️ Multi-PC system monitoring
- 💾 Multiple storage drive tracking
- 📸 Snapshot-based state capture
- 🔐 SHA256 integrity hashing
- 📈 Historical change tracking
- 📊 Storage analytics and reporting

### 📚 Technology Stack

| Component | Technology |
|-----------|-----------|
| Framework | **.NET 10** |
| Language | **C# 14.0** |
| Database | **SQLite** |
| Data Access | **Dapper ORM** |
| Relationships | **SQLiteNetExtensions** |
| Identifiers | **ULID** (Sortable GUIDs) |
| Hashing | **SHA256** |

---

## 🗄️ Database Entities

### Core Entities

| Entity | Table Name | Purpose | Key Properties |
|:-------|:-----------|:--------|:----------------|
| **Pc** | `Pcs` | Personal computer records | `Id` (PK), `Name`, `DeviceId` |
| **StorageDrive** | `StorageDrives` | Physical storage devices | `Id` (PK), `Name`, `DeviceId`, `SerialNumber`, `TotalSize`, `MediaType`, `InterfaceType`, `Description` |
| **Volume** | `Volumes` | Storage partitions | `Id` (PK), `DriveLetter`, `VolumeName`, `VolumeSize`, `VolumeSerialNumber`, `StorageDriveId` (FK), `Description` |
| **VolumeInfo** | `VolumeInfos` | Point-in-time volume state | `Id` (PK), `FreeSpace`, `DriveStatus`, `VolumeId` (FK), `SnapshotId` (FK) |
| **Snapshot** | `Snapshots` | File system state captures | `Id` (PK), `Timestamp` |
| **FsFolder** | `Folders` | Directory entries | `Id` (PK), `Name`, `Size`, `Sha256Hash` |
| **FsFile** | `Files` | File entries | `Id` (PK), `Name`, `Size`, `Sha256Hash`, `FileExtension` |

### Junction Tables (Relationships)

| Entity | Table Name | Purpose | Composite Key |
|:-------|:-----------|:--------|:--------------|
| **PcsToStorageDrives** | `PcsToStorageDrives` | Links PCs to storage drives | `(PcId, StorageDriveId, SnapshotId)` |
| **FoldersToFolders** | `FoldersToFolders` | Folder hierarchy (parent-child) | `(SnapshotId, ParentFolderId, ChildFolderId)` |
| **FilesToFolders** | `FilesToFolders` | Links files to folders | `(SnapshotId, FolderId, FileId)` |

---

## 📐 Entity Relationships Diagram

```
╔════════════════════════════════════════════════════════════════════════════╗
║                  UNIVERSAL FILE OBSERVER DATABASE MODEL                    ║
╚════════════════════════════════════════════════════════════════════════════╝

                                ┌─────────────────┐
                                │      PC         │
                                │   (Pcs)         │
                                ├─────────────────┤
                                │ ◆ Id            │
                                │ • Name          │
                                │ • DeviceId      │
                                └────────┬────────┘
                                         │
                                         │ Many-to-Many
                                         │
                        ┌────────────────┼─────────────────┐
                        │   PcsToStorageDrives (Junction)  │
                        ├──────────────────────────────────┤
                        │ ◆ PcId (FK)                      │
                        │ ◆ StorageDriveId (FK)            │
                        │ ◆ SnapshotId (FK)                │
                        └────────────────┬─────────────────┘
                                         │
                ┌────────────────────────┼────────────────────┐
                │                        │                    │
    ┌───────────▼──────────────┐  ┌──────▼─────────┐  ┌───────▼──────────┐
    │   StorageDrive           │  │   Snapshot     │  │  (1:1)           │
    │   (StorageDrives)        │  │   (Snapshots)  │  │  relation        │
    ├──────────────────────────┤  ├────────────────┤  └──────────────────┘
    │ ◆ Id                     │  │ ◆ Id           │
    │ • Name                   │  │ • Timestamp    │
    │ • DeviceId               │  │ • RootFolder*  │
    │ • SerialNumber           │  │ • VolumeInfo*  │
    │ • TotalSize              │  └────────┬───────┘
    │ • Description            │           │
    │ • MediaType              │           │ One-to-Many
    │ • InterfaceType          │           │
    └──────────┬───────────────┘           │
               │                           │
               │ One-to-Many         ┌─────▼────────────────┐
               │                     │   VolumeInfo         │
    ┌──────────▼──────────────┐      │   (VolumeInfos)      │
    │  Volume                 │      ├──────────────────────┤
    │  (Volumes)              │      │ ◆ Id                 │
    ├─────────────────────────┤      │ • FreeSpace          │
    │ ◆ Id                    │      │ • DriveStatus        │
    │ • DriveLetter           │      │ ► VolumeId (FK)      │
    │ • VolumeName            │      │ ► SnapshotId (FK)    │
    │ • Description           │      └──────────────────────┘
    │ • VolumeSerialNumber    │
    │ • VolumeSize            │
    │ ► StorageDriveId (FK)   │
    └─────────────────────────┘


                         ╔═══════════════════════════════════════╗
                         ║    FILE SYSTEM HIERARCHY              ║
                         ╚═══════════════════════════════════════╝

                        ┌──────────────────────┐
                        │   FsFolder           │
                        │   (Folders)          │
                        ├──────────────────────┤
                        │ ◆ Id                 │
                        │ • Name               │
                        │ • Size               │
                        │ • Sha256Hash         │
                        └────────────┬─────────┘
                                     │
                   ┌─────────────────┼──────────────────┐
                   │                 │                  │
             Many-to-Many       Many-to-Many        Many-to-Many
                   │                 │                  │
    ┌──────────────▼──────────┐      │      ┌───────────▼──────────┐
    │  FoldersToFolders       │      │      │  FilesToFolders      │
    │  (Hierarchy Junction)   │      │      │  (File Mapping)      │
    ├─────────────────────────┤      │      ├──────────────────────┤
    │ ◆ SnapshotId (FK)       │      │      │ ◆ SnapshotId (FK)    │
    │ • ParentFolderId (FK)   │      │      │ ◆ FolderId (FK)      │
    │ ◆ ChildFolderId (FK)    │      │      │ ◆ FileId (FK)        │
    └─────────────────────────┘      │      └──────────────────────┘
                                     │
                           ┌─────────▼──────────┐
                           │   FsFile           │
                           │   (Files)          │
                           ├────────────────────┤
                           │ ◆ Id               │
                           │ • Name             │
                           │ • Size             │
                           │ • Sha256Hash       │
                           │ • FileExtension    │
                           └────────────────────┘

Legend: ◆ = Primary Key | • = Standard Property | ► = Foreign Key
```

---

## 📑 SQLite Database Schema

### 🖥️ Storage Infrastructure

| Table | Purpose | Key Strategy |
|:------|:--------|:-------------|
| **Pcs** | Computer system records with device identification | ULID primary key for sortable uniqueness |
| **StorageDrives** | Physical hardware devices attached to PCs | Stores metadata: serial number, media type, interface type |
| **Volumes** | Logical partitions on storage drives | Links to StorageDrive via foreign key (CASCADE delete) |
| **VolumeInfos** | Temporal snapshots of volume state | Tracks free space and drive status over time |

**Usage Example:**
```
PC (Desktop) → StorageDrive (Samsung SSD) → Volume (C:) → VolumeInfo (free space tracking)
```

### 📂 File System Hierarchy

| Table | Purpose | Structure |
|:------|:--------|:----------|
| **Folders** | Directory entries with metadata | Parent-child relationships via FoldersToFolders |
| **Files** | File entries with extension and hash | Linked to parent folders via FilesToFolders |
| **FoldersToFolders** | Represents directory tree structure | Nullable ParentFolderId for root folders |
| **FilesToFolders** | Maps files to their parent directories | Enables file location queries |

**Hierarchy Example:**
```
Snapshot #1
└── Root (ParentFolderId = NULL)
    ├── Documents (ParentFolderId = Root)
    │   ├── Project.txt
    │   └── Budget.xlsx
    └── Downloads
        ├── Image.jpg
        └── Archive.zip
```

### ⏱️ Temporal & Relationship Management

| Table | Purpose | Time Awareness |
|:------|:--------|:---------------|
| **Snapshots** | Point-in-time captures | Timestamp-based state capture |
| **PcsToStorageDrives** | PC-Drive associations per snapshot | Tracks drive connectivity changes |
| **FoldersToFolders** | Folder hierarchy per snapshot | Enables multi-version comparisons |
| **FilesToFolders** | File locations per snapshot | Historical file tracking |

---

## 🏗️ Key Design Patterns

### 1️⃣ **Temporal Snapshots** ⏸️
- **What:** Point-in-time captures of complete system state
- **Why:** Enables historical tracking and state comparison
- **Example:** Compare file system between Monday's and Friday's snapshots

### 2️⃣ **Hierarchical File System** 🌳
- **What:** Parent-child folder relationships with nullable root
- **Why:** Supports arbitrary directory depth and complex hierarchies
- **Example:** `C:\Users\John\Documents\Projects\Current\Source\`

### 3️⃣ **Many-to-Many Relationships** 🔗
- **What:** Junction tables for complex associations
- **Why:** Flexible modeling of complex real-world relationships
- **Examples:**
  - PCs with multiple drives across multiple snapshots
  - Folders with multiple files and files in multiple snapshots

### 4️⃣ **Entity Base Class** 👨‍👩‍👧
- **What:** Common parent class providing Id and Name
- **Why:** Reduces code duplication and ensures consistency
- **Inherited by:** PcEntity, StorageDriveEntity, FsFolderEntity, FsFileEntity

### 5️⃣ **Content Hashing** 🔐
- **What:** SHA256 hashes for files and folders
- **Why:** Enables duplicate detection and integrity verification
- **Use Cases:**
  - Identify unchanged files across snapshots
  - Detect duplicate content
  - Verify file integrity

### 6️⃣ **ULID Identifiers** 🆔
- **What:** Sortable unique identifiers replacing standard GUIDs
- **Why:** Improves database performance and enables natural sorting
- **Benefits:**
  - ✅ Sortable and time-ordered
  - ✅ Better index performance
  - ✅ Human-readable timestamp information

---

## 🔑 Foreign Key Relationships & Delete Strategy

### Delete Strategy Rationale

```
CASCADE DELETE                          NO ACTION DELETE
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  ━━━━━━━━━━━━━━━━━━
• Volumes → StorageDrives             • Snapshots ← Historical references
• VolumeInfos → Volumes               • PC data ← Maintain relationships
                                      • File system ← Preserve integrity
```

### Detailed Relationship Table

| Relationship | Delete Rule | Rationale |
|:-------------|:------------|:----------|
| **Volumes → StorageDrives** | **CASCADE** | Volumes are dependent physical artifacts |
| **VolumeInfos → Volumes** | **CASCADE** | Volume info belongs to specific volume |
| **VolumeInfos → Snapshots** | **NO ACTION** | ✅ Preserves snapshot data for historical analysis |
| **PcsToStorageDrives → Pcs** | **NO ACTION** | ✅ Maintains PC-drive associations over time |
| **PcsToStorageDrives → StorageDrives** | **NO ACTION** | ✅ Tracks drive history and migrations |
| **PcsToStorageDrives → Snapshots** | **NO ACTION** | ✅ Snapshots are immutable records |
| **FoldersToFolders → Snapshots** | **NO ACTION** | ✅ Preserves folder structure snapshots |
| **FoldersToFolders → Folders** | **NO ACTION** | ✅ Maintains folder relationships |
| **FilesToFolders → Folders** | **NO ACTION** | ✅ Maintains file location records |
| **FilesToFolders → Files** | **NO ACTION** | ✅ Preserves file references |
| **FilesToFolders → Snapshots** | **NO ACTION** | ✅ Enables time-based file queries |

---

## 🔄 Data Flow & Usage Scenarios

### 📸 **Snapshot Capture Workflow**

```
Step 1: System Discovery
┌──────────────────────────────────┐
│ Enumerate all PCs and devices    │
│ • Scan hardware inventory        │
│ • Identify storage drives        │
│ • Detect volume partitions       │
└────────────────┬─────────────────┘
                 ↓
Step 2: Create Snapshot Record
┌──────────────────────────────────┐
│ Create Snapshot with Timestamp   │
│ • Record current date/time       │
│ • Capture volume free space      │
│ • Store drive health status      │
└────────────────┬─────────────────┘
                 ↓
Step 3: File System Scan
┌──────────────────────────────────┐
│ Recursively scan all folders     │
│ • Traverse directory tree        │
│ • Calculate SHA256 hashes        │
│ • Record file metadata           │
│ • Store in Folders & Files       │
└────────────────┬─────────────────┘
                 ↓
Step 4: Link to Snapshot
┌──────────────────────────────────┐
│ Associate all data with Snapshot │
│ • Create folder relationships    │
│ • Link files to parents          │
│ • Record PC-Drive associations   │
└──────────────────────────────────┘
```

### 🔍 **Common Query Scenarios**

#### Scenario A: Get all files in a folder
```sql
SELECT f.* FROM Files f
JOIN FilesToFolders ftf ON f.Id = ftf.FileId
WHERE ftf.FolderId = @folderId 
  AND ftf.SnapshotId = @snapshotId
```
**Use Case:** Browse folder contents at specific point in time

#### Scenario B: Track storage usage trend
```sql
SELECT v.VolumeName, vi.FreeSpace, s.Timestamp
FROM VolumeInfos vi
JOIN Volumes v ON vi.VolumeId = v.Id
JOIN Snapshots s ON vi.SnapshotId = s.Id
WHERE v.StorageDriveId = @driveId
ORDER BY s.Timestamp DESC
```
**Use Case:** Generate storage usage charts over time

#### Scenario C: Find all PCs with specific drive
```sql
SELECT DISTINCT p.* FROM Pcs p
JOIN PcsToStorageDrives ptsd ON p.Id = ptsd.PcId
WHERE ptsd.StorageDriveId = @driveId
  AND ptsd.SnapshotId = @snapshotId
```
**Use Case:** Asset tracking and inventory management

#### Scenario D: Compare folder contents across snapshots
```sql
SELECT * FROM FoldersToFolders
WHERE ParentFolderId = @folderId
  AND SnapshotId IN (@snapshot1, @snapshot2)
```
**Use Case:** Detect folder changes between time periods

#### Scenario E: Find duplicate files by hash
```sql
SELECT Sha256Hash, COUNT(*) as Count, GROUP_CONCAT(Name)
FROM Files
GROUP BY Sha256Hash
HAVING COUNT(*) > 1
```
**Use Case:** Identify redundant content and save storage space

---

## ✅ Design Strengths

### 🎯 **Complete Historical Tracking**
- ✓ Maintains complete file system history through snapshots
- ✓ Enables time-series analysis of storage and changes
- ✓ Supports audit trails and compliance reporting
- ✓ Answers: "What was the state on date X?"

### 🏢 **Enterprise-Scale Multi-PC Support**
- ✓ Track multiple PCs with multiple storage drives
- ✓ Flexible for small offices to large enterprises
- ✓ Scales to hundreds of snapshots over months/years
- ✓ Supports: "Which PCs have drive SN-12345?"

### ⚡ **Performance Optimization**
- ✓ Proper indexing with primary/foreign keys
- ✓ Composite keys prevent duplicate entries
- ✓ Normalized schema reduces redundancy
- ✓ ULID provides better index performance

### 📊 **Rich Metadata & Analytics**
- ✓ Tracks structural data (hierarchy) + metadata (size, hash, status)
- ✓ SHA256 enables integrity verification
- ✓ Duplicate detection across snapshots
- ✓ Drive health status tracking

### 📈 **Flexible Temporal Analysis**
- ✓ Point-in-time consistency
- ✓ Multi-snapshot comparisons
- ✓ Trend analysis capabilities
- ✓ Supports: "What changed between snapshots?"

### 🌳 **Unlimited Hierarchy Support**
- ✓ Supports arbitrary directory depth
- ✓ Nullable root folder support
- ✓ Tree queries at any level
- ✓ Handles: `C:\...\...\...\very\deep\folder\path\`

---

## ⚠️ Design Considerations & Optimizations

### 📦 **Data Volume at Scale**

**Challenge:**
```
Large File Systems = Significant Storage Requirements
Example: 1,000,000 files × 52 snapshots/year = 52,000,000+ records
```

**Mitigations:**
- 🔄 Archive old snapshots to separate database
- 📊 Implement incremental/differential snapshots
- 🗜️ Compress historical snapshot data
- 🧹 Implement data retention policies

---

### 🔗 **Query Performance on Deep Hierarchies**

**Challenge:**
```
Deep Folder Hierarchies = Multiple Joins Required
Recursive Queries: C:\A\B\C\D\E\F\G\H\I\J\K\...
```

**Current Approach:**
- ✓ Works well with indexed foreign keys
- ⚠️ May need optimization for 20+ level deep paths

**Potential Optimizations:**
- 🆕 Materialized path pattern (store full path as string)
- 🆕 Nested set model for fast ancestor queries
- 📍 Common table expressions (CTEs) for recursive queries

---

### 🔒 **Snapshot Deletion Constraints**

**Challenge:**
```
ON DELETE NO ACTION prevents cascading deletes
Cannot delete snapshots without explicit cleanup
```

**Strategy:**
- ✅ Preserves data integrity (intentional)
- ✅ Requires explicit deletion procedures
- ✅ Prevents accidental data loss
- 📋 Implement archive/purge stored procedures

---

### ⏰ **Temporal Metadata Gaps**

**Challenge:**
```
StorageDrive table missing timestamps
Cannot track when drive properties changed
```

**Current State:**
- ❌ Serial number changes undetected
- ❌ Drive renames not timestamped
- ❌ Hardware upgrades not logged

**Potential Enhancement:**
```csharp
// Add audit columns to Pcs and StorageDrives
CreatedAt: DateTimeOffset
UpdatedAt: DateTimeOffset
```

---

## 🏗️ Solution Architecture

### 📦 **Projects in Solution**

| Project | Purpose | Key Responsibilities |
|:--------|:--------|:-------------------|
| **Ufo.Abstractions** | Core definitions | Entity models, interfaces, DTOs |
| **Ufo.Database** | Data access layer | Dapper context, repositories, migrations |
| **Ufo.Server** | API application | Controllers, services, data providers |
| **Ufo.UnitTests** | Testing suite | Repository tests, integration tests |

### 📂 **Key Components by Layer**

**Database Layer:**
- 📄 `DapperDataContext.cs` - Schema initialization and migrations
- 📄 `FileSystemSqLiteRepository.cs` - CRUD operations
- 📁 `Entities/` - All entity definitions

**API Layer:**
- 🌐 `SnapshotController.cs` - RESTful snapshot endpoints
- 📊 `SystemInfoProvider.cs` - System data collection

**Domain Layer:**
- 🔧 Entity models with business logic
- 📝 Service interfaces and implementations

---

## 🛠️ Technology Integration Stack

```
┌─────────────────────────────────────────┐
│    Universal File Observer Stack        │
├─────────────────────────────────────────┤
│ Framework:        .NET 10               │
│ Language:         C# 14.0               │
│ API:              ASP.NET Core          │
├─────────────────────────────────────────┤
│ Database:         SQLite                │
│ Connection:       Microsoft.Data.Sqlite │
│ ORM:              Dapper                │
│ Extensions:       SQLiteNetExtensions   │
├─────────────────────────────────────────┤
│ Identifiers:      ULID                  │
│ Hashing:          SHA256                │
│ JSON:             System.Text.Json      │
├─────────────────────────────────────────┤
│ Testing:          xUnit / NUnit         │
│ Build:            .NET CLI              │
│ VCS:              Git                   │
└─────────────────────────────────────────┘
```

### 🔌 **Integration Details**

- **SQLite** via `Microsoft.Data.Sqlite` - Embedded database without external dependencies
- **Dapper** - Lightweight, high-performance micro-ORM for SQL execution
- **SQLiteNetExtensions** - Simplifies navigation properties and relationship management
- **ULID** - Sortable unique identifiers with built-in timestamp information
- **SHA256** - Industry-standard cryptographic hashing for content integrity

---

## 📊 Summary Matrix

| Aspect | Strength | Consideration |
|:-------|:---------|:--------------|
| **Scalability** | ✅ Multi-PC, multi-drive | ⚠️ Large snapshots impact |
| **Performance** | ✅ Indexed relationships | ⚠️ Deep hierarchies need optimization |
| **Data Integrity** | ✅ SHA256 hashing | ⚠️ No cascade on snapshots (by design) |
| **History Tracking** | ✅ Comprehensive snapshots | ⚠️ Requires archive strategy |
| **Flexibility** | ✅ ULID, no rigid types | ⚠️ Temporal metadata limited |
| **Hierarchy Support** | ✅ Unlimited depth | ⚠️ May need materialized paths |

---

## 📝 Quick Reference

### Common Database Queries

**Get folder contents:**
```sql
SELECT * FROM Files 
WHERE Id IN (
  SELECT FileId FROM FilesToFolders 
  WHERE FolderId = ? AND SnapshotId = ?
)
```

**Get drive usage history:**
```sql
SELECT Timestamp, FreeSpace 
FROM VolumeInfos vi
JOIN Snapshots s ON vi.SnapshotId = s.Id
WHERE vi.VolumeId = ?
ORDER BY s.Timestamp DESC
```

**Find duplicates:**
```sql
SELECT Sha256Hash, COUNT(*) FROM Files 
GROUP BY Sha256Hash 
HAVING COUNT(*) > 1
```

---

**📌 Document Information**
- **Created:** 2024
- **Project:** Universal File Observer (UFO)
- **Framework:** .NET 10 with C# 14.0
- **Status:** Comprehensive Architecture Analysis
- **Repository:** https://github.com/LA777/universal-file-observer
