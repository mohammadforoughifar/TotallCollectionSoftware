using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  ایندکس‌های داشبورد دستور کار — نسخه ۱
//
//  چرا این کلاس لازم است؟
//    جدول WorkOrderAssignees در مایگریشن اولیه فقط کلید اصلی دارد؛ نه روی
//    OrderId ایندکسی ساخته شده و نه روی UserId. اما تقریباً هر ویجت دستور کار
//    یک نیمه‌جوین روی همین جدول می‌زند:
//        WHERE EXISTS (SELECT 1 FROM WorkOrderAssignees a
//                      WHERE a.OrderId = w.Id AND a.UserId = @me)
//    بدون ایندکس، این کوئری با رشد داده به اسکن کامل تبدیل می‌شود.
//
//  چرا داخل سورس و نه یک فایل SQL دستی؟
//    مثل بقیهٔ *SchemaV1 ها، خودتعمیر و idempotent است و در راه‌اندازی اجرا
//    می‌شود؛ لازم نیست کسی روی دیتابیس تولید اسکریپت اجرا کند.
//    (EnsureCreated روی SQLite و Migrate روی SQL Server جدول/ایندکس تازه را
//     به دیتابیس‌های قدیمی اضافه نمی‌کنند.)
//
//  ایندکس‌های موجود (مثل IX_WorkOrders_Status_DueAt که سرویس یادآور از آن
//  استفاده می‌کند) دست‌نخورده می‌مانند.
// ============================================================
public static class WorkOrderDashboardIndexSchemaV1
{
    /// <summary>آخرین خطای ساخت ایندکس (null یعنی موفق).</summary>
    public static string? LastError { get; private set; }

    /// <summary>تعداد ایندکس‌هایی که در آخرین اجرا ساخته شدند.</summary>
    public static int CreatedCount { get; private set; }

    private static int _done;
    private static int _running;

    /// <summary>فهرست ایندکس‌ها — برای لاگ و عیب‌یابی.</summary>
    public static readonly string[] IndexNames =
    {
        "IX_WorkOrderAssignees_UserId_OrderId",
        "IX_WorkOrderAssignees_OrderId",
        "IX_WorkOrders_Company_Status_DueAt",
        "IX_WorkOrders_Company_CreatedAt",
        "IX_WorkOrders_OwnerUserId",
        "IX_WorkOrderChecklistItems_OrderId_IsDone",
    };

    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? EnsureSqliteAsync(db) : EnsureSqlServerAsync(db);

    /// <summary>تضمین یک‌بار اجرا در هر فرایند (با تلاش دوباره در صورت شکست قبلی).</summary>
    public static async Task EnsureOnceAsync(AppDbContext db)
    {
        if (Volatile.Read(ref _done) == 1 && LastError is null) return;
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
        try
        {
            LastError = null;
            CreatedCount = 0;
            await EnsureAsync(db);
            if (LastError is null) Volatile.Write(ref _done, 1);
        }
        finally { Interlocked.Exchange(ref _running, 0); }
    }

    // ==================== SQL Server ====================
    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        // ۱) حیاتی‌ترین: مسیر «دستورهای من»
        //    INCLUDE باعث می‌شود کوئری پوششی بماند و به جدول اصلی برنگردد.
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.WorkOrderAssignees', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes
                    WHERE name = N'IX_WorkOrderAssignees_UserId_OrderId'
                      AND object_id = OBJECT_ID(N'dbo.WorkOrderAssignees'))
    CREATE NONCLUSTERED INDEX [IX_WorkOrderAssignees_UserId_OrderId]
        ON [dbo].[WorkOrderAssignees] ([UserId], [OrderId])
        INCLUDE ([Done], [RepliedAt], [SeenAt], [OwnerDecision]);");

        // ۲) مسیر «گیرندگانِ این دستور»
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.WorkOrderAssignees', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes
                    WHERE name = N'IX_WorkOrderAssignees_OrderId'
                      AND object_id = OBJECT_ID(N'dbo.WorkOrderAssignees'))
    CREATE NONCLUSTERED INDEX [IX_WorkOrderAssignees_OrderId]
        ON [dbo].[WorkOrderAssignees] ([OrderId]);");

        // ۳) کوئری غالب داشبورد: شرکت + وضعیت + مهلت
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.WorkOrders', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes
                    WHERE name = N'IX_WorkOrders_Company_Status_DueAt'
                      AND object_id = OBJECT_ID(N'dbo.WorkOrders'))
    CREATE NONCLUSTERED INDEX [IX_WorkOrders_Company_Status_DueAt]
        ON [dbo].[WorkOrders] ([CompanyId], [Status], [DueAt])
        INCLUDE ([OwnerUserId], [Priority], [ProjectId], [ExtensionCount])
        WHERE [DeletedAt] IS NULL;");

        // ۴) ویجت‌های بازه‌دار روی تاریخ ثبت
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.WorkOrders', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes
                    WHERE name = N'IX_WorkOrders_Company_CreatedAt'
                      AND object_id = OBJECT_ID(N'dbo.WorkOrders'))
    CREATE NONCLUSTERED INDEX [IX_WorkOrders_Company_CreatedAt]
        ON [dbo].[WorkOrders] ([CompanyId], [CreatedAt])
        INCLUDE ([OwnerUserId], [Status], [ClosedAt])
        WHERE [DeletedAt] IS NULL;");

        // ۵) دستورهای صادرشده توسط من
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.WorkOrders', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes
                    WHERE name = N'IX_WorkOrders_OwnerUserId'
                      AND object_id = OBJECT_ID(N'dbo.WorkOrders'))
    CREATE NONCLUSTERED INDEX [IX_WorkOrders_OwnerUserId]
        ON [dbo].[WorkOrders] ([OwnerUserId])
        WHERE [DeletedAt] IS NULL;");

        // ۶) چک‌لیست: پیشرفت پروژه و درصد تکمیل
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.WorkOrderChecklistItems', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes
                    WHERE name = N'IX_WorkOrderChecklistItems_OrderId_IsDone'
                      AND object_id = OBJECT_ID(N'dbo.WorkOrderChecklistItems'))
    CREATE NONCLUSTERED INDEX [IX_WorkOrderChecklistItems_OrderId_IsDone]
        ON [dbo].[WorkOrderChecklistItems] ([OrderId], [IsDone]);");
    }

    // ==================== SQLite ====================
    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        // ایندکس فیلتردار (WHERE) در SQLite هم پشتیبانی می‌شود.
        // INCLUDE مخصوص SQL Server است و اینجا حذف می‌شود.
        await SafeAsync(db, @"
CREATE INDEX IF NOT EXISTS [IX_WorkOrderAssignees_UserId_OrderId]
    ON [WorkOrderAssignees] ([UserId], [OrderId]);");

        await SafeAsync(db, @"
CREATE INDEX IF NOT EXISTS [IX_WorkOrderAssignees_OrderId]
    ON [WorkOrderAssignees] ([OrderId]);");

        await SafeAsync(db, @"
CREATE INDEX IF NOT EXISTS [IX_WorkOrders_Company_Status_DueAt]
    ON [WorkOrders] ([CompanyId], [Status], [DueAt]) WHERE [DeletedAt] IS NULL;");

        await SafeAsync(db, @"
CREATE INDEX IF NOT EXISTS [IX_WorkOrders_Company_CreatedAt]
    ON [WorkOrders] ([CompanyId], [CreatedAt]) WHERE [DeletedAt] IS NULL;");

        await SafeAsync(db, @"
CREATE INDEX IF NOT EXISTS [IX_WorkOrders_OwnerUserId]
    ON [WorkOrders] ([OwnerUserId]) WHERE [DeletedAt] IS NULL;");

        await SafeAsync(db, @"
CREATE INDEX IF NOT EXISTS [IX_WorkOrderChecklistItems_OrderId_IsDone]
    ON [WorkOrderChecklistItems] ([OrderId], [IsDone]);");
    }

    /// <summary>
    /// اجرای امن — نبودِ جدول یا ایندکس تکراری نباید راه‌اندازی برنامه را متوقف کند.
    /// روی دیتابیس قدیمی که هنوز جدول دستور کار ندارد، بی‌صدا رد می‌شود.
    /// </summary>
    private static async Task SafeAsync(AppDbContext db, string sql)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(sql);
            CreatedCount++;
        }
        catch (Exception ex)
        {
            var m = ex.Message ?? "";
            // ایندکس موجود / جدول موجود → طبیعی است، خطا نیست
            if (m.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
                return;

            // جدول هنوز ساخته نشده (دیتابیس قدیمی) → ایندکس بعداً ساخته می‌شود
            if (m.Contains("no such table", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase))
                return;

            LastError = m;
            Console.WriteLine($"[DB] WorkOrderDashboardIndexSchemaV1: {m}");
        }
    }
}
