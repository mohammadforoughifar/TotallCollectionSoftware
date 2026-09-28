using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
// خودتعمیرِ اسکیمای ماژول انبارداری — نسخه ۱
// • افزودن ستون‌های جدید انبارداری به دیتابیس‌های موجود به صورت ایمن و idempotent:
//   - Warehouses.Valuation: روش قیمت‌گذاری پیش‌فرض انبار
//   - ProductCategories.IsVatIncluded / VatRate: ارزش افزوده در سطح گروه کالا
//   - ProductAttributeValues.IsRequired: الزام ویژگی در سطح کالا
//   - InvDocLines.IsVatIncluded / VatRate / VatAmount: اقلام ارزش افزوده در سطرهای سند انبار
// ============================================================
public static class WarehousingSchemaV1
{
    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? EnsureSqliteAsync(db) : EnsureSqlServerAsync(db);

    // ==================== SQL Server ====================
    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        // Warehouses.Valuation
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.Warehouses', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Warehouses', N'Valuation') IS NULL
    ALTER TABLE dbo.Warehouses ADD Valuation int NULL;");

        // ProductCategories.IsVatIncluded / VatRate
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.ProductCategories', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ProductCategories', N'IsVatIncluded') IS NULL
    ALTER TABLE dbo.ProductCategories ADD IsVatIncluded bit NULL;");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.ProductCategories', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ProductCategories', N'VatRate') IS NULL
    ALTER TABLE dbo.ProductCategories ADD VatRate decimal(18,2) NULL;");

        // ProductAttributeValues.IsRequired
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.ProductAttributeValues', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ProductAttributeValues', N'IsRequired') IS NULL
    ALTER TABLE dbo.ProductAttributeValues ADD IsRequired bit NOT NULL DEFAULT(0);");

        // InvDocLines.IsVatIncluded / VatRate / VatAmount
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.InvDocLines', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.InvDocLines', N'IsVatIncluded') IS NULL
    ALTER TABLE dbo.InvDocLines ADD IsVatIncluded bit NOT NULL DEFAULT(0);");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.InvDocLines', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.InvDocLines', N'VatRate') IS NULL
    ALTER TABLE dbo.InvDocLines ADD VatRate decimal(18,2) NOT NULL DEFAULT(0);");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.InvDocLines', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.InvDocLines', N'VatAmount') IS NULL
    ALTER TABLE dbo.InvDocLines ADD VatAmount decimal(18,2) NOT NULL DEFAULT(0);");
    }

    // ==================== SQLite ====================
    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        if (!await ColumnExistsAsync(db, "Warehouses", "Valuation"))
            await SafeAsync(db, "ALTER TABLE Warehouses ADD COLUMN Valuation INTEGER NULL;");

        if (!await ColumnExistsAsync(db, "ProductCategories", "IsVatIncluded"))
            await SafeAsync(db, "ALTER TABLE ProductCategories ADD COLUMN IsVatIncluded INTEGER NULL;");

        if (!await ColumnExistsAsync(db, "ProductCategories", "VatRate"))
            await SafeAsync(db, "ALTER TABLE ProductCategories ADD COLUMN VatRate NUMERIC NULL;");

        if (!await ColumnExistsAsync(db, "ProductAttributeValues", "IsRequired"))
            await SafeAsync(db, "ALTER TABLE ProductAttributeValues ADD COLUMN IsRequired INTEGER NOT NULL DEFAULT 0;");

        if (!await ColumnExistsAsync(db, "InvDocLines", "IsVatIncluded"))
            await SafeAsync(db, "ALTER TABLE InvDocLines ADD COLUMN IsVatIncluded INTEGER NOT NULL DEFAULT 0;");

        if (!await ColumnExistsAsync(db, "InvDocLines", "VatRate"))
            await SafeAsync(db, "ALTER TABLE InvDocLines ADD COLUMN VatRate NUMERIC NOT NULL DEFAULT 0;");

        if (!await ColumnExistsAsync(db, "InvDocLines", "VatAmount"))
            await SafeAsync(db, "ALTER TABLE InvDocLines ADD COLUMN VatAmount NUMERIC NOT NULL DEFAULT 0;");
    }

    private static async Task<bool> ColumnExistsAsync(AppDbContext db, string table, string column)
    {
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}'";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { return false; }
    }

    private static async Task SafeAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch (Exception ex)
        {
            var m = ex.Message ?? "";
            if (!m.Contains("duplicate", StringComparison.OrdinalIgnoreCase) &&
                !m.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"[DB] WarehousingSchemaV1: {m}");
        }
    }
}
