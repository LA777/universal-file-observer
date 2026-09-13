using FluentAssertions;
using Ufo.Abstractions.Database.Entities;
using Ufo.Server.Mappers;

namespace Ufo.UnitTests.Server.Mappers;

/// <summary>
/// <see cref="SnapshotMapper"/> on its own. The two shapes it produces differ
/// in one thing - the summary carries the root folder alone, the full DTO the
/// whole tree - and everything else must come through identically on both.
/// </summary>
public class SnapshotMapperTests : BaseTest
{
    private static readonly DateTimeOffset SnapshotTakenAt =
        new(2026, 3, 14, 15, 9, 26, TimeSpan.FromHours(2));

    private readonly UserEntity _user = new()
    {
        Id = Ulid.NewUlid(),
        Name = "snapshot-mapper-user",
        PasswordHash = "hash"
    };

    private SnapshotEntity CreateSnapshot(string? description = "nightly") => new()
    {
        Id = Ulid.NewUlid(),
        Description = description,
        Timestamp = SnapshotTakenAt,
        UserId = _user.Id,
        User = _user
    };

    private FolderEntity CreateFolder(string name) => new()
    {
        Id = Ulid.NewUlid(),
        Name = name,
        Size = 4096,
        Sha256Hash = $"hash-of-{name}",
        CreatedAt = "2026-01-01T00:00:00Z",
        UpdatedAt = "2026-01-02T00:00:00Z",
        UserId = _user.Id,
        User = _user
    };

    private FileEntity CreateFile(string name) => new()
    {
        Id = Ulid.NewUlid(),
        Name = name,
        FileExtension = ".mp4",
        Size = 1234,
        Sha256Hash = $"hash-of-{name}",
        CreatedAt = "2026-01-01T00:00:00Z",
        UpdatedAt = "2026-01-02T00:00:00Z",
        UserId = _user.Id,
        User = _user
    };

    private LabelEntity CreateLabel(string name) => new()
    {
        Id = Ulid.NewUlid(),
        Name = name,
        ColorHex = "#64b5f6",
        UserId = _user.Id,
        User = _user
    };

    private VolumeInfoEntity CreateVolumeInfo() => new()
    {
        Id = Ulid.NewUlid(),
        FreeSpace = 250,
        DriveStatus = "OK",
        UserId = _user.Id,
        User = _user,
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
        }
    };

    /// <summary>
    /// A root with a child folder and a file, so a test can tell a full tree
    /// from a root-only mapping.
    /// </summary>
    private FolderEntity CreateTree()
    {
        var root = CreateFolder("root");
        root.ChildFolders.Add(CreateFolder("child"));
        root.Files.Add(CreateFile("clip"));
        return root;
    }

    #region ToSummaryDto

    [Fact]
    public void ToSummaryDto_MapsIdentityDescriptionAndTimestamp()
    {
        var entity = CreateSnapshot();

        var dto = entity.ToSummaryDto();

        dto.Id.Should().Be(entity.Id);
        dto.UserId.Should().Be(_user.Id);
        dto.Description.Should().Be("nightly");
        dto.Timestamp.Should().Be(SnapshotTakenAt);
    }

    [Fact]
    public void ToSummaryDto_KeepsTheTimestampsOffset()
    {
        // A snapshot says when it was taken in the taker's local time. Converting
        // to UTC on the way out would be a silent shift of the displayed hour.
        var dto = CreateSnapshot().ToSummaryDto();

        dto.Timestamp.Offset.Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public void ToSummaryDto_WithoutADescription_LeavesItNull()
    {
        var dto = CreateSnapshot(description: null).ToSummaryDto();

        dto.Description.Should().BeNull();
    }

    [Fact]
    public void ToSummaryDto_MapsEveryLabelInOrder()
    {
        var entity = CreateSnapshot();
        entity.Labels.Add(CreateLabel("first"));
        entity.Labels.Add(CreateLabel("second"));

        var dto = entity.ToSummaryDto();

        dto.Labels.Select(label => label.Name).Should().Equal("first", "second");
        dto.Labels.Should().OnlyContain(label => label.UserId == _user.Id);
        dto.Labels.Should().OnlyContain(label => label.ColorHex == "#64b5f6");
    }

    [Fact]
    public void ToSummaryDto_WithoutLabels_HasAnEmptyListNotNull()
    {
        var dto = CreateSnapshot().ToSummaryDto();

        dto.Labels.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void ToSummaryDto_MapsTheRootFolderWithoutItsChildren()
    {
        // The summary sits inside every file and folder DTO; carrying the whole
        // tree there would make each response quadratic in the snapshot's size.
        var entity = CreateSnapshot();
        entity.RootFolder = CreateTree();

        var dto = entity.ToSummaryDto();

        dto.RootOnlyFolder.Should().NotBeNull();
        dto.RootOnlyFolder!.Id.Should().Be(entity.RootFolder.Id);
        dto.RootOnlyFolder.Name.Should().Be("root");
        dto.RootOnlyFolder.Sha256Hash.Should().Be("hash-of-root");
        dto.RootOnlyFolder.UserId.Should().Be(_user.Id);
        dto.RootOnlyFolder.ChildFolders.Should().BeEmpty();
        dto.RootOnlyFolder.Files.Should().BeEmpty();
    }

    [Fact]
    public void ToSummaryDto_WithoutARootFolder_LeavesItNull()
    {
        var dto = CreateSnapshot().ToSummaryDto();

        dto.RootOnlyFolder.Should().BeNull();
    }

    [Fact]
    public void ToSummaryDto_MapsVolumeInfoAndItsVolume()
    {
        var entity = CreateSnapshot();
        entity.VolumeInfo = CreateVolumeInfo();

        var dto = entity.ToSummaryDto();

        dto.VolumeInfo.Should().NotBeNull();
        dto.VolumeInfo!.Id.Should().Be(entity.VolumeInfo.Id);
        dto.VolumeInfo.UserId.Should().Be(_user.Id);
        dto.VolumeInfo.FreeSpace.Should().Be(250);
        dto.VolumeInfo.DriveStatus.Should().Be("OK");
        dto.VolumeInfo.Volume.Should().NotBeNull();
        dto.VolumeInfo.Volume!.DriveLetter.Should().Be("D:");
        dto.VolumeInfo.Volume.VolumeName.Should().Be("Data");
    }

    [Fact]
    public void ToSummaryDto_WithoutVolumeInfo_LeavesItNull()
    {
        var dto = CreateSnapshot().ToSummaryDto();

        dto.VolumeInfo.Should().BeNull();
    }

    #endregion

    #region ToDto

    [Fact]
    public void ToDto_MapsIdentityDescriptionAndTimestamp()
    {
        var entity = CreateSnapshot();

        var dto = entity.ToDto();

        dto.Id.Should().Be(entity.Id);
        dto.UserId.Should().Be(_user.Id);
        dto.Description.Should().Be("nightly");
        dto.Timestamp.Should().Be(SnapshotTakenAt);
        dto.Timestamp.Offset.Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public void ToDto_MapsEveryLabelInOrder()
    {
        var entity = CreateSnapshot();
        entity.Labels.Add(CreateLabel("first"));
        entity.Labels.Add(CreateLabel("second"));

        var dto = entity.ToDto();

        dto.Labels.Select(label => label.Name).Should().Equal("first", "second");
        dto.Labels.Should().OnlyContain(label => label.UserId == _user.Id);
    }

    [Fact]
    public void ToDto_MapsTheWholeRootFolderTree()
    {
        var entity = CreateSnapshot();
        entity.RootFolder = CreateTree();

        var dto = entity.ToDto();

        dto.RootFolder.Should().NotBeNull();
        dto.RootFolder!.Id.Should().Be(entity.RootFolder.Id);
        dto.RootFolder.Name.Should().Be("root");
        dto.RootFolder.UserId.Should().Be(_user.Id);
        dto.RootFolder.ChildFolders.Should().ContainSingle().Which.Name.Should().Be("child");
        dto.RootFolder.Files.Should().ContainSingle().Which.Name.Should().Be("clip");
    }

    [Fact]
    public void ToDto_WithoutARootFolder_LeavesItNull()
    {
        var dto = CreateSnapshot().ToDto();

        dto.RootFolder.Should().BeNull();
    }

    [Fact]
    public void ToDto_MapsVolumeInfo()
    {
        var entity = CreateSnapshot();
        entity.VolumeInfo = CreateVolumeInfo();

        var dto = entity.ToDto();

        dto.VolumeInfo.Should().NotBeNull();
        dto.VolumeInfo!.FreeSpace.Should().Be(250);
        dto.VolumeInfo.DriveStatus.Should().Be("OK");
        dto.VolumeInfo.Volume!.DriveLetter.Should().Be("D:");
    }

    [Fact]
    public void ToDto_WithoutVolumeInfo_LeavesItNull()
    {
        var dto = CreateSnapshot().ToDto();

        dto.VolumeInfo.Should().BeNull();
    }

    [Fact]
    public void ToDto_AndToSummaryDto_AgreeOnEverythingButTheTree()
    {
        var entity = CreateSnapshot();
        entity.Labels.Add(CreateLabel("shared"));
        entity.RootFolder = CreateTree();
        entity.VolumeInfo = CreateVolumeInfo();

        var full = entity.ToDto();
        var summary = entity.ToSummaryDto();

        summary.Id.Should().Be(full.Id);
        summary.UserId.Should().Be(full.UserId);
        summary.Description.Should().Be(full.Description);
        summary.Timestamp.Should().Be(full.Timestamp);
        summary.Labels.Select(label => label.Id).Should().Equal(full.Labels.Select(label => label.Id));
        summary.VolumeInfo!.Id.Should().Be(full.VolumeInfo!.Id);
        summary.RootOnlyFolder!.Id.Should().Be(full.RootFolder!.Id);
    }

    #endregion

    #region List mappers

    [Fact]
    public void ToSummaryDtoList_MapsEachEntityInOrder()
    {
        IList<SnapshotEntity> entities = [CreateSnapshot("one"), CreateSnapshot("two"), CreateSnapshot("three")];

        var dtos = entities.ToSummaryDtoList();

        dtos.Select(dto => dto.Id).Should().Equal(entities.Select(entity => entity.Id));
        dtos.Select(dto => dto.Description).Should().Equal("one", "two", "three");
        dtos.Should().OnlyContain(dto => dto.UserId == _user.Id);
    }

    [Fact]
    public void ToSummaryDtoList_OnAnEmptyList_ReturnsAnEmptyList()
    {
        IList<SnapshotEntity> entities = [];

        var dtos = entities.ToSummaryDtoList();

        dtos.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void ToDtoList_MapsEachEntityInOrder()
    {
        IList<SnapshotEntity> entities = [CreateSnapshot("one"), CreateSnapshot("two")];
        entities[1].RootFolder = CreateTree();

        var dtos = entities.ToDtoList();

        dtos.Select(dto => dto.Id).Should().Equal(entities.Select(entity => entity.Id));
        dtos[0].RootFolder.Should().BeNull();
        dtos[1].RootFolder!.Files.Should().HaveCount(1);
    }

    [Fact]
    public void ToDtoList_OnAnEmptyList_ReturnsAnEmptyList()
    {
        IList<SnapshotEntity> entities = [];

        var dtos = entities.ToDtoList();

        dtos.Should().NotBeNull().And.BeEmpty();
    }

    #endregion
}
