using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>Additive shop-unit precision; never rewrites historical quantities or invoices.</summary>
public static class ShopQuantitySchemaV1
{
    // UPDATE is dynamic because SQL Server compiles column references before ALTER in the same batch.
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.MeasureUnits', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.MeasureUnits', N'AllowsFractionalQuantity') IS NULL
    ALTER TABLE [dbo].[MeasureUnits] ADD [AllowsFractionalQuantity] bit NULL;
IF OBJECT_ID(N'dbo.MeasureUnits', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.MeasureUnits', N'AllowsFractionalQuantity') IS NOT NULL
    EXEC(N'UPDATE [dbo].[MeasureUnits] SET [AllowsFractionalQuantity] = CASE
        WHEN [Name] IN (N''عدد'', N''بسته'', N''کارتن'', N''جفت'', N''حلقه'', N''دستگاه'', N''شاخه'', N''رول'') THEN 0
        WHEN [Name] IN (N''کیلوگرم'', N''گرم'', N''لیتر'', N''متر'', N''سانتی‌متر'', N''مترمربع'', N''تن'') THEN 1
        ELSE NULL END
        WHERE [AllowsFractionalQuantity] IS NULL AND [Name] IN
        (N''عدد'', N''بسته'', N''کارتن'', N''جفت'', N''حلقه'', N''دستگاه'', N''شاخه'', N''رول'',
         N''کیلوگرم'', N''گرم'', N''لیتر'', N''متر'', N''سانتی‌متر'', N''مترمربع'', N''تن'')');";

    public const string SqliteDefaults = @"
UPDATE [MeasureUnits] SET [AllowsFractionalQuantity] = CASE
    WHEN [Name] IN ('عدد', 'بسته', 'کارتن', 'جفت', 'حلقه', 'دستگاه', 'شاخه', 'رول') THEN 0
    WHEN [Name] IN ('کیلوگرم', 'گرم', 'لیتر', 'متر', 'سانتی‌متر', 'مترمربع', 'تن') THEN 1
    ELSE NULL END
WHERE [AllowsFractionalQuantity] IS NULL AND [Name] IN
    ('عدد', 'بسته', 'کارتن', 'جفت', 'حلقه', 'دستگاه', 'شاخه', 'رول',
     'کیلوگرم', 'گرم', 'لیتر', 'متر', 'سانتی‌متر', 'مترمربع', 'تن');";

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
            exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='MeasureUnits'";
            if (Convert.ToInt32(await exists.ExecuteScalarAsync()) == 0) return;

            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var columns = connection.CreateCommand())
            {
                columns.CommandText = "PRAGMA table_info('MeasureUnits')";
                await using var reader = await columns.ExecuteReaderAsync();
                while (await reader.ReadAsync()) present.Add(reader.GetString(1));
            }
            if (!present.Contains("AllowsFractionalQuantity"))
            {
                await using var alter = connection.CreateCommand();
                alter.CommandText = "ALTER TABLE [MeasureUnits] ADD COLUMN [AllowsFractionalQuantity] INTEGER NULL";
                await alter.ExecuteNonQueryAsync();
            }
            await using var defaults = connection.CreateCommand();
            defaults.CommandText = SqliteDefaults;
            await defaults.ExecuteNonQueryAsync();
        }
        finally { if (close) await connection.CloseAsync(); }
    }
}
