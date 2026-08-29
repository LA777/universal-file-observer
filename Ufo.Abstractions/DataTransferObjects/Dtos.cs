using Cysharp.Serialization.Json;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Ufo.Abstractions.DataTransferObjects;

public abstract class DtoBase
{
    // No default value: an unmapped Id must surface as Ulid.Empty rather than a
    // plausible-looking random value that hides a mapping bug.
    [JsonConverter(typeof(UlidJsonConverter))]
    [JsonPropertyOrder(0)]
    public Ulid Id { get; set; }
}

public abstract class DtoWithNameAndIdBase : DtoBase
{
    [JsonPropertyOrder(1)]
    [Required]
    [MaxLength(256)]
    public string Name { get; set; } = string.Empty;
}

public abstract class FsItemDto : DtoWithNameAndIdBase
{
    [JsonPropertyOrder(2)]
    public long? Size { get; set; }

    [Required]
    [MaxLength(128)]
    [JsonPropertyOrder(3)]
    public string Sha256Hash { get; set; } = string.Empty;

    [MaxLength(64)] // TODO LA - Update tests to cover this field. Verify MaxLength.
    [JsonPropertyOrder(10)]
    public string CreatedAt { get; set; } = string.Empty;

    [MaxLength(64)] // TODO LA - Update tests to cover this field. Verify MaxLength.
    [JsonPropertyOrder(11)]
    public string UpdatedAt { get; set; } = string.Empty;

    [JsonPropertyOrder(12)]
    public bool IsHidden { get; set; } = false;

    [JsonPropertyOrder(13)]
    public string? FullPath { get; set; }

    [JsonPropertyOrder(14)]
    public bool HasParent { get; set; }

    [JsonPropertyOrder(15)]
    public string ParentFolderPath { get; set; } = string.Empty;
}

public class LabelDto : DtoWithNameAndIdBase
{
    [JsonPropertyOrder(14)]
    public string ColorHex { get; set; } = string.Empty;

    [JsonPropertyOrder(20)]
    public List<Ulid> SnapshotIds { get; set; } = [];
}

public class PcDto : DtoWithNameAndIdBase
{
    [JsonPropertyOrder(14)]
    public string MachineId { get; set; } = string.Empty;

    [JsonPropertyOrder(15)]
    public string HardwareUuid { get; set; } = string.Empty;

    [JsonPropertyOrder(16)]
    public string HardwareSerialNumber { get; set; } = string.Empty;

    // TODO LA - Consider adding these fields
    //[JsonPropertyOrder(80)]
    //[JsonIgnore]
    //public List<StorageDriveEntity> StorageDrives { get; set; } = [];

    //[JsonPropertyOrder(90)]
    //[JsonIgnore]
    //public List<SnapshotEntity> Snapshots { get; set; } = [];
}

public class StorageDriveDto : DtoWithNameAndIdBase
{
    [JsonPropertyOrder(10)]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyOrder(15)]
    public string SerialNumber { get; set; } = string.Empty;

    [JsonPropertyOrder(20)]
    public long TotalSize { get; set; }

    [JsonPropertyOrder(25)]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyOrder(30)]
    public string MediaType { get; set; } = string.Empty;

    [JsonPropertyOrder(35)]
    public string InterfaceType { get; set; } = string.Empty;

    [JsonPropertyOrder(40)]
    public List<PcDto> Pcs { get; set; } = [];
}

public class VolumeDto : DtoBase
{
    [JsonPropertyOrder(5)]
    public string DriveLetter { get; set; } = string.Empty;

    [JsonPropertyOrder(10)]
    public string VolumeName { get; set; } = string.Empty;

    [JsonPropertyOrder(15)]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyOrder(20)]
    public string VolumeSerialNumber { get; set; } = string.Empty;

    [JsonPropertyOrder(25)]
    public long VolumeSize { get; set; }

    [JsonPropertyOrder(100)]
    public StorageDriveDto? StorageDrive { get; set; }
}

public class VolumeInfoDto : DtoBase
{
    [JsonPropertyOrder(5)]
    public long FreeSpace { get; set; }

    [JsonPropertyOrder(10)]
    public string DriveStatus { get; set; } = string.Empty;

    [JsonPropertyOrder(100)]
    public VolumeDto? Volume { get; set; }

    // TODO LA - Consider adding more volume info fields here, such as FileSystemType, TotalSize, UsedSpace etc.
}

public class FileDto : FsItemDto
{
    [JsonPropertyOrder(50)] // TODO LA - Rename to Extension
    public string FileExtension { get; set; } = string.Empty;

    [JsonPropertyOrder(90)]
    public List<SnapshotSummaryDto> Snapshots { get; set; } = [];
}

public class FolderDto : FsItemDto
{
    [JsonPropertyOrder(99)]
    public List<FolderDto> ChildFolders { get; set; } = [];

    [JsonPropertyOrder(100)]
    public List<FileDto> Files { get; set; } = [];

    [JsonPropertyOrder(90)]
    public List<SnapshotSummaryDto> Snapshots { get; set; } = [];
}

/// <summary>
/// Lightweight snapshot reference used inside File and Folder
/// to avoid circular references with the full Snapshot class.
/// </summary>
public class SnapshotSummaryDto : SnapshotSummaryBase
{
    [JsonPropertyOrder(15)]
    public FolderDto? RootOnlyFolder { get; set; }
}

public class SnapshotSummaryBase : DtoBase
{
    [JsonPropertyOrder(5)]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyOrder(6)]
    public string? Description { get; set; }

    [JsonPropertyOrder(99)]
    public List<LabelDto> Labels { get; set; } = [];

    [JsonPropertyOrder(20)]
    public VolumeInfoDto? VolumeInfo { get; set; }
}

public class SnapshotDto : SnapshotSummaryBase
{
    [JsonPropertyOrder(15)]
    public FolderDto? RootFolder { get; set; }
}
