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

public class ServerSettingsRepositoryIntegrationTests : IAsyncLifetime
{
    private readonly UserEntity _adminUser = new() { Id = Ulid.NewUlid(), Name = "AdminUser", IsAdmin = true };
    private Mock<ILogger<ServerSettingsRepository>> _loggerMock = null!;
    private Mock<IDbConnectionFactory> _dbConnectionFactoryMock = null!;
    private SqliteConnection _sqLiteConnection = null!;
    private ServerSettingsRepository _serverSettingsRepository = null!;

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

        _loggerMock = new Mock<ILogger<ServerSettingsRepository>>();

        await DapperDataContext.InitiateDatabaseAsync(_sqLiteConnection);
        _serverSettingsRepository = new ServerSettingsRepository(_dbConnectionFactoryMock.Object, _loggerMock.Object);

        await _sqLiteConnection.ExecuteAsync(
            "INSERT INTO Users (Id, Name, PasswordHash, IsAdmin) VALUES (@Id, @Name, @PasswordHash, @IsAdmin)",
            new { _adminUser.Id, _adminUser.Name, PasswordHash = "hash", _adminUser.IsAdmin });
    }

    public async Task DisposeAsync()
    {
        if (_sqLiteConnection is not null)
        {
            await _sqLiteConnection.DisposeAsync();
        }
    }

    private static ServerSettingsEntity ACertificateRow(byte[]? blob = null, string source = CertificateSources.SelfSigned) =>
        new()
        {
            CertificatePfx = blob ?? [1, 2, 3, 4, 5],
            CertificateThumbprint = "AA11BB22CC33",
            CertificateSubject = "CN=ufo-host",
            CertificateNotBefore = DateTimeOffset.UtcNow.ToString("o"),
            CertificateNotAfter = DateTimeOffset.UtcNow.AddYears(2).ToString("o"),
            CertificateSource = source,
            UpdatedAt = DateTimeOffset.UtcNow.ToString("o")
        };

    #endregion

    [Fact]
    public async Task GetServerSettingsAsync_OnAFreshDatabase_ReturnsNull()
    {
        var settings = await _serverSettingsRepository.GetServerSettingsAsync();

        settings.Should().BeNull();
    }

    [Fact]
    public async Task SaveCertificateAsync_ThenGet_RoundTripsEveryField()
    {
        var entity = ACertificateRow();
        entity.UpdatedByUserId = _adminUser.Id;

        var result = await _serverSettingsRepository.SaveCertificateAsync(entity);
        result.Result.Should().Be(Result.Success);

        var stored = await _serverSettingsRepository.GetServerSettingsAsync();

        stored.Should().NotBeNull();
        stored!.CertificatePfx.Should().Equal(entity.CertificatePfx);
        stored.CertificateThumbprint.Should().Be(entity.CertificateThumbprint);
        stored.CertificateSubject.Should().Be(entity.CertificateSubject);
        stored.CertificateNotAfter.Should().Be(entity.CertificateNotAfter);
        stored.CertificateSource.Should().Be(CertificateSources.SelfSigned);
        stored.UpdatedByUserId.Should().Be(_adminUser.Id);
    }

    [Fact]
    public async Task SaveCertificateAsync_StoresTheBlobAsGivenWithoutReencoding()
    {
        // The blob is already sealed by the certificate protector; the repository
        // must not touch it, or it would not decrypt on the way back.
        var blob = new byte[] { 0xFF, 0x00, 0x10, 0x80, 0x7F, 0xAB };

        await _serverSettingsRepository.SaveCertificateAsync(ACertificateRow(blob));
        var stored = await _serverSettingsRepository.GetServerSettingsAsync();

        stored!.CertificatePfx.Should().Equal(blob);
    }

    [Fact]
    public async Task SaveCertificateAsync_CalledTwice_UpdatesInPlaceRatherThanAddingARow()
    {
        await _serverSettingsRepository.SaveCertificateAsync(ACertificateRow([1, 1, 1]));

        var replacement = ACertificateRow([2, 2, 2], CertificateSources.UserSupplied);
        replacement.CertificateThumbprint = "DD44EE55";
        replacement.UpdatedByUserId = _adminUser.Id;

        var result = await _serverSettingsRepository.SaveCertificateAsync(replacement);
        result.Result.Should().Be(Result.Success);

        // The upsert is what makes the table a singleton in practice; the
        // SingletonGuard UNIQUE is what makes it one by construction.
        var rowCount = await _sqLiteConnection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ServerSettings;");
        rowCount.Should().Be(1);

        var stored = await _serverSettingsRepository.GetServerSettingsAsync();
        stored!.CertificatePfx.Should().Equal([2, 2, 2]);
        stored.CertificateThumbprint.Should().Be("DD44EE55");
        stored.CertificateSource.Should().Be(CertificateSources.UserSupplied);
    }

    [Fact]
    public async Task ServerSettings_RefusesASecondRow()
    {
        await _serverSettingsRepository.SaveCertificateAsync(ACertificateRow());

        var insertASecondRow = async () => await _sqLiteConnection.ExecuteAsync(
            "INSERT INTO ServerSettings (Id, SingletonGuard) VALUES (@Id, 1);",
            new { Id = Ulid.NewUlid().ToString() });

        // A TLS certificate belongs to the listener, so a second row would be a
        // question with no answer. The schema refuses rather than tolerating it.
        await insertASecondRow.Should().ThrowAsync<SqliteException>();
    }

    [Fact]
    public async Task ServerSettings_RefusesAGuardValueOtherThanOne()
    {
        var insertWithAnotherGuard = async () => await _sqLiteConnection.ExecuteAsync(
            "INSERT INTO ServerSettings (Id, SingletonGuard) VALUES (@Id, 2);",
            new { Id = Ulid.NewUlid().ToString() });

        // Without the CHECK, a second row could simply pick a different guard
        // value and slip past the UNIQUE.
        await insertWithAnotherGuard.Should().ThrowAsync<SqliteException>();
    }

    [Fact]
    public async Task ServerSettings_SurvivesDeletingTheAdministratorWhoSetIt()
    {
        var entity = ACertificateRow();
        entity.UpdatedByUserId = _adminUser.Id;
        await _serverSettingsRepository.SaveCertificateAsync(entity);

        await _sqLiteConnection.ExecuteAsync("DELETE FROM Users WHERE Id = @Id;", new { _adminUser.Id });

        var stored = await _serverSettingsRepository.GetServerSettingsAsync();

        // ON DELETE SET NULL rather than CASCADE: removing the administrator who
        // uploaded a certificate must not take the server's TLS down with them.
        stored.Should().NotBeNull();
        stored!.UpdatedByUserId.Should().BeNull();
        stored.CertificatePfx.Should().NotBeNull();
    }

    [Fact]
    public async Task Users_HaveAnIsAdminColumnDefaultingToFalse()
    {
        var plainUserId = Ulid.NewUlid();
        await _sqLiteConnection.ExecuteAsync(
            "INSERT INTO Users (Id, Name, PasswordHash) VALUES (@Id, 'PlainUser', 'hash');",
            new { Id = plainUserId.ToString() });

        var isAdmin = await _sqLiteConnection.ExecuteScalarAsync<long>(
            "SELECT IsAdmin FROM Users WHERE Id = @Id;", new { Id = plainUserId.ToString() });

        // Administrator is opt-in: only the first registrant gets it.
        isAdmin.Should().Be(0);
    }

    [Fact]
    public async Task InitiateDatabaseAsync_AddsIsAdminToADatabaseCreatedWithoutIt()
    {
        // Stands in for a database created before the column existed:
        // CREATE TABLE IF NOT EXISTS is a no-op against it, so only the guarded
        // ALTER in DapperDataContext can bring it up to date.
        var legacyDbName = $"legacydb-{Guid.NewGuid()}";
        await using var legacyConnection = new SqliteConnection(
            $"Data Source={legacyDbName};Mode=Memory;Cache=Shared;Foreign Keys=True");
        await legacyConnection.OpenAsync();

        await legacyConnection.ExecuteAsync(
            """
            CREATE TABLE Users (
                Id           TEXT NOT NULL UNIQUE PRIMARY KEY,
                Name         TEXT NOT NULL UNIQUE,
                PasswordHash TEXT NOT NULL,
                CreatedAt    TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """);
        await legacyConnection.ExecuteAsync(
            "INSERT INTO Users (Id, Name, PasswordHash) VALUES ('01ABC', 'Existing', 'hash');");

        await DapperDataContext.InitiateDatabaseAsync(legacyConnection);

        var columns = await legacyConnection.QueryAsync<string>("SELECT name FROM pragma_table_info('Users');");
        columns.Should().Contain("IsAdmin");

        // The longest-standing account is promoted, because a database that
        // already had users would otherwise end up with no administrator at all
        // and nobody able to reach the server-scoped settings.
        var isAdmin = await legacyConnection.ExecuteScalarAsync<long>(
            "SELECT IsAdmin FROM Users WHERE Id = '01ABC';");
        isAdmin.Should().Be(1);
    }

    [Fact]
    public async Task InitiateDatabaseAsync_PromotesOnlyTheEarliestOfSeveralExistingUsers()
    {
        var legacyDbName = $"legacydb-{Guid.NewGuid()}";
        await using var legacyConnection = new SqliteConnection(
            $"Data Source={legacyDbName};Mode=Memory;Cache=Shared;Foreign Keys=True");
        await legacyConnection.OpenAsync();

        await legacyConnection.ExecuteAsync(
            """
            CREATE TABLE Users (
                Id           TEXT NOT NULL UNIQUE PRIMARY KEY,
                Name         TEXT NOT NULL UNIQUE,
                PasswordHash TEXT NOT NULL,
                CreatedAt    TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """);
        await legacyConnection.ExecuteAsync(
            """
            INSERT INTO Users (Id, Name, PasswordHash, CreatedAt) VALUES
                ('02NEW', 'Newer',  'hash', '2026-05-01 10:00:00'),
                ('01OLD', 'Oldest', 'hash', '2026-01-01 10:00:00'),
                ('03NEW', 'Newest', 'hash', '2026-09-01 10:00:00');
            """);

        await DapperDataContext.InitiateDatabaseAsync(legacyConnection);

        var administrators = (await legacyConnection.QueryAsync<string>(
            "SELECT Name FROM Users WHERE IsAdmin = 1;")).ToList();

        administrators.Should().ContainSingle().Which.Should().Be("Oldest");
    }

    [Fact]
    public async Task InitiateDatabaseAsync_DoesNotRePromoteOnASubsequentRun()
    {
        // The seeded administrator is demoted to stand in for an operator who
        // deliberately took the privilege away. Startup must not hand it back.
        await _sqLiteConnection.ExecuteAsync("UPDATE Users SET IsAdmin = 0;");

        await DapperDataContext.InitiateDatabaseAsync(_sqLiteConnection);

        var administratorCount = await _sqLiteConnection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM Users WHERE IsAdmin = 1;");

        // The promotion runs only in the branch that adds the column, which has
        // already happened for this database.
        administratorCount.Should().Be(0);
    }

    [Fact]
    public async Task InitiateDatabaseAsync_IsIdempotentAcrossRepeatedRuns()
    {
        // Startup runs this every time; a second ALTER TABLE ADD COLUMN would be
        // an error rather than a no-op if the guard were missing.
        await DapperDataContext.InitiateDatabaseAsync(_sqLiteConnection);
        await DapperDataContext.InitiateDatabaseAsync(_sqLiteConnection);

        var columns = (await _sqLiteConnection.QueryAsync<string>(
            "SELECT name FROM pragma_table_info('Users');")).ToList();

        columns.Should().Contain("IsAdmin");
        columns.Count(column => column == "IsAdmin").Should().Be(1);
    }
}
