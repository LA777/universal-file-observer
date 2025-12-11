# ? Quick Reference Card

## Installation (Copy & Paste)

```bash
# Step 1: Add NuGet package
dotnet add Ufo.Database/Ufo.Database.csproj package Microsoft.EntityFrameworkCore.Sqlite --version 8.0.22

# Step 2: Create migration
cd Ufo.Database
dotnet ef migrations add InitialCreate

# Step 3: Apply migration
dotnet ef database update

# Step 4: Verify
dotnet build
```

## Configuration

**appsettings.json**:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=ufo.db;Cache=Shared"
  }
}
```

## Common Commands

```bash
# View pending migrations
dotnet ef migrations list

# Remove last migration (if wrong)
dotnet ef migrations remove

# Revert database to previous migration
dotnet ef database update PreviousMigrationName

# Generate SQL script (don't execute)
dotnet ef migrations script > migrate.sql

# Drop entire database (careful!)
dotnet ef database drop --force

# Create backup migration
dotnet ef migrations add Backup_<date>
```

## Key Files Location

```
Ufo.Database/
??? UfoDbContext.cs ........................ DbContext configuration
??? UfoDbContextFactory.cs ................. Design-time factory
??? FileSystemEfCoreRepository.cs ......... Repository implementation
??? DependencyExtension.cs ................. DI configuration
??? START_HERE.md .......................... Read first!
```

## Common Queries

### Basic Query
```csharp
// Find by ID
var snapshot = await _dbContext.Snapshots.FindAsync(id);

// Where clause
var snapshots = await _dbContext.Snapshots
    .Where(s => s.Timestamp > DateTime.Now.AddDays(-7))
    .ToListAsync();
```

### With Related Data
```csharp
// Include related entities
var snapshot = await _dbContext.Snapshots
    .Include(s => s.VolumeInfo)
        .ThenInclude(vi => vi.Volume)
    .FirstOrDefaultAsync(s => s.Id == id);
```

### Count/Any
```csharp
// Count
var count = await _dbContext.Snapshots.CountAsync();

// Check existence
var exists = await _dbContext.Folders
    .AnyAsync(f => f.Id == folderId);
```

## Common Operations

### Add Entity
```csharp
var pc = new PcEntity { Id = Ulid.NewUlid(), Name = "TestPC" };
_dbContext.Pcs.Add(pc);
await _dbContext.SaveChangesAsync();
```

### Update Entity
```csharp
var pc = await _dbContext.Pcs.FindAsync(id);
pc.Name = "NewName";
await _dbContext.SaveChangesAsync(); // Auto-tracked
```

### Delete Entity
```csharp
var pc = await _dbContext.Pcs.FindAsync(id);
_dbContext.Pcs.Remove(pc);
await _dbContext.SaveChangesAsync();
```

### Transaction
```csharp
await using var transaction = await _dbContext.Database.BeginTransactionAsync();
try
{
    // ... operations ...
    await _dbContext.SaveChangesAsync();
    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}
```

## Debugging

### Enable SQL Logging
```csharp
optionsBuilder.LogTo(Console.WriteLine);

// Or in appsettings.json
"Logging": {
  "LogLevel": {
    "Microsoft.EntityFrameworkCore": "Information"
  }
}
```

### Check Entity State
```csharp
var entry = _dbContext.Entry(entity);
var state = entry.State; // Added, Modified, Unchanged, Deleted, Detached
```

### Force Reload
```csharp
await _dbContext.Entry(entity).ReloadAsync();
```

## Performance Tips

```csharp
// 1. Use AsNoTracking() for read-only
var folders = await _dbContext.Folders
    .AsNoTracking()
    .ToListAsync();

// 2. Pagination
var page = await _dbContext.Snapshots
    .Skip((pageNum - 1) * pageSize)
    .Take(pageSize)
    .ToListAsync();

// 3. Projection (get only needed fields)
var names = await _dbContext.Pcs
    .Select(p => p.Name)
    .ToListAsync();

// 4. Batch operations
var ids = new[] { id1, id2, id3 };
var items = await _dbContext.Items
    .Where(i => ids.Contains(i.Id))
    .ToListAsync();
```

## Error Handling

```csharp
try
{
    await _dbContext.SaveChangesAsync();
}
catch (DbUpdateException ex)
{
    // Database error (constraint, FK, etc.)
    _logger.LogError(ex, "Database update failed");
}
catch (DbUpdateConcurrencyException ex)
{
    // Concurrency conflict
    _logger.LogError(ex, "Concurrency conflict");
}
catch (Exception ex)
{
    // Other error
    _logger.LogError(ex, "Unexpected error");
}
```

## Entity States

```
BEFORE SaveChanges()           AFTER SaveChanges()
????????????????????           ??????????????????
Detached                   ?   Unchanged/Detached
Added                      ?   Unchanged
Modified                   ?   Unchanged
Unchanged                  ?   Unchanged
Deleted                    ?   Detached
```

## Connection Strings

```
Standard:              Data Source=ufo.db
With cache:            Data Source=ufo.db;Cache=Shared
Full options:          Data Source=ufo.db;Cache=Shared;Mode=ReadWrite
In-memory:             Data Source=:memory:
Read-only:             Data Source=ufo.db;Mode=ReadOnly
Network:               Data Source=\\server\path\ufo.db
```

## Navigation Properties

```csharp
// One-to-Many
snapshot.VolumeInfo           // VolumeInfoEntity
volumeInfo.Snapshot           // SnapshotEntity
volume.VolumeInfos            // IList<VolumeInfoEntity>

// Many-to-Many (via join entity)
folder.ChildFolderLinks       // IList<FoldersToFoldersEntity>
folderToFolder.ChildFolder    // FsFolderEntity
folderToFolder.ParentFolder   // FsFolderEntity
folderToFolder.Snapshot       // SnapshotEntity
```

## Relationship Queries

```csharp
// Find parent folders
var parentLinks = await _dbContext.FoldersToFolders
    .Where(f => f.ChildFolderId == folderId)
    .Include(f => f.ParentFolder)
    .ToListAsync();

// Find child folders
var childLinks = await _dbContext.FoldersToFolders
    .Where(f => f.ParentFolderId == folderId)
    .Include(f => f.ChildFolder)
    .ToListAsync();
```

## Documentation

| File | Purpose | Time |
|------|---------|------|
| START_HERE.md | Overview | 5 min |
| README.md | Quick ref | 2 min |
| SETUP_INSTRUCTIONS.md | Install | 10 min |
| EF_CORE_BEST_PRACTICES.md | Patterns | 20 min |
| ARCHITECTURE.md | Design | 15 min |
| CHECKLIST.md | Verify | 30 min |
| SUMMARY.md | Complete | 10 min |

## Status Check

```bash
# Build
dotnet build

# Run tests (if configured)
dotnet test

# Check migrations
dotnet ef migrations list

# Validate schema
# (Create snapshot with small data, verify in database browser)
```

## Contact / Questions

- Check documentation first
- Search error message in troubleshooting
- Review code examples in best practices
- Examine EF Core official docs

## Remember

? Always backup database before migrations
? Test on development first
? Use transactions for multi-step operations
? Include related data explicitly with Include()
? Use AsNoTracking() for read-only queries
? Check entity state for debugging
? Log SQL queries for optimization

---

**Last Updated**: 2024
**Version**: 1.0 Complete
**Status**: ? Ready to Use
