using Microsoft.EntityFrameworkCore;
namespace Inventory.Api.Data;
public static class ShopVisitorDiscountSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.ShopVisitorDiscounts', N'U') IS NULL
 CREATE TABLE [dbo].[ShopVisitorDiscounts] ([CompanyId] int NOT NULL, [UserId] int NOT NULL, [MaxDiscountPercent] decimal(18,2) NOT NULL, CONSTRAINT [PK_ShopVisitorDiscounts] PRIMARY KEY ([CompanyId], [UserId]));";
    public const string Sqlite = @"
CREATE TABLE IF NOT EXISTS ShopVisitorDiscounts (CompanyId INTEGER NOT NULL, UserId INTEGER NOT NULL, MaxDiscountPercent TEXT NOT NULL, PRIMARY KEY (CompanyId, UserId));";
    public static Task EnsureAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync(db.Database.IsSqlite() ? Sqlite : SqlServer);
}
