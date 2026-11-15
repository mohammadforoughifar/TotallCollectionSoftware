using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>افزودن ستون مالکیت شرکت به حساب‌های خزانه بدون حذف یا تغییر حساب‌های موجود.</summary>
public static class TreasuryAccountCompanySchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.TrsAccounts', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.TrsAccounts', N'CompanyId') IS NULL
        ALTER TABLE [dbo].[TrsAccounts] ADD [CompanyId] int NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsAccounts_CompanyId' AND [object_id] = OBJECT_ID(N'dbo.TrsAccounts'))
        EXEC(N'CREATE INDEX [IX_TrsAccounts_CompanyId] ON [dbo].[TrsAccounts] ([CompanyId])');
END";

    public static async Task EnsureAsync(AppDbContext db)
    {
        if (!db.Database.IsSqlite())
        {
            await db.Database.ExecuteSqlRawAsync(SqlServer);
            return;
        }
        var connection = db.Database.GetDbConnection();
        var close = connection.State != ConnectionState.Open;
        if (close) await connection.OpenAsync();
        try
        {
            await using var exists = connection.CreateCommand();
            exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='TrsAccounts'";
            if (Convert.ToInt32(await exists.ExecuteScalarAsync()) == 0) return;

            var hasCompanyId = false;
            await using (var info = connection.CreateCommand())
            {
                info.CommandText = "PRAGMA table_info('TrsAccounts')";
                await using var reader = await info.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    if (string.Equals(reader.GetString(1), "CompanyId", StringComparison.OrdinalIgnoreCase))
                        hasCompanyId = true;
                }
            }
            if (!hasCompanyId)
            {
                await using var alter = connection.CreateCommand();
                alter.CommandText = "ALTER TABLE \"TrsAccounts\" ADD COLUMN \"CompanyId\" INTEGER NULL";
                await alter.ExecuteNonQueryAsync();
            }
            await using var idx = connection.CreateCommand();
            idx.CommandText = "CREATE INDEX IF NOT EXISTS \"IX_TrsAccounts_CompanyId\" ON \"TrsAccounts\" (\"CompanyId\")";
            await idx.ExecuteNonQueryAsync();
        }
        finally
        {
            if (close) await connection.CloseAsync();
        }
    }
}
