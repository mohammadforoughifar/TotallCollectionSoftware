using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>Additive outbox and request-deduplication schema. Never backfills old events.</summary>
public static class WorkOrderSmsSchemaV1
{
    // Dynamic CREATE INDEX statements are compiled after ALTER TABLE has added the columns.
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.WorkOrderLogs', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.WorkOrderLogs', N'RequestKey') IS NULL
    ALTER TABLE [dbo].[WorkOrderLogs] ADD [RequestKey] nvarchar(64) NULL;
IF OBJECT_ID(N'dbo.WorkOrderLogs', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.WorkOrderLogs', N'PayloadFingerprint') IS NULL
    ALTER TABLE [dbo].[WorkOrderLogs] ADD [PayloadFingerprint] nvarchar(64) NULL;
IF OBJECT_ID(N'dbo.WorkOrderLogs', N'U') IS NOT NULL AND NOT EXISTS
    (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.WorkOrderLogs') AND [name]=N'IX_WorkOrderLogs_RequestKey')
    EXEC(N'CREATE UNIQUE INDEX [IX_WorkOrderLogs_RequestKey] ON [dbo].[WorkOrderLogs] ([RequestKey]) WHERE [RequestKey] IS NOT NULL');
IF OBJECT_ID(N'dbo.WorkOrderLogs', N'U') IS NOT NULL AND NOT EXISTS
    (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.WorkOrderLogs') AND [name]=N'IX_WorkOrderLogs_PayloadFingerprint_CreatedAt')
    EXEC(N'CREATE INDEX [IX_WorkOrderLogs_PayloadFingerprint_CreatedAt] ON [dbo].[WorkOrderLogs] ([PayloadFingerprint], [CreatedAt]) WHERE [PayloadFingerprint] IS NOT NULL');
IF OBJECT_ID(N'dbo.WorkOrders', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.WorkOrderLogs', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.WorkOrderSmsNotifications', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[WorkOrderSmsNotifications] (
        [Id] int NOT NULL IDENTITY(1,1) CONSTRAINT [PK_WorkOrderSmsNotifications] PRIMARY KEY,
        [CompanyId] int NOT NULL,
        [WorkOrderId] int NOT NULL,
        [EventLogId] int NOT NULL,
        [RecipientUserId] int NOT NULL,
        [RecipientKey] nvarchar(64) NOT NULL,
        [EventType] int NOT NULL,
        [Status] int NOT NULL,
        [PermissionRevision] bigint NULL,
        [Recipient] nvarchar(20) NULL,
        [ProviderMessageId] bigint NULL,
        [ProviderCode] int NULL,
        [ProviderMessageStatus] int NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [AttemptStartedAtUtc] datetime2 NULL,
        [CompletedAtUtc] datetime2 NULL,
        CONSTRAINT [FK_WorkOrderSmsNotifications_WorkOrders_WorkOrderId] FOREIGN KEY ([WorkOrderId])
            REFERENCES [dbo].[WorkOrders] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_WorkOrderSmsNotifications_WorkOrderLogs_EventLogId] FOREIGN KEY ([EventLogId])
            REFERENCES [dbo].[WorkOrderLogs] ([Id])
    );
END;
IF OBJECT_ID(N'dbo.WorkOrderSmsNotifications', N'U') IS NOT NULL AND NOT EXISTS
    (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.WorkOrderSmsNotifications') AND [name]=N'IX_WorkOrderSmsNotifications_EventLogId_RecipientKey')
    CREATE UNIQUE INDEX [IX_WorkOrderSmsNotifications_EventLogId_RecipientKey] ON [dbo].[WorkOrderSmsNotifications] ([EventLogId], [RecipientKey]);
IF OBJECT_ID(N'dbo.WorkOrderSmsNotifications', N'U') IS NOT NULL AND NOT EXISTS
    (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.WorkOrderSmsNotifications') AND [name]=N'IX_WorkOrderSmsNotifications_Status_CreatedAtUtc')
    CREATE INDEX [IX_WorkOrderSmsNotifications_Status_CreatedAtUtc] ON [dbo].[WorkOrderSmsNotifications] ([Status], [CreatedAtUtc]);
IF OBJECT_ID(N'dbo.WorkOrderSmsNotifications', N'U') IS NOT NULL AND NOT EXISTS
    (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.WorkOrderSmsNotifications') AND [name]=N'IX_WorkOrderSmsNotifications_WorkOrderId')
    CREATE INDEX [IX_WorkOrderSmsNotifications_WorkOrderId] ON [dbo].[WorkOrderSmsNotifications] ([WorkOrderId]);";

    public const string Sqlite = @"
CREATE UNIQUE INDEX IF NOT EXISTS [IX_WorkOrderLogs_RequestKey] ON [WorkOrderLogs] ([RequestKey]) WHERE [RequestKey] IS NOT NULL;
CREATE INDEX IF NOT EXISTS [IX_WorkOrderLogs_PayloadFingerprint_CreatedAt] ON [WorkOrderLogs] ([PayloadFingerprint], [CreatedAt]) WHERE [PayloadFingerprint] IS NOT NULL;
CREATE TABLE IF NOT EXISTS [WorkOrderSmsNotifications] (
    [Id] INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    [CompanyId] INTEGER NOT NULL,
    [WorkOrderId] INTEGER NOT NULL,
    [EventLogId] INTEGER NOT NULL,
    [RecipientUserId] INTEGER NOT NULL,
    [RecipientKey] TEXT NOT NULL,
    [EventType] INTEGER NOT NULL,
    [Status] INTEGER NOT NULL,
    [PermissionRevision] INTEGER NULL,
    [Recipient] TEXT NULL,
    [ProviderMessageId] INTEGER NULL,
    [ProviderCode] INTEGER NULL,
    [ProviderMessageStatus] INTEGER NULL,
    [CreatedAtUtc] TEXT NOT NULL,
    [AttemptStartedAtUtc] TEXT NULL,
    [CompletedAtUtc] TEXT NULL,
    FOREIGN KEY ([WorkOrderId]) REFERENCES [WorkOrders] ([Id]) ON DELETE CASCADE,
    FOREIGN KEY ([EventLogId]) REFERENCES [WorkOrderLogs] ([Id])
);
CREATE UNIQUE INDEX IF NOT EXISTS [IX_WorkOrderSmsNotifications_EventLogId_RecipientKey] ON [WorkOrderSmsNotifications] ([EventLogId], [RecipientKey]);
CREATE INDEX IF NOT EXISTS [IX_WorkOrderSmsNotifications_Status_CreatedAtUtc] ON [WorkOrderSmsNotifications] ([Status], [CreatedAtUtc]);
CREATE INDEX IF NOT EXISTS [IX_WorkOrderSmsNotifications_WorkOrderId] ON [WorkOrderSmsNotifications] ([WorkOrderId]);";

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
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var check = connection.CreateCommand())
            {
                check.CommandText = "PRAGMA table_info([WorkOrderLogs]);";
                await using var reader = await check.ExecuteReaderAsync();
                while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
            }
            if (!columns.Contains("RequestKey"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE [WorkOrderLogs] ADD COLUMN [RequestKey] TEXT NULL;");
            if (!columns.Contains("PayloadFingerprint"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE [WorkOrderLogs] ADD COLUMN [PayloadFingerprint] TEXT NULL;");
            await db.Database.ExecuteSqlRawAsync(Sqlite);
        }
        finally { if (close) await connection.CloseAsync(); }
    }
}
