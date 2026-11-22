using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  خودتعمیرِ اسکیمای «بانک ایمیل مخاطبین دبیرخانه» در زمان اجرا
//  مثل OfficeEmailSchemaV1: بدون مایگریشن جدید، جدول در دیتابیس‌های
//  موجود (SQLite و SQL Server) ساخته می‌شود.
//  جدول: Oto_TBL_EmailBank  — آدرس‌های ایمیل استفاده‌شده در ارسال نامه صادره
// ============================================================
public static class EmailBankSchemaV1
{
    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? EnsureSqliteAsync(db) : EnsureSqlServerAsync(db);

    // ==================== SQL Server ====================
    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.Oto_TBL_EmailBank', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Oto_TBL_EmailBank](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [Email] nvarchar(250) NOT NULL,
        [Organization] nvarchar(250) NULL,
        [PersonName] nvarchar(250) NULL,
        [Note] nvarchar(500) NULL,
        [IsActive] bit NOT NULL DEFAULT(1),
        [CreatedAt] datetime2 NOT NULL,
        [LastUsedAt] datetime2 NULL,
        [UseCount] int NOT NULL DEFAULT(0),
        [CreatedByUserId] int NULL,
        [CreatedByName] nvarchar(150) NULL
    );
    CREATE UNIQUE INDEX [UX_Oto_TBL_EmailBank_Email] ON [dbo].[Oto_TBL_EmailBank] ([Email]);
    CREATE INDEX [IX_Oto_TBL_EmailBank_Organization] ON [dbo].[Oto_TBL_EmailBank] ([Organization]);
END");
    }

    // ==================== SQLite ====================
    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        await SafeAsync(db, @"CREATE TABLE IF NOT EXISTS Oto_TBL_EmailBank (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Email TEXT NOT NULL,
    Organization TEXT NULL,
    PersonName TEXT NULL,
    Note TEXT NULL,
    IsActive INTEGER NOT NULL DEFAULT 1,
    CreatedAt TEXT NOT NULL,
    LastUsedAt TEXT NULL,
    UseCount INTEGER NOT NULL DEFAULT 0,
    CreatedByUserId INTEGER NULL,
    CreatedByName TEXT NULL);");

        // COLLATE NOCASE ⇒ مقایسهٔ ایمیل بدون حساسیت به حروف بزرگ/کوچک (مثل SQL Server)
        await SafeAsync(db, @"CREATE UNIQUE INDEX IF NOT EXISTS UX_Oto_TBL_EmailBank_Email
    ON Oto_TBL_EmailBank (Email COLLATE NOCASE);");
        await SafeAsync(db, @"CREATE INDEX IF NOT EXISTS IX_Oto_TBL_EmailBank_Organization
    ON Oto_TBL_EmailBank (Organization);");
    }

    /// <summary>اجرای امن — خطای «جدول/ستون از قبل هست» را می‌پذیریم</summary>
    private static async Task SafeAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch (Exception ex)
        {
            var msg = ex.Message ?? "";
            if (msg.Contains("already") || msg.Contains("duplicate") || msg.Contains("تکراری")) return;
            Console.WriteLine($"[DB] هشدار EmailBankSchemaV1: {ex.Message}");
        }
    }
}
