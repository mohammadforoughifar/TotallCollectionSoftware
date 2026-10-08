using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// ستون LastSendPayload روی MoadianInvoices — JSON دقیق payload آخرین تلاش ثبت به
/// سامانه مودیان برای عیب‌یابی خطاهای سامانه (بررسی مقادیر واقعی setm/pmt/tob/bid/tinb).
/// دیتابیس‌های قدیمی این ستون را ندارند؛ مثل سایر Schemaهای خودتعمیر، idempotent اضافه می‌شود.
/// </summary>
public static class MoadianSchemaV11
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite())
        {
            await AddSqliteColumnAsync(db, "MoadianInvoices", "LastSendPayload", "TEXT NULL");
            return;
        }

        await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.MoadianInvoices', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.MoadianInvoices', N'LastSendPayload') IS NULL
    ALTER TABLE dbo.MoadianInvoices ADD LastSendPayload nvarchar(max) NULL;");
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
