using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Data;
using Ufo.Abstractions.Database;
using Ufo.Abstractions.Options;

namespace Ufo.Database.Contexts;

public class SqliteConnectionFactory : IDbConnectionFactory, IDisposable
{
    private readonly ILogger<SqliteConnectionFactory> _logger;
    private readonly string _connectionString;
    private readonly SqliteConnection _sqlLiteConnection;

    public SqliteConnectionFactory(IOptionsMonitor<DatabaseOptions>? databaseOptionsMonitor, ILogger<SqliteConnectionFactory>? logger)
    {
        _connectionString = databaseOptionsMonitor?.CurrentValue.ConnectionString ?? throw new ArgumentNullException(nameof(databaseOptionsMonitor));
        _sqlLiteConnection = new SqliteConnection(_connectionString);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation("Connection to SQLite database created successfully.");
    }

    public async Task<SqliteConnection> GetSqliteConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_sqlLiteConnection.State != ConnectionState.Open)
        {
            await _sqlLiteConnection.OpenAsync(cancellationToken);
            await ApplyConnectionPragmasAsync(_sqlLiteConnection, cancellationToken);
        }

        return _sqlLiteConnection;
    }

    /// <summary>
    /// Settings that SQLite keeps per connection rather than in the database file.
    /// </summary>
    /// <remarks>
    /// The journal mode is written into the file once at startup and every
    /// connection inherits it; <c>synchronous</c> is not, and this factory is
    /// scoped, so each request opens its own connection and gets SQLite's
    /// default of FULL - an fsync of the write-ahead log on every commit. With
    /// WAL that fsync buys nothing the application needs: NORMAL still survives
    /// a crash with the database intact, and only the last transactions before
    /// a power loss can be lost, which for a snapshot index is a re-run. It is
    /// the difference between a single-row commit costing about 9 ms and about
    /// 0.04 ms on a local disk, paid on every token rotation, flag, rating and
    /// tag. The statement costs nothing on a connection the pool hands back
    /// already set.
    /// </remarks>
    private static async Task ApplyConnectionPragmasAsync(SqliteConnection sqliteConnection, CancellationToken cancellationToken)
    {
        await using var command = sqliteConnection.CreateCommand();
        command.CommandText = "PRAGMA synchronous = NORMAL;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public void Dispose()
    {
        _sqlLiteConnection?.Dispose();
    }
}
