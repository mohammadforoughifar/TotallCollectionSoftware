using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>ساخت افزایشی دفتر تسویه نسیه؛ سوابق قدیمی حدس یا بازنویسی نمی‌شوند.</summary>
public static class ShopOrderCreditSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.ShopOrderCreditReceipts', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ShopOrderCreditReceipts] (
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ShopOrderCreditReceipts] PRIMARY KEY,
        [CompanyId] int NULL, [OperationId] uniqueidentifier NOT NULL, [TransactionId] int NOT NULL,
        [OrderNumber] nvarchar(30) NOT NULL, [OrderType] int NOT NULL,
        [PartyId] int NOT NULL, [PartyName] nvarchar(200) NULL, [TrsAccountId] int NOT NULL,
        [Amount] decimal(18,2) NOT NULL, [Date] datetime2 NOT NULL, [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(120) NULL, [ReversalOperationId] uniqueidentifier NULL,
        [ReversalDate] datetime2 NULL, [ReversedAt] datetime2 NULL, [ReversedBy] nvarchar(120) NULL,
        CONSTRAINT [FK_ShopOrderCreditReceipts_Transactions_TransactionId] FOREIGN KEY ([TransactionId]) REFERENCES [dbo].[Transactions] ([Id]),
        CONSTRAINT [FK_ShopOrderCreditReceipts_TrsAccounts_TrsAccountId] FOREIGN KEY ([TrsAccountId]) REFERENCES [dbo].[TrsAccounts] ([Id])
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopOrderCreditReceipts') AND [name]=N'IX_ShopOrderCreditReceipts_OperationId')
    EXEC(N'CREATE UNIQUE INDEX [IX_ShopOrderCreditReceipts_OperationId] ON [dbo].[ShopOrderCreditReceipts] ([OperationId]);');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopOrderCreditReceipts') AND [name]=N'IX_ShopOrderCreditReceipts_ReversalOperationId')
    EXEC(N'CREATE UNIQUE INDEX [IX_ShopOrderCreditReceipts_ReversalOperationId] ON [dbo].[ShopOrderCreditReceipts] ([ReversalOperationId]) WHERE [ReversalOperationId] IS NOT NULL;');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopOrderCreditReceipts') AND [name]=N'IX_ShopOrderCreditReceipts_TransactionId')
    EXEC(N'CREATE INDEX [IX_ShopOrderCreditReceipts_TransactionId] ON [dbo].[ShopOrderCreditReceipts] ([TransactionId]);');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopOrderCreditReceipts') AND [name]=N'IX_ShopOrderCreditReceipts_TrsAccountId')
    EXEC(N'CREATE INDEX [IX_ShopOrderCreditReceipts_TrsAccountId] ON [dbo].[ShopOrderCreditReceipts] ([TrsAccountId]);');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopOrderCreditReceipts') AND [name]=N'IX_ShopOrderCreditReceipts_CompanyId_TrsAccountId')
    EXEC(N'CREATE INDEX [IX_ShopOrderCreditReceipts_CompanyId_TrsAccountId] ON [dbo].[ShopOrderCreditReceipts] ([CompanyId], [TrsAccountId]);');";

    public const string Sqlite = @"
CREATE TABLE IF NOT EXISTS [ShopOrderCreditReceipts] (
    [Id] INTEGER NOT NULL CONSTRAINT [PK_ShopOrderCreditReceipts] PRIMARY KEY AUTOINCREMENT,
    [CompanyId] INTEGER NULL, [OperationId] TEXT NOT NULL, [TransactionId] INTEGER NOT NULL,
    [OrderNumber] TEXT NOT NULL, [OrderType] INTEGER NOT NULL, [PartyId] INTEGER NOT NULL,
    [PartyName] TEXT NULL, [TrsAccountId] INTEGER NOT NULL, [Amount] TEXT NOT NULL,
    [Date] TEXT NOT NULL, [CreatedAt] TEXT NOT NULL, [CreatedBy] TEXT NULL,
    [ReversalOperationId] TEXT NULL, [ReversalDate] TEXT NULL, [ReversedAt] TEXT NULL, [ReversedBy] TEXT NULL,
    CONSTRAINT [FK_ShopOrderCreditReceipts_Transactions_TransactionId] FOREIGN KEY ([TransactionId]) REFERENCES [Transactions] ([Id]) ON DELETE RESTRICT,
    CONSTRAINT [FK_ShopOrderCreditReceipts_TrsAccounts_TrsAccountId] FOREIGN KEY ([TrsAccountId]) REFERENCES [TrsAccounts] ([Id]) ON DELETE RESTRICT
);
CREATE UNIQUE INDEX IF NOT EXISTS [IX_ShopOrderCreditReceipts_OperationId] ON [ShopOrderCreditReceipts] ([OperationId]);
CREATE UNIQUE INDEX IF NOT EXISTS [IX_ShopOrderCreditReceipts_ReversalOperationId] ON [ShopOrderCreditReceipts] ([ReversalOperationId]) WHERE [ReversalOperationId] IS NOT NULL;
CREATE INDEX IF NOT EXISTS [IX_ShopOrderCreditReceipts_TransactionId] ON [ShopOrderCreditReceipts] ([TransactionId]);
CREATE INDEX IF NOT EXISTS [IX_ShopOrderCreditReceipts_TrsAccountId] ON [ShopOrderCreditReceipts] ([TrsAccountId]);
CREATE INDEX IF NOT EXISTS [IX_ShopOrderCreditReceipts_CompanyId_TrsAccountId] ON [ShopOrderCreditReceipts] ([CompanyId], [TrsAccountId]);";

    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.ExecuteSqlRawAsync(db.Database.IsSqlite() ? Sqlite : SqlServer);
}
