using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// V12 — تفکیک سال/دورهٔ مالی و شماره‌گذاری به‌ازای هر خدمات‌دهنده:
///  • ستون ServiceProviderId روی MoadianFiscalYears و MoadianFiscalPeriods (۰ = عمومی/سوابق)
///  • یکتایی DocumentNumber از «سراسری» به «به‌ازای هر خدمات‌دهنده» تغییر می‌کند:
///    ایندکس یکتای قدیمی حذف و ایندکس (ServiceProviderId, DocumentNumber) ساخته می‌شود.
/// مثل سایر Schemaها idempotent است.
/// </summary>
public static class MoadianSchemaV12
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite())
        {
            await AddSqliteColumnAsync(db, "MoadianFiscalYears", "ServiceProviderId", "INTEGER NOT NULL DEFAULT 0");
            await AddSqliteColumnAsync(db, "MoadianFiscalPeriods", "ServiceProviderId", "INTEGER NOT NULL DEFAULT 0");

            await db.Database.ExecuteSqlRawAsync(@"
DROP INDEX IF EXISTS IX_MoadianInvoices_DocumentNumber;
CREATE UNIQUE INDEX IF NOT EXISTS IX_MoadianInvoices_ProviderDocumentNumber
    ON MoadianInvoices (ServiceProviderId, DocumentNumber)
    WHERE DocumentNumber IS NOT NULL;");
            return;
        }

        await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.MoadianFiscalYears', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.MoadianFiscalYears', N'ServiceProviderId') IS NULL
    ALTER TABLE dbo.MoadianFiscalYears ADD ServiceProviderId int NOT NULL CONSTRAINT DF_MoadianFiscalYears_ServiceProviderId DEFAULT 0;
IF OBJECT_ID(N'dbo.MoadianFiscalPeriods', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.MoadianFiscalPeriods', N'ServiceProviderId') IS NULL
    ALTER TABLE dbo.MoadianFiscalPeriods ADD ServiceProviderId int NOT NULL CONSTRAINT DF_MoadianFiscalPeriods_ServiceProviderId DEFAULT 0;");

        await db.Database.ExecuteSqlRawAsync(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MoadianInvoices_DocumentNumber' AND object_id = OBJECT_ID(N'dbo.MoadianInvoices'))
    EXEC(N'DROP INDEX IX_MoadianInvoices_DocumentNumber ON dbo.MoadianInvoices');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MoadianInvoices_ProviderDocumentNumber' AND object_id = OBJECT_ID(N'dbo.MoadianInvoices'))
    EXEC(N'CREATE UNIQUE INDEX IX_MoadianInvoices_ProviderDocumentNumber ON dbo.MoadianInvoices(ServiceProviderId, DocumentNumber) WHERE DocumentNumber IS NOT NULL');");
    }

    private static async Task AddSqliteColumnAsync(AppDbContext db, string table, string column, string definition)
    {
        if (!await SqliteColumnExistsAsync(db, table, column))
            await db.Database.ExecuteSqlRawAsync($"ALTER TABLE [{table}] ADD COLUMN [{column}] {definition};");
    }

    private static async Task<bool> SqliteColumnExistsAsync(AppDbContext db, string table, string column)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info('{table}');";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }
}
