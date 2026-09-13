using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  خودتعمیرِ اسکیمای دستور کار — نسخه ۱ (اولویت + یادآور مهلت)
//  • EnsureCreated (SQLite) و Migrate (SQL Server) ساختارهای جدید را به
//    دیتابیس‌های قدیمی اضافه نمی‌کنند؛ مثل OfficeEmailSchemaV1 اینجا
//    ستون/جدول تازه را به‌صورت ایمن و idempotent می‌سازیم.
//  • ستون WorkOrders.Priority: اولویت (0=کم 1=عادی 2=بالا 3=فوری)
//  • جدول WorkOrderReminderLogs: ثبت یادآورهای ارسال‌شده مهلت
//    (هر آستانه به‌ازای هر مهلت فقط یک‌بار — بعد از تمدید دوباره فعال می‌شود)
// ============================================================
public static class WorkOrderSchemaV1
{
    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? EnsureSqliteAsync(db) : EnsureSqlServerAsync(db);

    // ==================== SQL Server ====================
    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.WorkOrders', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.WorkOrders', N'Priority') IS NULL
    ALTER TABLE dbo.WorkOrders ADD Priority int NOT NULL DEFAULT(1);");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.WorkOrderReminderLogs', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[WorkOrderReminderLogs](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [OrderId] int NOT NULL,
        [ThresholdHours] int NOT NULL,
        [DueAtSnapshot] datetime2 NOT NULL,
        [SentAt] datetime2 NOT NULL DEFAULT(SYSDATETIME())
    );
    CREATE UNIQUE INDEX [IX_WorkOrderReminderLogs_OrderId_ThresholdHours_DueAtSnapshot]
        ON [dbo].[WorkOrderReminderLogs] ([OrderId], [ThresholdHours], [DueAtSnapshot]);
END");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.WorkOrders', N'U') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WorkOrders_Status_DueAt' AND object_id = OBJECT_ID(N'dbo.WorkOrders'))
    CREATE INDEX [IX_WorkOrders_Status_DueAt] ON [dbo].[WorkOrders] ([Status], [DueAt]);");

        // ---------- موج ۲: تکرارشونده + چک‌لیست زیرکار ----------
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.WorkOrders', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.WorkOrders', N'Recurrence') IS NULL
    ALTER TABLE dbo.WorkOrders ADD Recurrence int NOT NULL DEFAULT(0);
IF OBJECT_ID(N'dbo.WorkOrders', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.WorkOrders', N'RecurrenceParentId') IS NULL
    ALTER TABLE dbo.WorkOrders ADD RecurrenceParentId int NULL;");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.WorkOrderChecklistItems', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[WorkOrderChecklistItems](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [OrderId] int NOT NULL,
        [Text] nvarchar(300) NOT NULL,
        [SortOrder] int NOT NULL DEFAULT(0),
        [IsDone] bit NOT NULL DEFAULT(0),
        [DoneByUserId] int NULL,
        [DoneByName] nvarchar(150) NULL,
        [DoneAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT(SYSDATETIME())
    );
    CREATE INDEX [IX_WorkOrderChecklistItems_OrderId] ON [dbo].[WorkOrderChecklistItems] ([OrderId]);
END");
    }

    // ==================== SQLite ====================
    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        // ستون Priority — فقط اگر جدول هست و ستون نیست
        var hasPriority = false;
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('WorkOrders') WHERE name='Priority'";
            hasPriority = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { /* جدول هنوز ساخته نشده — EnsureCreated آن را با ستون جدید می‌سازد */ }

        if (!hasPriority)
            await SafeAsync(db, "ALTER TABLE WorkOrders ADD COLUMN Priority INTEGER NOT NULL DEFAULT 1;");

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS WorkOrderReminderLogs (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    OrderId INTEGER NOT NULL,
    ThresholdHours INTEGER NOT NULL,
    DueAtSnapshot TEXT NOT NULL,
    SentAt TEXT NOT NULL
);");
        await SafeAsync(db, @"
CREATE UNIQUE INDEX IF NOT EXISTS IX_WorkOrderReminderLogs_OrderId_ThresholdHours_DueAtSnapshot
    ON WorkOrderReminderLogs (OrderId, ThresholdHours, DueAtSnapshot);");
        await SafeAsync(db, @"
CREATE INDEX IF NOT EXISTS IX_WorkOrders_Status_DueAt ON WorkOrders (Status, DueAt);");

        // ---------- موج ۲: تکرارشونده + چک‌لیست زیرکار ----------
        foreach (var (col, ddl) in new[]
        {
            ("Recurrence", "ALTER TABLE WorkOrders ADD COLUMN Recurrence INTEGER NOT NULL DEFAULT 0;"),
            ("RecurrenceParentId", "ALTER TABLE WorkOrders ADD COLUMN RecurrenceParentId INTEGER NULL;"),
        })
        {
            var has = false;
            try
            {
                var conn = db.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('WorkOrders') WHERE name='{col}'";
                has = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
            }
            catch { }
            if (!has) await SafeAsync(db, ddl);
        }

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS WorkOrderChecklistItems (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    OrderId INTEGER NOT NULL,
    Text TEXT NOT NULL,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    IsDone INTEGER NOT NULL DEFAULT 0,
    DoneByUserId INTEGER NULL,
    DoneByName TEXT NULL,
    DoneAt TEXT NULL,
    CreatedAt TEXT NOT NULL
);");
        await SafeAsync(db, @"
CREATE INDEX IF NOT EXISTS IX_WorkOrderChecklistItems_OrderId ON WorkOrderChecklistItems (OrderId);");
    }

    /// <summary>اجرای امن — خطای «شیء تکراری/موجود» راه‌اندازی برنامه را متوقف نکند.</summary>
    private static async Task SafeAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch (Exception ex)
        {
            var m = ex.Message ?? "";
            // تکراری‌ها بی‌صدا؛ سایر خطاها فقط لاگ (مثل الگوی OfficeEmailSchemaV1)
            if (!m.Contains("duplicate", StringComparison.OrdinalIgnoreCase) &&
                !m.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"[DB] WorkOrderSchemaV1: {m}");
        }
    }
}
