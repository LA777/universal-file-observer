using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Ufo.Abstractions.Database.Entities;

namespace Ufo.Database.Contexts;

public sealed class UfoDbContext : DbContext
{
    public UfoDbContext(DbContextOptions<UfoDbContext> options)
        : base(options)
    {
    }

    public DbSet<PcEntity> Pcs { get; set; }
    public DbSet<StorageDriveEntity> StorageDrives { get; set; }
    public DbSet<SnapshotEntity> Snapshots { get; set; }
    public DbSet<VolumeEntity> Volumes { get; set; }
    public DbSet<VolumeInfoEntity> VolumeInfos { get; set; }
    public DbSet<FsFolderEntity> Folders { get; set; }
    public DbSet<FsFileEntity> Files { get; set; }
    public DbSet<PcsToStorageDrivesEntity> PcsToStorageDrives { get; set; }
    public DbSet<FoldersToFoldersEntity> FoldersToFolders { get; set; }
    public DbSet<FilesToFoldersEntity> FilesToFolders { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure Ulid value converter for SQLite (stores as string)
        var ulidConverter = new ValueConverter<Ulid, string>(
            v => v.ToString(),
            v => Ulid.Parse(v));

        var nullableUlidConverter = new ValueConverter<Ulid?, string>(
            v => v.HasValue ? v.Value.ToString() : null,
            v => string.IsNullOrEmpty(v) ? null : Ulid.Parse(v));

        // Table mappings
        modelBuilder.Entity<PcEntity>().ToTable("Pcs");
        modelBuilder.Entity<StorageDriveEntity>().ToTable("StorageDrives");
        modelBuilder.Entity<SnapshotEntity>().ToTable("Snapshots");
        modelBuilder.Entity<VolumeEntity>().ToTable("Volumes");
        modelBuilder.Entity<VolumeInfoEntity>().ToTable("VolumeInfos");
        modelBuilder.Entity<FsFolderEntity>().ToTable("Folders");
        modelBuilder.Entity<FsFileEntity>().ToTable("Files");
        modelBuilder.Entity<PcsToStorageDrivesEntity>().ToTable("PcsToStorageDrives");
        modelBuilder.Entity<FoldersToFoldersEntity>().ToTable("FoldersToFolders");
        modelBuilder.Entity<FilesToFoldersEntity>().ToTable("FilesToFolders");

        // Keys - expect Id properties on main entities. Preserve caller-assigned Ids.
        modelBuilder.Entity<PcEntity>().HasKey(e => e.Id);
        modelBuilder.Entity<PcEntity>().Property(e => e.Id).ValueGeneratedNever().HasConversion(ulidConverter);

        modelBuilder.Entity<StorageDriveEntity>().HasKey(e => e.Id);
        modelBuilder.Entity<StorageDriveEntity>().Property(e => e.Id).ValueGeneratedNever().HasConversion(ulidConverter);

        modelBuilder.Entity<SnapshotEntity>().HasKey(e => e.Id);
        modelBuilder.Entity<SnapshotEntity>().Property(e => e.Id).ValueGeneratedNever().HasConversion(ulidConverter);

        modelBuilder.Entity<VolumeEntity>().HasKey(e => e.Id);
        modelBuilder.Entity<VolumeEntity>().Property(e => e.Id).ValueGeneratedNever().HasConversion(ulidConverter);

        modelBuilder.Entity<VolumeInfoEntity>().HasKey(e => e.Id);
        modelBuilder.Entity<VolumeInfoEntity>().Property(e => e.Id).ValueGeneratedNever().HasConversion(ulidConverter);

        modelBuilder.Entity<FsFolderEntity>().HasKey(e => e.Id);
        modelBuilder.Entity<FsFolderEntity>().Property(e => e.Id).ValueGeneratedNever().HasConversion(ulidConverter);

        modelBuilder.Entity<FsFileEntity>().HasKey(e => e.Id);
        modelBuilder.Entity<FsFileEntity>().Property(e => e.Id).ValueGeneratedNever().HasConversion(ulidConverter);

        // Join / relationship entities: composite keys
        modelBuilder.Entity<PcsToStorageDrivesEntity>()
            .HasKey(e => new { e.PcId, e.StorageDriveId, e.SnapshotId });

        modelBuilder.Entity<FoldersToFoldersEntity>()
            .HasKey(e => new { e.ParentFolderId, e.ChildFolderId, e.SnapshotId });

        modelBuilder.Entity<FilesToFoldersEntity>()
            .HasKey(e => new { e.FolderId, e.FileId, e.SnapshotId });

        // Configure Ulid conversions for all foreign key properties
        // PcsToStorageDrivesEntity
        modelBuilder.Entity<PcsToStorageDrivesEntity>()
            .Property(e => e.SnapshotId).HasConversion(ulidConverter);
        modelBuilder.Entity<PcsToStorageDrivesEntity>()
            .Property(e => e.PcId).HasConversion(ulidConverter);
        modelBuilder.Entity<PcsToStorageDrivesEntity>()
            .Property(e => e.StorageDriveId).HasConversion(ulidConverter);

        // FoldersToFoldersEntity
        modelBuilder.Entity<FoldersToFoldersEntity>()
            .Property(e => e.SnapshotId).HasConversion(ulidConverter);
        modelBuilder.Entity<FoldersToFoldersEntity>()
            .Property(e => e.ParentFolderId).HasConversion(nullableUlidConverter);
        modelBuilder.Entity<FoldersToFoldersEntity>()
            .Property(e => e.ChildFolderId).HasConversion(ulidConverter);

        // FilesToFoldersEntity
        modelBuilder.Entity<FilesToFoldersEntity>()
            .Property(e => e.SnapshotId).HasConversion(ulidConverter);
        modelBuilder.Entity<FilesToFoldersEntity>()
            .Property(e => e.FolderId).HasConversion(ulidConverter);
        modelBuilder.Entity<FilesToFoldersEntity>()
            .Property(e => e.FileId).HasConversion(ulidConverter);

        // VolumeEntity foreign keys
        modelBuilder.Entity<VolumeEntity>()
            .Property(e => e.StorageDriveId).HasConversion(ulidConverter);

        // VolumeInfoEntity foreign keys
        modelBuilder.Entity<VolumeInfoEntity>()
            .Property(e => e.VolumeId).HasConversion(ulidConverter);
        modelBuilder.Entity<VolumeInfoEntity>()
            .Property(e => e.SnapshotId).HasConversion(ulidConverter);

        // Relationships

        // Snapshot <-> VolumeInfo (one-to-one)
        modelBuilder.Entity<SnapshotEntity>()
            .HasOne(s => s.VolumeInfo)
            .WithOne(vi => vi.Snapshot)
            .HasForeignKey<VolumeInfoEntity>(vi => vi.SnapshotId)
            .OnDelete(DeleteBehavior.Cascade);

        // VolumeInfo -> Volume (many-to-one)
        modelBuilder.Entity<VolumeInfoEntity>()
            .HasOne(vi => vi.Volume)
            .WithMany(v => v.VolumeInfos)
            .HasForeignKey(vi => vi.VolumeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Volume -> StorageDrive
        modelBuilder.Entity<VolumeEntity>()
            .HasOne(v => v.StorageDrive)
            .WithMany(sd => sd.Volumes)
            .HasForeignKey(v => v.StorageDriveId)
            .OnDelete(DeleteBehavior.Restrict);

        // StorageDrive <-> Pc (many-to-many with payload Snapshot)
        modelBuilder.Entity<PcsToStorageDrivesEntity>()
            .HasOne(pt => pt.Pc)
            .WithMany(p => p.StorageDrivesLinks)
            .HasForeignKey(pt => pt.PcId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PcsToStorageDrivesEntity>()
            .HasOne(pt => pt.StorageDrive)
            .WithMany(sd => sd.PcsLinks)
            .HasForeignKey(pt => pt.StorageDriveId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PcsToStorageDrivesEntity>()
            .HasOne(pt => pt.Snapshot)
            .WithMany(s => s.PcsToStorageDrives)
            .HasForeignKey(pt => pt.SnapshotId)
            .OnDelete(DeleteBehavior.Cascade);

        // Folder self-relations via FoldersToFoldersEntity (parent/child per snapshot)
        modelBuilder.Entity<FoldersToFoldersEntity>()
            .HasOne(ff => ff.ParentFolder)
            .WithMany(f => f.ChildFolderLinks)
            .HasForeignKey(ff => ff.ParentFolderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<FoldersToFoldersEntity>()
            .HasOne(ff => ff.ChildFolder)
            .WithMany(f => f.ParentFolderLinks)
            .HasForeignKey(ff => ff.ChildFolderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<FoldersToFoldersEntity>()
            .HasOne(ff => ff.Snapshot)
            .WithMany(s => s.FoldersToFolders)
            .HasForeignKey(ff => ff.SnapshotId)
            .OnDelete(DeleteBehavior.Cascade);

        // Files to Folders
        modelBuilder.Entity<FilesToFoldersEntity>()
            .HasOne(ff => ff.Folder)
            .WithMany(f => f.FilesLinks)
            .HasForeignKey(ff => ff.FolderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<FilesToFoldersEntity>()
            .HasOne(ff => ff.File)
            .WithMany(f => f.ParentFolderLinks)
            .HasForeignKey(ff => ff.FileId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<FilesToFoldersEntity>()
            .HasOne(ff => ff.Snapshot)
            .WithMany(s => s.FilesToFolders)
            .HasForeignKey(ff => ff.SnapshotId)
            .OnDelete(DeleteBehavior.Cascade);

        // Files basic properties constraints (optional examples)
        modelBuilder.Entity<FsFileEntity>()
            .Property(f => f.Name)
            .HasMaxLength(512);

        modelBuilder.Entity<FsFolderEntity>()
            .Property(f => f.Name)
            .HasMaxLength(512);
    }
}