using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>نرخ پورسانت فاکتورهای جدید؛ اسناد قدیمی بدون حدس و backfill باقی می‌مانند.</summary>
public static class ShopReferrerCommissionSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.Transactions', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.Transactions', N'ReferrerGoodsPercentSnapshot') IS NULL
        ALTER TABLE [dbo].[Transactions] ADD [ReferrerGoodsPercentSnapshot] decimal(18,6) NULL;
    IF COL_LENGTH(N'dbo.Transactions', N'ReferrerServicePercentSnapshot') IS NULL
        ALTER TABLE [dbo].[Transactions] ADD [ReferrerServicePercentSnapshot] decimal(18,6) NULL;
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
            exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Transactions'";
            if (Convert.ToInt32(await exists.ExecuteScalarAsync()) == 0) return;
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info('Transactions')";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) present.Add(reader.GetString(1));
            }
            foreach (var name in new[] { "ReferrerGoodsPercentSnapshot", "ReferrerServicePercentSnapshot" })
            {
                if (present.Contains(name)) continue;
                await using var alter = connection.CreateCommand();
                alter.CommandText = $"ALTER TABLE \"Transactions\" ADD COLUMN \"{name}\" TEXT NULL";
                await alter.ExecuteNonQueryAsync();
            }
        }
        finally { if (close) await connection.CloseAsync(); }
    }
}
