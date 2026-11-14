using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>Additive schema only: no historical repair is queued for SMS.</summary>
public static class RepairSmsSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.RepairOrders', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.RepairSmsNotifications', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[RepairSmsNotifications] (
        [Id] int NOT NULL IDENTITY(1,1) CONSTRAINT [PK_RepairSmsNotifications] PRIMARY KEY,
        [CompanyId] int NULL,
        [RepairOrderId] int NOT NULL,
        [PartyId] int NOT NULL,
        [EventType] int NOT NULL,
        [Status] int NOT NULL,
        [Recipient] nvarchar(20) NULL,
        [ProviderMessageId] bigint NULL,
        [ProviderCode] int NULL,
        [ProviderMessageStatus] int NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [AttemptStartedAtUtc] datetime2 NULL,
        [CompletedAtUtc] datetime2 NULL,
        CONSTRAINT [FK_RepairSmsNotifications_RepairOrders_RepairOrderId] FOREIGN KEY ([RepairOrderId])
            REFERENCES [dbo].[RepairOrders] ([Id]) ON DELETE CASCADE
    );
END;
IF OBJECT_ID(N'dbo.RepairSmsNotifications', N'U') IS NOT NULL AND NOT EXISTS
    (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.RepairSmsNotifications') AND [name]=N'IX_RepairSmsNotifications_RepairOrderId_EventType')
    CREATE UNIQUE INDEX [IX_RepairSmsNotifications_RepairOrderId_EventType] ON [dbo].[RepairSmsNotifications] ([RepairOrderId], [EventType]);
IF OBJECT_ID(N'dbo.RepairSmsNotifications', N'U') IS NOT NULL AND NOT EXISTS
    (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.RepairSmsNotifications') AND [name]=N'IX_RepairSmsNotifications_Status_CreatedAtUtc')
    CREATE INDEX [IX_RepairSmsNotifications_Status_CreatedAtUtc] ON [dbo].[RepairSmsNotifications] ([Status], [CreatedAtUtc]);";

    public const string Sqlite = @"
CREATE TABLE IF NOT EXISTS [RepairSmsNotifications] (
    [Id] INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    [CompanyId] INTEGER NULL,
    [RepairOrderId] INTEGER NOT NULL,
    [PartyId] INTEGER NOT NULL,
    [EventType] INTEGER NOT NULL,
    [Status] INTEGER NOT NULL,
    [Recipient] TEXT NULL,
    [ProviderMessageId] INTEGER NULL,
    [ProviderCode] INTEGER NULL,
    [ProviderMessageStatus] INTEGER NULL,
    [CreatedAtUtc] TEXT NOT NULL,
    [AttemptStartedAtUtc] TEXT NULL,
    [CompletedAtUtc] TEXT NULL,
    FOREIGN KEY ([RepairOrderId]) REFERENCES [RepairOrders] ([Id]) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS [IX_RepairSmsNotifications_RepairOrderId_EventType] ON [RepairSmsNotifications] ([RepairOrderId], [EventType]);
CREATE INDEX IF NOT EXISTS [IX_RepairSmsNotifications_Status_CreatedAtUtc] ON [RepairSmsNotifications] ([Status], [CreatedAtUtc]);";

    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.ExecuteSqlRawAsync(db.Database.IsSqlite() ? Sqlite : SqlServer);
}
