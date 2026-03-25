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
        }

        return _sqlLiteConnection;
    }

    public void Dispose()
    {
        _sqlLiteConnection?.Dispose();
    }
}
