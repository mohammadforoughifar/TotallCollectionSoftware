using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>ستون‌های مستقل ارزش افزوده عملیات؛ اسناد قدیمی همیشه صفر می‌مانند.</summary>
public static class OrderVatSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.Products', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Products', N'InvoiceVatValue') IS NULL
    ALTER TABLE [dbo].[Products] ADD [InvoiceVatValue] decimal(18,2) NULL;
IF OBJECT_ID(N'dbo.TransactionLines', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.TransactionLines', N'GrossAmount') IS NULL
        ALTER TABLE [dbo].[TransactionLines] ADD [GrossAmount] decimal(18,2) NULL;
    IF COL_LENGTH(N'dbo.TransactionLines', N'VatValue') IS NULL
        ALTER TABLE [dbo].[TransactionLines] ADD [VatValue] decimal(18,2) NOT NULL DEFAULT(0);
    IF COL_LENGTH(N'dbo.TransactionLines', N'VatAmount') IS NULL
        ALTER TABLE [dbo].[TransactionLines] ADD [VatAmount] decimal(18,2) NOT NULL DEFAULT(0);
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
            foreach (var (table, columns) in new[] {
                ("Products", new[] { "InvoiceVatValue" }),
                ("TransactionLines", new[] { "GrossAmount", "VatValue", "VatAmount" }) })
            {
                await using var exists = connection.CreateCommand();
                exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='" + table + "'";
                if (Convert.ToInt32(await exists.ExecuteScalarAsync()) == 0) continue;
                var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                await using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA table_info('" + table + "')";
                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync()) present.Add(reader.GetString(1));
                }
                foreach (var column in columns.Where(c => !present.Contains(c)))
                {
                    await using var alter = connection.CreateCommand();
                    alter.CommandText = "ALTER TABLE \"" + table + "\" ADD COLUMN \"" + column + "\" NUMERIC" +
                        (table == "Products" || column == "GrossAmount" ? " NULL" : " NOT NULL DEFAULT 0");
                    await alter.ExecuteNonQueryAsync();
                }
            }
        }
        finally { if (close) await connection.CloseAsync(); }
    }
}
