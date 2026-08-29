using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.DataProviders;

namespace Ufo.DataProviders;

/// <summary>
/// Builds the entity graph that a snapshot is attached to (snapshot, PC, storage
/// drive, volume, volume info) and delegates the two platform-specific parts -
/// drive interrogation and machine identity - to a derived provider.
/// </summary>
/// <remarks>
/// The platform split is by assembly, not by <c>#if</c>: the Windows provider
/// lives in <c>Ufo.Platform.Windows</c> (targeting <c>net10.0-windows</c>) so its
/// WMI and registry code actually compiles, and the container image never loads
/// it. See <c>_docs/AI_DUAL_TARGET_PLAN.md</c>.
/// </remarks>
public abstract class SystemInfoProviderBase : ISystemInfoProvider
{
    protected SystemInfoProviderBase(ILogger logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected ILogger Logger { get; }

    public SnapshotEntity GetSystemInformation(string path, UserEntity user)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(user);

        var deviceIdentifiers = ReadDeviceIdentifiers();

        var snapshotEntity = new SnapshotEntity { User = user, UserId = user.Id };
        var pc = new PcEntity
        {
            Name = Environment.MachineName,
            HardwareUuid = deviceIdentifiers.HardwareUuid,
            HardwareSerialNumber = deviceIdentifiers.HardwareSerialNumber,
            MachineId = deviceIdentifiers.MachineId,
            User = user,
            UserId = user.Id
        };
        var storageDriveEntity = new StorageDriveEntity { User = user, UserId = user.Id };
        var volume = new VolumeEntity { User = user, UserId = user.Id };
        var volumeInfo = new VolumeInfoEntity { User = user, UserId = user.Id };

        pc.Snapshots.Add(snapshotEntity);
        pc.StorageDrives.Add(storageDriveEntity);
        storageDriveEntity.Pcs.Add(pc);
        storageDriveEntity.Volumes.Add(volume);
        volume.VolumeInfos.Add(volumeInfo);
        volume.StorageDrive = storageDriveEntity;
        volume.StorageDriveId = storageDriveEntity.Id;
        volumeInfo.Volume = volume;
        volumeInfo.VolumeId = volume.Id;
        volumeInfo.Snapshot = snapshotEntity;
        volumeInfo.SnapshotId = snapshotEntity.Id;
        snapshotEntity.VolumeInfo = volumeInfo;

        try
        {
            PopulateDriveInformation(path, volumeInfo, user);
        }
        catch (Exception exception)
        {
            // Losing drive metadata must not lose the snapshot itself - the file
            // tree is the valuable part and has already been paid for.
            Logger.LogError(exception, "Failed to read drive information for path {Path}. Snapshot continues without it.", path);
        }

        return snapshotEntity;
    }

    /// <summary>
    /// Fills in storage-drive and volume details for the drive holding
    /// <paramref name="path"/>. Implementations must not throw for an
    /// unrecognised path; leaving the fields at their defaults is preferred.
    /// </summary>
    protected abstract void PopulateDriveInformation(string path, VolumeInfoEntity volumeInfo, UserEntity user);

    /// <summary>
    /// Reads the machine identity. Implementations must never throw; unavailable
    /// fields degrade to <see cref="DeviceIdentifiers.Unknown"/>.
    /// </summary>
    protected abstract DeviceIdentifiers ReadDeviceIdentifiers();
}
