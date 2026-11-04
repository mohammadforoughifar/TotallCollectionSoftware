using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>تنظیم مستقل حساب درآمد چک در قواعد موجود فاکتور.</summary>
public static class SaleChequeIncomeAccountSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.FacRules', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.FacRules', N'ChequeIncomeAccountId') IS NULL
    ALTER TABLE [dbo].[FacRules] ADD [ChequeIncomeAccountId] int NULL;";

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
            exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='FacRules'";
            if (Convert.ToInt32(await exists.ExecuteScalarAsync()) == 0) return;
            var present = false;
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info('FacRules')";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    if (string.Equals(reader.GetString(1), "ChequeIncomeAccountId", StringComparison.OrdinalIgnoreCase)) present = true;
            }
            if (!present)
            {
                await using var alter = connection.CreateCommand();
                alter.CommandText = "ALTER TABLE \"FacRules\" ADD COLUMN \"ChequeIncomeAccountId\" INTEGER NULL";
                await alter.ExecuteNonQueryAsync();
            }
        }
        finally { if (close) await connection.CloseAsync(); }
    }
}
