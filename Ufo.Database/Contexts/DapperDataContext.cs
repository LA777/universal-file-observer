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

        // Migrate existing schema to add missing columns
        await MigrateSchemaAsync(sqLiteConnection);
    }

    private static async Task MigrateSchemaAsync(SqliteConnection sqLiteConnection)
    {
        try
        {
            // Check if Snapshots table exists and has UserId column
            const string checkSnapshotsUserIdColumn = "PRAGMA table_info(Snapshots);";
            var snapshotsColumns = await sqLiteConnection.QueryAsync<dynamic>(checkSnapshotsUserIdColumn);
            var snapshotsColumnList = snapshotsColumns.ToList();

            if (snapshotsColumnList.Count > 0)
            {
                var hasUserIdColumn = snapshotsColumnList.Any(c => c.name == "UserId");

                if (!hasUserIdColumn)
                {
                    // Add UserId column to Snapshots table if it's missing
                    const string addUserIdToSnapshots = @"ALTER TABLE Snapshots ADD COLUMN UserId TEXT NOT NULL DEFAULT '';";
                    await sqLiteConnection.ExecuteAsync(addUserIdToSnapshots);
                }
            }
        }
        catch
        {
            // Migration errors should not prevent application startup
            // as the schema might already be correct or will fail later with a more specific error
        }
    }
}
