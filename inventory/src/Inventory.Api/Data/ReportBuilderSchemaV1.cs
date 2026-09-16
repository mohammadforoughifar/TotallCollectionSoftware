using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  خودتعمیرِ اسکیمای «گزارش‌ساز شخصی» — نسخه ۱
//  • EnsureCreated (SQLite) و Migrate (SQL Server) جدول‌های تازه را به
//    دیتابیس‌های قدیمی اضافه نمی‌کنند؛ مثل DashboardSchemaV1 اینجا
//    جدول‌ها را به‌صورت ایمن و idempotent می‌سازیم.
//  • UserReports         : گزارش‌های ذخیره‌شدهٔ کاربران
//  • UserReportRoleShares: اشتراک‌گذاری گزارش با نقش‌ها
// ============================================================
public static class ReportBuilderSchemaV1
{
    private static int _done;       // یک بار با موفقیت انجام شده
    private static int _running;    // جلوگیری از اجرای هم‌زمان

    /// <summary>آخرین خطای ساخت جدول (null یعنی موفق). برای نمایش به کاربر/لاگ.</summary>
    public static string? LastError { get; private set; }

    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? EnsureSqliteAsync(db) : EnsureSqlServerAsync(db);

    /// <summary>
    /// تضمین ساخت جدول‌ها فقط یک بار در هر فرایند.
    /// اگر در زمان راه‌اندازی به دلیلی ناموفق بوده، در اولین درخواست دوباره تلاش می‌شود
    /// (خودتعمیری) تا کاربر مجبور به ری‌استارت سرویس نباشد.
    /// </summary>
    public static async Task EnsureOnceAsync(AppDbContext db)
    {
        if (Volatile.Read(ref _done) == 1 && LastError is null) return;
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
        try
        {
            LastError = null;
            await EnsureAsync(db);
            if (LastError is null) Volatile.Write(ref _done, 1);
        }
        finally { Interlocked.Exchange(ref _running, 0); }
    }

    /// <summary>آیا جدول‌های گزارش‌ساز در دیتابیس وجود دارند؟</summary>
    public static async Task<bool> TablesExistAsync(AppDbContext db)
    {
        try
        {
            var n = db.Database.IsSqlite()
                ? await db.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*) AS [Value] FROM sqlite_master WHERE type='table' AND name IN ('UserReports','UserReportRoleShares')")
                    .ToListAsync()
                : await db.Database.SqlQueryRaw<int>(
                    "SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.UserReports', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.UserReportRoleShares', N'U') IS NOT NULL THEN 1 ELSE 0 END AS int) AS [Value]")
                    .ToListAsync();
            return n.Count > 0 && n[0] == 2;
        }
        catch { return false; }
    }

    // ==================== SQL Server ====================
    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.UserReports', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[UserReports](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [UserId] int NOT NULL,
        [Name] nvarchar(140) NOT NULL DEFAULT(N'گزارش بدون نام'),
        [DatasetKey] nvarchar(60) NOT NULL,
        [Module] nvarchar(60) NOT NULL DEFAULT(''),
        [Visibility] int NOT NULL DEFAULT(0),
        [QueryJson] nvarchar(max) NOT NULL DEFAULT('{}'),
        [CreatedAt] datetime2 NOT NULL DEFAULT(SYSDATETIME()),
        [UpdatedAt] datetime2 NOT NULL DEFAULT(SYSDATETIME())
    );
    CREATE INDEX [IX_UserReports_UserId] ON [dbo].[UserReports] ([UserId]);
END");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.UserReportRoleShares', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[UserReportRoleShares](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [ReportId] int NOT NULL,
        [RoleId] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT(SYSDATETIME()),
        CONSTRAINT [FK_UserReportRoleShares_UserReports] FOREIGN KEY ([ReportId])
            REFERENCES [dbo].[UserReports] ([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_UserReportRoleShares_ReportId] ON [dbo].[UserReportRoleShares] ([ReportId]);
    CREATE UNIQUE INDEX [UX_UserReportRoleShares] ON [dbo].[UserReportRoleShares] ([ReportId], [RoleId]);
END");
    }

    // ==================== SQLite ====================
    // توجه: در اسکریپت SQLite برای QueryJson مقدار پیش‌فرض آکولادیِ خالی نگذارید؛
    // پارسر Microsoft.Data.Sqlite روی آن خطای «Expected an ASCII digit» می‌دهد و CREATE TABLE اجرا نمی‌شود
    // (در نتیجه جدول ساخته نمی‌شود و ذخیرهٔ گزارش با خطای «no such table: UserReports» می‌افتد).
    // مقدار پیش‌فرض QueryJson لازم نیست چون EF همیشه آن را ارسال می‌کند.
    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS UserReports (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    UserId INTEGER NOT NULL,
    Name TEXT NOT NULL DEFAULT 'گزارش بدون نام',
    DatasetKey TEXT NOT NULL,
    Module TEXT NOT NULL DEFAULT '',
    Visibility INTEGER NOT NULL DEFAULT 0,
    QueryJson TEXT NOT NULL DEFAULT '',
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_UserReports_UserId ON UserReports (UserId);");

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS UserReportRoleShares (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    ReportId INTEGER NOT NULL,
    RoleId INTEGER NOT NULL,
    CreatedAt TEXT NOT NULL,
    CONSTRAINT FK_UserReportRoleShares_UserReports FOREIGN KEY (ReportId)
        REFERENCES UserReports (Id) ON DELETE CASCADE
);");
        await SafeAsync(db,
            "CREATE INDEX IF NOT EXISTS IX_UserReportRoleShares_ReportId ON UserReportRoleShares (ReportId);");
        await SafeAsync(db,
            "CREATE UNIQUE INDEX IF NOT EXISTS UX_UserReportRoleShares ON UserReportRoleShares (ReportId, RoleId);");
    }

    /// <summary>
    /// اجرای امن — خطای «شیء تکراری/موجود» راه‌اندازی برنامه را متوقف نکند.
    /// اما خطای واقعی (مثلاً نبود مجوز CREATE TABLE) ثبت و به‌صورت واضح لاگ می‌شود تا
    /// کاربر بداند چرا ذخیرهٔ گزارش کار نمی‌کند.
    /// </summary>
    private static async Task SafeAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch (Exception ex)
        {
            var m = ex.Message ?? "";
            if (m.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("already exists", StringComparison.OrdinalIgnoreCase)) return;

            LastError = m;
            Console.WriteLine();
            Console.WriteLine("================= خطای ساخت جداول گزارش‌ساز =================");
            Console.WriteLine($"[DB] ReportBuilderSchemaV1: {m}");
            Console.WriteLine("جدول‌های UserReports / UserReportRoleShares ساخته نشدند؛");
            Console.WriteLine("ذخیرهٔ گزارش کار نخواهد کرد. اسکریپت آماده در مسیر زیر را با یک کاربر دارای");
            Console.WriteLine("مجوز DDL در SSMS اجرا کنید:  inventory/sql/UserDashboards-UserReports.sql");
            Console.WriteLine("=============================================================");
            Console.WriteLine();
        }
    }
}
