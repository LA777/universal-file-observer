using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.DataProviders;
using Ufo.Abstractions.Options;
using Ufo.DataProviders;

namespace Ufo.UnitTests.Server.DataProviders;

public class PosixSystemInfoProviderTests : BaseTest
{
    private readonly Mock<ILogger<PosixSystemInfoProvider>> _loggerMock;
    private readonly UfoHostOptions _hostOptions;
    private readonly string _dataDirectory;
    private readonly PosixSystemInfoProvider _sut;

    public PosixSystemInfoProviderTests()
    {
        _loggerMock = new Mock<ILogger<PosixSystemInfoProvider>>();
        _dataDirectory = Path.Combine(Path.GetTempPath(), $"ufo-posix-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataDirectory);

        _hostOptions = new UfoHostOptions { DataDirectory = _dataDirectory };
        _sut = new PosixSystemInfoProvider(_loggerMock.Object, Options.Create(_hostOptions));
    }

    private static UserEntity CreateUser() => new() { Id = Ulid.NewUlid(), Name = "test-user" };

    [Fact]
    public void GetSystemInformation_BuildsTheFullEntityGraph()
    {
        var user = CreateUser();

        var snapshot = _sut.GetSystemInformation(Path.GetTempPath(), user);

        snapshot.Should().NotBeNull();
        snapshot.UserId.Should().Be(user.Id);
        snapshot.VolumeInfo.Should().NotBeNull();
        snapshot.VolumeInfo!.Volume.Should().NotBeNull();
        snapshot.VolumeInfo.Volume!.StorageDrive.Should().NotBeNull();
        snapshot.VolumeInfo.SnapshotId.Should().Be(snapshot.Id);
    }

    [Fact]
    public void GetSystemInformation_ResolvesTheMountContainingThePath()
    {
        var user = CreateUser();
        var temporaryPath = Path.GetTempPath();
        var expectedMount = DriveInfo.GetDrives()
            .Where(drive => Path.GetFullPath(temporaryPath)
                .StartsWith(drive.RootDirectory.FullName, StringComparison.Ordinal))
            .OrderByDescending(drive => drive.RootDirectory.FullName.Length)
            .First();

        var snapshot = _sut.GetSystemInformation(temporaryPath, user);

        // The previous implementation matched drives on the first character of the
        // path, which selects an arbitrary mount on POSIX because every mount
        // point begins with '/'.
        snapshot.VolumeInfo!.Volume!.DriveLetter.Should().Be(expectedMount.Name);
    }

    [Fact]
    public void GetSystemInformation_DoesNotThrow_WhenNoMountMatches()
    {
        var user = CreateUser();

        var act = () => _sut.GetSystemInformation("this-path-does-not-exist", user);

        act.Should().NotThrow();
    }

    [Fact]
    public void GetSystemInformation_UsesTheConfiguredMachineId()
    {
        _hostOptions.MachineId = "host-machine-id";
        var user = CreateUser();

        var snapshot = _sut.GetSystemInformation(Path.GetTempPath(), user);

        // The PC is reachable through the storage drive the volume hangs off.
        var pc = snapshot.VolumeInfo!.Volume!.StorageDrive!.Pcs.Single();
        pc.MachineId.Should().Be("host-machine-id");
    }

    [Fact]
    public void GetSystemInformation_PersistsAGeneratedMachineId_WhenNoneCanBeDetected()
    {
        // Only meaningful where the OS supplies no machine id of its own; on
        // Linux /etc/machine-id normally wins and no file is written.
        var user = CreateUser();

        _sut.GetSystemInformation(Path.GetTempPath(), user);

        var persistedMachineIdPath = Path.Combine(_dataDirectory, "machine-id");
        if (File.Exists(persistedMachineIdPath))
        {
            File.ReadAllText(persistedMachineIdPath).Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void GetSystemInformation_NeverReturnsNullIdentifiers()
    {
        var user = CreateUser();

        var snapshot = _sut.GetSystemInformation(Path.GetTempPath(), user);

        var pc = snapshot.VolumeInfo!.Volume!.StorageDrive!.Pcs.Single();
        pc.HardwareUuid.Should().NotBeNullOrWhiteSpace();
        pc.HardwareSerialNumber.Should().NotBeNullOrWhiteSpace();
        pc.MachineId.Should().NotBeNullOrWhiteSpace();
        pc.Name.Should().Be(Environment.MachineName);
    }

    [Fact]
    public void GetSystemInformation_Throws_ForAnEmptyPath()
    {
        var user = CreateUser();

        var act = () => _sut.GetSystemInformation(string.Empty, user);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DeviceIdentifiers_Empty_IsAllUnknown()
    {
        DeviceIdentifiers.Empty.HardwareUuid.Should().Be(DeviceIdentifiers.Unknown);
        DeviceIdentifiers.Empty.HardwareSerialNumber.Should().Be(DeviceIdentifiers.Unknown);
        DeviceIdentifiers.Empty.MachineId.Should().Be(DeviceIdentifiers.Unknown);
    }
}
