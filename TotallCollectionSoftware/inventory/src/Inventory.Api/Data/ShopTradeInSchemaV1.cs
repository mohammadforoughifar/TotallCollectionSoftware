using Microsoft.EntityFrameworkCore;
namespace Inventory.Api.Data;

/// <summary>جداول افزایشی اعتبار تعویض؛ بدون دستکاری پذیرش یا فاکتورهای قبلی.</summary>
public static class ShopTradeInSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.ShopTradeIns', N'U') IS NULL
BEGIN
 CREATE TABLE [dbo].[ShopTradeIns] (
  [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ShopTradeIns] PRIMARY KEY,
  [CompanyId] int NULL, [OperationId] uniqueidentifier NOT NULL, [Number] nvarchar(30) NOT NULL,
  [PartyId] int NOT NULL, [DeviceType] nvarchar(100) NOT NULL, [DeviceModel] nvarchar(200) NULL,
  [SerialNumber] nvarchar(100) NULL, [Note] nvarchar(500) NULL, [Value] decimal(18,2) NOT NULL,
  [ReceivedAt] datetime2 NOT NULL, [FinalizedAt] datetime2 NULL, [CreatedAt] datetime2 NOT NULL,
  [CreatedBy] nvarchar(120) NULL, [FinalizedBy] nvarchar(120) NULL,
  CONSTRAINT [FK_ShopTradeIns_Parties_PartyId] FOREIGN KEY ([PartyId]) REFERENCES [dbo].[Parties] ([Id])
 );
END;
IF OBJECT_ID(N'dbo.ShopTradeInUses', N'U') IS NULL
BEGIN
 CREATE TABLE [dbo].[ShopTradeInUses] (
  [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ShopTradeInUses] PRIMARY KEY,
  [CompanyId] int NULL, [OperationId] uniqueidentifier NOT NULL, [TradeInId] int NOT NULL,
  [Kind] int NOT NULL, [TargetSaleId] int NULL, [TrsAccountId] int NULL, [TrsVoucherId] int NULL,
  [Amount] decimal(18,2) NOT NULL, [Date] datetime2 NOT NULL, [Note] nvarchar(200) NULL,
  [CreatedBy] nvarchar(120) NULL, [CreatedAt] datetime2 NOT NULL,
  CONSTRAINT [FK_ShopTradeInUses_ShopTradeIns_TradeInId] FOREIGN KEY ([TradeInId]) REFERENCES [dbo].[ShopTradeIns] ([Id]),
  CONSTRAINT [FK_ShopTradeInUses_Transactions_TargetSaleId] FOREIGN KEY ([TargetSaleId]) REFERENCES [dbo].[Transactions] ([Id]),
  CONSTRAINT [FK_ShopTradeInUses_TrsAccounts_TrsAccountId] FOREIGN KEY ([TrsAccountId]) REFERENCES [dbo].[TrsAccounts] ([Id]),
  CONSTRAINT [FK_ShopTradeInUses_TrsVouchers_TrsVoucherId] FOREIGN KEY ([TrsVoucherId]) REFERENCES [dbo].[TrsVouchers] ([Id])
 );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopTradeIns') AND [name]=N'IX_ShopTradeIns_OperationId')
 EXEC(N'CREATE UNIQUE INDEX [IX_ShopTradeIns_OperationId] ON [dbo].[ShopTradeIns] ([OperationId])');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopTradeIns') AND [name]=N'IX_ShopTradeIns_CompanyId_PartyId_FinalizedAt')
 EXEC(N'CREATE INDEX [IX_ShopTradeIns_CompanyId_PartyId_FinalizedAt] ON [dbo].[ShopTradeIns] ([CompanyId],[PartyId],[FinalizedAt])');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopTradeIns') AND [name]=N'IX_ShopTradeIns_PartyId')
 EXEC(N'CREATE INDEX [IX_ShopTradeIns_PartyId] ON [dbo].[ShopTradeIns] ([PartyId])');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopTradeInUses') AND [name]=N'IX_ShopTradeInUses_OperationId')
 EXEC(N'CREATE UNIQUE INDEX [IX_ShopTradeInUses_OperationId] ON [dbo].[ShopTradeInUses] ([OperationId])');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopTradeInUses') AND [name]=N'IX_ShopTradeInUses_TradeInId')
 EXEC(N'CREATE INDEX [IX_ShopTradeInUses_TradeInId] ON [dbo].[ShopTradeInUses] ([TradeInId])');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopTradeInUses') AND [name]=N'IX_ShopTradeInUses_TargetSaleId')
 EXEC(N'CREATE INDEX [IX_ShopTradeInUses_TargetSaleId] ON [dbo].[ShopTradeInUses] ([TargetSaleId])');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopTradeInUses') AND [name]=N'IX_ShopTradeInUses_TrsAccountId')
 EXEC(N'CREATE INDEX [IX_ShopTradeInUses_TrsAccountId] ON [dbo].[ShopTradeInUses] ([TrsAccountId])');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopTradeInUses') AND [name]=N'IX_ShopTradeInUses_TrsVoucherId')
 EXEC(N'CREATE INDEX [IX_ShopTradeInUses_TrsVoucherId] ON [dbo].[ShopTradeInUses] ([TrsVoucherId])');";

    public const string Sqlite = @"
CREATE TABLE IF NOT EXISTS [ShopTradeIns] (
 [Id] INTEGER NOT NULL CONSTRAINT [PK_ShopTradeIns] PRIMARY KEY AUTOINCREMENT,
 [CompanyId] INTEGER NULL, [OperationId] TEXT NOT NULL, [Number] TEXT NOT NULL, [PartyId] INTEGER NOT NULL,
 [DeviceType] TEXT NOT NULL, [DeviceModel] TEXT NULL, [SerialNumber] TEXT NULL, [Note] TEXT NULL,
 [Value] TEXT NOT NULL, [ReceivedAt] TEXT NOT NULL, [FinalizedAt] TEXT NULL, [CreatedAt] TEXT NOT NULL,
 [CreatedBy] TEXT NULL, [FinalizedBy] TEXT NULL,
 CONSTRAINT [FK_ShopTradeIns_Parties_PartyId] FOREIGN KEY ([PartyId]) REFERENCES [Parties] ([Id]) ON DELETE RESTRICT
);
CREATE TABLE IF NOT EXISTS [ShopTradeInUses] (
 [Id] INTEGER NOT NULL CONSTRAINT [PK_ShopTradeInUses] PRIMARY KEY AUTOINCREMENT,
 [CompanyId] INTEGER NULL, [OperationId] TEXT NOT NULL, [TradeInId] INTEGER NOT NULL, [Kind] INTEGER NOT NULL,
 [TargetSaleId] INTEGER NULL, [TrsAccountId] INTEGER NULL, [TrsVoucherId] INTEGER NULL, [Amount] TEXT NOT NULL,
 [Date] TEXT NOT NULL, [Note] TEXT NULL, [CreatedBy] TEXT NULL, [CreatedAt] TEXT NOT NULL,
 CONSTRAINT [FK_ShopTradeInUses_ShopTradeIns_TradeInId] FOREIGN KEY ([TradeInId]) REFERENCES [ShopTradeIns] ([Id]) ON DELETE RESTRICT,
 CONSTRAINT [FK_ShopTradeInUses_Transactions_TargetSaleId] FOREIGN KEY ([TargetSaleId]) REFERENCES [Transactions] ([Id]) ON DELETE RESTRICT,
 CONSTRAINT [FK_ShopTradeInUses_TrsAccounts_TrsAccountId] FOREIGN KEY ([TrsAccountId]) REFERENCES [TrsAccounts] ([Id]) ON DELETE RESTRICT,
 CONSTRAINT [FK_ShopTradeInUses_TrsVouchers_TrsVoucherId] FOREIGN KEY ([TrsVoucherId]) REFERENCES [TrsVouchers] ([Id]) ON DELETE RESTRICT
);
CREATE UNIQUE INDEX IF NOT EXISTS [IX_ShopTradeIns_OperationId] ON [ShopTradeIns] ([OperationId]);
CREATE INDEX IF NOT EXISTS [IX_ShopTradeIns_CompanyId_PartyId_FinalizedAt] ON [ShopTradeIns] ([CompanyId],[PartyId],[FinalizedAt]);
CREATE INDEX IF NOT EXISTS [IX_ShopTradeIns_PartyId] ON [ShopTradeIns] ([PartyId]);
CREATE UNIQUE INDEX IF NOT EXISTS [IX_ShopTradeInUses_OperationId] ON [ShopTradeInUses] ([OperationId]);
CREATE INDEX IF NOT EXISTS [IX_ShopTradeInUses_TradeInId] ON [ShopTradeInUses] ([TradeInId]);
CREATE INDEX IF NOT EXISTS [IX_ShopTradeInUses_TargetSaleId] ON [ShopTradeInUses] ([TargetSaleId]);
CREATE INDEX IF NOT EXISTS [IX_ShopTradeInUses_TrsAccountId] ON [ShopTradeInUses] ([TrsAccountId]);
CREATE INDEX IF NOT EXISTS [IX_ShopTradeInUses_TrsVoucherId] ON [ShopTradeInUses] ([TrsVoucherId]);";

    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.ExecuteSqlRawAsync(db.Database.IsSqlite() ? Sqlite : SqlServer);
}
