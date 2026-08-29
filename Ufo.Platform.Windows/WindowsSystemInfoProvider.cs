using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using System.Management;
using System.Runtime.Versioning;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.DataProviders;
using Ufo.Abstractions.Options;
using Ufo.DataProviders;

namespace Ufo.Platform.Windows;

/// <summary>
/// Drive and machine-identity provider backed by WMI and the registry.
/// </summary>
/// <remarks>
/// This code previously sat in Ufo.Server behind <c>#if WINDOWS</c>. That symbol
/// is only defined for a <c>net10.0-windows</c> target framework, and the server
/// targets plain <c>net10.0</c>, so the whole body compiled away to nothing -
/// which is why snapshots recorded no volume or storage-drive details. Moving it
/// to a genuinely Windows-targeted assembly makes it compile and run.
/// </remarks>
[SupportedOSPlatform("windows")]
public class WindowsSystemInfoProvider : SystemInfoProviderBase
{
    private const string SqmClientRegistryKeyPath = @"SOFTWARE\Microsoft\SQMClient";
    private const string MachineIdRegistryValueName = "MachineId";

    private readonly IOptions<UfoHostOptions> _hostOptions;

    public WindowsSystemInfoProvider(ILogger<WindowsSystemInfoProvider> logger, IOptions<UfoHostOptions> hostOptions)
        : base(logger)
    {
        _hostOptions = hostOptions ?? throw new ArgumentNullException(nameof(hostOptions));
    }

    protected override void PopulateDriveInformation(string path, VolumeInfoEntity volumeInfo, UserEntity user)
    {
        var fullPath = Path.GetFullPath(path);
        var pathRoot = Path.GetPathRoot(fullPath);

        if (string.IsNullOrEmpty(pathRoot) || pathRoot.Length < 2 || pathRoot[1] != ':')
        {
            // UNC shares and similar rootless paths have no logical disk to query.
            Logger.LogWarning("Path {Path} is not on a lettered volume; drive details are unavailable.", path);
            return;
        }

        var driveLetter = char.ToUpperInvariant(pathRoot[0]);

        var volume = volumeInfo.Volume ??= new VolumeEntity { User = user, UserId = user.Id };
        var storageDrive = volume.StorageDrive ??= new StorageDriveEntity { User = user, UserId = user.Id };

        using var physicalDriveSearcher = new ManagementObjectSearcher("select * from Win32_DiskDrive");

        foreach (var physicalDriveBaseObject in physicalDriveSearcher.Get())
        {
            using var physicalDrive = (ManagementObject)physicalDriveBaseObject;
            using var partitionSearcher = new ManagementObjectSearcher(
                $"associators of {{{physicalDrive.Path.RelativePath}}} where AssocClass = Win32_DiskDriveToDiskPartition");

            foreach (var partitionBaseObject in partitionSearcher.Get())
            {
                using var partition = (ManagementObject)partitionBaseObject;
                using var logicalDiskSearcher = new ManagementObjectSearcher(
                    $"associators of {{{partition.Path.RelativePath}}} where AssocClass = Win32_LogicalDiskToPartition");

                foreach (var logicalDiskBaseObject in logicalDiskSearcher.Get())
                {
                    using var logicalDisk = (ManagementObject)logicalDiskBaseObject;
                    var logicalDiskName = logicalDisk.Properties["Name"].Value?.ToString();

                    if (string.IsNullOrWhiteSpace(logicalDiskName) || char.ToUpperInvariant(logicalDiskName[0]) != driveLetter)
                    {
                        continue;
                    }

                    storageDrive.Name = ReadString(physicalDrive, "Caption");                 // WDC PC SN730 SDBQNGY-512G-1201
                    storageDrive.DeviceId = ReadString(physicalDrive, "DeviceID");             // \\.\PHYSICALDRIVE0
                    storageDrive.SerialNumber = ReadString(physicalDrive, "SerialNumber");
                    storageDrive.TotalSize = ReadInt64(physicalDrive, "Size");
                    storageDrive.Description = ReadString(physicalDrive, "Description");       // Disk drive
                    storageDrive.MediaType = ReadString(physicalDrive, "MediaType");           // Fixed hard disk media
                    storageDrive.InterfaceType = ReadString(physicalDrive, "InterfaceType");   // SCSI

                    volumeInfo.DriveStatus = ReadString(physicalDrive, "Status");              // OK

                    volume.DriveLetter = ReadString(logicalDisk, "Name");                      // C:
                    volume.Description = ReadString(logicalDisk, "Description");               // Local Fixed Disk
                    volume.VolumeSerialNumber = ReadString(logicalDisk, "VolumeSerialNumber");
                    volume.VolumeSize = ReadInt64(logicalDisk, "Size");
                    volume.VolumeName = ReadString(logicalDisk, "VolumeName");                 // System_Drive

                    volumeInfo.FreeSpace = ReadInt64(logicalDisk, "FreeSpace");

                    return;
                }
            }
        }

        Logger.LogWarning("No physical drive was found backing volume {DriveLetter}:.", driveLetter);
    }

    protected override DeviceIdentifiers ReadDeviceIdentifiers()
    {
        var hardwareUuid = DeviceIdentifiers.Unknown;
        var hardwareSerialNumber = DeviceIdentifiers.Unknown;
        var machineId = DeviceIdentifiers.Unknown;

        try
        {
            using var computerSystemProductSearcher = new ManagementObjectSearcher("SELECT UUID FROM Win32_ComputerSystemProduct");
            foreach (var computerSystemProduct in computerSystemProductSearcher.Get())
            {
                using (computerSystemProduct)
                {
                    hardwareUuid = computerSystemProduct["UUID"]?.ToString() ?? DeviceIdentifiers.Unknown;
                }
            }

            using var biosSearcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_Bios");
            foreach (var bios in biosSearcher.Get())
            {
                using (bios)
                {
                    hardwareSerialNumber = bios["SerialNumber"]?.ToString() ?? DeviceIdentifiers.Unknown;
                }
            }
        }
        catch (Exception exception)
        {
            // A WMI failure must not sink the snapshot. The previous implementation
            // logged here and then fell through to a PlatformNotSupportedException.
            Logger.LogError(exception, "Error retrieving Windows hardware identifiers.");
        }

        try
        {
            using var sqmClientKey = Registry.LocalMachine.OpenSubKey(SqmClientRegistryKeyPath);
            var machineIdValue = sqmClientKey?.GetValue(MachineIdRegistryValueName);

            if (machineIdValue != null)
            {
                // Stored as {GUID}; the braces are trimmed to match the Settings UI.
                machineId = machineIdValue.ToString()?.Trim('{', '}') ?? DeviceIdentifiers.Unknown;
            }
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Error retrieving the Windows machine id from the registry.");
        }

        var configuredMachineId = _hostOptions.Value.MachineId;
        if (!string.IsNullOrWhiteSpace(configuredMachineId))
        {
            machineId = configuredMachineId.Trim();
        }

        return new DeviceIdentifiers(hardwareUuid, hardwareSerialNumber, machineId);
    }

    private static string ReadString(ManagementObject managementObject, string propertyName)
    {
        try
        {
            return Convert.ToString(managementObject.Properties[propertyName].Value) ?? string.Empty;
        }
        catch (ManagementException)
        {
            return string.Empty;
        }
    }

    private static long ReadInt64(ManagementObject managementObject, string propertyName)
    {
        try
        {
            var propertyValue = managementObject.Properties[propertyName].Value;

            return propertyValue == null ? 0L : Convert.ToInt64(propertyValue);
        }
        catch (Exception exception) when (exception is ManagementException or FormatException or InvalidCastException or OverflowException)
        {
            return 0L;
        }
    }
}
