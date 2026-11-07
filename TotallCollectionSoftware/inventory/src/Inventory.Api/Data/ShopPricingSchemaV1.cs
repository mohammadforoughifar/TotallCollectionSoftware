using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>Shop-only pricing tables and a current-date baseline, never invented past prices.</summary>
public static class ShopPricingSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.ShopPricingPolicies', N'U') IS NULL
BEGIN
 CREATE TABLE [dbo].[ShopPricingPolicies] (
  [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ShopPricingPolicies] PRIMARY KEY,
  [CompanyId] int NULL, [MinimumProfitEnabled] bit NOT NULL,
  [MinimumMarkupPercent] decimal(9,2) NOT NULL, [UpdatedAt] datetime2 NOT NULL, [UpdatedBy] nvarchar(120) NULL
 );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopPricingPolicies') AND [name]=N'IX_ShopPricingPolicies_CompanyId')
 EXEC(N'CREATE UNIQUE INDEX [IX_ShopPricingPolicies_CompanyId] ON [dbo].[ShopPricingPolicies] ([CompanyId]) WHERE [CompanyId] IS NOT NULL;');
IF OBJECT_ID(N'dbo.ShopSalePrices', N'U') IS NULL
BEGIN
 CREATE TABLE [dbo].[ShopSalePrices] (
  [Id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ShopSalePrices] PRIMARY KEY,
  [CompanyId] int NULL, [ProductId] int NOT NULL, [EffectiveDate] datetime2 NOT NULL,
  [Price] decimal(18,2) NOT NULL, [PreviousPrice] decimal(18,2) NULL, [Source] nvarchar(24) NOT NULL,
  [PurchaseTransactionId] int NULL, [OperationId] uniqueidentifier NULL, [PreviewHash] nvarchar(64) NULL,
  [CreatedAt] datetime2 NOT NULL, [CreatedBy] nvarchar(120) NULL,
  CONSTRAINT [FK_ShopSalePrices_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([Id]) ON DELETE CASCADE
 );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopSalePrices') AND [name]=N'IX_ShopSalePrices_CompanyId_ProductId_EffectiveDate_Id')
 EXEC(N'CREATE INDEX [IX_ShopSalePrices_CompanyId_ProductId_EffectiveDate_Id] ON [dbo].[ShopSalePrices] ([CompanyId], [ProductId], [EffectiveDate], [Id]);');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopSalePrices') AND [name]=N'IX_ShopSalePrices_ProductId')
 EXEC(N'CREATE INDEX [IX_ShopSalePrices_ProductId] ON [dbo].[ShopSalePrices] ([ProductId]);');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopSalePrices') AND [name]=N'IX_ShopSalePrices_PurchaseTransactionId')
 EXEC(N'CREATE INDEX [IX_ShopSalePrices_PurchaseTransactionId] ON [dbo].[ShopSalePrices] ([PurchaseTransactionId]);');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopSalePrices') AND [name]=N'IX_ShopSalePrices_OperationId_ProductId')
 EXEC(N'CREATE UNIQUE INDEX [IX_ShopSalePrices_OperationId_ProductId] ON [dbo].[ShopSalePrices] ([OperationId], [ProductId]) WHERE [OperationId] IS NOT NULL;');";

    public const string Sqlite = @"
CREATE TABLE IF NOT EXISTS [ShopPricingPolicies] (
 [Id] INTEGER NOT NULL CONSTRAINT [PK_ShopPricingPolicies] PRIMARY KEY AUTOINCREMENT,
 [CompanyId] INTEGER NULL, [MinimumProfitEnabled] INTEGER NOT NULL, [MinimumMarkupPercent] TEXT NOT NULL,
 [UpdatedAt] TEXT NOT NULL, [UpdatedBy] TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS [IX_ShopPricingPolicies_CompanyId] ON [ShopPricingPolicies] ([CompanyId]);
CREATE TABLE IF NOT EXISTS [ShopSalePrices] (
 [Id] INTEGER NOT NULL CONSTRAINT [PK_ShopSalePrices] PRIMARY KEY AUTOINCREMENT,
 [CompanyId] INTEGER NULL, [ProductId] INTEGER NOT NULL, [EffectiveDate] TEXT NOT NULL,
 [Price] TEXT NOT NULL, [PreviousPrice] TEXT NULL, [Source] TEXT NOT NULL,
 [PurchaseTransactionId] INTEGER NULL, [OperationId] TEXT NULL, [PreviewHash] TEXT NULL,
 [CreatedAt] TEXT NOT NULL, [CreatedBy] TEXT NULL,
 CONSTRAINT [FK_ShopSalePrices_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS [IX_ShopSalePrices_CompanyId_ProductId_EffectiveDate_Id] ON [ShopSalePrices] ([CompanyId], [ProductId], [EffectiveDate], [Id]);
CREATE INDEX IF NOT EXISTS [IX_ShopSalePrices_ProductId] ON [ShopSalePrices] ([ProductId]);
CREATE INDEX IF NOT EXISTS [IX_ShopSalePrices_PurchaseTransactionId] ON [ShopSalePrices] ([PurchaseTransactionId]);
CREATE UNIQUE INDEX IF NOT EXISTS [IX_ShopSalePrices_OperationId_ProductId] ON [ShopSalePrices] ([OperationId], [ProductId]) WHERE [OperationId] IS NOT NULL;";

    public static async Task EnsureAsync(AppDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(db.Database.IsSqlite() ? Sqlite : SqlServer);
        // The product's known owner is authoritative if a legacy unowned product
        // was assigned to the sole company after the first startup pass.
        var ownership = db.Database.IsSqlite() ? @"
UPDATE [ShopSalePrices] SET [CompanyId]=(SELECT p.[CompanyId] FROM [Products] p WHERE p.[Id]=[ShopSalePrices].[ProductId])
 WHERE [CompanyId] IS NULL AND EXISTS (SELECT 1 FROM [Products] p WHERE p.[Id]=[ShopSalePrices].[ProductId] AND p.[CompanyId] IS NOT NULL);" : @"
UPDATE h SET h.[CompanyId]=p.[CompanyId] FROM [dbo].[ShopSalePrices] h JOIN [dbo].[Products] p ON p.[Id]=h.[ProductId]
 WHERE h.[CompanyId] IS NULL AND p.[CompanyId] IS NOT NULL;";
        await db.Database.ExecuteSqlRawAsync(ownership);
        // This is a snapshot on the day of the upgrade, not a claim about past prices.
        // Repeated startup and future-scheduled records must not fabricate another baseline.
        var baseline = db.Database.IsSqlite() ? @"
INSERT INTO [ShopSalePrices] ([CompanyId],[ProductId],[EffectiveDate],[Price],[PreviousPrice],[Source],[CreatedAt],[CreatedBy])
 SELECT p.[CompanyId],p.[Id],date('now','localtime') || ' 00:00:00',p.[SalePrice],NULL,'baseline',datetime('now','localtime'),NULL
 FROM [Products] p WHERE NOT EXISTS (SELECT 1 FROM [ShopSalePrices] h WHERE h.[ProductId]=p.[Id]);" : @"
INSERT INTO [dbo].[ShopSalePrices] ([CompanyId],[ProductId],[EffectiveDate],[Price],[PreviousPrice],[Source],[CreatedAt],[CreatedBy])
 SELECT p.[CompanyId],p.[Id],CAST(CAST(SYSDATETIME() AS date) AS datetime2),p.[SalePrice],NULL,N'baseline',SYSDATETIME(),NULL
 FROM [dbo].[Products] p WHERE NOT EXISTS (SELECT 1 FROM [dbo].[ShopSalePrices] h WHERE h.[ProductId]=p.[Id]);";
        await db.Database.ExecuteSqlRawAsync(baseline);
    }
}
