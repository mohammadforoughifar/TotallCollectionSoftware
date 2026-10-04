using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// Adds independent Moadian invoice type/pattern/subject fields and the related
/// invoice tax ID to databases created by earlier application versions.
/// </summary>
public static class MoadianSchemaV3
{
    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? EnsureSqliteAsync(db) : EnsureSqlServerAsync(db);

    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        await AddSqliteColumnAsync(db, "MoadianInvoices", "InvoicePattern", "INTEGER NOT NULL DEFAULT 0");
        await AddSqliteColumnAsync(db, "MoadianInvoices", "InvoiceSubject", "INTEGER NOT NULL DEFAULT 0");
        await AddSqliteColumnAsync(db, "MoadianInvoices", "ReferenceTaxId", "TEXT NULL");
    }

    private static Task EnsureSqlServerAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.MoadianInvoices', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.MoadianInvoices', N'InvoicePattern') IS NULL
    ALTER TABLE dbo.MoadianInvoices ADD InvoicePattern int NOT NULL CONSTRAINT DF_MoadianInvoices_InvoicePattern DEFAULT(0);
IF OBJECT_ID(N'dbo.MoadianInvoices', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.MoadianInvoices', N'InvoiceSubject') IS NULL
    ALTER TABLE dbo.MoadianInvoices ADD InvoiceSubject int NOT NULL CONSTRAINT DF_MoadianInvoices_InvoiceSubject DEFAULT(0);
IF OBJECT_ID(N'dbo.MoadianInvoices', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.MoadianInvoices', N'ReferenceTaxId') IS NULL
    ALTER TABLE dbo.MoadianInvoices ADD ReferenceTaxId nvarchar(22) NULL;
");

    private static async Task AddSqliteColumnAsync(AppDbContext db, string table, string column, string declaration)
    {
        if (await ColumnExistsAsync(db, table, column))
            return;

        await db.Database.ExecuteSqlRawAsync($"ALTER TABLE [{table}] ADD COLUMN [{column}] {declaration};");
    }

    private static async Task<bool> ColumnExistsAsync(AppDbContext db, string table, string column)
    {
        try
        {
            var connection = db.Database.GetDbConnection();
            var openedHere = connection.State != System.Data.ConnectionState.Open;
            if (openedHere) await connection.OpenAsync();
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}'";
                return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
            }
            finally
            {
                if (openedHere) await connection.CloseAsync();
            }
        }
        catch
        {
            // A newly created schema already has all model columns.
            return false;
        }
    }
}
