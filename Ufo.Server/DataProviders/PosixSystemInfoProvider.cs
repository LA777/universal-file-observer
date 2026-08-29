using Microsoft.Extensions.Options;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.DataProviders;
using Ufo.Abstractions.Options;

namespace Ufo.DataProviders;

/// <summary>
/// Drive and machine-identity provider for Linux, macOS and any host without the
/// Windows-only WMI stack. This is the provider the container image uses.
/// </summary>
public class PosixSystemInfoProvider : SystemInfoProviderBase
{
    private const string MachineIdFileName = "machine-id";
    private const string LinuxProductUuidPath = "/sys/class/dmi/id/product_uuid";
    private const string LinuxProductSerialPath = "/sys/class/dmi/id/product_serial";
    private const string LinuxMachineIdPath = "/etc/machine-id";
    private const string LinuxDbusMachineIdPath = "/var/lib/dbus/machine-id";

    private readonly IOptions<UfoHostOptions> _hostOptions;

    public PosixSystemInfoProvider(ILogger<PosixSystemInfoProvider> logger, IOptions<UfoHostOptions> hostOptions)
        : base(logger)
    {
        _hostOptions = hostOptions ?? throw new ArgumentNullException(nameof(hostOptions));
    }

    protected override void PopulateDriveInformation(string path, VolumeInfoEntity volumeInfo, UserEntity user)
    {
        var mount = ResolveMountForPath(path);
        if (mount == null)
        {
            Logger.LogWarning("No mount point could be resolved for path {Path}.", path);
            return;
        }

        // The base class has already linked volume and volume info in both
        // directions; adding to VolumeInfos again here (as the original code did)
        // put the same entity in the collection twice.
        var volume = volumeInfo.Volume ??= new VolumeEntity { User = user, UserId = user.Id };
        volume.DriveLetter = mount.Name;

        // An unmounted or unreadable device throws on every size/label member,
        // so probe IsReady once rather than guarding each property.
        if (!mount.IsReady)
        {
            Logger.LogWarning("Mount {MountPoint} is not ready; volume size and label are unavailable.", mount.Name);
            return;
        }

        try
        {
            volume.VolumeName = mount.VolumeLabel;
            volume.Description = mount.DriveFormat;
            volume.VolumeSize = mount.TotalSize;
            volumeInfo.FreeSpace = mount.TotalFreeSpace;
            volumeInfo.DriveStatus = mount.DriveType.ToString();

            var storageDrive = volume.StorageDrive;
            if (storageDrive != null)
            {
                storageDrive.Name = mount.Name;
                storageDrive.DeviceId = mount.Name;
                storageDrive.Description = mount.DriveFormat;
                storageDrive.MediaType = mount.DriveType.ToString();
                storageDrive.TotalSize = mount.TotalSize;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Logger.LogWarning(exception, "Could not read volume details for mount {MountPoint}.", mount.Name);
        }
    }

    /// <summary>
    /// Returns the mount whose root is the longest prefix of <paramref name="path"/>.
    /// </summary>
    /// <remarks>
    /// The previous implementation matched on the first character of the path,
    /// which works for Windows drive letters and is meaningless on POSIX, where
    /// every mount starts with '/' - it selected an arbitrary mount (frequently
    /// "/proc" or "/sys") and recorded its size against the snapshot.
    /// </remarks>
    private DriveInfo? ResolveMountForPath(string path)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Logger.LogWarning(exception, "Path {Path} could not be canonicalised.", path);
            return null;
        }

        DriveInfo? bestMatch = null;
        var bestMatchLength = -1;

        foreach (var candidateMount in DriveInfo.GetDrives())
        {
            string mountPoint;
            try
            {
                mountPoint = candidateMount.RootDirectory.FullName;
            }
            catch (Exception exception)
            {
                Logger.LogDebug(exception, "Skipping unreadable mount {MountName}.", candidateMount.Name);
                continue;
            }

            if (!IsWithin(fullPath, mountPoint) || mountPoint.Length <= bestMatchLength)
            {
                continue;
            }

            bestMatchLength = mountPoint.Length;
            bestMatch = candidateMount;
        }

        return bestMatch;
    }

    private static bool IsWithin(string fullPath, string root)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var normalisedRoot = root.TrimEnd(Path.DirectorySeparatorChar);
        if (normalisedRoot.Length == 0)
        {
            // The filesystem root itself ("/" trimmed to empty) contains everything.
            return true;
        }

        return fullPath.Equals(normalisedRoot, comparison)
            || fullPath.StartsWith(normalisedRoot + Path.DirectorySeparatorChar, comparison);
    }

    protected override DeviceIdentifiers ReadDeviceIdentifiers()
    {
        var hardwareUuid = DeviceIdentifiers.Unknown;
        var hardwareSerialNumber = DeviceIdentifiers.Unknown;

        if (OperatingSystem.IsLinux())
        {
            // Both DMI files are root-readable only on many distributions and are
            // absent altogether inside containers; "Unknown" is an expected result.
            hardwareUuid = ReadSystemFile(LinuxProductUuidPath);
            hardwareSerialNumber = ReadSystemFile(LinuxProductSerialPath);
        }

        return new DeviceIdentifiers(hardwareUuid, hardwareSerialNumber, ResolveMachineId());
    }

    /// <summary>
    /// Machine id precedence: explicit configuration, then the OS install id,
    /// then an id generated once and kept in the data directory.
    /// </summary>
    /// <remarks>
    /// The generated fallback matters for containers: a container's own
    /// /etc/machine-id is regenerated on every recreation, so snapshots taken
    /// before and after a `docker compose up` would be attributed to different
    /// machines. Persisting the id on the mounted data volume keeps one identity
    /// for the life of the deployment; <c>Ufo__MachineId</c> overrides it when the
    /// physical host's identity is what should be recorded.
    /// </remarks>
    private string ResolveMachineId()
    {
        var configuredMachineId = _hostOptions.Value.MachineId;
        if (!string.IsNullOrWhiteSpace(configuredMachineId))
        {
            return configuredMachineId.Trim();
        }

        if (OperatingSystem.IsLinux())
        {
            var machineId = ReadSystemFile(LinuxMachineIdPath);
            if (IsKnown(machineId))
            {
                return machineId;
            }

            machineId = ReadSystemFile(LinuxDbusMachineIdPath);
            if (IsKnown(machineId))
            {
                return machineId;
            }
        }

        return ReadOrCreatePersistedMachineId();
    }

    private static bool IsKnown(string value) =>
        !string.Equals(value, DeviceIdentifiers.Unknown, StringComparison.OrdinalIgnoreCase);

    private string ReadOrCreatePersistedMachineId()
    {
        var dataDirectory = _hostOptions.Value.DataDirectory;
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            dataDirectory = AppContext.BaseDirectory;
        }

        var machineIdFilePath = Path.Combine(dataDirectory, MachineIdFileName);

        try
        {
            if (File.Exists(machineIdFilePath))
            {
                var persistedMachineId = File.ReadAllText(machineIdFilePath).Trim();
                if (!string.IsNullOrWhiteSpace(persistedMachineId))
                {
                    return persistedMachineId;
                }
            }

            var generatedMachineId = Ulid.NewUlid().ToString();
            Directory.CreateDirectory(dataDirectory);
            File.WriteAllText(machineIdFilePath, generatedMachineId);
            Logger.LogInformation("Generated a machine id and persisted it to {MachineIdFilePath}.", machineIdFilePath);

            return generatedMachineId;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Logger.LogWarning(exception, "Could not persist a machine id to {MachineIdFilePath}.", machineIdFilePath);
            return DeviceIdentifiers.Unknown;
        }
    }

    private string ReadSystemFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return DeviceIdentifiers.Unknown;
            }

            var content = File.ReadAllText(filePath).Trim();

            return string.IsNullOrWhiteSpace(content) ? DeviceIdentifiers.Unknown : content;
        }
        catch (UnauthorizedAccessException)
        {
            Logger.LogWarning("Permission denied reading system file {FilePath}.", filePath);
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Unexpected error reading system file {FilePath}.", filePath);
        }

        return DeviceIdentifiers.Unknown;
    }
}
