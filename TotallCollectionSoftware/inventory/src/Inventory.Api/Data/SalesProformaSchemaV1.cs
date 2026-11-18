using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

public static class SalesProformaSchemaV1
{
    public const string Sqlite = @"CREATE TABLE IF NOT EXISTS SalesProformas (
        Id TEXT NOT NULL PRIMARY KEY, CompanyId INTEGER NOT NULL, Number TEXT NOT NULL,
        CreatedAt TEXT NOT NULL, DocumentJson TEXT NOT NULL, SaleOrderId INTEGER NULL);
        CREATE INDEX IF NOT EXISTS IX_SalesProformas_CompanyId_CreatedAt ON SalesProformas (CompanyId, CreatedAt);";
    public const string SqlServer = @"IF OBJECT_ID(N'dbo.SalesProformas', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.SalesProformas (Id nvarchar(32) NOT NULL PRIMARY KEY, CompanyId int NOT NULL,
            Number nvarchar(40) NOT NULL, CreatedAt datetime2 NOT NULL, DocumentJson nvarchar(max) NOT NULL,
            SaleOrderId int NULL);
        CREATE INDEX IX_SalesProformas_CompanyId_CreatedAt ON dbo.SalesProformas (CompanyId, CreatedAt);
    END";
    public static Task EnsureAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync(db.Database.IsSqlite() ? Sqlite : SqlServer);
}
