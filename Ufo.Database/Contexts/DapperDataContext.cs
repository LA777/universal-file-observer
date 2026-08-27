using Dapper;
using Microsoft.Data.Sqlite;
using Ufo.Database.Handlers;

namespace Ufo.Database.Contexts;

public static class DapperDataContext
{
    public static async Task InitiateDatabaseAsync(SqliteConnection sqLiteConnection)
    {
        SqlMapper.AddTypeHandler(new SqlUlidTypeHandler());
        SqlMapper.AddTypeHandler(new SqlNullableUlidTypeHandler());
        SqlMapper.RemoveTypeMap(typeof(Ulid));
        SqlMapper.RemoveTypeMap(typeof(Ulid?));
        SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());

        // WAL lets readers proceed while a snapshot is being written and makes bulk
        // inserts much faster; NORMAL sync is safe with WAL and skips an fsync per
        // transaction. journal_mode is persisted in the db file; synchronous applies
        // to this connection, which the factory keeps for the app's lifetime.
        // (In-memory test databases ignore WAL and keep their own journal mode.)
        await sqLiteConnection.ExecuteAsync("PRAGMA journal_mode = WAL;");
        await sqLiteConnection.ExecuteAsync("PRAGMA synchronous = NORMAL;");

        await sqLiteConnection.ExecuteAsync(SqlScripts.CreateDatabaseSqlScript);
    }
}
