using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>ثبت حساب‌های زمان صدور برای حفظ انتساب صحیح در گزارش‌های بعدی.</summary>
public static class SaleChequeAccountsSnapshotSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.SaleChequeCharges', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.SaleChequeCharges', N'IncomeAccountId') IS NULL
        ALTER TABLE [dbo].[SaleChequeCharges] ADD [IncomeAccountId] int NULL;
    IF COL_LENGTH(N'dbo.SaleChequeCharges', N'PartyAccountId') IS NULL
        ALTER TABLE [dbo].[SaleChequeCharges] ADD [PartyAccountId] int NULL;
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
            foreach (var name in new[] { "IncomeAccountId", "PartyAccountId" })
            {
                if (present.Contains(name)) continue;
                await using var alter = connection.CreateCommand();
                alter.CommandText = $"ALTER TABLE \"SaleChequeCharges\" ADD COLUMN \"{name}\" INTEGER NULL";
                await alter.ExecuteNonQueryAsync();
            }
        }
        finally { if (close) await connection.CloseAsync(); }
    }
}
