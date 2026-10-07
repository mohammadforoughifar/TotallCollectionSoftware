using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  خودتعمیرِ جدول پروفایل شرکت — نسخه ۱
//  • Migrate (SQL Server) و EnsureCreated (SQLite) جدول‌های تازه را به
//    دیتابیس‌های قدیمی اضافه نمی‌کنند؛ مثل AppSettingsSchemaV1 اینجا
//    جدول CompanyProfiles را به‌صورت ایمن و idempotent می‌سازیم.
//  • AppSettings-style single-row table: تعریف شرکت (نام فروشگاه، شماره حساب،
//    شماره تماس) + تنظیمات چاپ فاکتور (سربرگ/زیرنویس، اندازه کاغذ، امضا).
// ============================================================
public static class CompanyProfileSchemaV1
{
    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? EnsureSqliteAsync(db) : EnsureSqlServerAsync(db);

    // ==================== SQL Server ====================
    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.CompanyProfiles', N'U') IS NULL
CREATE TABLE dbo.CompanyProfiles (
    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Name nvarchar(200) NOT NULL CONSTRAINT DF_CompanyProfiles_Name DEFAULT (''),
    AccountNumber nvarchar(100) NULL,
    Phone nvarchar(100) NULL,
    Address nvarchar(500) NULL,
    EconomicCode nvarchar(50) NULL,
    TaxId nvarchar(50) NULL,
    Email nvarchar(200) NULL,
    Website nvarchar(200) NULL,
    PrintHeaderLines nvarchar(1000) NULL,
    PrintFooterLines nvarchar(1000) NULL,
    PrintPaperSize nvarchar(10) NOT NULL CONSTRAINT DF_CompanyProfiles_Paper DEFAULT ('A4'),
    PrintShowSignatures bit NOT NULL CONSTRAINT DF_CompanyProfiles_Sign DEFAULT (1),
    UpdatedAt datetime2 NULL
);");
    }

    // ==================== SQLite ====================
    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS CompanyProfiles (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL DEFAULT '',
    AccountNumber TEXT NULL,
    Phone TEXT NULL,
    Address TEXT NULL,
    EconomicCode TEXT NULL,
    TaxId TEXT NULL,
    Email TEXT NULL,
    Website TEXT NULL,
    PrintHeaderLines TEXT NULL,
    PrintFooterLines TEXT NULL,
    PrintPaperSize TEXT NOT NULL DEFAULT 'A4',
    PrintShowSignatures INTEGER NOT NULL DEFAULT 1,
    UpdatedAt TEXT NULL
);");
    }

    private static async Task SafeAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch (Exception ex)
        {
            var m = ex.Message ?? "";
            if (!m.Contains("duplicate", StringComparison.OrdinalIgnoreCase) &&
                !m.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"[DB] CompanyProfileSchemaV1: {m}");
        }
    }
}
