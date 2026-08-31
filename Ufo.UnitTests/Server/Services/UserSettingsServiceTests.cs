using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Ufo.Abstractions;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.Requests;
using Ufo.Server.Services;

namespace Ufo.UnitTests.Server.Services;

public class UserSettingsServiceTests : BaseTest
{
    private readonly Mock<IUserSettingsRepository> _userSettingsRepositoryMock = new();
    private readonly Mock<ILogger<UserSettingsService>> _loggerMock = new();
    private readonly Ulid _userId = Ulid.NewUlid();

    private UserSettingsService CreateSut() =>
        new(_userSettingsRepositoryMock.Object, _loggerMock.Object);

    private void SetupStoredSettings(UserSettingsEntity? storedSettings) =>
        _userSettingsRepositoryMock
            .Setup(repository => repository.GetUserSettingsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedSettings);

    private void SetupSuccessfulSave() =>
        _userSettingsRepositoryMock
            .Setup(repository => repository.SaveUserSettingsAsync(
                It.IsAny<UserSettingsEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServerResult { Result = Result.Success });

    #region GetUserSettingsAsync

    [Fact]
    public async Task GetUserSettingsAsync_WhenNothingIsStored_FallsBackToTheDefaults()
    {
        SetupStoredSettings(null);

        var settings = await CreateSut().GetUserSettingsAsync(_userId, CancellationToken.None);

        // Never null: the client applies a theme on first load without having to
        // special-case an empty response.
        settings.Should().NotBeNull();
        settings.Theme.Should().Be(UiThemes.Default);
        settings.UserId.Should().Be(_userId);
    }

    [Fact]
    public async Task GetUserSettingsAsync_WhenSettingsAreStored_ReturnsThemMapped()
    {
        var storedSettings = new UserSettingsEntity
        {
            Id = Ulid.NewUlid(),
            Theme = UiThemes.Light,
            UserId = _userId
        };
        SetupStoredSettings(storedSettings);

        var settings = await CreateSut().GetUserSettingsAsync(_userId, CancellationToken.None);

        settings.Id.Should().Be(storedSettings.Id);
        settings.Theme.Should().Be(UiThemes.Light);
        settings.UserId.Should().Be(_userId);
    }

    [Fact]
    public async Task GetUserSettingsAsync_ScopesTheReadToTheCallingUser()
    {
        SetupStoredSettings(null);

        await CreateSut().GetUserSettingsAsync(_userId, CancellationToken.None);

        _userSettingsRepositoryMock.Verify(
            repository => repository.GetUserSettingsAsync(_userId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region SaveUserSettingsAsync

    [Theory]
    [InlineData(UiThemes.Light)]
    [InlineData(UiThemes.Dark)]
    public async Task SaveUserSettingsAsync_WithASupportedTheme_WritesItForTheCallingUser(string theme)
    {
        SetupSuccessfulSave();
        UserSettingsEntity? savedSettings = null;
        _userSettingsRepositoryMock
            .Setup(repository => repository.SaveUserSettingsAsync(
                It.IsAny<UserSettingsEntity>(), It.IsAny<CancellationToken>()))
            .Callback<UserSettingsEntity, CancellationToken>((entity, _) => savedSettings = entity)
            .ReturnsAsync(new ServerResult { Result = Result.Success });

        var result = await CreateSut().SaveUserSettingsAsync(
            new UserSettingsRequest { Theme = theme }, _userId, CancellationToken.None);

        result.Result.Should().Be(Result.Success);
        savedSettings.Should().NotBeNull();
        savedSettings!.Theme.Should().Be(theme);
        // The request carries no user id — it has to come from the JWT, never the body.
        savedSettings.UserId.Should().Be(_userId);
    }

    [Fact]
    public async Task SaveUserSettingsAsync_WithAnUnknownTheme_IsRejectedWithoutWriting()
    {
        var result = await CreateSut().SaveUserSettingsAsync(
            new UserSettingsRequest { Theme = "solarized" }, _userId, CancellationToken.None);

        result.Result.Should().Be(Result.Error);
        result.Priority.Should().Be(ActionPriority.Highest);
        result.Message.Should().Contain("solarized").And.Contain(UiThemes.Light).And.Contain(UiThemes.Dark);

        _userSettingsRepositoryMock.Verify(
            repository => repository.SaveUserSettingsAsync(
                It.IsAny<UserSettingsEntity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SaveUserSettingsAsync_WithNoTheme_IsRejectedRatherThanStoringNull()
    {
        // [Required] stops this at the controller, but the service is reachable
        // on its own and must not write a null theme into a NOT NULL column.
        var result = await CreateSut().SaveUserSettingsAsync(
            new UserSettingsRequest { Theme = null }, _userId, CancellationToken.None);

        result.Result.Should().Be(Result.Error);
        _userSettingsRepositoryMock.Verify(
            repository => repository.SaveUserSettingsAsync(
                It.IsAny<UserSettingsEntity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("DARK")]
    public async Task SaveUserSettingsAsync_WithADifferentlyCasedTheme_IsRejectedNotNormalised(string theme)
    {
        var result = await CreateSut().SaveUserSettingsAsync(
            new UserSettingsRequest { Theme = theme }, _userId, CancellationToken.None);

        result.Result.Should().Be(Result.Error);
        _userSettingsRepositoryMock.Verify(
            repository => repository.SaveUserSettingsAsync(
                It.IsAny<UserSettingsEntity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SaveUserSettingsAsync_WithNoRequest_Throws()
    {
        var save = () => CreateSut().SaveUserSettingsAsync(null!, _userId, CancellationToken.None);

        await save.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SaveUserSettingsAsync_PassesAFailedWriteBackToTheCaller()
    {
        _userSettingsRepositoryMock
            .Setup(repository => repository.SaveUserSettingsAsync(
                It.IsAny<UserSettingsEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServerResult { Result = Result.Error, Message = "write failed" });

        var result = await CreateSut().SaveUserSettingsAsync(
            new UserSettingsRequest { Theme = UiThemes.Light }, _userId, CancellationToken.None);

        result.Result.Should().Be(Result.Error);
        result.Message.Should().Be("write failed");
    }

    #endregion
}
