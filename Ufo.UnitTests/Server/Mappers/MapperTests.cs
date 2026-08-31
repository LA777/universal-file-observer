using FluentAssertions;
using Ufo.Abstractions;
using Ufo.Abstractions.Database.Entities;
using Ufo.Server.Mappers;

namespace Ufo.UnitTests.Server.Mappers;

/// <summary>
/// Entity → DTO mapping tests. UserId assertions are regression coverage:
/// no mapper set UserId before 2026-07, so every API response carried a zero id.
/// </summary>
public class MapperTests : BaseTest
{
    private readonly UserEntity _user = new()
    {
        Id = Ulid.NewUlid(),
        Name = "mapper-user",
        PasswordHash = "hash"
    };

    private FileEntity CreateFile(string name = "report") => new()
    {
        Id = Ulid.NewUlid(),
        Name = name,
        FileExtension = ".pdf",
        Size = 1234,
        Sha256Hash = "file-hash",
        CreatedAt = "2026-01-01T00:00:00Z",
        UpdatedAt = "2026-01-02T00:00:00Z",
        IsHidden = true,
        UserId = _user.Id,
        User = _user
    };

    private FolderEntity CreateFolder(string name = "documents") => new()
    {
        Id = Ulid.NewUlid(),
        Name = name,
        Size = 4096,
        Sha256Hash = "folder-hash",
        CreatedAt = "2026-01-01T00:00:00Z",
        UpdatedAt = "2026-01-02T00:00:00Z",
        UserId = _user.Id,
        User = _user
    };

    private SnapshotEntity CreateSnapshot() => new()
    {
        Id = Ulid.NewUlid(),
        Description = "test snapshot",
        UserId = _user.Id,
        User = _user
    };

    private LabelEntity CreateLabel(string name = "backups") => new()
    {
        Id = Ulid.NewUlid(),
        Name = name,
        ColorHex = "#e57373",
        UserId = _user.Id,
        User = _user
    };

    #region FileMapper

    [Fact]
    public void FileMapper_ToDto_MapsAllScalarFields()
    {
        var entity = CreateFile();

        var dto = entity.ToDto();

        dto.Id.Should().Be(entity.Id);
        dto.UserId.Should().Be(_user.Id); // regression: was never mapped
        dto.Name.Should().Be(entity.Name);
        dto.FileExtension.Should().Be(entity.FileExtension);
        dto.Size.Should().Be(entity.Size);
        dto.Sha256Hash.Should().Be(entity.Sha256Hash);
        dto.CreatedAt.Should().Be(entity.CreatedAt);
        dto.UpdatedAt.Should().Be(entity.UpdatedAt);
        dto.IsHidden.Should().Be(entity.IsHidden);
    }

    [Fact]
    public void FileMapper_ToDto_WithoutParent_HasNoParentAndBareFullPath()
    {
        var dto = CreateFile("report").ToDto();

        dto.HasParent.Should().BeFalse();
        dto.FullPath.Should().Be("report.pdf");
    }

    [Fact]
    public void FileMapper_ToDto_WithParentFolder_SetsHasParentAndPath()
    {
        var entity = CreateFile("report");
        var parent = CreateFolder("documents");
        entity.ParentFolders.Add(parent);

        var dto = entity.ToDto();

        dto.HasParent.Should().BeTrue();
        dto.FullPath.Should().Be(Path.Combine("documents", "report.pdf"));
    }

    [Fact]
    public void FileMapper_ToDto_MapsSnapshotsToSummaries()
    {
        var entity = CreateFile();
        entity.Snapshots.Add(CreateSnapshot());

        var dto = entity.ToDto();

        dto.Snapshots.Should().HaveCount(1);
        dto.Snapshots[0].UserId.Should().Be(_user.Id);
    }

    #endregion

    #region FolderMapper

    [Fact]
    public void FolderMapper_ToDto_MapsScalarFieldsAndUserId()
    {
        var entity = CreateFolder();

        var dto = entity.ToDto();

        dto.Id.Should().Be(entity.Id);
        dto.UserId.Should().Be(_user.Id); // regression: was never mapped
        dto.Name.Should().Be(entity.Name);
        dto.Size.Should().Be(entity.Size);
        dto.Sha256Hash.Should().Be(entity.Sha256Hash);
    }

    [Fact]
    public void FolderMapper_ToDto_MapsChildFoldersAndFilesRecursively()
    {
        var root = CreateFolder("root");
        var child = CreateFolder("child");
        root.ChildFolders.Add(child);
        root.Files.Add(CreateFile());

        var dto = root.ToDto();

        dto.ChildFolders.Should().HaveCount(1);
        dto.ChildFolders[0].Name.Should().Be("child");
        dto.ChildFolders[0].UserId.Should().Be(_user.Id);
        dto.Files.Should().HaveCount(1);
        dto.Files[0].UserId.Should().Be(_user.Id);
    }

    [Fact]
    public void FolderMapper_ToRootOnlyDto_MapsIdentityWithoutThrowing()
    {
        var root = CreateFolder("root");
        root.ChildFolders.Add(CreateFolder("child"));

        var dto = root.ToRootOnlyDto();

        dto.Should().NotBeNull();
        dto.Name.Should().Be("root");
        dto.UserId.Should().Be(_user.Id);
    }

    #endregion

    #region LabelMapper

    [Fact]
    public void LabelMapper_ToDto_MapsFieldsIncludingColorAndUserId()
    {
        var entity = CreateLabel();
        var snapshot = CreateSnapshot();
        entity.Snapshots.Add(snapshot);

        var dto = entity.ToDto();

        dto.Id.Should().Be(entity.Id);
        dto.UserId.Should().Be(_user.Id); // regression: was never mapped
        dto.Name.Should().Be(entity.Name);
        dto.ColorHex.Should().Be("#e57373");
        dto.SnapshotIds.Should().ContainSingle().Which.Should().Be(snapshot.Id);
    }

    #endregion

    #region UserSettingsMapper

    [Fact]
    public void UserSettingsMapper_ToDto_MapsThemeAndUserId()
    {
        var entity = new UserSettingsEntity
        {
            Id = Ulid.NewUlid(),
            Theme = UiThemes.Light,
            UserId = _user.Id
        };

        var dto = entity.ToDto();

        dto.Id.Should().Be(entity.Id);
        dto.UserId.Should().Be(_user.Id);
        dto.Theme.Should().Be(UiThemes.Light);
    }

    [Fact]
    public void UserSettingsMapper_DefaultsFor_CarriesTheCallersUserIdAndTheDefaultTheme()
    {
        var dto = UserSettingsMapper.DefaultsFor(_user.Id);

        dto.UserId.Should().Be(_user.Id);
        dto.Theme.Should().Be(UiThemes.Default);
    }

    #endregion

    #region SnapshotMapper

    [Fact]
    public void SnapshotMapper_ToSummaryDto_MapsLabelsRootFolderAndUserId()
    {
        var entity = CreateSnapshot();
        entity.Labels.Add(CreateLabel());
        entity.RootFolder = CreateFolder("snapshot-root");

        var dto = entity.ToSummaryDto();

        dto.Id.Should().Be(entity.Id);
        dto.UserId.Should().Be(_user.Id); // regression: was never mapped
        dto.Description.Should().Be(entity.Description);
        dto.Labels.Should().HaveCount(1);
        dto.Labels[0].ColorHex.Should().Be("#e57373");
        dto.RootOnlyFolder.Should().NotBeNull();
        dto.RootOnlyFolder!.Name.Should().Be("snapshot-root");
    }

    [Fact]
    public void SnapshotMapper_ToDto_MapsFullRootFolder()
    {
        var entity = CreateSnapshot();
        var root = CreateFolder("snapshot-root");
        root.Files.Add(CreateFile());
        entity.RootFolder = root;

        var dto = entity.ToDto();

        dto.UserId.Should().Be(_user.Id);
        dto.RootFolder.Should().NotBeNull();
        dto.RootFolder!.Files.Should().HaveCount(1);
    }

    #endregion

    #region System info mappers

    [Fact]
    public void PcMapper_ToDto_MapsIdentityFields()
    {
        var entity = new PcEntity
        {
            Id = Ulid.NewUlid(),
            Name = "MyPC",
            MachineId = "machine-1",
            HardwareUuid = "uuid-1",
            HardwareSerialNumber = "serial-1",
            UserId = _user.Id,
            User = _user
        };

        var dto = entity.ToDto();

        dto.Id.Should().Be(entity.Id);
        dto.UserId.Should().Be(_user.Id); // regression: was never mapped
        dto.Name.Should().Be("MyPC");
        dto.MachineId.Should().Be("machine-1");
        dto.HardwareUuid.Should().Be("uuid-1");
        dto.HardwareSerialNumber.Should().Be("serial-1");
    }

    [Fact]
    public void StorageDriveMapper_ToDto_MapsNameAndUserId()
    {
        var entity = new StorageDriveEntity
        {
            Id = Ulid.NewUlid(),
            Name = "Samsung SSD",
            DeviceId = "dev-1",
            SerialNumber = "sn-1",
            TotalSize = 1_000_000,
            Description = "Primary drive",
            MediaType = "SSD",
            InterfaceType = "NVMe",
            UserId = _user.Id,
            User = _user
        };

        var dto = entity.ToDto();

        dto.Name.Should().Be("Samsung SSD"); // regression: Name was never mapped
        dto.UserId.Should().Be(_user.Id);    // regression: UserId was never mapped
        dto.DeviceId.Should().Be("dev-1");
        dto.SerialNumber.Should().Be("sn-1");
        dto.TotalSize.Should().Be(1_000_000);
        dto.MediaType.Should().Be("SSD");
        dto.InterfaceType.Should().Be("NVMe");
    }

    [Fact]
    public void VolumeMapper_ToDto_MapsFieldsAndNestedStorageDrive()
    {
        var drive = new StorageDriveEntity
        {
            Id = Ulid.NewUlid(),
            Name = "Drive",
            DeviceId = "dev-1",
            SerialNumber = "sn-1",
            TotalSize = 1,
            Description = "d",
            MediaType = "SSD",
            InterfaceType = "SATA",
            UserId = _user.Id,
            User = _user
        };
        var entity = new VolumeEntity
        {
            Id = Ulid.NewUlid(),
            DriveLetter = "C:",
            VolumeName = "System",
            Description = "system volume",
            VolumeSerialNumber = "vsn-1",
            VolumeSize = 500,
            StorageDrive = drive,
            UserId = _user.Id,
            User = _user
        };

        var dto = entity.ToDto();

        dto.UserId.Should().Be(_user.Id);
        dto.DriveLetter.Should().Be("C:");
        dto.VolumeName.Should().Be("System");
        dto.VolumeSerialNumber.Should().Be("vsn-1");
        dto.StorageDrive.Should().NotBeNull();
        dto.StorageDrive!.Name.Should().Be("Drive");
    }

    [Fact]
    public void VolumeInfoMapper_ToDto_MapsFieldsAndNestedVolume()
    {
        var entity = new VolumeInfoEntity
        {
            Id = Ulid.NewUlid(),
            FreeSpace = 250,
            DriveStatus = "OK",
            Volume = new VolumeEntity
            {
                Id = Ulid.NewUlid(),
                DriveLetter = "D:",
                VolumeName = "Data",
                Description = "data volume",
                VolumeSerialNumber = "vsn-2",
                VolumeSize = 1000,
                UserId = _user.Id,
                User = _user
            },
            UserId = _user.Id,
            User = _user
        };

        var dto = entity.ToDto();

        dto.UserId.Should().Be(_user.Id);
        dto.FreeSpace.Should().Be(250);
        dto.DriveStatus.Should().Be("OK");
        dto.Volume.Should().NotBeNull();
        dto.Volume!.DriveLetter.Should().Be("D:");
    }

    #endregion
}
