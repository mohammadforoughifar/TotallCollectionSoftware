using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

// ============================================================
//  آماده‌ساز «دیتابیس قدیمی» برای آزمون مسیر ارتقا
//
//  دلیل وجود: کوئری‌های EXISTS روی خودِ دیتابیس، تنها راه اطمینان از این است که
//  برنامه روی دیتابیس مشتری (که سال‌ها قبل ساخته شده) هم بی‌دردسر بالا می‌آید،
//  نه فقط روی دیتابیس خالی. این برنامه همان وضعیت را بازسازی می‌کند:
//
//    ۱) دیتابیس را از صفر می‌سازد (EnsureCreated → کل مدل، مثل نصب فعلی مشتری)
//    ۲) همهٔ مایگریشن‌ها را در __EFMigrationsHistory «stamp» می‌کند
//       (یعنی: هیچ مایگریشن معلقی نیست)
//    ۳) جدول‌های لایسنس را پاک می‌کند (چون در نسخهٔ قبلی نبودند)
//
//  نتیجه: دیتابیسی که «همهٔ ۳۹ مایگریشن را دارد و خبری از لایسنس نیست» — دقیقاً
//  همان چیزی که اولین بار بعد از آپدیت، DbInitializer باید بدون خطا ارتقا دهد.
// ============================================================

var connectionString = args.FirstOrDefault()
    ?? Environment.GetEnvironmentVariable("LICENSING_TEST_CONN");

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.WriteLine("استفاده: dotnet run --project tests/LicensingUpgrade -- \"<connection string>\"");
    return 1;
}

var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
var databaseName = builder.InitialCatalog;

if (string.IsNullOrWhiteSpace(databaseName) ||
    !databaseName.Contains("Test", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine($"⛔ نام دیتابیس باید شامل «Test» باشد (فعلی: «{databaseName}»). این ابزار دیتابیس را پاک و بازمی‌سازد.");
    return 2;
}

Console.WriteLine($"[Upgrade] دیتابیس هدف: {databaseName}");

var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;

// ---------- ۱) ساخت کامل مدل + ۲) stamp مایگریشن‌ها + ۳) حذف جدول‌های لایسنس ----------
await using (var db = new AppDbContext(options))
{
    // ⚠️ دیتابیس باید از صفر ساخته شود؛ اگر از اجرای قبلی چیزی مانده باشد،
    // اسکیمای نیمه‌کاره (نتیجهٔ Migrate ناقص) باقی می‌ماند و آزمون را مسموم می‌کند.
    await db.Database.EnsureDeletedAsync();
    var created = await db.Database.EnsureCreatedAsync();
    Console.WriteLine($"[Upgrade] دیتابیس از صفر ساخته شد: {created}");

    var migrations = db.Database.GetMigrations().ToList();
    await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
    CREATE TABLE [dbo].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId]));");

    foreach (var id in migrations)
    {
        await db.Database.ExecuteSqlRawAsync(
            "IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = {0}) " +
            "INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES ({0}, '8.0.1')",
            id);
    }

    Console.WriteLine($"[Upgrade] {migrations.Count} مایگریشن stamp شد (هیچ مایگریشن معلقی نماند).");

    // جدول‌های لایسنس را حذف می‌کنیم تا وضعیت «قبل از انتشار لایسنس» بازسازی شود.
    foreach (var table in new[]
             {
                 "LicenseUsageLedger", "LicenseUsageBaselines", "LicenseUsages",
                 "LicenseSessions", "LicenseEvents", "LicenseItemRows", "LicenseGrants", "LicenseInstallations"
             })
    {
        await db.Database.ExecuteSqlRawAsync(
            $"IF OBJECT_ID(N'dbo.[{table}]', N'U') IS NOT NULL DROP TABLE [dbo].[{table}];");
    }

    var remaining = await db.Database
        .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.tables WHERE name LIKE 'License%'")
        .ToListAsync();
    Console.WriteLine($"[Upgrade] جدول‌های License* باقی‌مانده: {remaining.FirstOrDefault()}");
}

// ---------- گزارش وضعیت ----------
await using (var db = new AppDbContext(options))
{
    var tables = await db.Database
        .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.tables")
        .ToListAsync();
    var history = await db.Database
        .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM dbo.__EFMigrationsHistory")
        .ToListAsync();

    Console.WriteLine($"[Upgrade] جدول‌های دیتابیس: {tables.FirstOrDefault()} | ردیف‌های تاریخچهٔ مایگریشن: {history.FirstOrDefault()}");
    Console.WriteLine("[Upgrade] آماده است. حالا برنامه را با --update-database دو بار اجرا کنید.");
}

return 0;
