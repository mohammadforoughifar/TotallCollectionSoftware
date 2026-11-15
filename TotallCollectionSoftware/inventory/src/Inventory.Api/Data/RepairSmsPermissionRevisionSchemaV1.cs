using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// Old outbox events have no permission revision and cannot be sent after a later opt-in.
/// This additive column does not rewrite or enqueue historical records.
/// </summary>
public static class RepairSmsPermissionRevisionSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.RepairSmsNotifications',N'U') IS NOT NULL AND COL_LENGTH(N'dbo.RepairSmsNotifications',N'PermissionRevision') IS NULL
    ALTER TABLE [dbo].[RepairSmsNotifications] ADD [PermissionRevision] bigint NULL;";
    public const string SqliteAlter = "ALTER TABLE [RepairSmsNotifications] ADD COLUMN [PermissionRevision] INTEGER NULL;";

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
            var found = false;
            await using (var check = connection.CreateCommand())
            {
                check.CommandText = "PRAGMA table_info([RepairSmsNotifications]);";
                await using var reader = await check.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    if (string.Equals(reader.GetString(1), "PermissionRevision", StringComparison.OrdinalIgnoreCase)) found = true;
            }
            if (!found)
            {
                await using var alter = connection.CreateCommand();
                alter.CommandText = SqliteAlter;
                await alter.ExecuteNonQueryAsync();
            }
        }
        finally { if (close) await connection.CloseAsync(); }
    }
}
