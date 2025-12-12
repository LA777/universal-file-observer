using FluentAssertions.Equivalency;
using Ufo.Abstractions.Database.Entities;

namespace Ufo.IntegrationTests
{
    /// <summary>
    /// Extension methods for configuring FluentAssertions equivalency options.
    /// Handles circular references in entity comparisons.
    /// </summary>
    public static class FluentAssertionsExtensions
    {
        /// <summary>
        /// Configures equivalency options to exclude circular references in snapshot entities.
        /// </summary>
        public static EquivalencyAssertionOptions<SnapshotEntity> ExcludingCircularReferences(
            this EquivalencyAssertionOptions<SnapshotEntity> options)
        {
            return options
                .Excluding(x => x.VolumeInfo!.Snapshot)
                .Excluding(x => x.VolumeInfo!.Volume!.StorageDrive!.Snapshots)
                .Excluding(x => x.VolumeInfo!.Volume!.StorageDrive!.Pcs)
                .Excluding(x => x.VolumeInfo!.Volume!.StorageDrive!.Volumes)
                .Excluding(x => x.VolumeInfo!.Volume!.VolumeInfos)
                .Excluding(x => x.RootFolder!.Snapshots)
                .Excluding(ctx => ctx.Path.Contains("ParentFolders"))
                .Excluding(ctx => ctx.Path.Contains("Snapshots") && ctx.Path.Contains("Files"))
                .WithoutStrictOrdering();
        }

        /// <summary>
        /// Configures equivalency options to exclude circular references in volume entities.
        /// </summary>
        public static EquivalencyAssertionOptions<VolumeEntity> ExcludingCircularReferences(
            this EquivalencyAssertionOptions<VolumeEntity> options)
        {
            return options
                .Excluding(x => x.StorageDrive!.Snapshots)
                .Excluding(x => x.StorageDrive!.Pcs)
                .Excluding(x => x.StorageDrive!.Volumes)
                .Excluding(x => x.VolumeInfos);
        }

        /// <summary>
        /// Configures equivalency options to exclude circular references in storage drive entities.
        /// </summary>
        public static EquivalencyAssertionOptions<StorageDriveEntity> ExcludingCircularReferences(
            this EquivalencyAssertionOptions<StorageDriveEntity> options)
        {
            return options
                .Excluding(x => x.Snapshots)
                .Excluding(x => x.Pcs)
                .Excluding(x => x.Volumes);
        }

        /// <summary>
        /// Configures equivalency options to exclude circular references in PC entities.
        /// </summary>
        public static EquivalencyAssertionOptions<PcEntity> ExcludingCircularReferences(
            this EquivalencyAssertionOptions<PcEntity> options)
        {
            return options
                .Excluding(x => x.Snapshots)
                .Excluding(x => x.StorageDrives);
        }

        /// <summary>
        /// Configures equivalency options to exclude circular references in folder entities.
        /// Recursively excludes ParentFolders at all levels to handle nested folder hierarchies.
        /// </summary>
        public static EquivalencyAssertionOptions<FsFolderEntity> ExcludingCircularReferences(
            this EquivalencyAssertionOptions<FsFolderEntity> options)
        {
            return options
                .Excluding(x => x.ParentFolders)
                .Excluding(x => x.Snapshots)
                .WithoutStrictOrdering();
        }

        /// <summary>
        /// Configures equivalency options to exclude circular references in file entities.
        /// </summary>
        public static EquivalencyAssertionOptions<FsFileEntity> ExcludingCircularReferences(
            this EquivalencyAssertionOptions<FsFileEntity> options)
        {
            return options
                .Excluding(x => x.ParentFolders)
                .Excluding(x => x.Snapshots);
        }

        /// <summary>
        /// Configures equivalency options to exclude circular references in volume info entities.
        /// </summary>
        public static EquivalencyAssertionOptions<VolumeInfoEntity> ExcludingCircularReferences(
            this EquivalencyAssertionOptions<VolumeInfoEntity> options)
        {
            return options
                .Excluding(x => x.Snapshot)
                .Excluding(x => x.Volume!.StorageDrive!.Snapshots)
                .Excluding(x => x.Volume!.StorageDrive!.Pcs)
                .Excluding(x => x.Volume!.StorageDrive!.Volumes)
                .Excluding(x => x.Volume!.VolumeInfos);
        }
    }
}
