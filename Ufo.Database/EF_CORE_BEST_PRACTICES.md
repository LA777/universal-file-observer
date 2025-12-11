# EF Core Best Practices for Your Project

## Transaction Management

### Proper Pattern
```csharp
await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
try
{
    // ... perform operations ...
    await _dbContext.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);
}
catch (Exception ex)
{
    await transaction.RollbackAsync(cancellationToken);
    throw;
}
```

## Entity State Management

### Tracking vs No-Tracking
```csharp
// Tracked queries (default) - for updates/deletes
var folders = await _dbContext.Folders
    .Where(f => f.Id == id)
    .ToListAsync();

// No-tracking queries - for read-only scenarios
var folders = await _dbContext.Folders
    .AsNoTracking()
    .Where(f => f.Id == id)
    .ToListAsync();
```

## Null Propagation in LINQ

? **Avoid in LINQ expressions**:
```csharp
var exists = await _dbContext.Items
    .AnyAsync(x => x.Id == parent?.Id); // ERROR: CS8072
```

? **Extract values first**:
```csharp
var parentId = parent?.Id;
var exists = await _dbContext.Items
    .AnyAsync(x => x.Id == parentId); // OK
```

## Recursive Operations Pattern

For your folder tree traversal:

```csharp
private async Task ProcessFolderRecursivelyAsync(
    FsFolderEntity folder, 
    FsFolderEntity? parentFolder, 
    SnapshotEntity snapshot,
    CancellationToken cancellationToken)
{
    // 1. Check/create current entity
    var existing = await _dbContext.Folders
        .FirstOrDefaultAsync(f => 
            f.Name == folder.Name && 
            f.Sha256Hash == folder.Sha256Hash,
            cancellationToken);
    
    if (existing == null)
    {
        _dbContext.Folders.Add(folder);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
    else
    {
        folder.Id = existing.Id;
    }

    // 2. Establish relationships
    if (parentFolder != null)
    {
        var link = new FoldersToFoldersEntity
        {
            ParentFolderId = parentFolder.Id,
            ChildFolderId = folder.Id,
            SnapshotId = snapshot.Id
        };
        _dbContext.FoldersToFolders.Add(link);
    }

    // 3. Process children
    foreach (var child in folder.ChildFolders)
    {
        await ProcessFolderRecursivelyAsync(child, folder, snapshot, cancellationToken);
    }

    await _dbContext.SaveChangesAsync(cancellationToken);
}
```

## Query Patterns for Complex Reads

### Include Related Data
```csharp
var snapshot = await _dbContext.Snapshots
    .Include(s => s.VolumeInfo)
        .ThenInclude(vi => vi.Volume)
            .ThenInclude(v => v.StorageDrive)
    .FirstOrDefaultAsync(s => s.Id == snapshotId);
```

### Use Filtered Includes
```csharp
var folders = await _dbContext.FoldersToFolders
    .Where(f => f.SnapshotId == snapshotId && f.ParentFolderId == null)
    .Include(f => f.ChildFolder)
    .ToListAsync();
```

## Common Repository Methods

### Find by Multiple Criteria
```csharp
public async Task<StorageDriveEntity?> FindStorageDriveAsync(
    string serialNumber, 
    string deviceId, 
    string name,
    CancellationToken cancellationToken = default)
{
    return await _dbContext.StorageDrives
        .FirstOrDefaultAsync(sd =>
            sd.SerialNumber == serialNumber &&
            sd.DeviceId == deviceId &&
            sd.Name == name,
            cancellationToken);
}
```

### Batch Operations
```csharp
public async Task<int> DeleteFilesAsync(
    IEnumerable<Ulid> fileIds,
    CancellationToken cancellationToken = default)
{
    var ids = fileIds.ToList();
    var filesToDelete = await _dbContext.Files
        .Where(f => ids.Contains(f.Id))
        .ToListAsync(cancellationToken);

    _dbContext.Files.RemoveRange(filesToDelete);
    return await _dbContext.SaveChangesAsync(cancellationToken);
}
```

## Snapshot-Scoped Queries

### Get All Folders in Snapshot
```csharp
public async Task<List<FsFolderEntity>> GetFoldersInSnapshotAsync(
    Ulid snapshotId,
    CancellationToken cancellationToken = default)
{
    return await _dbContext.FoldersToFolders
        .Where(ff => ff.SnapshotId == snapshotId)
        .Include(ff => ff.ChildFolder)
        .Select(ff => ff.ChildFolder)
        .ToListAsync(cancellationToken);
}
```

### Get Root Folder
```csharp
public async Task<FsFolderEntity?> GetRootFolderInSnapshotAsync(
    Ulid snapshotId,
    CancellationToken cancellationToken = default)
{
    return await _dbContext.FoldersToFolders
        .Where(ff => ff.SnapshotId == snapshotId && ff.ParentFolderId == null)
        .Select(ff => ff.ChildFolder)
        .FirstOrDefaultAsync(cancellationToken);
}
```

## Performance Considerations

### Pagination for Large Result Sets
```csharp
public async Task<(List<SnapshotEntity> items, int total)> GetSnapshotsPagedAsync(
    int page,
    int pageSize,
    CancellationToken cancellationToken = default)
{
    var query = _dbContext.Snapshots.OrderByDescending(s => s.Timestamp);
    var total = await query.CountAsync(cancellationToken);
    
    var items = await query
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync(cancellationToken);
    
    return (items, total);
}
```

### Projection to DTO (Reduce Transferred Data)
```csharp
public async Task<List<SnapshotDto>> GetSnapshotSummariesAsync(
    CancellationToken cancellationToken = default)
{
    return await _dbContext.Snapshots
        .Select(s => new SnapshotDto
        {
            Id = s.Id,
            Timestamp = s.Timestamp,
            FolderCount = s.FoldersToFolders.Count
        })
        .ToListAsync(cancellationToken);
}
```

## Error Handling

### Database Exceptions
```csharp
try
{
    await _dbContext.SaveChangesAsync(cancellationToken);
}
catch (DbUpdateException ex)
{
    _logger.LogError(ex, "Database update failed");
    throw;
}
catch (DbUpdateConcurrencyException ex)
{
    _logger.LogError(ex, "Concurrency conflict");
    throw;
}
catch (Exception ex)
{
    _logger.LogError(ex, "Unexpected database error");
    throw;
}
```

## Testing with In-Memory Database

```csharp
// In tests, use in-memory database
var options = new DbContextOptionsBuilder<UfoDbContext>()
    .UseInMemoryDatabase("TestDatabase")
    .Options;

using var context = new UfoDbContext(options);
// ... perform tests ...
```

## Migration Management

### Creating Migrations
```bash
# Add migration
dotnet ef migrations add AddNewFeature

# View pending migrations
dotnet ef migrations list

# Remove last migration
dotnet ef migrations remove
```

### Database Operations
```bash
# Apply migrations
dotnet ef database update

# Revert to specific migration
dotnet ef database update MigrationName

# Generate SQL script
dotnet ef migrations script > migrate.sql
```

## SQLite-Specific Considerations

### Connection String Optimization
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=ufo.db;Cache=Shared;Mode=ReadWrite;Synchronous=Normal"
  }
}
```

- `Cache=Shared`: Enables connection pooling
- `Synchronous=Normal`: Better performance than Full (less safety)
- `Mode=ReadWrite`: Allow write operations

### Bulk Operations Performance
```csharp
// Batch inserts for better performance
var options = new DbContextOptionsBuilder<UfoDbContext>()
    .UseSqlite(connectionString)
    .ConfigureWarnings(w => w.Ignore(RelationalEventId.AmbientTransactionWarning))
    .Options;
```

## Data Seeding

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);

    // Seed initial data
    modelBuilder.Entity<PcEntity>().HasData(
        new { Id = new Ulid(...), Name = "DefaultPC" }
    );
}
```
