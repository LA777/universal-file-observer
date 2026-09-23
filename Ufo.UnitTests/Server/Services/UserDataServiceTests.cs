using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Ufo.Abstractions;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Server.Services;

namespace Ufo.UnitTests.Server.Services;

public class UserDataServiceTests : BaseTest
{
    private readonly Mock<IUserDataRepository> _userDataRepositoryMock = new();
    private readonly Mock<ILogger<UserDataService>> _loggerMock = new();
    private readonly Ulid _userId = Ulid.NewUlid();

    private UserDataService CreateSut() => new(_userDataRepositoryMock.Object, _loggerMock.Object);

    [Fact]
    public void Constructor_WithoutARepository_Throws()
    {
        var construct = () => new UserDataService(null!, _loggerMock.Object);

        construct.Should().Throw<ArgumentNullException>();
    }

    #region DeleteSnapshotsAsync

    [Fact]
    public async Task DeleteSnapshotsAsync_ScopesTheDeleteToTheCallingUserAndReportsTheCount()
    {
        _userDataRepositoryMock
            .Setup(repository => repository.DeleteSnapshotsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var result = await CreateSut().DeleteSnapshotsAsync(_userId, CancellationToken.None);

        result.Result.Should().Be(Result.Success);
        result.Message.Should().Contain("3 snapshot");
        _userDataRepositoryMock.Verify(
            repository => repository.DeleteSnapshotsAsync(_userId, It.IsAny<CancellationToken>()), Times.Once);
        _userDataRepositoryMock.Verify(
            repository => repository.DeleteFileSystemDataAsync(It.IsAny<Ulid>(), It.IsAny<CancellationToken>()), Times.Never);
        _userDataRepositoryMock.Verify(
            repository => repository.DeleteSettingsAsync(It.IsAny<Ulid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteSnapshotsAsync_WhenNothingWasThere_IsStillSuccess()
    {
        _userDataRepositoryMock
            .Setup(repository => repository.DeleteSnapshotsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var result = await CreateSut().DeleteSnapshotsAsync(_userId, CancellationToken.None);

        // The state asked for is the state reached; there is nothing to report as a failure.
        result.Result.Should().Be(Result.Success);
        result.Message.Should().Contain("0 snapshot");
    }

    #endregion

    #region DeleteFileSystemDataAsync

    [Fact]
    public async Task DeleteFileSystemDataAsync_ScopesTheDeleteToTheCallingUserAndReportsTheCount()
    {
        _userDataRepositoryMock
            .Setup(repository => repository.DeleteFileSystemDataAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        var result = await CreateSut().DeleteFileSystemDataAsync(_userId, CancellationToken.None);

        result.Result.Should().Be(Result.Success);
        result.Message.Should().Contain("5");
        _userDataRepositoryMock.Verify(
            repository => repository.DeleteFileSystemDataAsync(_userId, It.IsAny<CancellationToken>()), Times.Once);
        _userDataRepositoryMock.Verify(
            repository => repository.DeleteSnapshotsAsync(It.IsAny<Ulid>(), It.IsAny<CancellationToken>()), Times.Never);
        _userDataRepositoryMock.Verify(
            repository => repository.DeleteSettingsAsync(It.IsAny<Ulid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region DeleteSettingsAsync

    [Fact]
    public async Task DeleteSettingsAsync_ScopesTheDeleteToTheCallingUserAndReportsTheCount()
    {
        _userDataRepositoryMock
            .Setup(repository => repository.DeleteSettingsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var result = await CreateSut().DeleteSettingsAsync(_userId, CancellationToken.None);

        result.Result.Should().Be(Result.Success);
        result.Message.Should().Contain("2 setting");
        _userDataRepositoryMock.Verify(
            repository => repository.DeleteSettingsAsync(_userId, It.IsAny<CancellationToken>()), Times.Once);
        _userDataRepositoryMock.Verify(
            repository => repository.DeleteSnapshotsAsync(It.IsAny<Ulid>(), It.IsAny<CancellationToken>()), Times.Never);
        _userDataRepositoryMock.Verify(
            repository => repository.DeleteFileSystemDataAsync(It.IsAny<Ulid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteSettingsAsync_PassesARepositoryFailureOn()
    {
        _userDataRepositoryMock
            .Setup(repository => repository.DeleteSettingsAsync(_userId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database locked"));

        var act = () => CreateSut().DeleteSettingsAsync(_userId, CancellationToken.None);

        // Not swallowed into a success: the page must be told nothing was deleted.
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    #endregion

    #region DeleteAllAsync

    [Fact]
    public async Task DeleteAllAsync_ScopesTheDeleteToTheCallingUserAndReportsEveryCount()
    {
        _userDataRepositoryMock
            .Setup(repository => repository.DeleteAllAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserDataDeletionCounts(Snapshots: 2, FileSystemItems: 5, Labels: 3, Settings: 4));

        var result = await CreateSut().DeleteAllAsync(_userId, CancellationToken.None);

        result.Result.Should().Be(Result.Success);
        result.Message.Should().Contain("2 snapshot")
            .And.Contain("5 flag")
            .And.Contain("3 label")
            .And.Contain("4 setting");
        _userDataRepositoryMock.Verify(
            repository => repository.DeleteAllAsync(_userId, It.IsAny<CancellationToken>()), Times.Once);
        // One transaction on the repository, not the three single-kind deletes run in turn.
        _userDataRepositoryMock.Verify(
            repository => repository.DeleteSnapshotsAsync(It.IsAny<Ulid>(), It.IsAny<CancellationToken>()), Times.Never);
        _userDataRepositoryMock.Verify(
            repository => repository.DeleteFileSystemDataAsync(It.IsAny<Ulid>(), It.IsAny<CancellationToken>()), Times.Never);
        _userDataRepositoryMock.Verify(
            repository => repository.DeleteSettingsAsync(It.IsAny<Ulid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAllAsync_PassesARepositoryFailureOn()
    {
        _userDataRepositoryMock
            .Setup(repository => repository.DeleteAllAsync(_userId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database locked"));

        var act = () => CreateSut().DeleteAllAsync(_userId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    #endregion
}
