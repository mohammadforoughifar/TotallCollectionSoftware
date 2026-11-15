using Microsoft.EntityFrameworkCore;
namespace Inventory.Api.Data;

/// <summary>Multiple devices per warranty, preserving existing single-device records.</summary>
public static class ShopWarrantyDeviceSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.ShopWarrantyDevices',N'U') IS NULL
BEGIN
 CREATE TABLE [dbo].[ShopWarrantyDevices] (
  [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ShopWarrantyDevices] PRIMARY KEY,
  [CompanyId] int NULL, [ShopWarrantyId] int NOT NULL, [Position] int NOT NULL,
  [DeviceType] nvarchar(100) NOT NULL, [DeviceModel] nvarchar(200) NULL,
  [SerialNumber] nvarchar(100) NULL, [ProblemDescription] nvarchar(1000) NULL, [Accessories] nvarchar(500) NULL,
  CONSTRAINT [FK_ShopWarrantyDevices_ShopWarranties_ShopWarrantyId] FOREIGN KEY ([ShopWarrantyId]) REFERENCES [dbo].[ShopWarranties] ([Id]) ON DELETE CASCADE
 );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopWarrantyDevices') AND [name]=N'IX_ShopWarrantyDevices_ShopWarrantyId_Position')
 EXEC(N'CREATE UNIQUE INDEX [IX_ShopWarrantyDevices_ShopWarrantyId_Position] ON [dbo].[ShopWarrantyDevices] ([ShopWarrantyId],[Position])');
INSERT INTO [dbo].[ShopWarrantyDevices] ([CompanyId],[ShopWarrantyId],[Position],[DeviceType],[DeviceModel],[SerialNumber],[ProblemDescription],[Accessories])
 SELECT w.[CompanyId],w.[Id],1,w.[DeviceType],w.[DeviceModel],w.[SerialNumber],w.[ProblemDescription],w.[Accessories]
 FROM [dbo].[ShopWarranties] w
 WHERE NOT EXISTS (SELECT 1 FROM [dbo].[ShopWarrantyDevices] d WHERE d.[ShopWarrantyId]=w.[Id]);";

    public const string Sqlite = @"
CREATE TABLE IF NOT EXISTS [ShopWarrantyDevices] (
 [Id] INTEGER NOT NULL CONSTRAINT [PK_ShopWarrantyDevices] PRIMARY KEY AUTOINCREMENT,
 [CompanyId] INTEGER NULL, [ShopWarrantyId] INTEGER NOT NULL, [Position] INTEGER NOT NULL,
 [DeviceType] TEXT NOT NULL, [DeviceModel] TEXT NULL, [SerialNumber] TEXT NULL,
 [ProblemDescription] TEXT NULL, [Accessories] TEXT NULL,
 CONSTRAINT [FK_ShopWarrantyDevices_ShopWarranties_ShopWarrantyId] FOREIGN KEY ([ShopWarrantyId]) REFERENCES [ShopWarranties] ([Id]) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS [IX_ShopWarrantyDevices_ShopWarrantyId_Position] ON [ShopWarrantyDevices] ([ShopWarrantyId],[Position]);
INSERT INTO [ShopWarrantyDevices] ([CompanyId],[ShopWarrantyId],[Position],[DeviceType],[DeviceModel],[SerialNumber],[ProblemDescription],[Accessories])
 SELECT w.[CompanyId],w.[Id],1,w.[DeviceType],w.[DeviceModel],w.[SerialNumber],w.[ProblemDescription],w.[Accessories]
 FROM [ShopWarranties] w
 WHERE NOT EXISTS (SELECT 1 FROM [ShopWarrantyDevices] d WHERE d.[ShopWarrantyId]=w.[Id]);";

    public static Task EnsureAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync(db.Database.IsSqlite() ? Sqlite : SqlServer);
}
