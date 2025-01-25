using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.DataProviders;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Ufo.DataProviders
{
    public class SystemInfoProvider: ISystemInfoProvider
    {
        public SnapshotEntity GetSystemInformation(string path)
        {
            var driveLetter = char.ToUpper(path[0]);
            var snapshotEntity = new SnapshotEntity();
            var pc = new PcEntity { Name = Environment.MachineName };
            var storageDriveEntity = new StorageDriveEntity();
            var volume = new VolumeEntity();
            var volumeInfo = new VolumeInfoEntity();
            pc.Snapshots.Add(snapshotEntity);
            pc.StorageDrives.Add(storageDriveEntity);
            storageDriveEntity.Pcs.Add(pc);
            storageDriveEntity.Volumes.Add(volume);
            volume.VolumeInfos.Add(volumeInfo);
            volume.StorageDrive = storageDriveEntity;
            volume.StorageDriveGuid = storageDriveEntity.Guid;
            volumeInfo.Volume = volume;
            volumeInfo.VolumeGuid = volume.Guid;
            volumeInfo.Snapshot = snapshotEntity;
            volumeInfo.SnapshotGuid = snapshotEntity.Guid;
            snapshotEntity.VolumeInfo = volumeInfo;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                GetDriveInformationForWindows(driveLetter, volumeInfo);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                GetDriveInformationSystem(driveLetter, volumeInfo);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                GetDriveInformationSystem(driveLetter, volumeInfo);
            }

            return snapshotEntity;
        }

        private void GetDriveInformationSystem(char driveLetter, VolumeInfoEntity volumeInfo)
        {
            var allDrives = DriveInfo.GetDrives().ToList();
            var pcDrive = allDrives.First(x => char.ToUpper(x.Name[0]) == driveLetter);

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
            var volume = volumeInfo.Volume;
            var storageDrive = volume.StorageDrive;

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
        }
    }
}
