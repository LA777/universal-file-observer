# SQLite with EF Core Configuration Guide

## Overview
Your project is now configured to use SQLite with Entity Framework Core. Here's what was set up:

## Changes Made

### 1. **NuGet Package Added**
- `Microsoft.EntityFrameworkCore.Sqlite` (v8.0.22)
  - Required: Add this manually via NuGet Package Manager or CLI since the .csproj file wasn't directly editable

### 2. **DbContext Configuration** (`UfoDbContext.cs`)
- Configured all entity mappings
- Set up relationships with proper foreign keys
- Composite keys for join entities (FoldersToFolders, FilesToFolders, PcsToStorageDrives)
- All IDs configured as `ValueGeneratedNever()` to preserve caller-assigned Ulid values

### 3. **Dependency Injection** (`DependencyExtension.cs`)
```csharp
services.AddDbContext<UfoDbContext>(options =>
    options.UseSqlite(connectionString)
);
```
- Registers UfoDbContext with SQLite provider
- Uses connection string from configuration

### 4. **DbContextFactory** (`UfoDbContextFactory.cs`)
- Required for EF Core CLI commands (migrations)
- Implements `IDesignTimeDbContextFactory<UfoDbContext>`
- Allows running `dotnet ef migrations` commands

### 5. **Program.cs Update**
- Removed duplicate `IFileSystemRepository` registration
- Now using only the EF Core implementation through DependencyExtension

## Next Steps

### 1. Add the NuGet Package
```bash
dotnet add Ufo.Database/Ufo.Database.csproj package Microsoft.EntityFrameworkCore.Sqlite --version 8.0.22
```

### 2. Create Initial Migration
```bash
cd Ufo.Database
dotnet ef migrations add InitialCreate
```

### 3. Update Database Schema
```bash
dotnet ef database update
```

## Connection String Configuration

Update your `appsettings.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=ufo.db;Cache=Shared"
  }
}
```

Or use environment variable:
```bash
$env:UFO_CONNECTION_STRING="Data Source=path/to/ufo.db;Cache=Shared"
```

## Entity Updates

Your entities now have EF Core navigation properties in addition to existing properties:

### Join Entities (already updated):
- **FoldersToFoldersEntity**: Snapshot, ParentFolder, ChildFolder
- **FilesToFoldersEntity**: Snapshot, Folder, File
- **PcsToStorageDrivesEntity**: Snapshot, Pc, StorageDrive

### Main Entities:
- **FsFolderEntity**: ChildFolderLinks, ParentFolderLinks, FilesLinks
- **FsFileEntity**: ParentFolderLinks
- **PcEntity**: StorageDrivesLinks
- **StorageDriveEntity**: PcsLinks
- **SnapshotEntity**: PcsToStorageDrives, FoldersToFolders, FilesToFolders

## Key Features

? **Ulid Support**: All IDs use Ulid type with `ValueGeneratedNever()`
? **Snapshot Scoping**: Join entities maintain snapshot context
? **Cascade Deletes**: Properly configured relationships
? **SQLite Constraints**: MaxLength constraints on string properties
? **Code-First Approach**: Complete entity configuration in OnModelCreating

## Troubleshooting

### Build Errors
If you get errors about missing `Microsoft.EntityFrameworkCore.Sqlite`:
1. Ensure the NuGet package is installed
2. Run `dotnet restore` in the Ufo.Database project

### Migration Issues
If migrations fail:
1. Check connection string in appsettings.json
2. Ensure database file location is writable
3. Delete existing database and migrations to start fresh

### Ulid Issues
SQLite doesn't have native Ulid support. EF Core converts Ulid to/from TEXT automatically through value converters.

## FileSystemEfCoreRepository

The `AddSnapshotAsync` method is already updated with:
- Transaction support via `_dbContext.Database.BeginTransactionAsync()`
- LINQ queries instead of raw SQL
- Proper entity tracking and SaveChangesAsync() calls
- Recursive folder/file insertion
- Snapshot-scoped relationship binding

All other repository methods are placeholder-ready for implementation.
