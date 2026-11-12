using Microsoft.EntityFrameworkCore;
namespace Inventory.Api.Data;
public static class ShopPrintSettingsSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.ShopPrintSettings', N'U') IS NULL
BEGIN
 CREATE TABLE [dbo].[ShopPrintSettings] ([Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY, [CompanyId] int NULL, [BanksJson] nvarchar(max) NOT NULL);
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.ShopPrintSettings') AND name=N'IX_ShopPrintSettings_CompanyId')
 CREATE UNIQUE INDEX [IX_ShopPrintSettings_CompanyId] ON [dbo].[ShopPrintSettings] ([CompanyId]) WHERE [CompanyId] IS NOT NULL;";
    public const string Sqlite = @"
CREATE TABLE IF NOT EXISTS ShopPrintSettings (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, CompanyId INTEGER NULL, BanksJson TEXT NOT NULL);
CREATE UNIQUE INDEX IF NOT EXISTS IX_ShopPrintSettings_CompanyId ON ShopPrintSettings (CompanyId);";
    public static Task EnsureAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync(db.Database.IsSqlite() ? Sqlite : SqlServer);
}
