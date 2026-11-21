using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>Stable actor identity for work-order history, including legacy databases.</summary>
public static class WorkOrderActorSchemaV1
{
    // Backfill only when adding the column. Never re-resolve historical identities after
    // a login has been renamed/reused. Created events belong to the order owner (some
    // integrations stored a display name); other interactive events stored a username.
    // System events and ambiguous/unknown usernames keep a null ID and a default avatar.
    // Dynamic SQL is compiled after ALTER TABLE so SQL Server sees the new column.
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.WorkOrderLogs', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.WorkOrderLogs', N'ActorUserId') IS NULL
BEGIN
    ALTER TABLE [dbo].[WorkOrderLogs] ADD [ActorUserId] int NULL;
    IF OBJECT_ID(N'dbo.WorkOrders', N'U') IS NOT NULL
        EXEC(N'UPDATE l SET [ActorUserId] = w.[OwnerUserId]
            FROM [dbo].[WorkOrderLogs] l
            INNER JOIN [dbo].[WorkOrders] w ON w.[Id] = l.[OrderId]
            WHERE l.[ActorUserId] IS NULL AND l.[Action] = N''Created''
              AND w.[OwnerUserId] > 0 AND l.[ActorName] <> N'''' AND l.[ActorName] <> N''سیستم'';');
    IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
        EXEC(N'UPDATE l SET [ActorUserId] = u.[Id]
            FROM [dbo].[WorkOrderLogs] l
            INNER JOIN (
                SELECT [Username], MIN([Id]) AS [Id] FROM [dbo].[Users]
                GROUP BY [Username] HAVING COUNT(*) = 1
            ) u ON u.[Username] = l.[ActorName]
            WHERE l.[ActorUserId] IS NULL AND l.[Action] <> N''Created''
              AND l.[ActorName] <> N'''' AND l.[ActorName] <> N''سیستم'';');
END;";

    public const string SqliteBackfill = @"
UPDATE [WorkOrderLogs]
SET [ActorUserId] = (
    SELECT w.[OwnerUserId] FROM [WorkOrders] w
    WHERE w.[Id] = [WorkOrderLogs].[OrderId] AND w.[OwnerUserId] > 0
)
WHERE [ActorUserId] IS NULL AND [Action] = 'Created'
  AND [ActorName] <> '' AND [ActorName] <> 'سیستم';
UPDATE [WorkOrderLogs]
SET [ActorUserId] = (
    SELECT MIN(u.[Id]) FROM [Users] u
    WHERE u.[Username] = [WorkOrderLogs].[ActorName]
    HAVING COUNT(*) = 1
)
WHERE [ActorUserId] IS NULL AND [Action] <> 'Created'
  AND [ActorName] <> '' AND [ActorName] <> 'سیستم';";

    public static async Task EnsureAsync(AppDbContext db)
    {
        if (db.Database.IsSqlServer())
        {
            await db.Database.ExecuteSqlRawAsync(SqlServer);
            return;
        }
        if (!db.Database.IsSqlite()) return;

        var connection = db.Database.GetDbConnection();
        var close = connection.State != ConnectionState.Open;
        if (close) await connection.OpenAsync();
        try
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var check = connection.CreateCommand())
            {
                check.CommandText = "PRAGMA table_info([WorkOrderLogs]);";
                await using var reader = await check.ExecuteReaderAsync();
                while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
            }
            if (columns.Count == 0 || columns.Contains("ActorUserId")) return;

            // Keep column creation and backfill atomic, including on a failed upgrade.
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [WorkOrderLogs] ADD COLUMN [ActorUserId] INTEGER NULL;");
            await db.Database.ExecuteSqlRawAsync(SqliteBackfill);
            await transaction.CommitAsync();
        }
        finally { if (close) await connection.CloseAsync(); }
    }
}
