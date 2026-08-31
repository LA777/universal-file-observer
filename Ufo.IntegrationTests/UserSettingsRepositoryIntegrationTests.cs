using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Moq;
using Ufo.Abstractions;
using Ufo.Abstractions.Database;
using Ufo.Abstractions.Database.Entities;
using Ufo.Database.Contexts;
using Ufo.Database.Repositories;

namespace Ufo.IntegrationTests;

public class UserSettingsRepositoryIntegrationTests : IAsyncLifetime
{
    private readonly UserEntity testUser = new() { Id = Ulid.NewUlid(), Name = "TestUser" };
    private readonly UserEntity otherUser = new() { Id = Ulid.NewUlid(), Name = "OtherUser" };
    private Mock<ILogger<UserSettingsRepository>> _loggerMock = null!;
    private Mock<IDbConnectionFactory> _dbConnectionFactoryMock = null!;
    private SqliteConnection _sqLiteConnection = null!;
    private UserSettingsRepository _userSettingsRepository = null!;

    #region Database Initialization and Cleanup

    public async Task InitializeAsync()
    {
        var dbName = $"testdb-{Guid.NewGuid()}";
        var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared;Foreign Keys=True";

        _dbConnectionFactoryMock = new Mock<IDbConnectionFactory>();
        _sqLiteConnection = new SqliteConnection(connectionString);
        await _sqLiteConnection.OpenAsync();
        _dbConnectionFactoryMock.Setup(f => f.GetSqliteConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _sqLiteConnection);

        _loggerMock = new Mock<ILogger<UserSettingsRepository>>();

        await DapperDataContext.InitiateDatabaseAsync(_sqLiteConnection);
        _userSettingsRepository = new UserSettingsRepository(_dbConnectionFactoryMock.Object, _loggerMock.Object);

        foreach (var user in new[] { testUser, otherUser })
        {
            await _sqLiteConnection.ExecuteAsync(
                "INSERT INTO Users (Id, Name, PasswordHash) VALUES (@Id, @Name, @PasswordHash)",
                new { user.Id, user.Name, PasswordHash = "hash" });
        }
    }

    public async Task DisposeAsync()
    {
        if (_sqLiteConnection is not null)
        {
            await _sqLiteConnection.DisposeAsync();
        }
    }

    #endregion

    #region GetUserSettingsAsync Tests

    [Fact]
    public async Task GetUserSettingsAsync_WhenNothingSaved_ReturnsNull()
    {
        var settings = await _userSettingsRepository.GetUserSettingsAsync(testUser.Id);

        settings.Should().BeNull();
    }

    [Fact]
    public async Task GetUserSettingsAsync_AfterSave_ReturnsSavedTheme()
    {
        await _userSettingsRepository.SaveUserSettingsAsync(new UserSettingsEntity
        {
            UserId = testUser.Id,
            Theme = UiThemes.Light
        });

        var settings = await _userSettingsRepository.GetUserSettingsAsync(testUser.Id);

        settings.Should().NotBeNull();
        settings!.Theme.Should().Be(UiThemes.Light);
        settings.UserId.Should().Be(testUser.Id);
    }

    [Fact]
    public async Task GetUserSettingsAsync_DoesNotReturnAnotherUsersSettings()
    {
        await _userSettingsRepository.SaveUserSettingsAsync(new UserSettingsEntity
        {
            UserId = otherUser.Id,
            Theme = UiThemes.Light
        });

        var settings = await _userSettingsRepository.GetUserSettingsAsync(testUser.Id);

        settings.Should().BeNull();
    }

    [Fact]
    public async Task GetUserSettingsAsync_WhenTheReadFails_ThrowsRatherThanReportingNoSettings()
    {
        await _sqLiteConnection.ExecuteAsync("DROP TABLE UserSettings;");

        var read = async () => await _userSettingsRepository.GetUserSettingsAsync(testUser.Id);

        // Swallowing this into a null would be indistinguishable from "nothing
        // saved yet", so the service would quietly serve the default theme and
        // the user would watch their choice appear to reset itself.
        await read.Should().ThrowAsync<SqliteException>();
    }

    #endregion

    #region SaveUserSettingsAsync Tests

    [Fact]
    public async Task SaveUserSettingsAsync_OnFirstSave_CreatesTheRow()
    {
        var result = await _userSettingsRepository.SaveUserSettingsAsync(new UserSettingsEntity
        {
            UserId = testUser.Id,
            Theme = UiThemes.Light
        });

        result.Result.Should().Be(Result.Success);

        var rowCount = await _sqLiteConnection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM UserSettings WHERE UserId = @UserId",
            new { UserId = testUser.Id.ToString() });
        rowCount.Should().Be(1);
    }

    [Fact]
    public async Task SaveUserSettingsAsync_WhenSavedTwice_UpdatesInPlaceInsteadOfAddingARow()
    {
        await _userSettingsRepository.SaveUserSettingsAsync(new UserSettingsEntity
        {
            UserId = testUser.Id,
            Theme = UiThemes.Light
        });

        var result = await _userSettingsRepository.SaveUserSettingsAsync(new UserSettingsEntity
        {
            UserId = testUser.Id,
            Theme = UiThemes.Dark
        });

        result.Result.Should().Be(Result.Success);

        var rowCount = await _sqLiteConnection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM UserSettings WHERE UserId = @UserId",
            new { UserId = testUser.Id.ToString() });
        rowCount.Should().Be(1);

        var settings = await _userSettingsRepository.GetUserSettingsAsync(testUser.Id);
        settings!.Theme.Should().Be(UiThemes.Dark);
    }

    [Fact]
    public async Task SaveUserSettingsAsync_WithNoSettings_Throws()
    {
        var save = async () => await _userSettingsRepository.SaveUserSettingsAsync(null!);

        await save.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SaveUserSettingsAsync_ForAUserThatDoesNotExist_IsRejectedByTheForeignKey()
    {
        var save = async () => await _userSettingsRepository.SaveUserSettingsAsync(new UserSettingsEntity
        {
            UserId = Ulid.NewUlid(),
            Theme = UiThemes.Light
        });

        await save.Should().ThrowAsync<SqliteException>();
    }

    #endregion

    #region Schema behaviour

    [Fact]
    public async Task DeletingTheUser_CascadesToTheirSettings()
    {
        await _userSettingsRepository.SaveUserSettingsAsync(new UserSettingsEntity
        {
            UserId = testUser.Id,
            Theme = UiThemes.Light
        });

        await _sqLiteConnection.ExecuteAsync(
            "DELETE FROM Users WHERE Id = @Id",
            new { Id = testUser.Id.ToString() });

        // ON DELETE CASCADE in the UserSettings DDL — without it the row would
        // outlive its user and block re-registering the same id.
        var settings = await _userSettingsRepository.GetUserSettingsAsync(testUser.Id);
        settings.Should().BeNull();
    }

    [Fact]
    public async Task SavedSettingsSurviveTheSchemaScriptRunningAgain()
    {
        await _userSettingsRepository.SaveUserSettingsAsync(new UserSettingsEntity
        {
            UserId = testUser.Id,
            Theme = UiThemes.Light
        });

        // InitiateDatabaseAsync runs on every startup, so the CREATE TABLE IF NOT
        // EXISTS has to stay non-destructive.
        await DapperDataContext.InitiateDatabaseAsync(_sqLiteConnection);

        var settings = await _userSettingsRepository.GetUserSettingsAsync(testUser.Id);
        settings!.Theme.Should().Be(UiThemes.Light);
    }

    #endregion

    #region Per-user isolation

    [Fact]
    public async Task SaveUserSettingsAsync_KeepsEachUsersSettingsSeparate()
    {
        await _userSettingsRepository.SaveUserSettingsAsync(new UserSettingsEntity
        {
            UserId = testUser.Id,
            Theme = UiThemes.Light
        });
        await _userSettingsRepository.SaveUserSettingsAsync(new UserSettingsEntity
        {
            UserId = otherUser.Id,
            Theme = UiThemes.Dark
        });

        var testUserSettings = await _userSettingsRepository.GetUserSettingsAsync(testUser.Id);
        var otherUserSettings = await _userSettingsRepository.GetUserSettingsAsync(otherUser.Id);

        testUserSettings!.Theme.Should().Be(UiThemes.Light);
        otherUserSettings!.Theme.Should().Be(UiThemes.Dark);
    }

    #endregion
}
