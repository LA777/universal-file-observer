using Microsoft.Data.Sqlite;

namespace Ufo.Abstractions.Database
{
    public interface IDbConnectionFactory
    {
        public Task<SqliteConnection> GetSqliteConnectionAsync(CancellationToken cancellationToken = default);
    }
}
