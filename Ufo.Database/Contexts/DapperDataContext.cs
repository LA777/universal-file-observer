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

        await sqLiteConnection.ExecuteAsync(SqlScripts.CreateDatabaseSqlScript);
    }
}
