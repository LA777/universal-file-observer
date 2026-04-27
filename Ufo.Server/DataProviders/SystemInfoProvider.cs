using Microsoft.Win32;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.DataProviders;

#if WINDOWS
using System.Management;
#endif

namespace Ufo.DataProviders;

public class SystemInfoProvider : ISystemInfoProvider
{
    private readonly ILogger<SystemInfoProvider> _logger;

    public SystemInfoProvider(ILogger<SystemInfoProvider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public SnapshotEntity GetSystemInformation(string path, UserEntity user)
    {
        var driveLetter = char.ToUpper(path[0]);
        var snapshotEntity = new SnapshotEntity() { User = user, UserId = user.Id };
        var (hardwareUuid, hardwareSerialNumber, machineId) = GetDeviceIdentifiers();
        var pc = new PcEntity { Name = Environment.MachineName, HardwareUuid = hardwareUuid, HardwareSerialNumber = hardwareSerialNumber, MachineId = machineId, User = user, UserId = user.Id };
        var storageDriveEntity = new StorageDriveEntity() { User = user, UserId = user.Id };
        var volume = new VolumeEntity() { User = user, UserId = user.Id };
        var volumeInfo = new VolumeInfoEntity() { User = user, UserId = user.Id };
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

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            GetDriveInformationForWindows(driveLetter, volumeInfo);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            GetDriveInformationSystem(driveLetter, volumeInfo, user);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            GetDriveInformationSystem(driveLetter, volumeInfo, user);
        }

        return snapshotEntity;
    }

    private void GetDriveInformationSystem(char driveLetter, VolumeInfoEntity volumeInfo, UserEntity user)
    {
        var allDrives = DriveInfo.GetDrives().ToList();
        var pcDrive = allDrives.First(x => char.ToUpper(x.Name[0]) == driveLetter);

        volumeInfo.Volume ??= new VolumeEntity() { User = user, UserId = user.Id };
        volumeInfo.Volume.DriveLetter = pcDrive.Name;
        volumeInfo.Volume.VolumeName = pcDrive.VolumeLabel;
        volumeInfo.Volume.VolumeInfos.Add(volumeInfo);
        volumeInfo.Volume.VolumeSize = pcDrive.TotalSize;
        volumeInfo.FreeSpace = pcDrive.TotalFreeSpace;

        //Name: C:\
        //AvailableFreeSpace: 20421255168
        //DriveFormat: NTFS
        //DriveType: Fixed
        //IsReady: True
        //RootDirectory: C:\
        //TotalFreeSpace: 20421255168
        //TotalSize: 508923756544
        //VolumeLabel: System_Drive

        //Name: D:\
        //AvailableFreeSpace: 31761494016
        //DriveFormat: NTFS
        //DriveType: Removable
        //IsReady: True
        //RootDirectory: D:\
        //TotalFreeSpace: 31761494016
        //TotalSize: 256322301952
        //VolumeLabel:
    }

    [SupportedOSPlatform("windows")]
    private void GetDriveInformationForWindows(char driveLetter, VolumeInfoEntity volumeInfo)
    {
#if WINDOWS
        var volume = volumeInfo.Volume ??= new VolumeEntity();
        var storageDrive = volume.StorageDrive ??= new StorageDriveEntity();

        var driveQuery = new ManagementObjectSearcher("select * from Win32_DiskDrive");

        foreach (var managementBaseObject in driveQuery.Get())
        {
            var managementObject = (ManagementObject)managementBaseObject;
            var partitionQuery = new ManagementObjectSearcher($"associators of {{{managementObject.Path.RelativePath}}} where AssocClass = Win32_DiskDriveToDiskPartition");

            foreach (var partition in partitionQuery.Get())
            {
                var partitionManagementObject = (ManagementObject)partition;
                var logicalDriveQuery = new ManagementObjectSearcher($"associators of {{{partitionManagementObject.Path.RelativePath}}} where AssocClass = Win32_LogicalDiskToPartition");
                foreach (var logicalDrive in logicalDriveQuery.Get())
                {
                    var logicalDriveManagementObject = (ManagementObject)logicalDrive;
                    var name = logicalDriveManagementObject.Properties["Name"].Value.ToString();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    if (char.ToUpper(name[0]) != driveLetter)
                    {
                        continue;
                    }

                    storageDrive.Name = Convert.ToString(managementObject.Properties["Caption"].Value) ?? string.Empty; // SDXC Card  // WDC PC SN730 SDBQNGY-512G-1201
                    storageDrive.DeviceId = Convert.ToString(managementObject.Properties["DeviceID"].Value) ?? string.Empty; // "\\.\PHYSICALDRIVE1"  // "\\\\.\\PHYSICALDRIVE0"
                    storageDrive.SerialNumber = Convert.ToString(managementObject.Properties["SerialNumber"].Value) ?? string.Empty; //   // 001B_678B_43E1_E2F9.
                    storageDrive.TotalSize = Convert.ToInt64(managementObject.Properties["Size"].Value); // 256349076480 // 512105932800
                    storageDrive.Description = Convert.ToString(managementObject.Properties["Description"].Value) ?? string.Empty; // Disk drive // Disk drive
                    storageDrive.MediaType = Convert.ToString(managementObject.Properties["MediaType"].Value) ?? string.Empty; // Removable Media // Fixed hard disk media
                    storageDrive.InterfaceType = Convert.ToString(managementObject.Properties["InterfaceType"].Value) ?? string.Empty; // USB // SCSI

                    volumeInfo.DriveStatus = Convert.ToString(managementObject.Properties["Status"].Value) ?? string.Empty; // OK // OK

                    volume.DriveLetter = Convert.ToString(logicalDriveManagementObject.Properties["Name"].Value) ?? string.Empty; // D: // C:
                    volume.Description = Convert.ToString(logicalDriveManagementObject.Properties["Description"].Value) ?? string.Empty; // Removable Disk // Local Fixed Disk
                    volume.VolumeSerialNumber = Convert.ToString(logicalDriveManagementObject.Properties["VolumeSerialNumber"].Value) ?? string.Empty; // 40BED394 // "80CE55B5"
                    volume.VolumeSize = Convert.ToInt64(logicalDriveManagementObject.Properties["Size"].Value); // 256322301952 // 508923756544
                    volume.VolumeName = Convert.ToString(logicalDriveManagementObject.Properties["VolumeName"].Value) ?? string.Empty; //   // System_Drive

                    volumeInfo.FreeSpace = Convert.ToInt64(logicalDriveManagementObject.Properties["FreeSpace"].Value); // 31761502208 // 26935435264
                }
            }
        }
#endif
    }

    (string HardwareUuid, string HardwareSerialNumber, string MachineId) GetDeviceIdentifiers()
    {
        string uuid = "Unknown";
        string serial = "Unknown";
        string machineId = "Unknown";

        if (OperatingSystem.IsWindows())
        {
            try
            {
                // Get UUID
                using var searcherUuid = new ManagementObjectSearcher("SELECT UUID FROM Win32_ComputerSystemProduct");
                foreach (var obj in searcherUuid.Get())
                {
                    uuid = obj["UUID"]?.ToString() ?? "Unknown";
                }

                // Get Serial Number
                using var searcherSerial = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_Bios");
                foreach (var obj in searcherSerial.Get())
                {
                    serial = obj["SerialNumber"]?.ToString() ?? "Unknown";
                }

                // HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\SQMClient
                using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\SQMClient"))
                {
                    // 3. Retrieve the 'MachineId' value
                   var machineIdObj = key?.GetValue("MachineId");

                    if (machineIdObj != null)
                    {
                        // Format: {UUID} -> we trim the curly braces to match the 'Settings' UI
                        machineId = machineIdObj.ToString()?.Trim('{', '}') ?? "ID Empty";
                    }
                }

                return (HardwareUuid: uuid, HardwareSerialNumber: serial, MachineId: machineId);
            }
            catch(Exception exception)
            {
                _logger.LogError(exception, "Error retrieving Windows device identifiers.");
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            try
            {
                // 1. Hardware UUID (Requires root for some distros, but often readable)
                uuid = ReadLinuxFile("/sys/class/dmi/id/product_uuid");

                // 2. Hardware Serial Number
                serial = ReadLinuxFile("/sys/class/dmi/id/product_serial");

                // 3. Machine ID (The OS-install specific ID)
                // This is the Linux equivalent to the Windows 'Device ID'`
                machineId = ReadLinuxFile("/etc/machine-id");

                // Some systems use /var/lib/dbus/machine-id as a fallback
                if (string.Equals(machineId, "Unknown", StringComparison.OrdinalIgnoreCase))
                {
                    machineId = ReadLinuxFile("/var/lib/dbus/machine-id");
                }

                return (HardwareUuid: uuid, HardwareSerialNumber: serial, MachineId: machineId);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Error retrieving Linux device identifiers.");
            }
        }
        // Add other platforms like macOS if needed (requires P/Invoke calls)
        // else if (OperatingSystem.IsMacOS())
        // {
        //     return GetMacOsDeviceId();
        // }

        throw new PlatformNotSupportedException("OS not supported.");
    }

    private string ReadLinuxFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                // Read the text, trim whitespace/newlines common in system files
                string content = File.ReadAllText(filePath).Trim();

                // Return content if not empty, otherwise fallback to Unknown
                return !string.IsNullOrWhiteSpace(content) ? content : "Unknown";
            }
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning("Permission denied accessing Linux system file: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error reading Linux system file: {FilePath}", filePath);
        }

        return "Unknown";
    }

    #region Windows Implementation (WMI)

    static string GetWindowsDeviceId()
    {
#if WINDOWS
            try
            {
                // Query WMI for the Motherboard Serial Number (considered stable)
                // Use Win32_BaseBoard or Win32_ComputerSystemProduct
                string query = "SELECT SerialNumber FROM Win32_BaseBoard";

                using (var searcher = new ManagementObjectSearcher(query))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        // Return the first serial number found
                        return obj["SerialNumber"]?.ToString().Trim();
                    }
                }
                Console.WriteLine("Warning: Failed to query Win32_BaseBoard.");
                return string.Empty;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error querying WMI on Windows: {ex.Message}");
                return string.Empty;
            }
#else
        // This case should be unreachable if using OperatingSystem.IsWindows()
        return string.Empty;
#endif
    }

    #endregion

    #region Linux Implementation (Reading /etc/machine-id)

    // On Linux, the standard stable ID is the /etc/machine-id
    static string GetLinuxDeviceId()
    {
        const string machineIdPath = "/etc/machine-id";

        if (File.Exists(machineIdPath))
        {
            try
            {
                // Read the entire file, which contains the 32-character hex string
                string id = File.ReadAllText(machineIdPath, Encoding.ASCII).Trim();

                if (id.Length == 32)
                {
                    return id;
                }
                Console.WriteLine($"Warning: Found file, but ID format is incorrect: {id.Length} chars.");
                return id;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading {machineIdPath}: {ex.Message}");
                return string.Empty;
            }
        }
        else
        {
            Console.WriteLine($"Warning: {machineIdPath} not found. This is normal in some environments (e.g., containers).");
            return string.Empty;
        }
    }

    #endregion
}
