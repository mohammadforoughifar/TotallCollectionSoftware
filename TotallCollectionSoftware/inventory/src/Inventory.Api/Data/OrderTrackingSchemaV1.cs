using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>شماره پیگیری جزء کارت‌به‌کارت سند فروش.</summary>
public static class OrderTrackingSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.Transactions', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Transactions', N'CashTrackingNumber') IS NULL
    ALTER TABLE [dbo].[Transactions] ADD [CashTrackingNumber] nvarchar(100) NULL;";

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
            var found = false;
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info('Transactions')";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    if (string.Equals(reader.GetString(1), "CashTrackingNumber", StringComparison.OrdinalIgnoreCase)) found = true;
            }
            if (!found)
            {
                await using var alter = connection.CreateCommand();
                alter.CommandText = "ALTER TABLE \"Transactions\" ADD COLUMN \"CashTrackingNumber\" TEXT NULL";
                await alter.ExecuteNonQueryAsync();
            }
        }
        finally { if (close) await connection.CloseAsync(); }
    }
}
