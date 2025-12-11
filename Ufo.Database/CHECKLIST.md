# ? Implementation Checklist

## Pre-Migration Setup

- [x] DbContext created with all entity mappings
- [x] Entity relationships configured
- [x] Dependency injection updated
- [x] Program.cs cleaned up
- [x] Entity models updated with EF Core navigation properties
- [x] All compilation errors fixed
- [x] Documentation created

## Installation Steps (Do These First)

### Step 1: Add NuGet Package
- [ ] Run: `dotnet add Ufo.Database/Ufo.Database.csproj package Microsoft.EntityFrameworkCore.Sqlite --version 8.0.22`
- [ ] Verify package added to `Ufo.Database.csproj`
- [ ] Run: `dotnet restore`

### Step 2: Create Initial Migration
- [ ] Open terminal in `Ufo.Database` directory
- [ ] Run: `dotnet ef migrations add InitialCreate`
- [ ] Verify `Migrations` folder created
- [ ] Check migration file in `Migrations/` folder

### Step 3: Apply Migration
- [ ] Run: `dotnet ef database update`
- [ ] Verify `ufo.db` file created in project root or configured location
- [ ] Verify no SQL errors in output

### Step 4: Configure Connection String
- [ ] Update `appsettings.json` with connection string
  ```json
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=ufo.db;Cache=Shared"
  }
  ```
- [ ] Or set environment variable: `UFO_CONNECTION_STRING=Data Source=ufo.db;Cache=Shared`
- [ ] Test connection by running application

## Verification Tests

### Test 1: DbContext Loads
```csharp
// Should not throw
using var context = serviceProvider.GetRequiredService<UfoDbContext>();
Assert.NotNull(context);
```

### Test 2: Database Connection Works
```csharp
// Should return 0 initially
var pcCount = await _dbContext.Pcs.CountAsync();
Assert.Equal(0, pcCount);
```

### Test 3: AddSnapshotAsync Works
```csharp
// Create test entities
var pc = new PcEntity { Id = Ulid.NewUlid(), Name = "TestPC" };
var storageDrive = new StorageDriveEntity 
{ 
    Id = Ulid.NewUlid(), 
    SerialNumber = "ABC123",
    Name = "TestDrive",
    Pcs = [pc]
};
// ... more setup ...

// Call method
var result = await _repository.AddSnapshotAsync(snapshot);
Assert.Equal(1, result);
```

## Code Quality Checks

### Compilation
- [ ] `dotnet build` succeeds with no C# errors
- [ ] No warnings (except Node.js which is unrelated)

### Entity Configuration
- [ ] All DbSet properties defined in UfoDbContext
- [ ] All table mappings present
- [ ] All primary keys defined
- [ ] All relationships configured

### Repository Implementation
- [ ] AddSnapshotAsync implemented and tested
- [ ] Transaction handling in place
- [ ] Error logging configured
- [ ] Other methods stubbed with TODO comments

### Documentation
- [ ] README.md reviewed
- [ ] SETUP_INSTRUCTIONS.md verified
- [ ] ARCHITECTURE.md understood
- [ ] EF_CORE_BEST_PRACTICES.md bookmarked

## Integration Tests (Optional but Recommended)

### Test 1: Full Snapshot Workflow
```csharp
[Fact]
public async Task AddSnapshotAsync_WithCompleteTree_CreatesAllEntities()
{
    // Arrange
    var snapshot = CreateTestSnapshot();
    
    // Act
    var result = await _repository.AddSnapshotAsync(snapshot);
    
    // Assert
    Assert.Equal(1, result);
    Assert.NotNull(await _dbContext.Snapshots.FindAsync(snapshot.Id));
}
```

### Test 2: Snapshot Relationships
```csharp
[Fact]
public async Task AddSnapshotAsync_CreatesCorrectRelationships()
{
    // Add snapshot
    await _repository.AddSnapshotAsync(snapshot);
    
    // Verify PcsToStorageDrives
    var pc2sd = await _dbContext.PcsToStorageDrives
        .AnyAsync(p => p.SnapshotId == snapshot.Id);
    Assert.True(pc2sd);
}
```

### Test 3: Transaction Rollback
```csharp
[Fact]
public async Task AddSnapshotAsync_InvalidData_RollsBack()
{
    // Arrange invalid snapshot
    var invalidSnapshot = new SnapshotEntity 
    { 
        VolumeInfo = null // Will cause error
    };
    
    // Act & Assert
    await Assert.ThrowsAsync<Exception>(
        () => _repository.AddSnapshotAsync(invalidSnapshot)
    );
    
    // Verify no partial data saved
    Assert.Empty(await _dbContext.Snapshots.ToListAsync());
}
```

## Migration Management

### Future Migrations
- [ ] If entity changes: `dotnet ef migrations add DescriptiveNameForChange`
- [ ] Apply: `dotnet ef database update`
- [ ] Revert if needed: `dotnet ef database update PreviousMigration`

### Database Backup
- [ ] Before applying migrations, backup `ufo.db` file
- [ ] Keep backup of schema changes

## Deployment Checklist

### Development
- [x] Local machine setup complete
- [x] Database migrations created
- [x] Tests passing

### Staging/Production
- [ ] Review connection string security
- [ ] Ensure database file location is writable
- [ ] Set appropriate file permissions
- [ ] Consider switching to PostgreSQL for production
- [ ] Set up automated backups
- [ ] Document restore procedures

## Performance Monitoring

- [ ] Query execution time < 1s for common operations
- [ ] Database file size stays reasonable
- [ ] No N+1 query problems (use Include/ThenInclude)
- [ ] Connection pooling enabled in connection string

## Common Issues & Solutions

### Issue: "Entity type without a key"
**Solution**: Ensure all entities have `Id` property configured as key in DbContext

### Issue: "Foreign key constraint failed"
**Solution**: Check cascade delete settings and relationship configuration

### Issue: "SQLite is locked"
**Solution**: Ensure only one connection at a time, or enable WAL mode in connection string

### Issue: "The value of member 'X' is not an entity or scalar type"
**Solution**: Check that join entity navigation properties are correctly configured

## Implementation Phases

### Phase 1: ? COMPLETE
- Core EF Core setup
- DbContext configuration
- Initial AddSnapshotAsync implementation
- All files created and documented

### Phase 2: ?? PENDING (Your Next Steps)
- Install NuGet package
- Create and apply migrations
- Test basic functionality
- Run integration tests

### Phase 3: ?? TODO (After Phase 2)
- Implement remaining GET methods
- Implement DELETE methods
- Add query optimization
- Performance testing

### Phase 4: ?? FUTURE (Pre-Production)
- Consider PostgreSQL migration
- Add caching layer
- Optimize N+1 queries
- Add database monitoring

## Success Criteria ?

### Must Have
- [x] DbContext loads without errors
- [x] All compilation succeeds
- [x] AddSnapshotAsync works
- [ ] Database file created
- [ ] Integration tests pass

### Nice to Have
- [ ] All repository methods implemented
- [ ] Query performance optimized
- [ ] Full test coverage
- [ ] Production-ready database

### Future
- [ ] Switch to PostgreSQL
- [ ] Add read replicas
- [ ] Database backup strategy
- [ ] Monitoring dashboards

## Resources

- [EF Core Documentation](https://learn.microsoft.com/en-us/ef/core/)
- [SQLite with EF Core](https://learn.microsoft.com/en-us/ef/core/providers/sqlite)
- [Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/)
- [Relationships](https://learn.microsoft.com/en-us/ef/core/modeling/relationships)
- [Value Converters](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions)

## Sign-Off

**Setup Complete**: ? 2024
**Installation Status**: ? Pending NuGet package + migrations
**Ready for Implementation**: ? Yes
**Documentation Level**: ????? Complete

---

**Next Action**: Install NuGet package and run migrations. Then celebrate! ??
