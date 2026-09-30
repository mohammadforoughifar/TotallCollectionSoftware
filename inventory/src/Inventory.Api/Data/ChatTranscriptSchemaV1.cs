using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// ستون‌های «تبدیل ویس به متن» روی جدول پیام‌های چت (ChatMessages).
///
/// چرا به‌صورت خودتعمیر و نه مایگریشن جدید؟ چون همان‌طور که در DbInitializer توضیح داده شده،
/// روی دیتابیس‌های موجود، مایگریشن squash شده «stamp و رد» می‌شود و جدول/ستون جدید هرگز
/// ساخته نمی‌شود. این کلاس همان الگوی Ensure* بقیهٔ ماژول‌ها را دارد: idempotent و بی‌خطر.
/// </summary>
public static class ChatTranscriptSchemaV1
{
    /// <summary>ستون‌ها به تفکیک دیتابیس (SQL Server نحو ADD بدون COLUMN دارد).</summary>
    private static readonly string[] SqliteColumns =
    {
        "ALTER TABLE ChatMessages ADD COLUMN Transcript TEXT NULL;",
        "ALTER TABLE ChatMessages ADD COLUMN TranscribedAt TEXT NULL;"
    };

    private static readonly string[] SqlServerColumns =
    {
        "ALTER TABLE ChatMessages ADD Transcript nvarchar(max) NULL;",
        "ALTER TABLE ChatMessages ADD TranscribedAt datetime2 NULL;"
    };

    public static Task EnsureSqliteAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? EnsureAsync(db, SqliteColumns) : Task.CompletedTask;

    public static Task EnsureSqlServerAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? Task.CompletedTask : EnsureAsync(db, SqlServerColumns);

    private static async Task EnsureAsync(AppDbContext db, string[] statements)
    {
        foreach (var sql in statements)
        {
            // ستون ممکن است از قبل وجود داشته باشد (دیتابیس تازه‌ساخته با EnsureCreated) — خطا نادیده گرفته می‌شود.
            try { await db.Database.ExecuteSqlRawAsync(sql); } catch { }
        }
    }
}
