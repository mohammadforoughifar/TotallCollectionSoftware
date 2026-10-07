using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// گره‌خوردن صورتحساب به خدمات‌دهنده + ردیابی ارسال/استعلام رسمی:
///   • ستون ServiceProviderId روی MoadianInvoices (nullable — سوابق قدیمی بدون خدمات‌دهنده می‌مانند)
///   • ستون Taxid22 (شماره منحصر به فرد مالیاتی ۲۲ نویسه‌ای؛ نام با TaxId فروشنده تداخل ندارد)
///   • ستون‌های LastInquiryAt / LastInquiryStatus (نماد آخرین استعلام رسمی)
/// افزودن ستون‌ها idempotent است و جداول دیگر دست‌نخورده می‌مانند.
/// </summary>
public static class MoadianSchemaV7
{
    private const string ServiceProviderIndex = "IX_MoadianInvoices_ServiceProviderId";

    public static async Task EnsureAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite()) await EnsureSqliteAsync(db);
        else await EnsureSqlServerAsync(db);
    }

    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        await AddSqliteColumnAsync(db, "MoadianInvoices", "ServiceProviderId", "INTEGER NULL");
        await AddSqliteColumnAsync(db, "MoadianInvoices", "Taxid22", "TEXT NULL");
        await AddSqliteColumnAsync(db, "MoadianInvoices", "LastInquiryAt", "TEXT NULL");
        await AddSqliteColumnAsync(db, "MoadianInvoices", "LastInquiryStatus", "TEXT NULL");

        await db.Database.ExecuteSqlRawAsync($@"
CREATE INDEX IF NOT EXISTS [{ServiceProviderIndex}] ON [MoadianInvoices] ([ServiceProviderId]);");
    }

    private static Task EnsureSqlServerAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync($@"
IF OBJECT_ID(N'dbo.MoadianInvoices', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.MoadianInvoices', N'ServiceProviderId') IS NULL
    ALTER TABLE dbo.MoadianInvoices ADD ServiceProviderId int NULL;
IF OBJECT_ID(N'dbo.MoadianInvoices', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.MoadianInvoices', N'Taxid22') IS NULL
    ALTER TABLE dbo.MoadianInvoices ADD Taxid22 nvarchar(22) NULL;
IF OBJECT_ID(N'dbo.MoadianInvoices', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.MoadianInvoices', N'LastInquiryAt') IS NULL
    ALTER TABLE dbo.MoadianInvoices ADD LastInquiryAt datetime2 NULL;
IF OBJECT_ID(N'dbo.MoadianInvoices', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.MoadianInvoices', N'LastInquiryStatus') IS NULL
    ALTER TABLE dbo.MoadianInvoices ADD LastInquiryStatus nvarchar(40) NULL;
IF OBJECT_ID(N'dbo.MoadianInvoices', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.MoadianInvoices', N'ServiceProviderId') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{ServiceProviderIndex}' AND object_id = OBJECT_ID(N'dbo.MoadianInvoices'))
    EXEC(N'CREATE INDEX {ServiceProviderIndex} ON dbo.MoadianInvoices(ServiceProviderId)');");

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
