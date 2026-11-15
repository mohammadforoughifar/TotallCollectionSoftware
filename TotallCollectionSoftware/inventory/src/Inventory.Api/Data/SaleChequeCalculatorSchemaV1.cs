using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>نگه‌داری ورودی ماشین‌حساب اولیه، مستقل از مبنا و نرخ تعدیل سررسید.</summary>
public static class SaleChequeCalculatorSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.SaleChequeCharges', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.SaleChequeCharges', N'CalculatorBaseAmount') IS NULL
        ALTER TABLE [dbo].[SaleChequeCharges] ADD [CalculatorBaseAmount] decimal(18,2) NULL;
    IF COL_LENGTH(N'dbo.SaleChequeCharges', N'CalculatorRatePercent') IS NULL
        ALTER TABLE [dbo].[SaleChequeCharges] ADD [CalculatorRatePercent] decimal(18,6) NULL;
    IF COL_LENGTH(N'dbo.SaleChequeCharges', N'CalculatorMonths') IS NULL
        ALTER TABLE [dbo].[SaleChequeCharges] ADD [CalculatorMonths] int NULL;
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
            exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='SaleChequeCharges'";
            if (Convert.ToInt32(await exists.ExecuteScalarAsync()) == 0) return;
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info('SaleChequeCharges')";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) present.Add(reader.GetString(1));
            }
            // Fixed column names and types, not supplied by a caller.
            foreach (var (name, type) in new[] { ("CalculatorBaseAmount", "TEXT"), ("CalculatorRatePercent", "TEXT"), ("CalculatorMonths", "INTEGER") })
            {
                if (present.Contains(name)) continue;
                await using var alter = connection.CreateCommand();
                alter.CommandText = $"ALTER TABLE \"SaleChequeCharges\" ADD COLUMN \"{name}\" {type} NULL";
                await alter.ExecuteNonQueryAsync();
            }
        }
        finally { if (close) await connection.CloseAsync(); }
    }
}
