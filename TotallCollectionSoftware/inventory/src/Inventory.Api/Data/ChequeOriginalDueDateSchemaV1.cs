using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>نگه‌داری سررسید اولیه برای جلوگیری از انباشته شدن خطای گردکردن.</summary>
public static class ChequeOriginalDueDateSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.Cheques', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Cheques', N'OriginalDueDate') IS NULL
    ALTER TABLE [dbo].[Cheques] ADD [OriginalDueDate] datetime2 NULL;";

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
            exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Cheques'";
            if (Convert.ToInt32(await exists.ExecuteScalarAsync()) == 0) return;
            var present = false;
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info('Cheques')";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    if (string.Equals(reader.GetString(1), "OriginalDueDate", StringComparison.OrdinalIgnoreCase)) present = true;
            }
            if (!present)
            {
                await using var alter = connection.CreateCommand();
                alter.CommandText = "ALTER TABLE \"Cheques\" ADD COLUMN \"OriginalDueDate\" TEXT NULL";
                await alter.ExecuteNonQueryAsync();
            }
        }
        finally { if (close) await connection.CloseAsync(); }
    }
}
