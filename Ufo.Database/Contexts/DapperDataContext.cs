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

        // Columns added to tables that already exist in deployed databases. The
        // schema is idempotent DDL with no migration history, and
        // CREATE TABLE IF NOT EXISTS is a no-op against an existing table - so a
        // column introduced after a database was first created has to be added
        // here or it will only ever appear on fresh installations.
        if (await EnsureColumnAsync(sqLiteConnection, "Users", "IsAdmin", "INTEGER NOT NULL DEFAULT 0"))
        {
            await PromoteEarliestUserToAdministratorAsync(sqLiteConnection);
        }
    }

    /// <summary>
    /// Makes the longest-standing account the administrator, for a database whose
    /// users all pre-date the <c>IsAdmin</c> column.
    /// </summary>
    /// <remarks>
    /// New installations get their administrator at sign-up, where the first
    /// account to register is flagged. An installation that already had users
    /// when the column arrived would otherwise have none at all - every row takes
    /// the column default of 0 - which locks everybody out of the server-scoped
    /// settings with no way back through the UI.
    /// <para>
    /// Deliberately run only in the branch that just added the column, so it is a
    /// one-time migration step. Running it on every startup would undo a
    /// deliberate demotion the next time the host restarted.
    /// </para>
    /// </remarks>
    private static async Task PromoteEarliestUserToAdministratorAsync(SqliteConnection sqLiteConnection)
    {
        var promoted = await sqLiteConnection.ExecuteAsync(
            """
            UPDATE Users SET IsAdmin = 1
            WHERE Id = (SELECT Id FROM Users ORDER BY CreatedAt, Id LIMIT 1)
              AND NOT EXISTS (SELECT 1 FROM Users WHERE IsAdmin = 1);
            """);

        if (promoted > 0)
        {
            // Worth a line in the log: it is a privilege change nobody asked for
            // interactively, and the operator should be able to see that it
            // happened and to whom.
            Console.WriteLine(
                "Granted administrator to the longest-standing account: no account had it after adding Users.IsAdmin.");
        }
    }

    /// <summary>
    /// Adds a column when the table does not already have it.
    /// </summary>
    /// <remarks>
    /// SQLite has no <c>ADD COLUMN IF NOT EXISTS</c>, and re-running a plain
    /// <c>ALTER TABLE ADD COLUMN</c> is an error rather than a no-op, so the
    /// column list is inspected first. This keeps startup idempotent, which is
    /// the property the rest of the schema script relies on.
    /// </remarks>
    /// <returns><c>true</c> when the column was added, <c>false</c> when it was already there.</returns>
    private static async Task<bool> EnsureColumnAsync(
        SqliteConnection sqLiteConnection,
        string tableName,
        string columnName,
        string columnDefinition)
    {
        // PRAGMA table_info does not accept a bound parameter for the table name.
        // Every caller is a compile-time literal in this file, so there is no
        // untrusted input here, but the names are still checked to keep it that
        // way if that ever changes.
        if (!IsSafeIdentifier(tableName) || !IsSafeIdentifier(columnName))
        {
            throw new ArgumentException($"Unsafe schema identifier: {tableName}.{columnName}");
        }

        var existingColumns = await sqLiteConnection.QueryAsync<string>(
            $"SELECT name FROM pragma_table_info('{tableName}');");

        if (existingColumns.Contains(columnName, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        await sqLiteConnection.ExecuteAsync(
            $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};");

        return true;
    }

    private static bool IsSafeIdentifier(string identifier) =>
        !string.IsNullOrWhiteSpace(identifier)
        && identifier.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
}
