using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// ستون «شرکت» برای اعلان‌ها — پایهٔ نمایش اعلان فقط در شرکت فعال انتخاب‌شده.
/// <para>
/// رکوردهای قدیمی (پیش از این تغییر) مقدار NULL می‌گیرند و مثل اعلان‌های عمومی در همهٔ شرکت‌ها
/// دیده می‌شوند؛ چون اطلاعاتی برای نسبت‌دادن آن‌ها به یک شرکت وجود ندارد.
/// </para>
/// </summary>
public static class NotificationCompanySchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.AppNotifications', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.AppNotifications', 'CompanyId') IS NULL
    ALTER TABLE [dbo].[AppNotifications] ADD [CompanyId] int NULL;
IF OBJECT_ID(N'dbo.AppNotifications', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AppNotifications_UserId_CompanyId' AND object_id = OBJECT_ID(N'dbo.AppNotifications'))
    CREATE INDEX [IX_AppNotifications_UserId_CompanyId] ON [dbo].[AppNotifications] ([UserId], [CompanyId], [IsRead]);";

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
            exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='AppNotifications'";
            if (Convert.ToInt32(await exists.ExecuteScalarAsync()) == 0) return;

            var present = false;
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info('AppNotifications')";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    if (string.Equals(reader.GetString(1), "CompanyId", StringComparison.OrdinalIgnoreCase)) present = true;
            }
            if (!present)
            {
                await using var alter = connection.CreateCommand();
                alter.CommandText = "ALTER TABLE \"AppNotifications\" ADD COLUMN \"CompanyId\" INTEGER NULL";
                await alter.ExecuteNonQueryAsync();
            }

            await using (var index = connection.CreateCommand())
            {
                index.CommandText = "CREATE INDEX IF NOT EXISTS \"IX_AppNotifications_UserId_CompanyId\" " +
                                    "ON \"AppNotifications\" (\"UserId\", \"CompanyId\", \"IsRead\")";
                await index.ExecuteNonQueryAsync();
            }
        }
        finally { if (close) await connection.CloseAsync(); }
    }
}
