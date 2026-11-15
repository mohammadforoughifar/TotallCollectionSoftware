using Microsoft.EntityFrameworkCore;
namespace Inventory.Api.Data;

/// <summary>پذیرش گارانتی و ردیف‌های مستقل؛ سابقه تعمیرهای قدیمی تغییر نمی‌کند.</summary>
public static class ShopWarrantySchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.ShopWarranties',N'U') IS NULL
BEGIN
 CREATE TABLE [dbo].[ShopWarranties] (
  [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ShopWarranties] PRIMARY KEY,
  [CompanyId] int NULL, [OperationId] uniqueidentifier NOT NULL, [Number] nvarchar(30) NOT NULL,
  [PartyId] int NOT NULL, [TechnicianId] int NULL, [WarehouseId] int NOT NULL,
  [DeviceType] nvarchar(100) NOT NULL, [DeviceModel] nvarchar(200) NULL, [SerialNumber] nvarchar(100) NULL,
  [ProblemDescription] nvarchar(1000) NULL, [Accessories] nvarchar(500) NULL, [Note] nvarchar(500) NULL,
  [ReceivedAt] datetime2 NOT NULL, [FinalizedAt] datetime2 NULL, [StockTransactionId] int NULL,
  [CreatedAt] datetime2 NOT NULL, [CreatedBy] nvarchar(120) NULL, [FinalizedBy] nvarchar(120) NULL,
  CONSTRAINT [FK_ShopWarranties_Parties_PartyId] FOREIGN KEY ([PartyId]) REFERENCES [dbo].[Parties] ([Id]),
  CONSTRAINT [FK_ShopWarranties_Technicians_TechnicianId] FOREIGN KEY ([TechnicianId]) REFERENCES [dbo].[Technicians] ([Id]),
  CONSTRAINT [FK_ShopWarranties_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [dbo].[Warehouses] ([Id]),
  CONSTRAINT [FK_ShopWarranties_Transactions_StockTransactionId] FOREIGN KEY ([StockTransactionId]) REFERENCES [dbo].[Transactions] ([Id])
 );
END;
IF OBJECT_ID(N'dbo.ShopWarrantyItems',N'U') IS NULL
BEGIN
 CREATE TABLE [dbo].[ShopWarrantyItems] (
  [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ShopWarrantyItems] PRIMARY KEY,
  [CompanyId] int NULL, [ShopWarrantyId] int NOT NULL, [Description] nvarchar(300) NOT NULL,
  [ProductId] int NULL, [Quantity] decimal(18,3) NOT NULL, [UnitCost] decimal(18,2) NOT NULL,
  [Decision] int NOT NULL, [Supplied] bit NOT NULL,
  CONSTRAINT [FK_ShopWarrantyItems_ShopWarranties_ShopWarrantyId] FOREIGN KEY ([ShopWarrantyId]) REFERENCES [dbo].[ShopWarranties] ([Id]) ON DELETE CASCADE,
  CONSTRAINT [FK_ShopWarrantyItems_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([Id])
 );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopWarranties') AND [name]=N'IX_ShopWarranties_OperationId') EXEC(N'CREATE UNIQUE INDEX [IX_ShopWarranties_OperationId] ON [dbo].[ShopWarranties] ([OperationId])');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopWarranties') AND [name]=N'IX_ShopWarranties_CompanyId_FinalizedAt') EXEC(N'CREATE INDEX [IX_ShopWarranties_CompanyId_FinalizedAt] ON [dbo].[ShopWarranties] ([CompanyId],[FinalizedAt])');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopWarranties') AND [name]=N'IX_ShopWarranties_PartyId') EXEC(N'CREATE INDEX [IX_ShopWarranties_PartyId] ON [dbo].[ShopWarranties] ([PartyId])');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopWarranties') AND [name]=N'IX_ShopWarranties_TechnicianId') EXEC(N'CREATE INDEX [IX_ShopWarranties_TechnicianId] ON [dbo].[ShopWarranties] ([TechnicianId])');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopWarranties') AND [name]=N'IX_ShopWarranties_WarehouseId') EXEC(N'CREATE INDEX [IX_ShopWarranties_WarehouseId] ON [dbo].[ShopWarranties] ([WarehouseId])');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopWarranties') AND [name]=N'IX_ShopWarranties_StockTransactionId') EXEC(N'CREATE UNIQUE INDEX [IX_ShopWarranties_StockTransactionId] ON [dbo].[ShopWarranties] ([StockTransactionId]) WHERE [StockTransactionId] IS NOT NULL');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopWarrantyItems') AND [name]=N'IX_ShopWarrantyItems_ShopWarrantyId') EXEC(N'CREATE INDEX [IX_ShopWarrantyItems_ShopWarrantyId] ON [dbo].[ShopWarrantyItems] ([ShopWarrantyId])');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopWarrantyItems') AND [name]=N'IX_ShopWarrantyItems_ProductId') EXEC(N'CREATE INDEX [IX_ShopWarrantyItems_ProductId] ON [dbo].[ShopWarrantyItems] ([ProductId])');";

    public const string Sqlite = @"
CREATE TABLE IF NOT EXISTS [ShopWarranties] (
 [Id] INTEGER NOT NULL CONSTRAINT [PK_ShopWarranties] PRIMARY KEY AUTOINCREMENT,
 [CompanyId] INTEGER NULL, [OperationId] TEXT NOT NULL, [Number] TEXT NOT NULL,
 [PartyId] INTEGER NOT NULL, [TechnicianId] INTEGER NULL, [WarehouseId] INTEGER NOT NULL,
 [DeviceType] TEXT NOT NULL, [DeviceModel] TEXT NULL, [SerialNumber] TEXT NULL,
 [ProblemDescription] TEXT NULL, [Accessories] TEXT NULL, [Note] TEXT NULL,
 [ReceivedAt] TEXT NOT NULL, [FinalizedAt] TEXT NULL, [StockTransactionId] INTEGER NULL,
 [CreatedAt] TEXT NOT NULL, [CreatedBy] TEXT NULL, [FinalizedBy] TEXT NULL,
 CONSTRAINT [FK_ShopWarranties_Parties_PartyId] FOREIGN KEY ([PartyId]) REFERENCES [Parties] ([Id]) ON DELETE RESTRICT,
 CONSTRAINT [FK_ShopWarranties_Technicians_TechnicianId] FOREIGN KEY ([TechnicianId]) REFERENCES [Technicians] ([Id]) ON DELETE RESTRICT,
 CONSTRAINT [FK_ShopWarranties_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE RESTRICT,
 CONSTRAINT [FK_ShopWarranties_Transactions_StockTransactionId] FOREIGN KEY ([StockTransactionId]) REFERENCES [Transactions] ([Id]) ON DELETE RESTRICT
);
CREATE TABLE IF NOT EXISTS [ShopWarrantyItems] (
 [Id] INTEGER NOT NULL CONSTRAINT [PK_ShopWarrantyItems] PRIMARY KEY AUTOINCREMENT,
 [CompanyId] INTEGER NULL, [ShopWarrantyId] INTEGER NOT NULL, [Description] TEXT NOT NULL,
 [ProductId] INTEGER NULL, [Quantity] TEXT NOT NULL, [UnitCost] TEXT NOT NULL, [Decision] INTEGER NOT NULL, [Supplied] INTEGER NOT NULL,
 CONSTRAINT [FK_ShopWarrantyItems_ShopWarranties_ShopWarrantyId] FOREIGN KEY ([ShopWarrantyId]) REFERENCES [ShopWarranties] ([Id]) ON DELETE CASCADE,
 CONSTRAINT [FK_ShopWarrantyItems_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE RESTRICT
);
CREATE UNIQUE INDEX IF NOT EXISTS [IX_ShopWarranties_OperationId] ON [ShopWarranties] ([OperationId]);
CREATE INDEX IF NOT EXISTS [IX_ShopWarranties_CompanyId_FinalizedAt] ON [ShopWarranties] ([CompanyId],[FinalizedAt]);
CREATE INDEX IF NOT EXISTS [IX_ShopWarranties_PartyId] ON [ShopWarranties] ([PartyId]);
CREATE INDEX IF NOT EXISTS [IX_ShopWarranties_TechnicianId] ON [ShopWarranties] ([TechnicianId]);
CREATE INDEX IF NOT EXISTS [IX_ShopWarranties_WarehouseId] ON [ShopWarranties] ([WarehouseId]);
CREATE UNIQUE INDEX IF NOT EXISTS [IX_ShopWarranties_StockTransactionId] ON [ShopWarranties] ([StockTransactionId]) WHERE [StockTransactionId] IS NOT NULL;
CREATE INDEX IF NOT EXISTS [IX_ShopWarrantyItems_ShopWarrantyId] ON [ShopWarrantyItems] ([ShopWarrantyId]);
CREATE INDEX IF NOT EXISTS [IX_ShopWarrantyItems_ProductId] ON [ShopWarrantyItems] ([ProductId]);";
    public static Task EnsureAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync(db.Database.IsSqlite() ? Sqlite : SqlServer);
}
