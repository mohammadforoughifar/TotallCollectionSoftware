using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// Adds a non-unique Number index used to allocate globally increasing Moadian
/// draft numbers under a serializable transaction. The index is deliberately
/// non-unique because historical invoices may reuse Number across fiscal periods.
/// </summary>
public static class MoadianSchemaV4
{
    private const string IndexName = "IX_MoadianInvoices_Number";

    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? EnsureSqliteAsync(db) : EnsureSqlServerAsync(db);

    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        if (!await SqliteTableExistsAsync(db, "MoadianInvoices"))
            return;

        await db.Database.ExecuteSqlRawAsync(
            $"CREATE INDEX IF NOT EXISTS [{IndexName}] ON [MoadianInvoices] ([Number]);");
    }

    private static Task EnsureSqlServerAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync($@"
IF OBJECT_ID(N'dbo.MoadianInvoices', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.MoadianInvoices', N'Number') IS NOT NULL
AND NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'{IndexName}' AND object_id = OBJECT_ID(N'dbo.MoadianInvoices')
)
    CREATE INDEX [{IndexName}] ON dbo.MoadianInvoices([Number]);
");

    private static async Task<bool> SqliteTableExistsAsync(AppDbContext db, string table)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        if (openedHere) await connection.OpenAsync();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $table";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$table";
            parameter.Value = table;
            command.Parameters.Add(parameter);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }
}
