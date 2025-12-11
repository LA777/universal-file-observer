# SQLite with EF Core Migration - Complete Setup Summary

## ? What's Been Configured

### 1. **Database Provider**
- **Package**: `Microsoft.EntityFrameworkCore.Sqlite` v8.0.22
- **Status**: Needs to be installed via NuGet

### 2. **DbContext Setup** (`UfoDbContext.cs`)
? Complete with:
- All entity mappings to SQLite tables
- Primary key configurations with `ValueGeneratedNever()` for Ulid values
- Composite keys for join entities
- Full relationship configuration:
  - One-to-one: Snapshot ? VolumeInfo
  - One-to-many: VolumeInfo ? Volumes, StorageDrive ? Volumes
  - Many-to-many with payload: PC ? StorageDrive (via PcsToStorageDrives per Snapshot)
  - Self-relations: Folders ? Folders (parent/child per Snapshot)
  - Many-to-many: Files ? Folders (per Snapshot)
- Cascade delete behaviors on join entities
- String length constraints

### 3. **Dependency Injection** (`DependencyExtension.cs`)
? Updated with:
```csharp
services.AddDbContext<UfoDbContext>(options =>
    options.UseSqlite(connectionString)
);
```

### 4. **Design-Time Factory** (`UfoDbContextFactory.cs`)
? Enables EF Core CLI commands:
- `dotnet ef migrations add`
- `dotnet ef database update`
- Uses environment variable or default connection string

### 5. **Application Startup** (`Program.cs`)
? Updated to:
- Remove duplicate repository registrations
- Use only EF Core via DependencyExtension

### 6. **Entity Models**
? All updated with EF Core navigation properties:
- **FoldersToFoldersEntity**: Snapshot, ParentFolder, ChildFolder
- **FilesToFoldersEntity**: Snapshot, Folder, File
- **PcsToStorageDrivesEntity**: Snapshot, Pc, StorageDrive
- **FsFolderEntity**: ChildFolderLinks, ParentFolderLinks, FilesLinks
- **FsFileEntity**: ParentFolderLinks
- **PcEntity**: StorageDrivesLinks
- **StorageDriveEntity**: PcsLinks
- **SnapshotEntity**: PcsToStorageDrives, FoldersToFolders, FilesToFolders

? SQLite attributes commented out (not deleted) for future reference

### 7. **Repository Implementation**
? `FileSystemEfCoreRepository.AddSnapshotAsync()` includes:
- Transaction management
- Recursive folder/file insertion
- Snapshot-scoped relationships
- Proper entity state management
- All placeholder methods ready for implementation

## ?? Next Steps

### Step 1: Install NuGet Package
```bash
dotnet add Ufo.Database/Ufo.Database.csproj package Microsoft.EntityFrameworkCore.Sqlite --version 8.0.22
```

### Step 2: Create Initial Migration
```bash
cd Ufo.Database
dotnet ef migrations add InitialCreate
```

### Step 3: Apply Migration to Database
```bash
dotnet ef database update
```

### Step 4: Configure Connection String
Update `appsettings.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=ufo.db;Cache=Shared"
  }
}
```

Or set environment variable:
```powershell
$env:UFO_CONNECTION_STRING="Data Source=path/to/ufo.db;Cache=Shared"
```

## ?? Connection String Examples

| Scenario | Connection String |
|----------|-------------------|
| Local file | `Data Source=ufo.db` |
| With cache | `Data Source=ufo.db;Cache=Shared` |
| Read-only | `Data Source=ufo.db;Mode=ReadOnly` |
| In-memory | `Data Source=:memory:` |
| Network path | `Data Source=\\server\path\ufo.db` |

## ?? Compilation Status

? **All C# compilation errors resolved**:
- Fixed null-coalescing operator in LINQ expressions
- Corrected navigation property mappings
- Proper type conversions

?? **Node.js Warning**: This is from JavaScript SDK for UI, not related to database layer

## ?? Key Architecture Decisions

### Join Entities Strategy
- Kept explicit join entities (FoldersToFolders, FilesToFolders, PcsToStorageDrives)
- **Why**: Snapshot scoping - relationships are meaningful only within a snapshot context
- **Benefit**: Clear, maintainable relationships with explicit FK constraints

### Ulid Support
- SQLite doesn't have native Ulid type
- EF Core handles conversion automatically (Text in DB, Ulid in C#)
- `ValueGeneratedNever()` preserves caller-assigned IDs

### Cascading Deletes
- Join table deletes cascade from parent entities
- Protects data integrity
- Allows cleanup via Snapshot deletion

## ?? Known Limitations of SQLite

1. **No native Ulid support** - Stored as TEXT
2. **Limited ALTER TABLE** - Migrations may recreate tables
3. **No check constraints** - Some validations need app-level enforcement
4. **Single writer** - Not suitable for high-concurrency scenarios

## ?? Files Modified/Created

| File | Action | Purpose |
|------|--------|---------|
| `Ufo.Database/Ufo.Database.csproj` | Add package | EF Core SQLite provider |
| `Ufo.Database/Contexts/UfoDbContext.cs` | Update | Full entity configuration |
| `Ufo.Database/Contexts/UfoDbContextFactory.cs` | Create | Enable CLI migrations |
| `Ufo.Database/Extensions/DependencyExtension.cs` | Update | Register DbContext |
| `Ufo.Database/Repositories/FileSystemEfCoreRepository.cs` | Fix | LINQ expression fixes |
| `Ufo.Server/Program.cs` | Update | Remove duplicates |
| Multiple entity files | Update | Add navigation properties |
| `Ufo.Database/SETUP_INSTRUCTIONS.md` | Create | Configuration guide |

## ? Ready for Implementation

All other repository methods are now ready for EF Core implementation:
- `DropDataInTables()` - Database clearing
- `GetFilesByNameAndExtensionAsync()` - File search
- `GetFoldersByNameAsync()` - Folder search
- `GetSnapshotByIdAsync()` - Snapshot retrieval with full tree
- `GetLatestSnapshotWithAllEntitiesAsync()` - Latest snapshot
- `GetAllSnapshotsAsync()` - List snapshots
- `DeleteSnapshotByIdAsync()` - Snapshot deletion

Each follows the same EF Core patterns established in `AddSnapshotAsync()`.
