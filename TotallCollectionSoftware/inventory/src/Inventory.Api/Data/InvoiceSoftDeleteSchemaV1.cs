using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// حذف نرم فاکتورها — افزودن ستون‌های <c>IsDeleted</c>، <c>DeletedAt</c> و <c>DeletedBy</c>.
///
/// <para>
/// <b>چرا؟</b> قاعدهٔ سهمیه (مرحله ۲): «حذف توسط کاربر، مصرف را برنمی‌گرداند».
/// اگر فاکتور فیزیکی پاک شود، دو چیز از دست می‌رود: (۱) تعداد واقعی رکوردها که
/// مبنای «مصرف مؤثر» است، (۲) امکان ممیزی «چه کسی چه چیزی را حذف کرد».
/// پس حذف فقط پرچم می‌زند و رکورد در دیتابیس می‌ماند.
/// </para>
///
/// <para>
/// همهٔ دستورها <b>افزایشی و idempotent</b> هستند: دو بار اجرا شوند خطا نمی‌دهند.
/// </para>
/// </summary>
public static class InvoiceSoftDeleteSchemaV1
{
    /// <summary>ایندکس کمکی برای فیلتر «حذف‌نشده‌ها» در فهرست‌های بزرگ.</summary>
    public const string IndexName = "IX_FacInvoices_IsDeleted_Kind_Date";

    public static async Task EnsureAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite())
        {
            // SQLite: ALTER TABLE ADD COLUMN اگر ستون باشد خطا می‌دهد ⇒ try/catch مثل بقیهٔ اسکیماهای پروژه
            await TryAsync(db, "ALTER TABLE \"FacInvoices\" ADD COLUMN \"IsDeleted\" INTEGER NOT NULL DEFAULT 0;");
            await TryAsync(db, "ALTER TABLE \"FacInvoices\" ADD COLUMN \"DeletedAt\" TEXT NULL;");
            await TryAsync(db, "ALTER TABLE \"FacInvoices\" ADD COLUMN \"DeletedBy\" TEXT NULL;");
            await TryAsync(db,
                $"CREATE INDEX IF NOT EXISTS \"{IndexName}\" ON \"FacInvoices\" (\"IsDeleted\", \"Kind\", \"Date\");");
            return;
        }

        await TryAsync(db, @"
IF OBJECT_ID(N'dbo.FacInvoices', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.FacInvoices', N'IsDeleted') IS NULL
        ALTER TABLE [dbo].[FacInvoices] ADD [IsDeleted] bit NOT NULL CONSTRAINT DF_FacInvoices_IsDeleted DEFAULT 0;

    IF COL_LENGTH(N'dbo.FacInvoices', N'DeletedAt') IS NULL
        ALTER TABLE [dbo].[FacInvoices] ADD [DeletedAt] datetime2 NULL;

    IF COL_LENGTH(N'dbo.FacInvoices', N'DeletedBy') IS NULL
        ALTER TABLE [dbo].[FacInvoices] ADD [DeletedBy] nvarchar(120) NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes
                   WHERE name = N'IX_FacInvoices_IsDeleted_Kind_Date'
                     AND object_id = OBJECT_ID(N'dbo.FacInvoices'))
        CREATE INDEX [IX_FacInvoices_IsDeleted_Kind_Date]
            ON [dbo].[FacInvoices] ([IsDeleted], [Kind], [Date]);
END;");
    }

    private static async Task TryAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch { /* ستون/ایندکس از قبل وجود دارد — بی‌خطر */ }
    }
}
