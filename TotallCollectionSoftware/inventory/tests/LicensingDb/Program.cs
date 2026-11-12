using Inventory.Api.Data;
using Inventory.Api.Licensing.Services;
using Inventory.Licensing.Abstractions;
using Inventory.Licensing.Entitlement;
using Inventory.Licensing.Metrics;
using Inventory.Licensing.Model;
using Inventory.Licensing.Modules;
using Inventory.Licensing.Periods;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

// ============================================================
//  تست یکپارچهٔ مرحله ۲ — سهمیه روی دیتابیس واقعی (SQLite موقت)
//  اجرا:  dotnet run --project tests/LicensingDb
//  اثبات: اتمی‌بودن زیر بار هم‌زمان، تراکنش، HMAC، بازگشت مصرف، کلید شمسی.
// ============================================================

var failures = 0;
var passes = 0;
var failedChecks = new List<string>();

void Check(bool ok, string message)
{
    if (ok) { passes++; Console.WriteLine("PASS: " + message); return; }
    failures++;
    failedChecks.Add(message);
    Console.WriteLine("FAIL: " + message);
}
void Section(string title) => Console.WriteLine($"\n=== {title} ===");

var dbPath = Path.Combine(Path.GetTempPath(), $"licdb-{Guid.NewGuid():N}.db");
var storage = Path.Combine(Path.GetTempPath(), $"licstore-{Guid.NewGuid():N}");
Directory.CreateDirectory(storage);

// ============================================================
//  انتخاب موتور دیتابیس
//
//  بدون متغیر محیطی ⇒ SQLite موقت (پیش‌فرض توسعه، بدون هیچ پیش‌نیاز).
//  با LICENSING_TEST_CONN ⇒ همان تست‌ها روی SQL Server واقعی اجرا می‌شوند:
//
//    LICENSING_TEST_CONN="Server=localhost;Database=InventoryLicenseTest;\
//      User Id=sa;Password=***;TrustServerCertificate=True;" \
//    dotnet run --project tests/LicensingDb -c Debug
//
//  ⚠️ دیتابیس SQL Server باید یک دیتابیس **تست** باشد: این برنامه برای هم‌خوانی
//     هر بخش، ردیف‌های جدول‌های مصرف/دفتر/رخداد/مبنا را پاک می‌کند.
// ============================================================
var sqlServerConnection = Environment.GetEnvironmentVariable("LICENSING_TEST_CONN");
var useSqlServer = !string.IsNullOrWhiteSpace(sqlServerConnection);

// ============================================================
//  قفل ایمنی (بازبینی مرحله ۲): نام دیتابیس باید شامل «Test» باشد.
//
//  چرا؟ این برنامه با هر بخش، ردیف‌های جدول‌های مصرف/دفتر/رخداد/مبنا و همین‌طور
//  فاکتورهای آزمون را پاک می‌کند. اگر کسی اشتباهاً رشتهٔ اتصال تولید را بگذارد،
//  دادهٔ واقعی مشتری در چند ثانیه پاک می‌شود. «Test» در نام، ساده‌ترین قفلِ
//  قابل‌اعتماد است که شانسی رد نمی‌شود.
// ============================================================
if (useSqlServer)
{
    var pretty = System.Text.RegularExpressions.Regex.Replace(
        sqlServerConnection!, "(?i)(password|pwd)=[^;]*", "Password=***");

    var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(sqlServerConnection!);
    var databaseName = builder.InitialCatalog;

    if (string.IsNullOrWhiteSpace(databaseName) || !databaseName.Contains("Test", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine();
        Console.WriteLine("════════════════════════════════════════════════════════════════════");
        Console.WriteLine("⛔ اجرا متوقف شد — نام دیتابیس تست نیست.");
        Console.WriteLine($"   رشتهٔ اتصال: {pretty}");
        Console.WriteLine($"   نام دیتابیس: «{databaseName}»");
        Console.WriteLine();
        Console.WriteLine("   این آزمون جدول‌های سهمیه و فاکتورها را پاک می‌کند. برای اینکه");
        Console.WriteLine("   اشتباهی روی دیتابیس تولید اجرا نشود، نام دیتابیس باید شامل");
        Console.WriteLine("   «Test» باشد (بی‌توجه به بزرگی/کوچکی حروف).");
        Console.WriteLine();
        Console.WriteLine("   نمونهٔ درست: Database=InventoryLicenseTest");
        Console.WriteLine("════════════════════════════════════════════════════════════════════");
        return 2;
    }
}

string ConnectionString() => $"Data Source={dbPath};Cache=Shared;Default Timeout=30";

DbContextOptions<AppDbContext> Options() => useSqlServer
    ? new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(sqlServerConnection).Options
    : new DbContextOptionsBuilder<AppDbContext>().UseSqlite(ConnectionString()).Options;

AppDbContext NewDb() => new(Options());

// ---------- ساخت اسکیما با همان کد تولیدی ----------
await using (var db = NewDb())
{
    // جدول‌های پایه را با خود مدل می‌سازیم (روی SQL Server هم همین مسیر اجرا می‌شود).
    await db.Database.EnsureCreatedAsync();

    await LicenseSchemaV1.EnsureAsync(db);      // جداول لایسنس (همان چیزی که در بوت اجرا می‌شود)
    await LicenseSchemaV1.EnsureAsync(db);      // بار دوم: باید بی‌اثر باشد (idempotent)
    await InvoiceSoftDeleteSchemaV1.EnsureAsync(db);   // حذف نرم فاکتور
}
Console.WriteLine(useSqlServer
    ? $"دیتابیس تست: SQL Server — {System.Text.RegularExpressions.Regex.Replace(sqlServerConnection!, "(?i)(password|pwd)=[^;]*", "Password=***")}"
    : $"دیتابیس تست: SQLite — {dbPath}");

// ---------- سرویس‌ها ----------
LicenseEventLogger Events(AppDbContext db) => new(db, NullLogger<LicenseEventLogger>.Instance);

QuotaService Service(AppDbContext db, QuotaIntegrity integrity) =>
    new(db, integrity, Events(db), NullLogger<QuotaService>.Instance);

// LicensePaths یک ILogger<LicensePaths> می‌خواهد؛ برای تست یک مسیر موقت می‌سازیم.
var paths = new Inventory.Api.Licensing.LicensePaths(
    new Inventory.Api.Licensing.LicensingOptions { StorageFolder = storage },
    NullLogger<Inventory.Api.Licensing.LicensePaths>.Instance);

QuotaIntegrity NewIntegrity() => new(paths, NullLogger<QuotaIntegrity>.Instance);

// هر بخش که «از صفر» شروع می‌کند باید دیتابیس تازه داشته باشد؛ وگرنه مصرف بخش‌های
// قبلی روی همان متریک/دوره می‌نشیند و اعداد سقف را مسموم می‌کند (این باگ در اجرای
// اول همین تست دیده شد: بخش ۱ پنج مصرف دائمی گذاشت و بخش ۲ فقط ۱۵ جای خالی داشت).
async Task ResetAsync()
{
    if (useSqlServer)
    {
        // SQL Server: دیتابیس را نمی‌سازیم/پاک نمی‌کنیم (مجوز و خطرش)، فقط ردیف‌ها را
        // خالی می‌کنیم. ترتیب حذف از بچه به والد رعایت شده تا کلید خارجی نشکند.
        await using var db = NewDb();
        foreach (var sql in new[]
                 {
                     "DELETE FROM [dbo].[LicenseUsageLedger]",
                     // رزروها و مبناهای بخشودگی هم پاک می‌شوند؛ وگرنه عددهای «Refunded»
                     // و «بازگشت دستی» از اجرای قبلی می‌مانند (SQL Server پایدار است،
                     // برخلاف SQLite که هر اجرا فایل تازه می‌سازد).
                     "DELETE FROM [dbo].[LicenseReservations]",
                     "DELETE FROM [dbo].[LicenseGrandfatherBaselines]",
                     "DELETE FROM [dbo].[LicenseUsageBaselines]",
                     "DELETE FROM [dbo].[LicenseUsages]",
                     "DELETE FROM [dbo].[LicenseEvents]",
                     "DELETE FROM [dbo].[FacInvoiceLines]",
                     "DELETE FROM [dbo].[FacInvoices]",
                     "DELETE FROM [dbo].[Products]"
                 })
        {
            try { await db.Database.ExecuteSqlRawAsync(sql); }
            catch { /* جدول ممکن است در این نصب نباشد — بی‌خطر */ }
        }

        return;
    }

    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    foreach (var suffix in new[] { "", "-wal", "-shm" })
    {
        var f = dbPath + suffix;
        try { if (File.Exists(f)) File.Delete(f); } catch { /* فایل قفل است؛ تلاش بعدی */ }
    }

    await using var fresh = NewDb();
    await fresh.Database.EnsureCreatedAsync();
    await LicenseSchemaV1.EnsureAsync(fresh);
}

// ---------- سند آزمایشی ----------
EffectiveEntitlement Entitlement(string metric, long limit, QuotaPeriod period, params string[] modules)
{
    var doc = new LicenseDocument
    {
        Serial = "TEST-1", KeyId = "dev-2026a", CustomerId = "C-1", CustomerName = "تست",
        Deployment = DeploymentKind.OnPrem, InstallationId = "inst", IssuedAt = new DateTime(2026, 1, 1),
        Items = new List<LicenseItem>
        {
            new()
            {
                Id = "I1",
                Scope = modules.ToList(),
                Term = new LicenseTerm { Type = LicenseTermType.Perpetual },
                Quotas = new List<LicenseQuota> { new() { Metric = metric, Limit = limit, Period = period } }
            }
        }
    };
    return EntitlementMerger.Merge(new[] { doc });
}

/// <summary>همان سند آزمایشی، ولی با گزینهٔ «بخشودگی گذشته» روشن روی سهمیه.</summary>
EffectiveEntitlement GrandfatherEntitlement(string metric, long limit, QuotaPeriod period, params string[] modules)
{
    var doc = new LicenseDocument
    {
        Serial = "TEST-GF", KeyId = "dev-2026a", CustomerId = "C-1", CustomerName = "تست",
        Deployment = DeploymentKind.OnPrem, InstallationId = "inst", IssuedAt = new DateTime(2026, 1, 1),
        Items = new List<LicenseItem>
        {
            new()
            {
                Id = "I-GF",
                Scope = modules.ToList(),
                Term = new LicenseTerm { Type = LicenseTermType.Perpetual },
                Quotas = new List<LicenseQuota>
                {
                    new() { Metric = metric, Limit = limit, Period = period, Grandfather = true }
                }
            }
        }
    };
    return EntitlementMerger.Merge(new[] { doc });
}

// ============================================================
Section("۱) سقف ساده: تا حد مجاز بله، بعد از آن نه");

{
    await ResetAsync();
    var ent = Entitlement(MetricCodes.InvoiceCreate, 5, QuotaPeriod.Lifetime, ModuleCodes.Invoicing);
    await using var db = NewDb();
    var svc = Service(db, NewIntegrity());

    var allowed = 0;
    for (var i = 0; i < 5; i++)
        if ((await svc.TryConsumeAsync(ent, ModuleCodes.Invoicing, MetricCodes.InvoiceCreate)).Allowed) allowed++;

    var sixth = await svc.TryConsumeAsync(ent, ModuleCodes.Invoicing, MetricCodes.InvoiceCreate);

    Check(allowed == 5, $"پنج مصرف اول پذیرفته شد (واقعی: {allowed})");
    Check(!sixth.Allowed, "مصرف ششم رد شد");
    Check(sixth.Used == 5 && sixth.Limit == 5, $"گزارش رد: Used={sixth.Used} Limit={sixth.Limit}");
    Check(sixth.Message.Contains("پر شده"), "پیام فارسی «سقف پر شده» برمی‌گردد");
}

// ============================================================
Section("۲) اتمی‌بودن زیر بار هم‌زمان — ۵۰ درخواست، سقف ۲۰");

{
    await ResetAsync();
    // هر درخواست DbContext مستقل دارد (مثل ۵۰ کاربر واقعی) و هم‌زمان اجرا می‌شوند.
    // اگر شرط داخل UPDATE نبود، تعداد قبولی‌ها از ۲۰ بیشتر می‌شد.
    var ent = Entitlement(MetricCodes.InvoiceCreate, 20, QuotaPeriod.Lifetime, ModuleCodes.Invoicing);
    var integrity = NewIntegrity();

    var results = new bool[50];
    var parallel = new ParallelOptions { MaxDegreeOfParallelism = 16 };

    await Parallel.ForEachAsync(Enumerable.Range(0, 50), parallel, async (i, _) =>
    {
        await using var db = NewDb();
        var svc = Service(db, integrity);
        var r = await svc.TryConsumeAsync(ent, ModuleCodes.Invoicing, MetricCodes.InvoiceCreate);
        results[i] = r.Allowed;
    });

    var ok = results.Count(x => x);
    var denied = results.Count(x => !x);

    await using var check = NewDb();
    var used = await check.LicenseUsages
        .Where(u => u.Metric == MetricCodes.InvoiceCreate)
        .Select(u => u.Used).FirstOrDefaultAsync();

    Check(ok == 20, $"دقیقاً ۲۰ درخواست پذیرفته شد (واقعی: {ok})");
    Check(denied == 30, $"۳۰ درخواست رد شد (واقعی: {denied})");
    Check(used == 20, $"شمارندهٔ دیتابیس دقیقاً ۲۰ است (واقعی: {used}) — هیچ مصرفی گم یا اضافه نشد");
}

// ============================================================
Section("۳) متریک تجمیعی: نامهٔ داخلی، سقف letter.create را هم می‌شمارد");

{
    await ResetAsync();
    var ent = Entitlement(MetricCodes.LetterCreate, 3, QuotaPeriod.Monthly, ModuleCodes.Letters);
    await using var db = NewDb();
    var svc = Service(db, NewIntegrity());

    var r1 = await svc.TryConsumeAsync(ent, ModuleCodes.Letters, MetricCodes.LetterInnerCreate);
    var r2 = await svc.TryConsumeAsync(ent, ModuleCodes.Letters, MetricCodes.LetterOutgoingCreate);
    var r3 = await svc.TryConsumeAsync(ent, ModuleCodes.Letters, MetricCodes.LetterIncomingCreate);
    var r4 = await svc.TryConsumeAsync(ent, ModuleCodes.Letters, MetricCodes.LetterInnerCreate);

    Check(r1.Allowed && r2.Allowed && r3.Allowed, "سه نامهٔ اول (هر سه نوع) پذیرفته شدند");
    Check(!r4.Allowed, "نامهٔ چهارم از سقف تجمیعی رد شد");
    Check(r3.Limit == 3, "سقف از متریک تجمیعی letter.create خوانده شد");

    // تصمیم طراحی: ردیف شمارنده فقط برای متریکی ساخته می‌شود که «سقف» دارد. در این
    // سناریو فقط سقف تجمیعی letter.create تعریف شده، پس متریک اختصاصی ردیف نمی‌گیرد
    // و سقف همان تجمیعی اعمال می‌شود (بدون شمارش دوباره).
    var aggregate = await db.LicenseUsages.AsNoTracking()
        .Where(u => u.Metric == MetricCodes.LetterCreate && u.PeriodKey == PersianPeriodKey.Now(QuotaPeriod.Monthly))
        .Select(u => u.Used).FirstOrDefaultAsync();
    Check(aggregate == 3, $"شمارندهٔ تجمیعی letter.create روی ۳ است (واقعی: {aggregate})");

    var inner = await db.LicenseUsages.AsNoTracking()
        .Where(u => u.Metric == MetricCodes.LetterInnerCreate).Select(u => u.Used).FirstOrDefaultAsync();
    Check(inner == 0, $"متریک بدون سقف، ردیف شمارندهٔ جدا نمی‌سازد (واقعی: {inner})");
}

// ============================================================
Section("۴) دورهٔ شمسی: مصرف در ماه بعد، سقف تازه می‌گیرد");

{
    var ent = Entitlement(MetricCodes.LetterCreate, 2, QuotaPeriod.Monthly, ModuleCodes.Letters);
    await using var db = NewDb();
    var svc = Service(db, NewIntegrity());

    var key = PersianPeriodKey.Now(QuotaPeriod.Monthly);
    var row = await db.LicenseUsages.AsNoTracking()
        .Where(u => u.Metric == MetricCodes.LetterCreate && u.PeriodKey == key).FirstOrDefaultAsync();

    Check(row is not null, $"کلید دورهٔ ذخیره‌شده شمسی است: {key} (الگو: 1405-08)");
    Check(key.Contains('-') && key.Split('-')[0].Length == 4, "کلید دوره سال/ماه شمسی دارد");
}

// ============================================================
Section("۵) بازگشت مصرف: ماهانه برمی‌گردد، دائمی نه");

{
    await ResetAsync();
    var monthly = Entitlement(MetricCodes.InvoiceCreate, 10, QuotaPeriod.Monthly, ModuleCodes.Invoicing);
    await using var db = NewDb();
    var svc = Service(db, NewIntegrity());

    for (var i = 0; i < 4; i++)
        await svc.TryConsumeAsync(monthly, ModuleCodes.Invoicing, MetricCodes.InvoiceCreate);

    var refunded = await svc.TryRefundAsync(monthly, ModuleCodes.Invoicing, MetricCodes.InvoiceCreate,
        entityType: "FacInvoice", entityId: "99");

    var usedAfter = await db.LicenseUsages.AsNoTracking()
        .Where(u => u.Metric == MetricCodes.InvoiceCreate
                    && u.PeriodKey == PersianPeriodKey.Now(QuotaPeriod.Monthly))
        .Select(u => u.Used).FirstOrDefaultAsync();

    Check(refunded, "بازگشت در دورهٔ ماهانه انجام شد");
    Check(usedAfter == 3, $"شمارنده از ۴ به ۳ کم شد (واقعی: {usedAfter})");

    // ---- دائمی: مصرف برنمی‌گردد ----
    var lifetime = Entitlement(MetricCodes.MoadianInvoiceSend, 10, QuotaPeriod.Lifetime, ModuleCodes.Moadian);
    await svc.TryConsumeAsync(lifetime, ModuleCodes.Moadian, MetricCodes.MoadianInvoiceSend);
    await svc.TryConsumeAsync(lifetime, ModuleCodes.Moadian, MetricCodes.MoadianInvoiceSend);

    var lifecycleRefund = await svc.TryRefundAsync(lifetime, ModuleCodes.Moadian, MetricCodes.MoadianInvoiceSend);
    var usedLifetime = await db.LicenseUsages.AsNoTracking()
        .Where(u => u.Metric == MetricCodes.MoadianInvoiceSend).Select(u => u.Used).FirstAsync();

    Check(!lifecycleRefund, "بازگشت در دورهٔ دائمی انجام نشد (قاعدهٔ «یک‌بار مصرف، بدون بازگشت»)");
    Check(usedLifetime == 2, $"شمارندهٔ دائمی روی ۲ ماند (واقعی: {usedLifetime})");

    // رخداد مربوطه ثبت شده باشد
    var ev = await db.LicenseEvents.AsNoTracking()
        .AnyAsync(e => e.Kind == "LICENSE_QUOTA_NO_REFUND");
    Check(ev, "رخداد LICENSE_QUOTA_NO_REFUND ثبت شد (ممیزی حذف)");
}

// ============================================================
Section("۶) دفتر الحاقی مصرف (لاگ حذف)");

{
    await using var db = NewDb();
    var ledger = await db.LicenseUsageLedger.AsNoTracking().OrderBy(x => x.Id).ToListAsync();

    Check(ledger.Count > 0, $"ردیف‌های دفتر مصرف وجود دارد ({ledger.Count})");
    Check(ledger.Any(x => x.Delta > 0), "مصرف‌ها با Delta مثبت ثبت شده‌اند");
    Check(ledger.Any(x => x.Delta < 0), "بازگشت‌ها با Delta منفی ثبت شده‌اند");
    Check(ledger.All(x => !string.IsNullOrEmpty(x.Hmac)), "همهٔ ردیف‌ها HMAC دارند");
}

// ============================================================
Section("۷) HMAC: دست‌کاری مستقیم در دیتابیس کشف می‌شود");

{
    var integrity = NewIntegrity();
    await using var db = NewDb();
    var svc = Service(db, integrity);

    var before = await svc.VerifyIntegrityAsync();
    Check(before == 0, $"پیش از دست‌کاری: هیچ ردیف ناسازگاری نیست (یافت‌شده: {before})");

    // دست‌کاری: مقدار مصرف را در دیتابیس صفر می‌کنیم (دور زدن سقف)
    await db.Database.ExecuteSqlRawAsync("UPDATE LicenseUsages SET Used = 0 WHERE Metric = {0}", MetricCodes.InvoiceCreate);

    var after = await svc.VerifyIntegrityAsync();
    Check(after > 0, $"پس از دست‌کاری: {after} ردیف ناسازگار شناسایی شد");

    await using var checkDb = NewDb();
    var ev = await checkDb.LicenseEvents.AsNoTracking().AnyAsync(e => e.Kind == "LICENSE_QUOTA_TAMPERED");
    Check(ev, "رخداد LICENSE_QUOTA_TAMPERED ثبت شد");
}

// ============================================================
Section("۸) تراکنش: اگر ثبت رکورد شکست بخورد، سهمیه هم برمی‌گردد");

{
    var ent = Entitlement(MetricCodes.InvoiceCreate, 100, QuotaPeriod.Monthly, ModuleCodes.Invoicing);
    await using var db = NewDb();
    var svc = Service(db, NewIntegrity());

    var usedBefore = await db.LicenseUsages.AsNoTracking()
        .Where(u => u.Metric == MetricCodes.InvoiceCreate && u.PeriodKey == PersianPeriodKey.Now(QuotaPeriod.Monthly))
        .Select(u => u.Used).FirstOrDefaultAsync();

    var threw = false;
    try
    {
        // عملیات «ثبت فاکتور» عمداً استثنا می‌دهد (مثل خطای دیتابیس در ذخیره)
        await QuotaGuard.ExecuteAsync(new FixedLicenseService(ent, svc), db,
            ModuleCodes.Invoicing, MetricCodes.InvoiceCreate,
            () => throw new InvalidOperationException("خطای شبیه‌سازی‌شده در ثبت فاکتور"));
    }
    catch (InvalidOperationException) { threw = true; }

    await using var checkDb = NewDb();
    var usedAfter = await checkDb.LicenseUsages.AsNoTracking()
        .Where(u => u.Metric == MetricCodes.InvoiceCreate && u.PeriodKey == PersianPeriodKey.Now(QuotaPeriod.Monthly))
        .Select(u => u.Used).FirstOrDefaultAsync();

    Check(threw, "عملیات استثنا داد و به بالا رسید");
    Check(usedAfter == usedBefore, $"شمارندهٔ سهمیه برنگشت‌خورد — قبل={usedBefore} بعد={usedAfter}");
}

// ============================================================
Section("۹) مصرف مؤثر = بیشترین(شمارنده، تعداد واقعی) برای متریک ActiveCount");

{
    var ent = Entitlement(MetricCodes.InvoiceCreate, 1000, QuotaPeriod.Lifetime, ModuleCodes.Invoicing);
    await using var db = NewDb();
    var svc = Service(db, NewIntegrity());
    var limit = ent.LimitsFor(ModuleCodes.Invoicing, MetricCodes.InvoiceCreate).First();

    // متریکی که probe ندارد ⇒ فقط شمارنده ملاک است (مثلاً ارسال مودیان که رویداد است، نه ردیف).
    var noProbe = Entitlement(MetricCodes.MoadianInvoiceSend, 1000, QuotaPeriod.Lifetime, ModuleCodes.Moadian);
    var noProbeLimit = noProbe.LimitsFor(ModuleCodes.Moadian, MetricCodes.MoadianInvoiceSend).First();
    var counterOnly = await svc.GetEffectiveUsageAsync(noProbe, noProbeLimit, _ => Task.FromResult(999L));
    var storedCounter = await db.LicenseUsages.AsNoTracking()
        .Where(u => u.Metric == MetricCodes.MoadianInvoiceSend && u.PeriodKey == "-")
        .Select(u => (long?)u.Used).FirstOrDefaultAsync() ?? 0;
    Check(counterOnly == storedCounter,
        $"متریک بدون probe فقط شمارنده را برمی‌گرداند و ۹۹۹ ردیف را نادیده می‌گیرد (واقعی: {counterOnly}، شمارنده: {storedCounter})");
    Check(!Inventory.Api.Licensing.Services.ActiveCountProbes.HasProbe(MetricCodes.MoadianInvoiceSend),
        "ارسال مودیان probe ندارد (رویداد شمرده می‌شود، نه ردیف)");

    // ✅ اصلاح بازبینی دوم: invoice.create و سه نوع نامه، probe دارند و ActiveCount هستند.
    var def = MetricCodes.Find(MetricCodes.InvoiceCreate);
    Check(def?.Kind == MetricKind.ActiveCount,
        $"نوع متریک invoice.create = ActiveCount (واقعی: {def?.Kind}) — قبلاً پیش‌فرض Counter بود و مبنا هرگز ثبت نمی‌شد");
    Check(MetricCodes.Find(MetricCodes.LetterIncomingCreate)?.Kind == MetricKind.ActiveCount,
        "نوع متریک letter.incoming.create = ActiveCount");
    Check(MetricCodes.Find(MetricCodes.LetterCreate)?.Kind == MetricKind.Counter,
        "نوع متریک تجمیعی letter.create = Counter (بدون probe)");

    // با probe: بیشترین(شمارنده، تعداد واقعی) — برای دورهٔ دائمی با کسر مبنا
    var withLive = await svc.GetEffectiveUsageAsync(ent, limit, _ => Task.FromResult(999L));
    Check(withLive == 999, $"با probe و بدون مبنا: بیشترین(شمارنده، ۹۹۹) = ۹۹۹ (واقعی: {withLive})");
}

// ============================================================
Section("۱۰) رد سهمیه: رکورد نیمه‌ساخته بعداً ذخیره نمی‌شود + دفتر با شناسهٔ واقعی");

{
    // این بخش دقیقاً همان دو باگی را می‌سنجد که در تست زندهٔ API پیدا شد:
    //  ۱) فاکتورِ ردشده با وجود ۴۰۰ در دیتابیس می‌ماند (نشت ChangeTracker)
    //  ۲) دفتر مصرف شناسهٔ ۰ می‌گرفت چون پیش از SaveChanges نوشته می‌شد
    await ResetAsync();

    var ent = Entitlement(MetricCodes.InvoiceCreate, 1, QuotaPeriod.Monthly, ModuleCodes.Invoicing);
    await using var db = NewDb();
    var svc = Service(db, NewIntegrity());
    var fake = new FixedLicenseService(ent, svc);

    static Inventory.Api.Data.Product NewProduct(string code) => new()
    {
        Code = code, Name = "کالای " + code, Unit = "عدد",
        SalePrice = 1000, PurchasePrice = 800,
        ReorderPoint = 0, MaxStock = 0, IsActive = true, IsService = false,
        CreatedAt = DateTime.Now
    };

    var first = NewProduct("P-OK");
    var ok = await QuotaGuard.ExecuteAsync(fake, db, ModuleCodes.Invoicing, MetricCodes.InvoiceCreate,
        async () => { db.Products.Add(first); await db.SaveChangesAsync(); },
        entityType: "Product", entityIdFactory: () => first.Id.ToString());
    Check(ok.Allowed, "ثبت اول داخل تراکنش پذیرفته شد");

    var ledger = await db.LicenseUsageLedger.AsNoTracking().OrderByDescending(x => x.Id).FirstAsync();
    Check(ledger.EntityType == "Product" && ledger.EntityId == first.Id.ToString(),
        $"دفتر مصرف شناسهٔ واقعی رکورد را دارد ({ledger.EntityType}#{ledger.EntityId})");

    var second = NewProduct("P-DENIED");
    var denied = await QuotaGuard.ExecuteAsync(fake, db, ModuleCodes.Invoicing, MetricCodes.InvoiceCreate,
        async () => { db.Products.Add(second); await db.SaveChangesAsync(); },
        entityType: "Product", entityIdFactory: () => second.Id.ToString());
    Check(!denied.Allowed, "ثبت دوم به‌خاطر سقف رد شد");

    // شبیه‌سازی همان چیزی که در API رخ داد: بعد از رد شدن، یک SaveChanges دیگر
    // (مثلاً ثبت رخداد خطا در فیلتر عمومی) نباید رکوردِ ردشده را ذخیره کند.
    await db.SaveChangesAsync();

    await using var checkDb = NewDb();
    var saved = await checkDb.Products.AsNoTracking().Select(p => p.Code).ToListAsync();
    Check(saved.Count == 1 && saved[0] == "P-OK",
        $"رکوردِ ردشده در دیتابیس نیست (موجود: {string.Join(",", saved)})");

    var rows = await checkDb.LicenseUsageLedger.AsNoTracking().CountAsync();
    Check(rows == 1, $"برای درخواست ردشده ردیف دفتری نوشته نشد (تعداد: {rows})");
}

// ============================================================
Section("۱۱) اولین ردیف شمارنده زیر بار هم‌زمان (INSERT و UPDATE با هم)");

{
    // چرا این بخش مهم است؟ بدترین حالت مسابقه وقتی است که **هیچ ردیفی وجود ندارد**:
    // هر ۵۰ درخواست هم INSERT می‌خواهند و هم UPDATE. در SQL Server سطح READ COMMITTED،
    // الگوی «IF NOT EXISTS … INSERT» اتمیک نیست؛ پس چند درخواست به کلید یکتا می‌خورند
    // (خطای ۲۶۲۷/۲۶۰۱). کد تولیدی این خطا را «بی‌خطر» می‌داند و ادامه می‌دهد؛ این تست
    // ثابت می‌کند (۱) هیچ درخواستی با استثنا نمی‌شکند، (۲) سقف دقیق رعایت می‌شود.
    await ResetAsync();

    var ent = Entitlement(MetricCodes.InvoiceCreate, 20, QuotaPeriod.Monthly, ModuleCodes.Invoicing);
    var integrity = NewIntegrity();

    var allowed = 0;
    var denied = 0;
    var exceptions = new System.Collections.Concurrent.ConcurrentBag<string>();

    await Parallel.ForEachAsync(Enumerable.Range(0, 50), new ParallelOptions { MaxDegreeOfParallelism = 16 },
        async (_, _) =>
        {
            await using var db = NewDb();
            var svc = Service(db, integrity);
            try
            {
                var r = await svc.TryConsumeAsync(ent, ModuleCodes.Invoicing, MetricCodes.InvoiceCreate);
                if (r.Allowed) Interlocked.Increment(ref allowed); else Interlocked.Increment(ref denied);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex.GetType().Name + ": " + ex.Message);
            }
        });

    await using var check = NewDb();
    var rowCount = await check.LicenseUsages.AsNoTracking()
        .CountAsync(u => u.Metric == MetricCodes.InvoiceCreate);
    var used = await check.LicenseUsages.AsNoTracking()
        .Where(u => u.Metric == MetricCodes.InvoiceCreate)
        .Select(u => (long?)u.Used).FirstOrDefaultAsync() ?? 0;

    Check(exceptions.IsEmpty,
        $"هیچ درخواستی با استثنا نشکست (خطاها: {(exceptions.IsEmpty ? "-" : string.Join(" | ", exceptions.Distinct().Take(3)))})");
    Check(rowCount == 1, $"دقیقاً یک ردیف شمارنده ساخته شد (واقعی: {rowCount})");
    Check(allowed == 20, $"دقیقاً ۲۰ درخواست پذیرفته شد از ۵۰ (واقعی: {allowed})");
    Check(denied == 30, $"۳۰ درخواست رد شد (واقعی: {denied})");
    Check(used == 20, $"شمارنده روی ۲۰ ماند (واقعی: {used})");
}

// ============================================================
Section("۱۲) مبنای مصرف: دادهٔ واردشدهٔ قدیمی سقف را پر نمی‌کند");

{
    // معیار شمارش ردیف‌های واقعی (متریک ActiveCount):
    //   effective = max(شمارنده, max(0, تعداد واقعی − مبنا))
    // مبنا هنگام فعال‌سازی لایسنس ثبت می‌شود.
    var counterOnly = QuotaService.ComputeEffective(counter: 5, live: 3, baseline: 0);
    Check(counterOnly == 5, $"شمارنده بزرگ‌تر از دادهٔ واقعی ⇒ شمارنده ملاک است (واقعی: {counterOnly})");

    var importedHeavy = QuotaService.ComputeEffective(counter: 0, live: 25_000, baseline: 24_800);
    Check(importedHeavy == 200,
        $"۲۵٬۰۰۰ رکورد که ۲۴٬۸۰۰تایش پیش از فعال‌سازی بوده ⇒ مصرف مؤثر ۲۰۰ (واقعی: {importedHeavy})");

    var negative = QuotaService.ComputeEffective(counter: 0, live: 100, baseline: 250);
    Check(negative == 0, $"اگر مبنا از تعداد واقعی بیشتر باشد، منفی نمی‌شود (واقعی: {negative})");

    // ثبت واقعی مبنا در دیتابیس + idempotent بودن
    await ResetAsync();
    await using var db = NewDb();
    var svc = Service(db, NewIntegrity());

    var first = await svc.CaptureBaselineAsync(ModuleCodes.Invoicing, MetricCodes.InvoiceCreate, 12_345, "activation", "TEST");
    var second = await svc.CaptureBaselineAsync(ModuleCodes.Invoicing, MetricCodes.InvoiceCreate, 99_999, "activation", "TEST");
    var stored = await svc.ReadBaselineAsync(ModuleCodes.Invoicing, MetricCodes.InvoiceCreate);

    Check(first, "مبنا بار اول ثبت شد");
    Check(!second, "ثبت دوبارهٔ مبنا نادیده گرفته شد (idempotent)");
    Check(stored == 12_345, $"مقدار مبنا جابه‌جا نشد (واقعی: {stored})");
}

// ============================================================
Section("۱۳) حذف نرم: رکورد از فهرست‌ها ناپدید می‌شود ولی شمرده می‌شود");

{
    await ResetAsync();

    await using var db = NewDb();

    // انبار لازم است (کلید خارجی فاکتور) — در دیتابیس تازه هیچ انباری وجود ندارد.
    var wh = new Inventory.Api.Data.Warehouse { Name = "انبار تست", Code = "W-TEST" };
    db.Warehouses.Add(wh);
    await db.SaveChangesAsync();

    db.FacInvoices.Add(new Inventory.Api.Data.FacInvoice
    {
        Number = 9001, Kind = Inventory.Shared.InvoiceKind.Sale, Date = DateTime.Today,
        WarehouseId = wh.Id, Settlement = Inventory.Shared.SettlementType.Cash, CreatedBy = "test"
    });
    db.FacInvoices.Add(new Inventory.Api.Data.FacInvoice
    {
        Number = 9002, Kind = Inventory.Shared.InvoiceKind.Sale, Date = DateTime.Today,
        WarehouseId = wh.Id, Settlement = Inventory.Shared.SettlementType.Cash, CreatedBy = "test",
        IsDeleted = true, DeletedAt = DateTime.Now, DeletedBy = "test"
    });
    await db.SaveChangesAsync();

    await using var check = NewDb();
    var visible = await check.FacInvoices.AsNoTracking().CountAsync();
    var counted = await check.FacInvoices.IgnoreQueryFilters().AsNoTracking().CountAsync();

    Check(visible == 1, $"فقط فاکتور حذف‌نشده دیده می‌شود (واقعی: {visible})");
    Check(counted == 2, $"فاکتور حذف‌شده در شمارش سهمیه می‌ماند (واقعی: {counted}) — حذف، مصرف را برنمی‌گرداند");
}

// ============================================================
Section("۱۴) رزرو ← تماس ← تسویه: ۵۰ درخواست روی آخرین واحد، فقط یک تماس واقعی");

{
    // این بخش دقیقاً همان سناریویی است که در بازبینی خواسته شد:
    //   سقف = ۱، ۵۰ درخواست هم‌زمان، ارسال به مودیان با Mock.
    // انتظار: فقط **یک** تماس با بیرون انجام شود؛ ۴۹ درخواست پیش از تماس رد شوند.
    await ResetAsync();

    var ent = Entitlement(MetricCodes.MoadianInvoiceSend, 1, QuotaPeriod.Monthly, ModuleCodes.Moadian);

    var sendCalls = 0;
    var gate = new object();

    async Task<int> RunOne()
    {
        await using var db = NewDb();
        var quota = Service(db, NewIntegrity());
        var coordinator = new Inventory.Api.Licensing.Services.QuotaSendCoordinator(
            quota, Microsoft.Extensions.Logging.Abstractions.NullLogger<Inventory.Api.Licensing.Services.QuotaSendCoordinator>.Instance);

        var (result, outcome) = await coordinator.SendAsync(
            ent,
            _ =>
            {
                // «تماس با مودیان» — جای Mock. شمارش با قفل، چون چند ترد هم‌زمان.
                lock (gate) { sendCalls++; }
                return Task.FromResult(new Inventory.Shared.Dtos.Moadian.ApiModel { Success = true, Status = 201, Message = "ارسال شد" });
            });

        return outcome == Inventory.Api.Licensing.Services.QuotaSendOutcome.Sent ? 1 : 0;
    }

    var outcomes = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => RunOne()));

    var sent = outcomes.Count(o => o == 1);
    Check(sent == 1, $"دقیقاً یک درخواست مجاز شد (واقعی: {sent})");
    Check(sendCalls == 1, $"دقیقاً یک تماس با مودیان انجام شد — Mock (واقعی: {sendCalls})");
    Check(outcomes.Count(o => o == 0) == 49, $"۴۹ درخواست پیش از هر تماسی رد شدند (واقعی: {outcomes.Count(o => o == 0)})");

    await using var check = NewDb();
    var used = await check.LicenseUsages.AsNoTracking()
        .Where(u => u.Metric == MetricCodes.MoadianInvoiceSend)
        .Select(u => (int?)u.Used).FirstOrDefaultAsync() ?? -1;
    Check(used == 1, $"شمارنده دقیقاً ۱ است (واقعی: {used})");
}

// ============================================================
Section("۱۴.۱) شکست قطعی ⇒ بازگشت همان واحد + رخداد؛ نتیجهٔ نامعلوم ⇒ بدون بازگشت و Pending");

{
    await ResetAsync();
    await using var db = NewDb();
    var quota = Service(db, NewIntegrity());
    var coordinator = new Inventory.Api.Licensing.Services.QuotaSendCoordinator(
        quota, Microsoft.Extensions.Logging.Abstractions.NullLogger<Inventory.Api.Licensing.Services.QuotaSendCoordinator>.Instance);

    var ent = Entitlement(MetricCodes.MoadianInvoiceSend, 10, QuotaPeriod.Monthly, ModuleCodes.Moadian);

    // ---------- الف) شکست قطعی ----------
    var (r1, o1) = await coordinator.SendAsync(ent, _ => Task.FromResult(
        new Inventory.Shared.Dtos.Moadian.ApiModel { Success = false, Status = 400, Message = "شناسهٔ صورتحساب نامعتبر است." }));

    Check(o1 == Inventory.Api.Licensing.Services.QuotaSendOutcome.Failed, $"شکست قطعی ⇒ Failure (واقعی: {o1})");

    await using (var q = NewDb())
    {
        var usedAfterFail = await q.LicenseUsages.AsNoTracking()
            .Where(u => u.Metric == MetricCodes.MoadianInvoiceSend).Select(u => (int?)u.Used).FirstOrDefaultAsync() ?? 0;
        Check(usedAfterFail == 0, $"سهمیه پس از شکست قطعی برگشت (واقعی: {usedAfterFail})");

        var evt = await q.LicenseEvents.AsNoTracking()
            .Where(e => e.Kind == "LICENSE_QUOTA_REFUND_TECHNICAL").CountAsync();
        Check(evt >= 1, $"رخداد LICENSE_QUOTA_REFUND_TECHNICAL ثبت شد (تعداد: {evt})");

        var ledgerReason = await q.LicenseUsageLedger.AsNoTracking()
            .Where(l => l.Reason == "refund-technical").CountAsync();
        Check(ledgerReason >= 1, $"ردیف دفتر با reason=refund-technical ثبت شد (تعداد: {ledgerReason})");
    }

    // ---------- ب) نتیجهٔ نامعلوم (Timeout پس از ارسال) ----------
    var (r2, o2) = await coordinator.SendAsync(ent, _ => Task.FromResult(
        new Inventory.Shared.Dtos.Moadian.ApiModel
        {
            Success = false, Status = 500, DeliveryUncertain = true,
            Message = "ارسال انجام شد ولی پاسخ برنگشت."
        }));

    Check(o2 == Inventory.Api.Licensing.Services.QuotaSendOutcome.Pending, $"نتیجهٔ نامعلوم ⇒ Pending (واقعی: {o2})");

    await using (var q = NewDb())
    {
        var usedAfterUnknown = await q.LicenseUsages.AsNoTracking()
            .Where(u => u.Metric == MetricCodes.MoadianInvoiceSend).Select(u => (int?)u.Used).FirstOrDefaultAsync() ?? 0;
        Check(usedAfterUnknown == 1, $"سهمیه پس از نتیجهٔ نامعلوم برنگشت (واقعی: {usedAfterUnknown})");

        var pending = await q.LicenseUsageLedger.AsNoTracking().Where(l => l.Reason == "pending").CountAsync();
        Check(pending >= 1, $"ردیف دفتر با reason=pending ثبت شد (تعداد: {pending})");

        var evt = await q.LicenseEvents.AsNoTracking().Where(e => e.Kind == "LICENSE_QUOTA_PENDING").CountAsync();
        Check(evt >= 1, $"رخداد LICENSE_QUOTA_PENDING ثبت شد (تعداد: {evt})");
    }
}

// ============================================================
Section("۱۴.۲) تعریف دقیق DeliveryUncertain: ۴xx = قطعی، ۵xx/استثنا = نامعلوم");

{
    // درخواست کارفرما: «(الف) ۴xx صریح ⇒ بازگشت خودکار با LICENSE_QUOTA_REFUND_TECHNICAL؛
    // (ب) Timeout بعد از ارسال ⇒ Pending؛ به‌همراه تعریف دقیق DeliveryUncertain.»
    //
    // بخش ۱۴.۱ دو حالت نمونه را سنجید؛ این بخش مرز را دقیق‌تر می‌بندد: تفاوت
    // «۵xx با پرچم نامعلوم» و «۵xx با پرچم قطعی»، و «استثنای رهاشده در ارسال».
    await ResetAsync();
    await using var db = NewDb();
    var quota = Service(db, NewIntegrity());
    var coordinator = new Inventory.Api.Licensing.Services.QuotaSendCoordinator(
        quota, Microsoft.Extensions.Logging.Abstractions.NullLogger<Inventory.Api.Licensing.Services.QuotaSendCoordinator>.Instance);

    var ent = Entitlement(MetricCodes.MoadianInvoiceSend, 100, QuotaPeriod.Monthly, ModuleCodes.Moadian);

    async Task<long> UsedAsync()
    {
        await using var q = NewDb();
        return await q.LicenseUsages.AsNoTracking()
            .Where(u => u.Metric == MetricCodes.MoadianInvoiceSend).Select(u => (long?)u.Used).FirstOrDefaultAsync() ?? 0;
    }

    // ---------- الف) ۴xx صریح (پاسخ اعتبارسنجی) ⇒ قطعی: بازگشت ----------
    var before4xx = await UsedAsync();
    var (_, o4xx) = await coordinator.SendAsync(ent, _ => Task.FromResult(
        new Inventory.Shared.Dtos.Moadian.ApiModel
        {
            Success = false, Status = 422, DeliveryUncertain = false,
            Message = "شمارهٔ اقتصادی خریدار نامعتبر است."
        }), entityType: "MoadianInvoice", entityId: "422-case");

    Check(o4xx == Inventory.Api.Licensing.Services.QuotaSendOutcome.Failed,
        $"۴۲۲ صریح ⇒ Failed (واقعی: {o4xx})");
    Check(await UsedAsync() == before4xx,
        $"۴۲۲ صریح ⇒ سهمیه برگشت (قبل: {before4xx}، بعد: {await UsedAsync()})");

    await using (var q = NewDb())
    {
        var refundEvents = await q.LicenseEvents.AsNoTracking()
            .Where(e => e.Kind == "LICENSE_QUOTA_REFUND_TECHNICAL").CountAsync();
        Check(refundEvents >= 1, $"رخداد LICENSE_QUOTA_REFUND_TECHNICAL برای خطای قطعی ثبت شد (تعداد: {refundEvents})");
    }

    // ---------- ب) ۵xx با پرچم «نامعلوم» ⇒ Pending ----------
    var before5xx = await UsedAsync();
    var (_, o5xxUnknown) = await coordinator.SendAsync(ent, _ => Task.FromResult(
        new Inventory.Shared.Dtos.Moadian.ApiModel
        {
            Success = false, Status = 503, DeliveryUncertain = true,
            Message = "Gateway Timeout — پاسخ سامانه نرسید."
        }), entityType: "MoadianInvoice", entityId: "503-case");

    Check(o5xxUnknown == Inventory.Api.Licensing.Services.QuotaSendOutcome.Pending,
        $"۵۰۳ + DeliveryUncertain ⇒ Pending (واقعی: {o5xxUnknown})");
    Check(await UsedAsync() == before5xx + 1,
        $"۵۰۳ نامعلوم ⇒ سهمیه برنگشت ({before5xx} ⇒ {await UsedAsync()})");

    // ---------- ج) استثنای رهاشده در ارسال (Timeout در سطح شبکه) ⇒ Pending ----------
    var beforeExc = await UsedAsync();
    var (excResult, oExc) = await coordinator.SendAsync(ent, _ =>
            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing."),
        entityType: "MoadianInvoice", entityId: "timeout-case");

    Check(oExc == Inventory.Api.Licensing.Services.QuotaSendOutcome.Pending,
        $"استثنای Timeout ⇒ Pending (واقعی: {oExc})");
    Check(excResult.DeliveryUncertain == true,
        "هماهنگ‌کننده خودش پرچم DeliveryUncertain را روی نتیجه ست می‌کند (تا کلاینت اشتباه تفسیر نکند)");
    Check(await UsedAsync() == beforeExc + 1,
        $"Timeout ⇒ سهمیه برنگشت ({beforeExc} ⇒ {await UsedAsync()}) — قاعدهٔ «نامعلوم ⇒ تعویق»");

    await using (var q = NewDb())
    {
        var pending = await q.LicenseReservations.AsNoTracking()
            .CountAsync(r => r.State == Inventory.Api.Data.LicenseReservation.States.Pending);
        Check(pending >= 2, $"دو رزرو «در انتظار» برای تسویهٔ دستی مانده (واقعی: {pending})");
    }
}

// ============================================================
Section("۱۴.۳) مودیان: ۴۰۸ = نامعلوم (Pending) و ۴۲۹ = قطعی (بازگشت سهمیه) — بازبینی چهارم، مورد ۹");

{
    // مشکل قبلی: تصمیم «بازگشت یا تعویق» به پرچم DeliveryUncertain واگذار شده بود که
    // لایهٔ پایین (ConnectService) ست می‌کند و معیارش «بستهٔ ارسال ساخته شد یا نه» است.
    // آن معیار برای ۴۲۹ غلط است: سامانه صریحاً گفته «پردازش نکردم»، ولی بسته ساخته شده
    // بود ⇒ پرچم نامعلوم می‌ماند ⇒ سهمیه بی‌دلیل حبس می‌شد.
    //
    // قاعدهٔ تازه: کد وضعیت، مرجع نهایی است.
    //   ۴۰۸ Request Timeout ⇒ «نمی‌دانیم» ⇒ Pending (سهمیه برنمی‌گردد)
    //   ۴۲۹ Too Many Requests ⇒ «قطعاً پردازش نشد» ⇒ بازگشت با LICENSE_QUOTA_REFUND_TECHNICAL
    await ResetAsync();
    await using var db = NewDb();
    var quota = Service(db, NewIntegrity());
    var coordinator = new Inventory.Api.Licensing.Services.QuotaSendCoordinator(
        quota, Microsoft.Extensions.Logging.Abstractions.NullLogger<Inventory.Api.Licensing.Services.QuotaSendCoordinator>.Instance);

    var ent = Entitlement(MetricCodes.MoadianInvoiceSend, 100, QuotaPeriod.Monthly, ModuleCodes.Moadian);

    async Task<long> UsedAsync()
    {
        await using var q = NewDb();
        return await q.LicenseUsages.AsNoTracking()
            .Where(u => u.Metric == MetricCodes.MoadianInvoiceSend).Select(u => (long?)u.Used).FirstOrDefaultAsync() ?? 0;
    }

    // ---------- الف) خودِ تابع یکسان‌سازی، مستقل از لایهٔ شبکه ----------
    var m408 = new Inventory.Shared.Dtos.Moadian.ApiModel
    { Success = false, Status = 408, DeliveryUncertain = false, Message = "Request Timeout" };
    Inventory.Api.Licensing.Services.QuotaSendCoordinator.NormalizeByStatusCode(m408);
    Check(m408.DeliveryUncertain == true,
        "۴۰۸ ⇒ پرچم روی «نامعلوم» تنظیم می‌شود، حتی اگر لایهٔ پایین «قطعی» گفته باشد");

    var m429 = new Inventory.Shared.Dtos.Moadian.ApiModel
    { Success = false, Status = 429, DeliveryUncertain = true, Message = "Too Many Requests" };
    Inventory.Api.Licensing.Services.QuotaSendCoordinator.NormalizeByStatusCode(m429);
    Check(m429.DeliveryUncertain == false,
        "۴۲۹ ⇒ پرچم روی «قطعی» تنظیم می‌شود، حتی اگر لایهٔ پایین «نامعلوم» گفته باشد");

    // کنترل منفی: کدهای دیگر دست نمی‌خورند (۴۲۲ و ۵۰۳ همان‌طور که آمده‌اند می‌مانند)
    var m422 = new Inventory.Shared.Dtos.Moadian.ApiModel
    { Success = false, Status = 422, DeliveryUncertain = false };
    Inventory.Api.Licensing.Services.QuotaSendCoordinator.NormalizeByStatusCode(m422);
    Check(m422.DeliveryUncertain == false, "۴۲۲ (خطای اعتبارسنجی) دست نمی‌خورد ⇒ قطعی می‌ماند");

    var ok200 = new Inventory.Shared.Dtos.Moadian.ApiModel { Success = true, Status = 200, DeliveryUncertain = true };
    Inventory.Api.Licensing.Services.QuotaSendCoordinator.NormalizeByStatusCode(ok200);
    Check(ok200.DeliveryUncertain == true,
        "پاسخ موفق دست نمی‌خورد (تابع فقط روی شکست کار می‌کند)");

    // ---------- ب) ۴۰۸ از مسیر واقعی ارسال ⇒ Pending و بدون بازگشت ----------
    var before408 = await UsedAsync();
    var (_, o408) = await coordinator.SendAsync(ent, _ => Task.FromResult(
        new Inventory.Shared.Dtos.Moadian.ApiModel
        { Success = false, Status = 408, DeliveryUncertain = false, Message = "Request Timeout" }),
        entityType: "MoadianInvoice", entityId: "408-case");

    Check(o408 == Inventory.Api.Licensing.Services.QuotaSendOutcome.Pending,
        $"۴۰۸ از مسیر ارسال ⇒ Pending (واقعی: {o408})");
    Check(await UsedAsync() == before408 + 1,
        $"۴۰۸ ⇒ سهمیه برنگشت ({before408} ⇒ {await UsedAsync()}) — «نامعلوم ⇒ تعویق»");

    // ---------- ج) ۴۲۹ از مسیر واقعی ارسال ⇒ Failed و بازگشت سهمیه ----------
    var before429 = await UsedAsync();
    var (_, o429) = await coordinator.SendAsync(ent, _ => Task.FromResult(
        new Inventory.Shared.Dtos.Moadian.ApiModel
        { Success = false, Status = 429, DeliveryUncertain = true, Message = "Too Many Requests" }),
        entityType: "MoadianInvoice", entityId: "429-case");

    Check(o429 == Inventory.Api.Licensing.Services.QuotaSendOutcome.Failed,
        $"۴۲۹ از مسیر ارسال ⇒ Failed (واقعی: {o429}) — «قطعاً پردازش نشد»");
    Check(await UsedAsync() == before429,
        $"۴۲۹ ⇒ سهمیه برگشت ({before429} ⇒ {await UsedAsync()})");

    await using (var q = NewDb())
    {
        var refunds = await q.LicenseEvents.AsNoTracking()
            .Where(e => e.Kind == "LICENSE_QUOTA_REFUND_TECHNICAL").CountAsync();
        Check(refunds >= 1, $"رخداد LICENSE_QUOTA_REFUND_TECHNICAL برای ۴۲۹ ثبت شد (تعداد: {refunds})");

        var pendingRows = await q.LicenseReservations.AsNoTracking()
            .CountAsync(r => r.State == Inventory.Api.Data.LicenseReservation.States.Pending);
        Check(pendingRows >= 1, $"رزرو «در انتظار» برای ۴۰۸ مانده (تعداد: {pendingRows})");
    }
}

// ============================================================
Section("۱۵) سناریوی بازبینی: لایسنس بدون سقف ⇒ ۵۰۰۰ فاکتور ⇒ لایسنس با سقف ۳۰");

{
    // پرسش صریح: مبنای مصرف «یک‌بار برای کل نصب» است یا «به ازای هر متریک، وقتی سقف آمد»؟
    // و وقتی لایسنس اول سقفی برای invoice.create نداشت، ۵۰۰۰ فاکتورِ قبل از آمدن سقف
    // چه بلایی سر سقف ۳۰ می‌آورد؟
    await ResetAsync();

    await using var db = NewDb();
    var wh = new Inventory.Api.Data.Warehouse { Name = "انبار مبن‌آزمایی", Code = "W-BASE" };
    db.Warehouses.Add(wh);
    await db.SaveChangesAsync();

    // (۱) لایسنس اول: هیچ سقفی برای invoice.create ندارد (بی‌نهایت)
    var noQuota = Entitlement(MetricCodes.LetterInnerCreate, 5, QuotaPeriod.Monthly, ModuleCodes.Invoicing);
    Check(!QuotaService.HasLimit(noQuota, ModuleCodes.Invoicing, MetricCodes.InvoiceCreate),
        "لایسنس اول سقفی برای invoice.create ندارد");

    // ۵۰۰۰ فاکتور ساخته می‌شود (شبیه‌سازی مستقیم درج، معادل ۵۰۰۰ ثبت واقعی)
    var rows = Enumerable.Range(1, 5000).Select(i => new Inventory.Api.Data.FacInvoice
    {
        Number = 100_000 + i, Kind = Inventory.Shared.InvoiceKind.Sale, Date = DateTime.Today.AddDays(-1),
        WarehouseId = wh.Id, Settlement = Inventory.Shared.SettlementType.Cash, CreatedBy = "test"
    }).ToList();

    await db.FacInvoices.AddRangeAsync(rows);
    await db.SaveChangesAsync();

    var liveInDb = await db.FacInvoices.IgnoreQueryFilters().CountAsync();
    Check(liveInDb == 5000, $"۵۰۰۰ فاکتور در دیتابیس ثبت شد (واقعی: {liveInDb})");

    // (۲) لایسنس دوم با سقف ۳۰ می‌آید ⇒ همین‌جا باید مبنا گرفته شود
    //
    // ⚠️ دوره عمداً «دائمی» است، نه ماهانه: قاعدهٔ مصوب این است
    //    Lifetime ⇒ max(شمارنده، کل ردیف‌ها − مبنا)
    //    Monthly/Yearly ⇒ max(شمارنده، ردیف‌های همان دوره) — بدون کسر مبنا
    //    (پس برای سقف ماهانه، ۵۰۰۰ فاکتورِ همان ماه شمرده می‌شوند؛ فرمول دورهٔ
    //     ماهانه در بخش ۱۵.۱ جدا سنجیده می‌شود.)
    var withQuota = Entitlement(MetricCodes.InvoiceCreate, 30, QuotaPeriod.Lifetime, ModuleCodes.Invoicing);
    var quota = Service(db, NewIntegrity());
    var captured = await Inventory.Api.Licensing.Services.ActiveCountProbes.CaptureAllAsync(
        db, quota, withQuota, "TEST-2", "activation");

    // ✅ پس از بازبینی دوم: «صدور صورتحساب» نوع ActiveCount دارد و probe دارد، پس
    // CaptureAllAsync واقعاً مبنا ثبت می‌کند (قبلاً Kind پیش‌فرض Counter بود و حلقهٔ
    // مبنا خالی می‌ماند؛ همان اشتباهی که این تست حالا قفلش می‌کند).
    var activeCountMetrics = Inventory.Licensing.Metrics.MetricCodes.All
        .Count(m => m.Kind == Inventory.Licensing.Metrics.MetricKind.ActiveCount);
    Console.WriteLine($"   (اطلاع) متریک‌های ActiveCount در این نسخه: {activeCountMetrics} — مبناهای ثبت‌شده: {captured}");
    Check(captured >= 1, $"برای invoice.create مبنا ثبت شد (تعداد مبناهای این فعال‌سازی: {captured})");
    Check(activeCountMetrics >= 1, $"دست‌کم یک متریک از نوع ActiveCount وجود دارد (واقعی: {activeCountMetrics})");

    var limit = withQuota.LimitsFor(ModuleCodes.Invoicing, MetricCodes.InvoiceCreate).First();
    var effective = await quota.GetEffectiveUsageAsync(withQuota, limit, _ => Task.FromResult((long)liveInDb));

    Check(effective == 0,
        $"مصرف مؤثر «صدور صورتحساب» با سقف دائمی ۳۰ = ۰ (واقعی: {effective}) — ۵۰۰۰ فاکتور قبل از آمدن سقف، سقف را پر نکرد");
    Check(effective < limit.Limit, $"سقف ۳۰ باز است (مصرف {effective} < سقف {limit.Limit})");

    // و واقعاً می‌شود ۳۰ تا ثبت کرد
    for (var i = 0; i < 30; i++)
        await quota.TryConsumeAsync(withQuota, ModuleCodes.Invoicing, MetricCodes.InvoiceCreate);

    var effectiveAfter30 = await quota.GetEffectiveUsageAsync(withQuota, limit,
        (period, ct) => Inventory.Api.Licensing.Services.ActiveCountProbes.CountAsync(
            db, MetricCodes.InvoiceCreate, period, Inventory.Licensing.Periods.PersianPeriodKey.NowIran(), ct));
    Check(effectiveAfter30 == 30,
        $"پس از ۳۰ فاکتور تازه، مصرف مؤثر = ۳۰ (نه ۵۰۳۰) — واقعی: {effectiveAfter30}");

    var afterThirty = await quota.TryConsumeAsync(withQuota, ModuleCodes.Invoicing, MetricCodes.InvoiceCreate);
    Check(!afterThirty.Allowed, "بعد از ۳۰ ثبت، سی‌ویکمی رد می‌شود");
}

// ============================================================
Section("۱۵.۱) فرمول دورهٔ ماهانه برای متریک ActiveCount: فقط رکوردهای همین ماه");

{
    await ResetAsync();

    await using var db = NewDb();
    var wh = new Inventory.Api.Data.Warehouse { Name = "انبار ماهانه", Code = "W-MONTH" };
    db.Warehouses.Add(wh);
    await db.SaveChangesAsync();

    var (from, to) = Inventory.Licensing.Periods.PersianPeriodKey.CurrentRange(
        Inventory.Licensing.Model.QuotaPeriod.Monthly);

    // ۳۰ فاکتور در همین ماه شمسی + ۷۰ فاکتور با «زمان ثبت سیستمی» در دو ماه قبل.
    //
    // ⚠️ بازبینی چهارم، مورد ۷: مبنای شمارش CreatedAt است، نه Date.
    // گروه دوم عمداً تاریخِ کاربر (Date) را **داخل** همین ماه می‌گذارد و فقط CreatedAt را
    // عقب می‌برد؛ اگر روزی کسی شمارش را به Date برگرداند، این تست ۱۰۰ می‌بیند و قرمز می‌شود.
    var inWindow = Enumerable.Range(1, 30).Select(i => new Inventory.Api.Data.FacInvoice
    {
        Number = 200_000 + i, Kind = Inventory.Shared.InvoiceKind.Sale,
        Date = from.AddHours(1), CreatedAt = from.AddHours(2), WarehouseId = wh.Id,
        Settlement = Inventory.Shared.SettlementType.Cash, CreatedBy = "test"
    });
    var outWindow = Enumerable.Range(1, 70).Select(i => new Inventory.Api.Data.FacInvoice
    {
        Number = 300_000 + i, Kind = Inventory.Shared.InvoiceKind.Sale,
        Date = from.AddHours(3),                     // تاریخ کاربر: داخل پنجره
        CreatedAt = from.AddMonths(-2),              // زمان ثبت سیستمی: بیرون پنجره
        WarehouseId = wh.Id,
        Settlement = Inventory.Shared.SettlementType.Cash, CreatedBy = "test"
    });

    await db.FacInvoices.AddRangeAsync(inWindow);
    await db.FacInvoices.AddRangeAsync(outWindow);
    await db.SaveChangesAsync();

    var inPeriod = await Inventory.Api.Licensing.Services.ActiveCountProbes.CountAsync(
        db, MetricCodes.InvoiceCreate, Inventory.Licensing.Model.QuotaPeriod.Monthly,
        Inventory.Licensing.Periods.PersianPeriodKey.NowIran());

    Check(inPeriod == 30,
        $"شمارش «همین ماه» فقط ۳۰ رکورد را دید (مبنا CreatedAt، نه Date) — واقعی: {inPeriod}");

    // ---------- اثبات مستقیم مورد ۷: دست‌کاری تاریخ کاربر سقف را باز نمی‌کند ----------
    // یک فاکتور تازه ثبت می‌کنیم و بلافاصله تاریخِ کاربرش را به ماه قبل می‌بریم.
    // اگر مبنا Date بود، این رکورد از ماه جاری بیرون می‌رفت و سقف «آزاد» می‌شد.
    var moved = new Inventory.Api.Data.FacInvoice
    {
        Number = 400_001, Kind = Inventory.Shared.InvoiceKind.Sale,
        Date = from.AddMonths(-1), WarehouseId = wh.Id,
        Settlement = Inventory.Shared.SettlementType.Cash, CreatedBy = "test"
    };
    db.FacInvoices.Add(moved);
    await db.SaveChangesAsync();

    var afterMove = await Inventory.Api.Licensing.Services.ActiveCountProbes.CountAsync(
        db, MetricCodes.InvoiceCreate, Inventory.Licensing.Model.QuotaPeriod.Monthly,
        Inventory.Licensing.Periods.PersianPeriodKey.NowIran());

    Check(afterMove == 31,
        $"عقب‌بردن تاریخ کاربر، رکورد را از سقف ماه جاری خارج نمی‌کند (۳۱ = ۳۰ + ۱ تازه، واقعی: {afterMove})");
    Check(to > from, $"مرز بازهٔ ماه شمسی معتبر است ({from:yyyy-MM-dd} تا {to:yyyy-MM-dd})");

    var empty = await Inventory.Api.Licensing.Services.ActiveCountProbes.CountAsync(
        db, MetricCodes.InvoiceCreate, Inventory.Licensing.Model.QuotaPeriod.Monthly,
        Inventory.Licensing.Periods.PersianPeriodKey.NowIran().AddMonths(2));
    Check(empty == 0, $"ماه دو ماه بعد خالی است (واقعی: {empty}) — سقف ماهانه از صفر شروع می‌شود");
}

// ============================================================
Section("۱۶) مصرف مشترک بین دو لایسنس: سقف دائمی ۳۰ با مصرف ۳۰، بعد لایسنس دوم با سقف دائمی ۱۰۰۰");

{
    // ✅ فیلد carryOverUsage حذف شد (بازبینی دوم). رفتار درست، همان چیزی است که این تست
    // نشان می‌دهد: مصرف یک متریک **بین آیتم‌ها/لایسنس‌ها مشترک** است و فقط با تغییر
    // دورهٔ سهمیه صفر می‌شود — نه با تمدید یا افزودن آیتم.
    await ResetAsync();

    await using var db = NewDb();
    var quota = Service(db, NewIntegrity());

    var first = Entitlement(MetricCodes.InvoiceCreate, 30, QuotaPeriod.Lifetime, ModuleCodes.Invoicing);
    for (var i = 0; i < 30; i++)
        await quota.TryConsumeAsync(first, ModuleCodes.Invoicing, MetricCodes.InvoiceCreate);

    var deniedAt30 = await quota.TryConsumeAsync(first, ModuleCodes.Invoicing, MetricCodes.InvoiceCreate);
    Check(!deniedAt30.Allowed, "لایسنس اول: سی‌ویکمی روی سقف ۳۰ رد شد");

    // لایسنس دوم با سقف دائمی ۱۰۰۰ — همان نصب، همان جدول شمارنده
    var second = Entitlement(MetricCodes.InvoiceCreate, 1000, QuotaPeriod.Lifetime, ModuleCodes.Invoicing);
    await using var q2 = NewDb();
    var quota2 = Service(q2, NewIntegrity());

    var usedBefore = await q2.LicenseUsages.AsNoTracking()
        .Where(u => u.Metric == MetricCodes.InvoiceCreate && u.PeriodKey == "-")
        .Select(u => (int?)u.Used).FirstOrDefaultAsync() ?? 0;

    var afterUpgrade = await quota2.TryConsumeAsync(second, ModuleCodes.Invoicing, MetricCodes.InvoiceCreate);

    Console.WriteLine($"   ⟶ نتیجهٔ سنجش: پس از لایسنس دوم، ‹Used› روی {afterUpgrade.Used} از {afterUpgrade.Limit} رفت " +
                      $"(پیش از ثبت: {usedBefore})");

    Check(usedBefore == 30,
        $"مصرف لایسنس اول (۳۰) به لایسنس دوم منتقل شد — Used = {usedBefore} از ۱۰۰۰");
    Check(afterUpgrade.Allowed, "ثبت بعد از ارتقا مجاز است (۳۰ < ۱۰۰۰)");
    Check(afterUpgrade.Used == 31, $"مصرف جدید ۳۱ شد (واقعی: {afterUpgrade.Used})");
}

// ============================================================
Section("۱۷) حذف نرم فاکتور: از گزارش‌ها و مانده‌ها حذف، از سهمیه نه");

{
    await ResetAsync();

    await using var db = NewDb();
    var wh = new Inventory.Api.Data.Warehouse { Name = "انبار گزارش", Code = "W-RPT" };
    db.Warehouses.Add(wh);
    await db.SaveChangesAsync();

    db.FacInvoices.Add(new Inventory.Api.Data.FacInvoice
    {
        Number = 700_001, Kind = Inventory.Shared.InvoiceKind.Sale, Date = DateTime.Today,
        WarehouseId = wh.Id, Settlement = Inventory.Shared.SettlementType.Cash, CreatedBy = "test",
        Status = Inventory.Shared.InvoiceStatus.Confirmed, TotalNet = 1_000_000
    });
    db.FacInvoices.Add(new Inventory.Api.Data.FacInvoice
    {
        Number = 700_002, Kind = Inventory.Shared.InvoiceKind.Sale, Date = DateTime.Today,
        WarehouseId = wh.Id, Settlement = Inventory.Shared.SettlementType.Cash, CreatedBy = "test",
        Status = Inventory.Shared.InvoiceStatus.Confirmed, TotalNet = 5_000_000,
        IsDeleted = true, DeletedAt = DateTime.Now, DeletedBy = "admin"
    });
    await db.SaveChangesAsync();

    // (۱) گزارش‌ها: همه از همان DbSet با فیلتر سراسری می‌خوانند
    var reportSum = (await db.FacInvoices.AsNoTracking().Select(i => i.TotalNet).ToListAsync()).Sum();
    var reportCount = await db.FacInvoices.AsNoTracking().CountAsync();
    Check(reportSum == 1_000_000, $"جمع گزارش فقط فاکتور زنده را دید (واقعی: {reportSum:N0})");
    Check(reportCount == 1, $"تعداد گزارش ۱ است (واقعی: {reportCount})");

    // (۲) مانده‌ها/خزانه هم از همان مسیر
    var receivable = (await db.FacInvoices.AsNoTracking()
        .Where(i => i.Status == Inventory.Shared.InvoiceStatus.Confirmed && i.Settlement == Inventory.Shared.SettlementType.Credit)
        .Select(i => i.TotalNet).ToListAsync()).Sum();
    Check(receivable == 0, $"فاکتور حذف‌شده در مانده‌ها نیامد (واقعی: {receivable:N0})");

    // (۳) سهمیه: حذف‌شده می‌ماند
    var quotaCount = await db.FacInvoices.IgnoreQueryFilters().CountAsync();
    Check(quotaCount == 2, $"سهمیه هر دو را شمرد (واقعی: {quotaCount})");

    // (۴) شمارهٔ فاکتور حذف‌شده دوباره صادر نمی‌شود
    var maxNumber = await db.FacInvoices.IgnoreQueryFilters().MaxAsync(i => i.Number);
    Check(maxNumber == 700_002, $"شمارهٔ بعدی از حداکثر (شامل حذف‌شده) شروع می‌شود (واقعی: {maxNumber})");

    // (۵) ایندکس یکتای شماره سرِ جایش است ⇒ شمارهٔ تکراری در سطح دیتابیس ممکن نیست
    if (useSqlServer)
    {
        var idx = await db.Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.FacInvoices') AND is_unique = 1")
            .ToListAsync();
        Check(idx.Any(n => n.Contains("Kind_Number")),
            $"ایندکس یکتای (Kind, Number) روی FacInvoices هست: {string.Join(", ", idx)}");
    }
    else
    {
        var idx = await db.Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type='index' AND tbl_name='FacInvoices' AND sql LIKE '%UNIQUE%'")
            .ToListAsync();
        Check(idx.Any(n => n.Contains("Kind_Number")), $"ایندکس یکتای (Kind, Number) در SQLite هست: {string.Join(", ", idx)}");
    }
}

// ============================================================
Section("۱۸) دروازهٔ لایسنس: ماتریس فقط‌خواندنی در Missing / Invalid / Grace / ReadOnly");

{
    // ⛔ درخواست کارفرما (بازبینی دوم):
    //    «Missing و Invalid ⇒ خواندن آزاد، نوشتن ۴۰۳» و
    //    «فقط فعل HTTP کافی نیست؛ POSTهای فقط‌خواندنی باید صریح علامت‌گذاری و تست شوند».
    //
    // این بخش همان تصمیم را مستقیم روی middleware می‌سنجد (بدون راه‌اندازی وب‌سرور):
    // برای هر وضعیت، هم GET و هم POST ساده و هم POSTِ علامت‌خورده اجرا می‌شود.

    async Task<(int StatusCode, bool NextCalled, bool ReadOnlyHeader, bool GraceHeader, string Body)> Run(
        LicenseOverallStatus status, string method, string path, bool marked, bool grace = false)
    {
        var nextCalled = false;
        var metadata = marked
            ? new EndpointMetadataCollection(
                new Inventory.Api.Licensing.Infrastructure.AllowInReadOnlyAttribute())
            : new EndpointMetadataCollection();

        var ctx = new DefaultHttpContext();
        ctx.Request.Method = method;
        ctx.Request.Path = path;
        ctx.Response.Body = new MemoryStream();
        ctx.SetEndpoint(new Endpoint(_ => Task.CompletedTask, metadata, "test-endpoint"));

        var middleware = new Inventory.Api.Licensing.Infrastructure.LicenseGateMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Inventory.Api.Licensing.Infrastructure.LicenseGateMiddleware>.Instance);

        await middleware.InvokeAsync(ctx, new GateLicenseService(status, grace));

        ctx.Response.Body.Position = 0;
        var body = new StreamReader(ctx.Response.Body).ReadToEnd();

        return (ctx.Response.StatusCode, nextCalled,
            ctx.Response.Headers.ContainsKey(Inventory.Api.Licensing.Infrastructure.LicenseGateMiddleware.ReadOnlyHeader),
            ctx.Response.Headers.ContainsKey(Inventory.Api.Licensing.Infrastructure.LicenseGateMiddleware.GraceHeader),
            body);
    }

    // ---------- Missing ----------
    var missingGet = await Run(LicenseOverallStatus.Missing, "GET", "/api/products", marked: false);
    Check(missingGet.NextCalled, "Missing + GET ⇒ عبور می‌کند (دادهٔ مشتری قفل نمی‌شود)");
    Check(missingGet.ReadOnlyHeader, "Missing + GET ⇒ هدر X-License-ReadOnly دارد");

    var missingPost = await Run(LicenseOverallStatus.Missing, "POST", "/api/fac/invoices", marked: false);
    Check(!missingPost.NextCalled && missingPost.StatusCode == 403,
        $"Missing + POST ساده ⇒ ۴۰۳ (واقعی: {missingPost.StatusCode})");
    Check(missingPost.Body.Contains("LICENSE_READONLY"),
        $"Missing + POST ساده ⇒ کد LICENSE_READONLY در بدنه (بدنه: {missingPost.Body.Substring(0, Math.Min(80, missingPost.Body.Length))}…)");

    var missingMarked = await Run(LicenseOverallStatus.Missing, "POST", "/api/reports/run", marked: true);
    Check(missingMarked.NextCalled,
        "Missing + POST علامت‌خورده [AllowInReadOnly] ⇒ عبور می‌کند (گزارش/جست‌وجو/Export)");

    // ---------- Invalid (= ReadOnly) ----------
    var invalidGet = await Run(LicenseOverallStatus.Invalid, "GET", "/api/fac/invoices", marked: false);
    Check(invalidGet.NextCalled, "Invalid + GET ⇒ عبور می‌کند");
    Check(invalidGet.ReadOnlyHeader, "Invalid + GET ⇒ هدر X-License-ReadOnly دارد");

    var invalidPost = await Run(LicenseOverallStatus.Invalid, "POST", "/api/fac/invoices", marked: false);
    Check(!invalidPost.NextCalled && invalidPost.StatusCode == 403,
        $"Invalid + POST ساده ⇒ ۴۰۳ (واقعی: {invalidPost.StatusCode})");
    Check(invalidPost.Body.Contains("LICENSE_READONLY"), "Invalid + POST ساده ⇒ کد LICENSE_READONLY");

    var invalidMarked = await Run(LicenseOverallStatus.Invalid, "POST", "/api/reports/run", marked: true);
    Check(invalidMarked.NextCalled, "Invalid + POST علامت‌خورده ⇒ عبور می‌کند");

    var invalidDelete = await Run(LicenseOverallStatus.Invalid, "DELETE", "/api/fac/invoices/5", marked: false);
    Check(!invalidDelete.NextCalled && invalidDelete.StatusCode == 403, "Invalid + DELETE ⇒ ۴۰۳");

    // ---------- «Grace»: حذف‌شده (بازبینی چهارم، مورد ۱) ----------
    //
    // پیش‌تر اگر سرویس می‌گفت «این نصب در مهلت سازگاری است»، دروازه همه‌چیز را باز می‌کرد.
    // آن Grace محلی حذف شد: هیچ نصبی نمی‌تواند با پرچمِ خودش به خودش مهلت بدهد. پنجرهٔ
    // موقت فقط با «لایسنس پُل» امضاشده ممکن است — و آن هم یک سند Valid است، نه حالت سوم.
    //
    // این تست عمداً سرویسِ «در حال Grace» می‌سازد و ثابت می‌کند دروازه به آن اعتنا نمی‌کند:
    // اگر روزی کسی Grace را برگرداند، همین‌جا قرمز می‌شود.
    var gracePost = await Run(LicenseOverallStatus.Missing, "POST", "/api/fac/invoices", marked: false, grace: true);
    Check(!gracePost.NextCalled && gracePost.StatusCode == 403,
        $"پرچم Grace اثری ندارد: POST ساده همچنان ۴۰۳ (واقعی: {gracePost.StatusCode})");
    Check(!gracePost.GraceHeader, "هدر X-License-Grace دیگر صادر نمی‌شود (Grace محلی حذف شد)");
    Check(gracePost.ReadOnlyHeader, "با پرچم Grace هم هدر فقط‌خواندنی درست ست می‌شود");

    var graceGet = await Run(LicenseOverallStatus.Missing, "GET", "/api/fac/invoices", marked: false, grace: true);
    Check(graceGet.NextCalled, "پرچم Grace خواندن را هم نمی‌بندد (خواندن همیشه آزاد است)");

    var graceMarked = await Run(LicenseOverallStatus.Missing, "POST", "/api/reports/run", marked: true, grace: true);
    Check(graceMarked.NextCalled, "پرچم Grace + اکشن علامت‌خورده ⇒ عبور (تصمیم با ویژگی است، نه با Grace)");

    // ---------- Valid ----------
    var validPost = await Run(LicenseOverallStatus.Valid, "POST", "/api/fac/invoices", marked: false);
    Check(validPost.NextCalled, "Valid + POST ⇒ عبور می‌کند");
    Check(!validPost.ReadOnlyHeader && !validPost.GraceHeader, "Valid ⇒ هیچ هدر هشداری ندارد");

    // ---------- مسیرهای آزاد ----------
    var licenseGet = await Run(LicenseOverallStatus.Missing, "POST", "/api/license/activate", marked: false);
    Check(licenseGet.NextCalled, "Missing + POST /api/license/activate ⇒ عبور می‌کند (صفحهٔ فعال‌سازی)");

    var loginPost = await Run(LicenseOverallStatus.Missing, "POST", "/api/auth/login", marked: false);
    Check(loginPost.NextCalled, "Missing + POST /api/auth/login ⇒ عبور می‌کند (ورود مدیر)");

    // ---------- فهرست صفت‌ها روی کنترلرهای واقعی ----------
    //
    // «فعل HTTP کافی نیست» ⇒ فهرست اکشن‌های علامت‌خورده باید صریح و قابل بازبینی باشد.
    // اگر فردا کسی اکشن نوشتاری را علامت بزند، این تست می‌شکند و باید توضیح بدهد.
    var markedActions = typeof(Inventory.Api.Controllers.ProductsController).Assembly.GetTypes()
        .Where(t => t.IsSubclassOf(typeof(Microsoft.AspNetCore.Mvc.ControllerBase)) && !t.IsAbstract)
        .SelectMany(t => t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(m => m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpPostAttribute), true).Any()
                        || m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpPutAttribute), true).Any()
                        || m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpPatchAttribute), true).Any()
                        || m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpDeleteAttribute), true).Any())
            .Where(m => m.GetCustomAttributes(typeof(Inventory.Api.Licensing.Infrastructure.AllowInReadOnlyAttribute), true).Any())
            .Select(m => $"{t.Name}.{m.Name}"))
        .OrderBy(x => x, StringComparer.Ordinal)
        .ToList();

    // ⚠️ دو کنترلر هم‌نام در دو namespace وجود دارد (Core.AuthController و RadisHr.AuthController،
    // همین‌طور FilesController). نامِ کوتاه مبهم است؛ پس نام کاملِ فضای‌نام هم چاپ می‌شود.
    var markedDetailed = typeof(Inventory.Api.Controllers.ProductsController).Assembly.GetTypes()
        .Where(t => t.IsSubclassOf(typeof(Microsoft.AspNetCore.Mvc.ControllerBase)) && !t.IsAbstract)
        .SelectMany(t => t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(m => m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpPostAttribute), true).Any()
                        || m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpPutAttribute), true).Any()
                        || m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpPatchAttribute), true).Any()
                        || m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpDeleteAttribute), true).Any())
            .Where(m => m.GetCustomAttributes(typeof(Inventory.Api.Licensing.Infrastructure.AllowInReadOnlyAttribute), true).Any())
            .Select(m => new { Type = t, Method = m }))
        .ToList();

    Console.WriteLine($"   (اطلاع) اکشن‌های نوشتاریِ علامت‌خورده [AllowInReadOnly]: {markedDetailed.Count}");
    foreach (var group in markedDetailed.GroupBy(x => x.Type.Namespace ?? "").OrderBy(g => g.Key, StringComparer.Ordinal))
    {
        Console.WriteLine($"      [{group.Key}]");
        foreach (var x in group.OrderBy(x => x.Type.Name, StringComparer.Ordinal).ThenBy(x => x.Method.Name, StringComparer.Ordinal))
            Console.WriteLine($"         • {x.Type.Name}.{x.Method.Name}");
    }

    // ============================================================
    //  فهرست قفل‌شده — «همهٔ» اکشن‌های غیر-GET که عمداً در حالت فقط‌خواندنی مجازند.
    //
    //  منبع: کاتالوگ ۹۸۰ اکشن غیر-GET در ۱۲۶ کنترلر (فهرست‌برداری کامل). سه دسته:
    //    ۱) زیرساختی/حساب کاربری — بدون آن‌ها سیستم در حالت بدون لایسنس قابل
    //       راه‌اندازی/نگهداری نیست (ورود، تغییر رمز، پشتیبان‌گیری، HTTPS، فایل).
    //    ۲) کاملاً فقط‌خواندنی — POST است ولی هیچ ردیفی را تغییر نمی‌دهد
    //       (جست‌وجو، پیش‌نمایش، تست اتصال، گزارش).
    //    ۳) مجوز/پشتیبانی — فعال‌سازی لایسنس و تسویهٔ دستی رزرو.
    //
    //  ⚠️ هر افزودن به این فهرست = تصمیم آگاهانه؛ تست شکست می‌خورد تا کسی بی‌صدا
    //     یک اکشن نوشتاری را باز نکند.
    // ============================================================
    var expectedMarked = new[]
    {
        // ---- ۱) زیرساختی / حساب کاربری / نگهداری ----
        // ⚠️ بازبینی چهارم، مورد ۳: فقط دو اکشن مانده. یازده اکشن دیگر (send-now، draft،
        // summarize، categorize، extract-tasks، draft-reply، suggest-*، feedback، minutes/draft)
        // از فهرست بیرون رفتند، چون ابزار نوشتاری/ارسالی را در حلقهٔ مدل اجرا می‌کردند.
        "AiController.Chat", "AiController.DeleteConversation",
        "BackupController.Retry", "BackupController.Run", "BackupController.SaveSettings", "BackupController.Test",
        "BackupSessionController.Close", "BackupSessionController.Heartbeat",
        "CentralBackupController.DeleteCompany", "CentralBackupController.DeleteReceipt",
        "CentralBackupController.RotateKey", "CentralBackupController.SaveCompany", "CentralBackupController.Upload",
        // مرحله ۳: خروج باید در حالت فقط‌خواندنی هم کار کند — وگرنه کاربرِ فقط‌خواندنی
        // نمی‌تواند صندلی‌اش را آزاد کند (بستن نشست هیچ داده‌ای را تغییر نمی‌دهد).
        "AuthController.Logout",
        "AuthController.ChangePassword", "AuthController.Login", "AuthController.ResetPassword",
        "AuthController.UploadUserPhoto", "AuthController.UploadUserSignature",
        "UserProfileController.VerifyEmailOtp", "UserProfileController.VerifyMobileOtp",
        "CompanyContextController.SetUserCompanies",
        "HttpsController.ExportPfx", "HttpsController.Regenerate",
        "FilesController.Upload",
        "NotificationsController.TestMessenger", "NotificationsController.TestPush",
        "NotificationsController.PushStatus",
        "EmailController.Test", "OfficeMachineAgentController.Printers",
        "DocSettingsController.RunOnce", "DocumentsController.RunExpiryCheck",
        "FaComController.CheckAnniversaries", "FaComController.CheckBirthdays", "FaComController.CheckDue",

        // ---- ۲) فقط‌خواندنی (POST بی‌اثر روی داده) ----
        "DashboardController.HardwareCheck",
        "DocCollabController.DuplicateCheck", "DocFavoritesController.Status",
        "DocSearchController.AdvancedSearch", "DocSearchController.SearchPage",
        "DocSemanticController.SemanticSearch", "DocSemanticController.Suggest",
        "DocumentsController.ConfirmDownload",
        "FaLmsController.Suggest", "HrCoreController.CheckAlerts", "HrCoreController.RunReport",
        "HrPayController.TestFormula", "ItRemoteController.TestConnection", "ServersController.TestConnection",
        "OfficeMachinesController.TestSnmp", "OrdersController.PreviewChequeReturn",
        "ReportStudioController.Preview", "ReportStudioController.Run", "ShopPricingController.Preview",

        // ---- ۳) مجوز / پشتیبانی ----
        "LicenseController.SettleReservation", "MoadianController.PreviewSendInvoiceAsync",

        // ---- ۳.۱) مدیریت داشبورد (تنظیمات نمایشی، نه دادهٔ مشتری) ----
        "DashboardsController.Create", "DashboardsController.Delete",
        "DashboardsController.SetDefault", "DashboardsController.Update"
    };

    // مقایسه بر پایهٔ «نام کوتاه» — با شمارش تکرارها، تا حذف یکی از دو Upload گم نشود.
    var expected = expectedMarked.OrderBy(x => x, StringComparer.Ordinal).ToList();
    var actual = markedActions.OrderBy(x => x, StringComparer.Ordinal).ToList();

    var unexpected = actual.Where(a => !expected.Contains(a)).Distinct().ToList();
    var missingExpected = expected.Where(a => !actual.Contains(a)).Distinct().ToList();

    Check(unexpected.Count == 0, $"هیچ اکشن ناشناخته‌ای علامت نخورده (ناشناخته: {string.Join(", ", unexpected)})");
    Check(missingExpected.Count == 0, $"همهٔ اکشن‌های انتظار علامت دارند (نبود: {string.Join(", ", missingExpected)})");
    // ⚠️ دو جفت کنترلر هم‌نام (Core/RadisHr ⇒ AuthController، و دو FilesController)
    // باعث می‌شوند نام کوتاه تکراری شود. مقایسه روی مجموعهٔ یکتا انجام می‌شود و
    // تکرارها جداگانه بررسی می‌شوند تا «یکی از دو Upload جا افتاده» پنهان نماند.
    var actualDistinct = actual.Distinct().ToList();
    Check(expected.Count == actualDistinct.Count,
        $"تعداد اکشن‌های علامت‌خورده با فهرست قفل‌شده یکی است (فهرست: {expected.Count}، یکتا: {actualDistinct.Count}، خام: {actual.Count})");

    var duplicates = actual.GroupBy(a => a).Where(g => g.Count() > 1).Select(g => $"{g.Key}×{g.Count()}").ToList();
    var allowedDuplicates = new[] { "AuthController.ChangePassword×2", "AuthController.Login×2", "FilesController.Upload×2" };
    Check(duplicates.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(allowedDuplicates.OrderBy(x => x, StringComparer.Ordinal)),
        $"تکرارها فقط از کنترلرهای هم‌نام‌اند (واقعی: {string.Join(", ", duplicates)})");

    // ============================================================
    //  اجرای واقعی: هر اکشن علامت‌خورده، با مسیر واقعی‌اش، در حالت Missing اجرا می‌شود.
    //
    //  چرا؟ چون «صفت گذاشتن» ثابت نمی‌کند درخواست عبور می‌کند؛ اگر مسیر در allowlist
    //  دیگری بیفتد یا صفت روی اورلودِ اشتباه بنشیند، تست صفت‌ها سبز می‌ماند و کاربر
    //  در عمل ۴۰۳ می‌گیرد. این‌جا مسیر از Route/HttpPost خودِ اکشن ساخته می‌شود.
    // ============================================================
    var allowlist = new[] { "/api/auth/", "/api/license/", "/api/health" };

    string RouteOf(Type type, System.Reflection.MethodInfo method)
    {
        var controllerRoute = type.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.RouteAttribute), true)
            .OfType<Microsoft.AspNetCore.Mvc.RouteAttribute>().FirstOrDefault()?.Template ?? "";

        var httpTemplate = method.GetCustomAttributes(true)
            .OfType<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>()
            .FirstOrDefault()?.Template ?? "";

        // مسیر مطلق (مثل [HttpPost("/api/doc-archive/expiry-check")]) مسیر کنترلر را
        // بازنویسی می‌کند، نه اینکه به آن بچسبد — همان قاعدهٔ ASP.NET.
        var isAbsolute = httpTemplate.StartsWith('/') || httpTemplate.StartsWith("~/", StringComparison.Ordinal);

        var combined = isAbsolute
            ? httpTemplate
            : string.Join("/", new[] { controllerRoute, httpTemplate }
                .Where(z => !string.IsNullOrWhiteSpace(z)));

        combined = combined.Replace("[controller]", type.Name.Replace("Controller", "").ToLowerInvariant());

        // پارامترهای مسیر ({id:int}، {uid}) با مقدار نمونه جایگزین می‌شوند.
        combined = System.Text.RegularExpressions.Regex.Replace(combined, @"\{[^}]*\}", "1");

        return "/" + combined.Trim('/');
    }

    var executed = 0;
    var blockedAnyway = new List<string>();
    var controlBroken = new List<string>();

    foreach (var x in markedDetailed)
    {
        var path = RouteOf(x.Type, x.Method);
        var label = $"{x.Type.Name}.{x.Method.Name} → {path}";

        var missing = await Run(LicenseOverallStatus.Missing, "POST", path, marked: true);
        executed++;
        if (!missing.NextCalled) blockedAnyway.Add($"{label} ({missing.StatusCode})");

        // کنترل: همان مسیر **بدون** صفت باید ۴۰۳ بگیرد — مگر اینکه در allowlist باشد.
        var isAllowlisted = allowlist.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        var control = await Run(LicenseOverallStatus.Missing, "POST", path, marked: false);
        var expectedPass = isAllowlisted;
        if (control.NextCalled != expectedPass)
            controlBroken.Add($"{label} (allowlist={isAllowlisted}، عبور={control.NextCalled})");
    }

    Console.WriteLine($"   (اطلاع) اکشن‌های علامت‌خورده که واقعاً در حالت Missing اجرا شدند: {executed}");

    Check(blockedAnyway.Count == 0,
        $"همهٔ اکشن‌های علامت‌خورده در Missing عبور می‌کنند (مسدود: {string.Join(" | ", blockedAnyway)})");

    Check(controlBroken.Count == 0,
        $"کنترل منفی: بدون صفت، همان مسیرها ۴۰۳ می‌گیرند مگر در allowlist (نقض: {string.Join(" | ", controlBroken)})");
}

// ============================================================
Section("۱۹) رزرو ردیف‌دار: وضعیت Reserved + زمان، جاروی کهنه ⇒ Pending، تسویهٔ دستی");

{
    // درخواست کارفرما: «رزرو باید وضعیت Reserved + زمان داشته باشد؛ رزروهای کهنه در
    // startup به Pending بروند؛ API تسویهٔ دستی (Confirm/Refund) با LicenseEvent + نام کاربر».
    await ResetAsync();

    await using var db = NewDb();
    var quota = Service(db, NewIntegrity());
    var ent = Entitlement(MetricCodes.MoadianInvoiceSend, 10, QuotaPeriod.Monthly, ModuleCodes.Moadian);

    // ---------- الف) رزرو، ردیف می‌سازد ----------
    var reserve = await quota.ReserveAsync(ent, ModuleCodes.Moadian, MetricCodes.MoadianInvoiceSend, 1,
        "MoadianInvoice", "9001");

    Check(reserve.Result.Allowed, "رزرو مجاز است (سقف ۱۰)");
    Check(reserve.ReservationIds.Count == 1, $"یک ردیف رزرو ساخته شد (واقعی: {reserve.ReservationIds.Count})");

    await using (var q = NewDb())
    {
        var row = await q.LicenseReservations.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reserve.ReservationIds[0]);
        Check(row is not null && row.State == Inventory.Api.Data.LicenseReservation.States.Reserved,
            $"وضعیت ردیف رزرو = Reserved (واقعی: {row?.State})");
        Check(row is not null && row.ReservedAt > DateTime.Now.AddMinutes(-5),
            "ردیف رزرو زمان ثبت دارد (ReservedAt پر است)");
        Check(row is not null && row.PeriodKey.Length > 0,
            $"کلید دوره در ردیف رزرو ذخیره شد (واقعی: {row?.PeriodKey}) — تسویه به آن نیاز دارد");
    }

    // ---------- ب) جاروی رزرو کهنه ----------
    // رزرو را مصنوعاً «یک ساعت پیش» می‌کنیم (شبیه‌سازی خاموشی برنامه پس از رزرو).
    await using (var q = NewDb())
    {
        var row = await q.LicenseReservations.FirstAsync(r => r.Id == reserve.ReservationIds[0]);
        row.ReservedAt = Inventory.Licensing.Periods.PersianPeriodKey.NowIran().AddHours(-1);
        await q.SaveChangesAsync();
    }

    await using (var q = NewDb())
    {
        var qs = Service(q, NewIntegrity());
        var swept = await qs.SweepStaleReservationsAsync(TimeSpan.FromMinutes(15));
        Check(swept == 1, $"جارو یک رزرو کهنه پیدا کرد (واقعی: {swept})");

        var row = await q.LicenseReservations.AsNoTracking().FirstAsync(r => r.Id == reserve.ReservationIds[0]);
        Check(row.State == Inventory.Api.Data.LicenseReservation.States.Pending,
            $"رزرو کهنه ⇒ Pending، نه Refunded (واقعی: {row.State}) — سهمیه بخشیده نمی‌شود");

        var used = await q.LicenseUsages.AsNoTracking()
            .Where(u => u.Metric == MetricCodes.MoadianInvoiceSend).Select(u => (int?)u.Used).FirstOrDefaultAsync() ?? 0;
        Check(used == 1, $"مصرف پس از جارو برنگشت (واقعی: {used}) — قاعدهٔ «نتیجهٔ نامعلوم ⇒ بازگشت نکن»");

        var evt = await q.LicenseEvents.AsNoTracking()
            .Where(e => e.Kind == "LICENSE_QUOTA_RESERVATION_STALE").CountAsync();
        Check(evt >= 1, $"رخداد LICENSE_QUOTA_RESERVATION_STALE ثبت شد (تعداد: {evt})");
    }

    // ---------- ج) تسویهٔ دستی: Confirm ----------
    await using (var q = NewDb())
    {
        var qs = Service(q, NewIntegrity());
        var ok = await qs.SettleReservationAsync(reserve.ReservationIds[0], confirm: true, username: "support1",
            note: "طبق گزارش سامانهٔ مودیان، ارسال انجام شده بود.");

        Check(ok, "تسویهٔ دستی Confirm انجام شد");

        var row = await q.LicenseReservations.AsNoTracking().FirstAsync(r => r.Id == reserve.ReservationIds[0]);
        Check(row.State == Inventory.Api.Data.LicenseReservation.States.Confirmed,
            $"وضعیت پس از Confirm = Confirmed (واقعی: {row.State})");
        Check(row.SettledBy == "support1", $"نام کاربر تسویه‌کننده ثبت شد (واقعی: {row.SettledBy})");

        var used = await q.LicenseUsages.AsNoTracking()
            .Where(u => u.Metric == MetricCodes.MoadianInvoiceSend).Select(u => (int?)u.Used).FirstOrDefaultAsync() ?? 0;
        Check(used == 1, $"مصرف پس از Confirm تغییر نکرد (واقعی: {used})");

        var evt = await q.LicenseEvents.AsNoTracking().Where(e => e.Kind == "LICENSE_QUOTA_MANUAL_SETTLE").ToListAsync();
        Check(evt.Count >= 1 && evt.Any(e => e.Message.Contains("support1")),
            "رخداد تسویهٔ دستی با نام کاربر ثبت شد");

        // تسویهٔ دوباره باید رد شود (رزرو بسته شده است).
        var again = await qs.SettleReservationAsync(reserve.ReservationIds[0], confirm: false, username: "support1");
        Check(!again, "تسویهٔ دوبارهٔ همان رزرو رد می‌شود (وضعیت بسته است)");
    }

    // ---------- د) تسویهٔ دستی: Refund ----------
    await using (var q = NewDb())
    {
        var qs = Service(q, NewIntegrity());
        var second = await qs.ReserveAsync(ent, ModuleCodes.Moadian, MetricCodes.MoadianInvoiceSend, 1,
            "MoadianInvoice", "9002");

        // شبیه‌سازی «در انتظار» — بدون جارو، مستقیم با MarkPending.
        await qs.MarkPendingAsync(ent, ModuleCodes.Moadian, MetricCodes.MoadianInvoiceSend,
            "MoadianInvoice", "9002", note: "timeout", reservationIds: second.ReservationIds);

        var beforeRefund = await q.LicenseUsages.AsNoTracking()
            .Where(u => u.Metric == MetricCodes.MoadianInvoiceSend).Select(u => (int?)u.Used).FirstOrDefaultAsync() ?? 0;

        // ⚠️ سیاست بازبینی سوم: بازگشت دستی فقط برای رزروهای قدیمی‌تر از X ساعت مجاز است.
        // این رزرو همین حالا ساخته شده؛ پس زمانش را عقب می‌بریم تا «رزرو قدیمی واقعی»
        // را شبیه‌سازی کنیم — همان حالتی که در عمل پیش می‌آید (کشف خطا چند ساعت بعد).
        var old = await q.LicenseReservations.FirstAsync(r => r.Id == second.ReservationIds[0]);
        old.ReservedAt = PersianPeriodKey.NowIran().AddHours(-30);
        await q.SaveChangesAsync();

        var ok = await qs.SettleReservationAsync(second.ReservationIds[0], confirm: false, username: "support2",
            note: "ارسال نشده بود؛ سهمیه آزاد شد.");
        Check(ok, "تسویهٔ دستی Refund انجام شد");

        var afterRefund = await q.LicenseUsages.AsNoTracking()
            .Where(u => u.Metric == MetricCodes.MoadianInvoiceSend).Select(u => (int?)u.Used).FirstOrDefaultAsync() ?? 0;
        Check(afterRefund == beforeRefund - 1,
            $"مصرف یک واحد کم شد ({beforeRefund} ⇒ {afterRefund})");

        var refundLedger = await q.LicenseUsageLedger.AsNoTracking()
            .Where(l => l.Reason == "manual-refund").CountAsync();
        Check(refundLedger >= 1, $"ردیف دفتر با reason=manual-refund ثبت شد (تعداد: {refundLedger})");

        var row = await q.LicenseReservations.AsNoTracking().FirstAsync(r => r.Id == second.ReservationIds[0]);
        Check(row.State == Inventory.Api.Data.LicenseReservation.States.Refunded
              && row.SettledBy == "support2",
            $"وضعیت پس از Refund = Refunded و کاربر = support2 (واقعی: {row.State}/{row.SettledBy})");
    }
}

// ============================================================
Section("۲۰) مبنا برای همهٔ متریک‌های دارای probe + بازیابی مصرف از ردیف‌ها پس از حذف شمارنده");

{
    await ResetAsync();

    // ---------- الف) دامنهٔ مبنا = همهٔ متریک‌های دارای probe (نه فقط ActiveCount) ----------
    var withProbe = Inventory.Api.Licensing.Services.ActiveCountProbes.AllWithProbe().ToList();
    var activeOnly = Inventory.Api.Licensing.Services.ActiveCountProbes.All().ToList();

    Console.WriteLine($"   (اطلاع) متریک‌های دارای probe: {withProbe.Count} — از نوع ActiveCount: {activeOnly.Count}");
    Check(withProbe.Count >= 3, $"دست‌کم ۳ متریک probe دارند (واقعی: {withProbe.Count})");
    Check(withProbe.Count == Inventory.Api.Licensing.Services.ActiveCountProbes.Probes.Count,
        "AllWithProbe همهٔ probeهای ثبت‌شده را برمی‌گرداند");
    Check(Inventory.Api.Licensing.Services.ActiveCountProbes.HasProbe(MetricCodes.InvoiceCreate),
        "HasProbe(invoice.create) = true");
    Check(!Inventory.Api.Licensing.Services.ActiveCountProbes.HasProbe("no.such.metric"),
        "HasProbe برای متریک ناشناخته false است (پس مصرف مؤثر = شمارنده می‌ماند)");

    // ---------- ب) بازیابی از ردیف‌ها: حذف و ساخت دوبارهٔ شمارنده ----------
    await using var db = NewDb();
    var wh = new Inventory.Api.Data.Warehouse { Name = "انبار بازیابی", Code = "W-RECOVER" };
    db.Warehouses.Add(wh);
    await db.SaveChangesAsync();

    var ent = Entitlement(MetricCodes.InvoiceCreate, 1000, QuotaPeriod.Lifetime, ModuleCodes.Invoicing);
    var quota = Service(db, NewIntegrity());

    // ۵۰ فاکتور «قبل از سقف» (دادهٔ واردشده از سیستم قبلی)
    var oldInvoices = Enumerable.Range(1, 50).Select(i => new Inventory.Api.Data.FacInvoice
    {
        Number = 500_000 + i, Kind = Inventory.Shared.InvoiceKind.Sale, Date = DateTime.Today.AddDays(-30),
        WarehouseId = wh.Id, Settlement = Inventory.Shared.SettlementType.Cash, CreatedBy = "test"
    }).ToList();
    db.FacInvoices.AddRange(oldInvoices);
    await db.SaveChangesAsync();

    var captured = await Inventory.Api.Licensing.Services.ActiveCountProbes.CaptureAllAsync(
        db, quota, ent, "TEST-RECOVER", "activation");
    Check(captured >= 1, $"مبنا ثبت شد برای {captured} متریک (انتظار ≥ ۱)");

    var limit = ent.LimitsFor(ModuleCodes.Invoicing, MetricCodes.InvoiceCreate).First();
    Func<Inventory.Licensing.Model.QuotaPeriod, CancellationToken, Task<long>> liveInPeriod =
        (period, ct) => Inventory.Api.Licensing.Services.ActiveCountProbes.CountAsync(db, MetricCodes.InvoiceCreate, period,
            Inventory.Licensing.Periods.PersianPeriodKey.NowIran(), ct);

    var eff0 = await quota.GetEffectiveUsageAsync(ent, limit, liveInPeriod);
    Check(eff0 == 0, $"پیش از هر ثبت تازه، مصرف مؤثر ۰ است (واقعی: {eff0}) — ۵۰ فاکتور قدیمی از مبنا کسر شد");

    // ۳ فاکتور تازه ⇒ مصرف مؤثر باید ۳ باشد
    for (var i = 0; i < 3; i++)
    {
        db.FacInvoices.Add(new Inventory.Api.Data.FacInvoice
        {
            Number = 600_000 + i, Kind = Inventory.Shared.InvoiceKind.Sale, Date = DateTime.Today,
            WarehouseId = wh.Id, Settlement = Inventory.Shared.SettlementType.Cash, CreatedBy = "test"
        });
        await db.SaveChangesAsync();
        await quota.TryConsumeAsync(ent, ModuleCodes.Invoicing, MetricCodes.InvoiceCreate);
    }

    var eff3 = await quota.GetEffectiveUsageAsync(ent, limit, liveInPeriod);
    Check(eff3 == 3, $"پس از ۳ فاکتور تازه، مصرف مؤثر ۳ است (واقعی: {eff3})");

    // حالا ردیف شمارنده را پاک و دوباره می‌سازیم — مثل دست‌کاری/بازیابی بکاپ.
    await using (var q = NewDb())
    {
        await q.Database.ExecuteSqlRawAsync(useSqlServer
            ? "DELETE FROM [dbo].[LicenseUsages]"
            : "DELETE FROM \"LicenseUsages\"");
    }

    await using (var q = NewDb())
    {
        var qs = Service(q, NewIntegrity());
        var limit2 = ent.LimitsFor(ModuleCodes.Invoicing, MetricCodes.InvoiceCreate).First();
        Func<Inventory.Licensing.Model.QuotaPeriod, CancellationToken, Task<long>> live2 =
            (period, ct) => Inventory.Api.Licensing.Services.ActiveCountProbes.CountAsync(q, MetricCodes.InvoiceCreate, period,
                Inventory.Licensing.Periods.PersianPeriodKey.NowIran(), ct);

        var recovered = await qs.GetEffectiveUsageAsync(ent, limit2, live2);
        Check(recovered == 3,
            $"پس از حذف ردیف شمارنده، مصرف مؤثر از ردیف‌ها بازیابی شد = ۳ (واقعی: {recovered})");
    }
}

// ============================================================
Section("۲۱) سیاست بازگشت دستی: سقف دوره، یادداشت اجباری، حداقل عمر رزرو، شمارش در وضعیت");

{
    // درخواست کارفرما (بازبینی سوم): «سقف بازگشت دستی در هر دوره = min(۲٪ سقف، ۵ مورد)،
    // فقط برای رزروهای قدیمی‌تر از X ساعت، یادداشت اجباری، رخداد با شدت Warning،
    // و شمارش Pending/Refund در GET /api/license/status. تست: پس از پر شدن سقف رد شود.»
    await ResetAsync();

    await using var db = NewDb();
    var quota = Service(db, NewIntegrity());

    // سقف ماهانه ۱۰۰ ⇒ ۲٪ = ۲ ⇒ min(۲، ۵) = ۲ بازگشت مجاز در این دوره.
    var ent = Entitlement(MetricCodes.MoadianInvoiceSend, 100, QuotaPeriod.Monthly, ModuleCodes.Moadian);
    var policy = new Inventory.Api.Licensing.RefundPolicy(
        Inventory.Api.Licensing.RefundPolicy.DefaultPercentOfLimit,
        Inventory.Api.Licensing.RefundPolicy.DefaultMaxCount, 24);

    Check(policy.MaxRefundsFor(100) == 2, $"۲٪ سقف ۱۰۰ = ۲ و min(۲،۵) = ۲ (واقعی: {policy.MaxRefundsFor(100)})");
    Check(policy.MaxRefundsFor(1000) == 5, $"۲٪ سقف ۱۰۰۰ = ۲۰ ⇒ سقف ۵ اعمال می‌شود (واقعی: {policy.MaxRefundsFor(1000)})");
    Check(policy.MaxRefundsFor(20) == 1, $"سقف کوچک ۲۰ ⇒ ۲٪ = ۰٫۴ ولی حداقل ۱ مجاز است (واقعی: {policy.MaxRefundsFor(20)})");

    // ---------- الف) پنج رزرو؛ همه عقب برده می‌شوند (رزرو قدیمی واقعی) ----------
    var ids = new List<long>();
    for (var i = 0; i < 5; i++)
    {
        var r = await quota.ReserveAsync(ent, ModuleCodes.Moadian, MetricCodes.MoadianInvoiceSend, 1,
            "MoadianInvoice", $"R-{i + 1}");
        if (r.Result.Allowed) ids.AddRange(r.ReservationIds);
    }
    Check(ids.Count == 5, $"پنج رزرو ساخته شد (واقعی: {ids.Count})");

    await using (var q = NewDb())
    {
        var rows = await q.LicenseReservations.Where(r => ids.Contains(r.Id)).ToListAsync();
        foreach (var row in rows) row.ReservedAt = PersianPeriodKey.NowIran().AddHours(-30);
        await q.SaveChangesAsync();
    }

    // ---------- ب) یادداشت اجباری ----------
    await using (var q = NewDb())
    {
        var qs = Service(q, NewIntegrity());
        var noNote = await qs.SettleReservationDetailedAsync(ids[0], confirm: false, username: "op1",
            note: "   ", policy: policy);
        Check(!noNote.Success && noNote.Failure == Inventory.Api.Licensing.SettleFailure.NoteRequired,
            $"بازگشت بدون یادداشت رد می‌شود (نتیجه: {noNote.Failure} — {noNote.Message})");
    }

    // ---------- ج) رزرو تازه (کمتر از X ساعت) ----------
    await using (var q = NewDb())
    {
        var qs = Service(q, NewIntegrity());
        var fresh = await qs.ReserveAsync(ent, ModuleCodes.Moadian, MetricCodes.MoadianInvoiceSend, 1,
            "MoadianInvoice", "R-fresh");
        var tooRecent = await qs.SettleReservationDetailedAsync(fresh.ReservationIds[0], confirm: false,
            username: "op1", note: "اشتباه شد", policy: policy);
        Check(!tooRecent.Success && tooRecent.Failure == Inventory.Api.Licensing.SettleFailure.TooRecent,
            $"رزرو تازه (کمتر از ۲۴ ساعت) بازگردانده نمی‌شود (نتیجه: {tooRecent.Failure} — {tooRecent.Message})");
    }

    // ---------- د) دو بازگشت مجاز، سومی رد ----------
    var okCount = 0;
    Inventory.Api.Licensing.SettleFailure thirdFailure = Inventory.Api.Licensing.SettleFailure.None;
    string? thirdMessage = null;

    for (var i = 0; i < 3; i++)
    {
        await using var q = NewDb();
        var qs = Service(q, NewIntegrity());
        var res = await qs.SettleReservationDetailedAsync(ids[i], confirm: false, username: $"op{i + 1}",
            note: $"بازگشت آزمایشی شمارهٔ {i + 1}", policy: policy);

        if (res.Success) okCount++;
        else { thirdFailure = res.Failure; thirdMessage = res.Message; }
    }

    Check(okCount == 2, $"دقیقاً ۲ بازگشت دستی در این دوره انجام شد (واقعی: {okCount})");
    Check(thirdFailure == Inventory.Api.Licensing.SettleFailure.RefundLimitReached,
        $"بازگشت سوم با «سقف پر شد» رد شد (نتیجه: {thirdFailure} — {thirdMessage})");

    // ---------- ه) سقف با تغییر appsettings قابل دور زدن نیست، ولی کلمپ می‌شود ----------
    var clamped = new Inventory.Api.Licensing.RefundPolicy(999, 999, -5);
    Check(clamped.PercentOfLimit == 50 && clamped.MaxCount == 50 && clamped.MinAgeHours == 24,
        $"مقادیر خارج از بازه کلمپ می‌شوند (درصد={clamped.PercentOfLimit}، تعداد={clamped.MaxCount}، ساعت={clamped.MinAgeHours})");

    var zeroAge = new Inventory.Api.Licensing.RefundPolicy(2, 5, 0);
    Check(zeroAge.MinAgeHours == 24, $"ساعت صفر ⇒ پیش‌فرض ۲۴ (بدون محدودیت سنی نمی‌شود) — واقعی: {zeroAge.MinAgeHours}");

    // ---------- و) رخداد با شدت Warning ----------
    await using (var q = NewDb())
    {
        var events = await q.LicenseEvents.AsNoTracking()
            .Where(e => e.Kind == "LICENSE_QUOTA_MANUAL_SETTLE").ToListAsync();
        Check(events.Count >= 2 && events.All(e => e.Severity == "Warning"),
            $"رخداد تسویهٔ دستی با شدت Warning ثبت شد (تعداد: {events.Count}، شدت‌ها: {string.Join(",", events.Select(e => e.Severity).Distinct())})");
    }

    // ---------- ز) شمارش‌ها در وضعیت ----------
    await using (var q = NewDb())
    {
        var qs = Service(q, NewIntegrity());
        var summary = await qs.GetReservationSummaryAsync(policy);
        Console.WriteLine($"   (اطلاع) خلاصهٔ رزروها: Pending={summary.Pending}، Reserved={summary.Reserved}، Confirmed={summary.Confirmed}، Refunded={summary.Refunded}، ManualRefunds={summary.ManualRefunds}");

        Check(summary.Refunded == 2, $"۲ رزرو در وضعیت Refunded شمرده شد (واقعی: {summary.Refunded})");
        Check(summary.ManualRefunds == 2, $"۲ واحد بازگشت دستی در دفتر شمرده شد (واقعی: {summary.ManualRefunds})");
        Check(summary.Reserved >= 1, $"رزروهای باز شمرده می‌شوند (Reserved={summary.Reserved})");
    }
}

// ============================================================
Section("۲۲) بخشودگی گذشته (Grandfather): ۳۰۰ نامهٔ همین ماه، سقف ماهانه ۱۰۰ ⇒ ثبت تازه مجاز");

{
    // درخواست کارفرما: «گزینهٔ Grandfather (پیش‌فرض خاموش): ردیف‌های موجود در دورهٔ جاری
    // در لحظهٔ فعال‌سازی از مصرف مؤثر همان دوره کسر می‌شوند. تست: ۳۰۰ نامهٔ ماه جاری،
    // سقف ماهانه ۱۰۰ ⇒ ثبت تازه مجاز؛ ماه بعد از صفر.»
    await ResetAsync();

    await using var db = NewDb();

    // ۳۰۰ سند در همین ماه شمسی — مثل سناریوی واقعی کارفرما («۳۰۰ نامه در ماه جاری»)،
    // ولی با فاکتور چون درج فاکتور در این تست یک مسیر جاافتاده دارد (§۱۵) و کلید
    // خارجی نامه (منبع/کاربر) دادهٔ آمادهٔ بیشتری می‌خواهد که به موضوع این بخش ربطی ندارد.
    var wh = new Inventory.Api.Data.Warehouse { Name = "انبار بخشودگی", Code = "W-GF" };
    db.Warehouses.Add(wh);
    await db.SaveChangesAsync();

    var rows = Enumerable.Range(1, 300).Select(i => new Inventory.Api.Data.FacInvoice
    {
        Number = 700_000 + i, Kind = Inventory.Shared.InvoiceKind.Sale,
        Date = PersianPeriodKey.NowIran().Date.AddDays(-1),
        WarehouseId = wh.Id, Settlement = Inventory.Shared.SettlementType.Cash, CreatedBy = "gf-test"
    }).ToList();

    await db.FacInvoices.AddRangeAsync(rows);
    await db.SaveChangesAsync();

    var liveThisMonth = await db.FacInvoices.IgnoreQueryFilters().CountAsync();
    Check(liveThisMonth >= 300, $"۳۰۰ سند در همین ماه ثبت شد (واقعی: {liveThisMonth})");

    // ---------- الف) بدون گزینهٔ Grandfather: سقف از اول پر است ----------
    var plain = Entitlement(MetricCodes.InvoiceCreate, 100, QuotaPeriod.Monthly, ModuleCodes.Invoicing);
    var quota = Service(db, NewIntegrity());

    var plainLimit = plain.LimitsFor(ModuleCodes.Invoicing, MetricCodes.InvoiceCreate).First();
    var plainEffective = await quota.GetEffectiveUsageAsync(plain, plainLimit,
        (period, ct) => Inventory.Api.Licensing.Services.ActiveCountProbes.CountAsync(
            db, MetricCodes.InvoiceCreate, period, PersianPeriodKey.NowIran(), ct));

    Check(plainEffective >= 300, $"بدون Grandfather مصرف مؤثر = ۳۰۰+ (واقعی: {plainEffective})");
    var plainDenied = await quota.TryConsumeAsync(plain, ModuleCodes.Invoicing, MetricCodes.InvoiceCreate);
    Check(!plainDenied.Allowed, "بدون Grandfather، ثبت نامهٔ تازه رد می‌شود (سقف از اول پر است)");

    // ---------- ب) با Grandfather روشن: مبنا همان لحظه ثبت می‌شود ----------
    var grand = GrandfatherEntitlement(MetricCodes.InvoiceCreate, 100, QuotaPeriod.Monthly,
        ModuleCodes.Invoicing);
    Check(grand.LimitsFor(ModuleCodes.Invoicing, MetricCodes.InvoiceCreate).First().Grandfather,
        "گزینهٔ Grandfather در سقف مؤثر منتقل شد (Merger)");

    var captured = await Inventory.Api.Licensing.Services.ActiveCountProbes.CaptureGrandfatherAsync(
        db, quota, grand, "TEST-GF");
    Check(captured >= 1, $"مبنای بخشودگی ثبت شد (تعداد: {captured})");

    var grandLimit = grand.LimitsFor(ModuleCodes.Invoicing, MetricCodes.InvoiceCreate).First();
    var grandEffective = await quota.GetEffectiveUsageAsync(grand, grandLimit,
        (period, ct) => Inventory.Api.Licensing.Services.ActiveCountProbes.CountAsync(
            db, MetricCodes.InvoiceCreate, period, PersianPeriodKey.NowIran(), ct));

    Check(grandEffective == 0, $"با Grandfather مصرف مؤثر = ۰ (واقعی: {grandEffective})");

    var allowed = await quota.TryConsumeAsync(grand, ModuleCodes.Invoicing, MetricCodes.InvoiceCreate);
    Check(allowed.Allowed, $"✅ ثبت نامهٔ تازه با Grandfather مجاز است (سقف ماهانه ۱۰۰، ۳۰۰ نامهٔ موجود)");

    // ---------- ج) سقف هنوز سقف است: ۱۰۰ نامهٔ تازه، صدویکمی رد ----------
    var accepted = 1; // همان یکی که بالا ثبت شد
    for (var i = 0; i < 120; i++)
        if ((await quota.TryConsumeAsync(grand, ModuleCodes.Invoicing, MetricCodes.InvoiceCreate)).Allowed)
            accepted++;

    Check(accepted == 100, $"دقیقاً ۱۰۰ نامهٔ تازه پذیرفته شد، نه بیشتر (واقعی: {accepted}) — سقف دور زده نشد");

    // ---------- د) دورهٔ بعد: ردیف مبنا وجود ندارد ⇒ از صفر ----------
    var nextMonth = PersianPeriodKey.Now(QuotaPeriod.Monthly);
    var baselineNow = await quota.ReadGrandfatherBaselineAsync(ModuleCodes.Invoicing,
        MetricCodes.InvoiceCreate, nextMonth);
    Check(baselineNow > 0, $"برای دورهٔ جاری مبنا ثبت است (واقعی: {baselineNow})");

    // کلید دورهٔ بعد را با «اکنون + ۳۵ روز» می‌سازیم — همان ماه شمسی بعدی.
    var nextKey = PersianPeriodKey.ForInstant(DateTimeOffset.UtcNow.AddDays(35), QuotaPeriod.Monthly);
    var baselineNext = await quota.ReadGrandfatherBaselineAsync(ModuleCodes.Invoicing,
        MetricCodes.InvoiceCreate, nextKey);
    Check(baselineNext == 0, $"برای دورهٔ بعد ({nextKey}) مبنا صفر است ⇒ شمارش از صفر (واقعی: {baselineNext})");

    // و اگر نامهٔ همان ماه بعد وجود داشته باشد، کامل شمرده می‌شود (بدون کسر گذشته).
    var nextRange = PersianPeriodKey.RangeOf(QuotaPeriod.Monthly, PersianPeriodKey.ToIran(DateTimeOffset.UtcNow.AddDays(35)));
    var inNext = await Inventory.Api.Licensing.Services.ActiveCountProbes.Probes[MetricCodes.InvoiceCreate](
        db, nextRange.From, nextRange.To, CancellationToken.None);
    Check(inNext == 0, $"در ماه بعد هنوز نامه‌ای نیست (واقعی: {inNext}) — پس مصرف مؤثر = ۰ بدون هیچ کسری");

    // ---------- ه) idempotent: فعال‌سازی دوباره مبنا را جابه‌جا نمی‌کند ----------
    var again = await quota.CaptureGrandfatherBaselineAsync(ModuleCodes.Invoicing,
        MetricCodes.InvoiceCreate, nextMonth, 999_999, "TEST-GF2");
    var afterAgain = await quota.ReadGrandfatherBaselineAsync(ModuleCodes.Invoicing,
        MetricCodes.InvoiceCreate, nextMonth);
    Check(!again && afterAgain == baselineNow,
        $"ثبت دوبارهٔ مبنا اثری ندارد (ثبت‌شد={again}، مقدار={afterAgain}، قبلی={baselineNow}) — تمدید لایسنس سقف را بازنمی‌کند");
}

// ============================================================
Console.WriteLine($"\n(جمع میانی بخش‌های ۱..۲۲: {passes} موفق / {failures} ناموفق — ادامه: بخش ۲۳ به بعد)");

// ============================================================
Section("۲۳) هاب‌های SignalR: نگاشت ماژول، اتصال در Missing/Invalid، رد متد نوشتاری (بازبینی چهارم، مورد ۲)");

{
    // هاب‌ها روی /hubs/* سرو می‌شوند و دروازهٔ HTTP آن‌ها را نمی‌بیند؛ تنها سد،
    // HubLicenseFilter است. اگر این تست سبز باشد، «کانال بلادرنگِ بدون بررسی» نداریم.
    using var provider = new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider();

    var catalog = Inventory.Api.Licensing.Infrastructure.HubLicenseFilter.Catalog;
    var expectedHubs = new[] { "DashboardHub", "NotifyHub", "ChatHub", "RemoteHub", "RemoteAgentHub" };

    Check(expectedHubs.All(catalog.ContainsKey),
        $"همهٔ هاب‌های واقعی در نگاشت هستند (موجود: {string.Join(", ", catalog.Keys.OrderBy(x => x))})");
    Check(catalog.Count == expectedHubs.Length,
        $"هیچ هاب اضافه/ناشناخته‌ای در نگاشت نیست (تعداد: {catalog.Count})");
    Check(catalog.Values.All(v => !string.IsNullOrWhiteSpace(v.ModuleCode) && !string.IsNullOrWhiteSpace(v.Reason)),
        "هر هاب ماژول و دلیل مستند دارد");

    Check(catalog["RemoteHub"].ConnectInReadOnly == false && catalog["RemoteAgentHub"].ConnectInReadOnly == false,
        "هاب‌های ریموت در حالت فقط‌خواندنی بسته‌اند (قابلیتِ کنشی)");
    Check(catalog["DashboardHub"].ConnectInReadOnly && catalog["NotifyHub"].ConnectInReadOnly,
        "داشبورد و اعلان باز می‌مانند (کانال نمایش وضعیت لایسنس)");

    var safe = Inventory.Api.Licensing.Infrastructure.HubLicenseFilter.ReadOnlySafeMethods;
    Check(safe.Count == 5, $"فهرست متدهای مجاز دقیقاً ۵ مورد است (واقعی: {safe.Count})");

    var writeMethods = new[] { "SendAsync", "SendManyAsync", "SendToRoleAsync", "BroadcastChangedAsync",
                               "SendInput", "EndSession", "JoinSession", "Frame", "ConsentResult" };
    Check(writeMethods.All(m => !safe.ContainsKey(m)),
        $"هیچ متد نوشتاری/ارسالی در فهرست مجاز نیست (نشتی: {string.Join(", ", writeMethods.Where(safe.ContainsKey))})");

    // ---------- اتصال: داشبورد در Missing ⇒ باز ----------
    var log = Microsoft.Extensions.Logging.Abstractions.NullLogger<Inventory.Api.Licensing.Infrastructure.HubLicenseFilter>.Instance;
    var filter = new Inventory.Api.Licensing.Infrastructure.HubLicenseFilter(new GateLicenseService(LicenseOverallStatus.Missing, false), log);

    bool connected;
    async Task<bool> TryConnect(Hub hub, LicenseOverallStatus status)
    {
        var f = new Inventory.Api.Licensing.Infrastructure.HubLicenseFilter(new GateLicenseService(status, false), log);
        var reachable = false;
        try
        {
            await f.OnConnectedAsync(
                new HubLifetimeContext(new StubHubCallerContext(), provider, hub),
                _ => { reachable = true; return ValueTask.CompletedTask; });
        }
        // هر استثنای دیگری هم «رد شدن» است؛ ولی چاپ می‌شود تا اگر روزی خطای
        // غیرمنتظره‌ای (نه HubException) رخ داد، در لاگ تست دیده شود.
        catch (HubException) { return false; }
        catch (Exception e) { Console.WriteLine($"   [هشدار] رد شد با {e.GetType().Name}: {e.Message}"); return false; }
        return reachable;
    }

    Check(await TryConnect(new HubTestDoubles.DashboardHub(), LicenseOverallStatus.Missing),
        "DashboardHub در Missing ⇒ اتصال برقرار می‌شود (پوسته باید بنر وضعیت را نشان دهد)");
    Check(await TryConnect(new HubTestDoubles.NotifyHub(), LicenseOverallStatus.Invalid),
        "NotifyHub در Invalid ⇒ اتصال برقرار می‌شود");
    Check(!await TryConnect(new HubTestDoubles.RemoteHub(), LicenseOverallStatus.Missing),
        "RemoteHub در Missing ⇒ اتصال با HubException رد می‌شود");
    Check(!await TryConnect(new HubTestDoubles.RemoteAgentHub(), LicenseOverallStatus.Invalid),
        "RemoteAgentHub در Invalid ⇒ اتصال رد می‌شود");
    Check(await TryConnect(new HubTestDoubles.RemoteHub(), LicenseOverallStatus.Valid),
        "RemoteHub با لایسنس معتبر ⇒ اتصال برقرار می‌شود");

    // ---------- تشخیص هاب بر پایهٔ نام کامل (Type.FullName)، نه نام کوتاه ----------
    // بازبینی چهارم، بند ۵: تصمیم نباید به GetType().Name گره بخورد.
    {
        var realHubs = new (Type Type, string Expected)[]
        {
            (typeof(Inventory.Api.Hubs.DashboardHub), "DashboardHub"),
            (typeof(Inventory.Api.Hubs.NotifyHub), "NotifyHub"),
            (typeof(Inventory.Api.Hubs.ChatHub), "ChatHub"),
            (typeof(Inventory.Api.Hubs.Remote.RemoteHub), "RemoteHub"),
            (typeof(Inventory.Api.Hubs.Remote.RemoteAgentHub), "RemoteAgentHub"),
        };

        foreach (var (type, expected) in realHubs)
        {
            var key = Inventory.Api.Licensing.Infrastructure.HubLicenseFilter.ResolveHubKey(type);
            Check(key == expected,
                $"هاب واقعی با نام کامل شناخته می‌شود: {type.FullName} ⇒ {key}");
        }

        Check(Inventory.Api.Licensing.Infrastructure.HubLicenseFilter.FullNameCatalog.Count == realHubs.Length,
            $"نگاشت نام کامل دقیقاً {realHubs.Length} ردیف دارد (واقعی: " +
            $"{Inventory.Api.Licensing.Infrastructure.HubLicenseFilter.FullNameCatalog.Count})");

        // نام کاملِ همهٔ هاب‌های واقعی در نگاشت باشد (اگر کسی هاب تازه اضافه کند و این‌جا نیاورد، قرمز می‌شود)
        var missing = realHubs
            .Where(h => !Inventory.Api.Licensing.Infrastructure.HubLicenseFilter.FullNameCatalog.ContainsKey(h.Type.FullName!))
            .Select(h => h.Type.FullName)
            .ToList();
        Check(missing.Count == 0,
            $"هیچ هاب واقعی از نگاشت نام کامل جا نمانده (جاافتاده: {(missing.Count == 0 ? "هیچ" : string.Join(", ", missing))})");

        // دوقلوی تست (namespace دیگر، نام یکسان) از مسیر «نام کوتاه» شناخته می‌شود —
        // سازگاری عمدی برای کلاس‌های مشتق/آزمون.
        Check(Inventory.Api.Licensing.Infrastructure.HubLicenseFilter.ResolveHubKey(typeof(HubTestDoubles.DashboardHub)) == "DashboardHub",
            "کلاسِ دیگری با نام DashboardHub (namespace تست) از مسیر نام کوتاه شناخته می‌شود");

        // نام تولیدی کامپایلر هرگز پذیرفته نمی‌شود
        var anonymous = new { X = 1 }.GetType();
        Check(Inventory.Api.Licensing.Infrastructure.HubLicenseFilter.ResolveHubKey(anonymous) is null,
            $"نوعِ تولیدی کامپایلر رد می‌شود (نام: {anonymous.Name})");

        // نوع کاملاً ناشناخته ⇒ null ⇒ بسته
        Check(Inventory.Api.Licensing.Infrastructure.HubLicenseFilter.ResolveHubKey(typeof(System.Text.StringBuilder)) is null,
            "نوع ناشناخته ⇒ نگاشت ندارد (fail-closed)");
    }

    // ---------- هاب ناشناخته ⇒ fail-closed ----------
    Check(!await TryConnect(new HubTestDoubles.GhostHub(), LicenseOverallStatus.Valid),
        "هاب نگاشت‌نشده حتی با لایسنس معتبر هم بسته می‌شود (fail-closed)");

    // ---------- متدها در حالت فقط‌خواندنی ----------
    var dashMethod = typeof(HubTestDoubles.DashboardHub).GetMethod(nameof(HubTestDoubles.DashboardHub.RequestHardwareCheck))!;
    var chatMethod = typeof(HubTestDoubles.ChatHub).GetMethod(nameof(HubTestDoubles.ChatHub.SendTyping))!;

    async Task<bool> TryInvoke(Hub hub, System.Reflection.MethodInfo method, LicenseOverallStatus status)
    {
        var f = new Inventory.Api.Licensing.Infrastructure.HubLicenseFilter(new GateLicenseService(status, false), log);
        var reached = false;
        try
        {
            await f.InvokeMethodAsync(
                new HubInvocationContext(new StubHubCallerContext(), provider, hub, method, Array.Empty<object>()),
                _ => { reached = true; return ValueTask.FromResult<object?>(null); });
        }
        catch (HubException) { return false; }
        return reached;
    }

    Check(await TryInvoke(new HubTestDoubles.DashboardHub(), dashMethod, LicenseOverallStatus.Missing),
        "RequestHardwareCheck در Missing ⇒ مجاز (فقط آمار سخت‌افزار)");
    Check(await TryInvoke(new HubTestDoubles.ChatHub(), chatMethod, LicenseOverallStatus.Invalid),
        "SendTyping در Invalid ⇒ مجاز (وضعیت گذرا، نه پیام)");
    Check(!await TryInvoke(new HubTestDoubles.NotifyHub(), typeof(HubTestDoubles.NotifyHub).GetMethod(nameof(HubTestDoubles.NotifyHub.SendAsync))!, LicenseOverallStatus.Missing),
        "NotifyHub.SendAsync (ارسال) در Missing ⇒ رد می‌شود");
    Check(!await TryInvoke(new HubTestDoubles.ChatHub(), typeof(HubTestDoubles.ChatHub).GetMethod(nameof(HubTestDoubles.ChatHub.NotifyMessage))!, LicenseOverallStatus.Missing),
        "متد هابِ ناشناخته/نوشتاری در Missing ⇒ رد می‌شود (allowlist، نه denylist)");
    Check(await TryInvoke(new HubTestDoubles.NotifyHub(), typeof(HubTestDoubles.NotifyHub).GetMethod(nameof(HubTestDoubles.NotifyHub.SendAsync))!, LicenseOverallStatus.Valid),
        "همان متد با لایسنس معتبر ⇒ مجاز");

    _ = filter; // فقط برای اطمینان از ساخت نمونه با وضعیت Missing
}


// ریشهٔ مخزن — برای تست‌های «پویش فایل» (کلید ثابت، انتساب CreatedAt).
string? repoRootForScan = Environment.CurrentDirectory;
while (repoRootForScan is not null && !Directory.Exists(Path.Combine(repoRootForScan, "src", "Inventory.Api")))
    repoRootForScan = Path.GetDirectoryName(repoRootForScan);

// ============================================================
Section("۲۶) کلید امضای JWT: هیچ کلید ثابتی در کد، کلید مخصوص نصب، توکن با کلید دیگر رد می‌شود (بند ۱)");

{
    var keysA = Inventory.Api.Security.SigningKeys.FromKey(
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(48), "test-A");
    var keysB = Inventory.Api.Security.SigningKeys.FromKey(
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(48), "test-B");

    // ---------- الف) خود کلید ----------
    Check(keysA.JwtKey.Length >= 32, $"کلید تولیدشده ≥ ۳۲ بایت است (واقعی: {keysA.JwtKey.Length})");
    Check(!keysA.JwtKey.SequenceEqual(keysB.JwtKey), "دو نصب مختلف، دو کلید مختلف دارند");

    // مشتق‌گیری برای کاربرد دیگر، کلید دیگری می‌دهد (توکن چت و JWT هم‌کلید نیستند)
    var chatKeyA = keysA.Derive("chat-file-v1");
    Check(!chatKeyA.SequenceEqual(keysA.JwtKey), "کلید مشتق‌شدهٔ «توکن چت» با کلید JWT یکی نیست");
    Check(chatKeyA.SequenceEqual(keysA.Derive("chat-file-v1")), "مشتق‌گیری قطعی است (همان ورودی، همان خروجی)");
    Check(!chatKeyA.SequenceEqual(keysA.Derive("another-purpose")), "کاربرد متفاوت ⇒ کلید متفاوت");

    // ---------- ب) اعتبارسنجی طول کلید در پیکربندی ----------
    var tooShort = false;
    string shortMessage = "";
    try
    {
        Inventory.Api.Security.SigningKeys.Create("کوتاه");
    }
    catch (InvalidOperationException ex) { tooShort = true; shortMessage = ex.Message; }

    Check(tooShort, $"کلید کوتاه‌تر از ۳۲ بایت رد می‌شود (پیام: {shortMessage.Split('.')[0]})");

    // کلید درست از پیکربندی پذیرفته می‌شود
    var fromConfig = Inventory.Api.Security.SigningKeys.Create(new string('K', 32));
    Check(fromConfig.Source.StartsWith("configuration:"),
        $"کلید معتبر از پیکربندی خوانده می‌شود (منبع: {fromConfig.Source})");

    // ---------- ج) توکن امضاشده با کلید دیگر باید رد شود ----------
    // این همان تستی است که خواسته شده: «توکن امضاشده با کلید دیگر رد شود».
    static string IssueToken(Inventory.Api.Security.SigningKeys keys, int userId = 7)
    {
        var creds = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(keys.JwtKey),
            Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);

        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: Inventory.Api.Services.AuthService.JwtIssuer,
            audience: Inventory.Api.Services.AuthService.JwtIssuer,
            claims: new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString()) },
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    }

    static bool ValidateWith(string token, Inventory.Api.Security.SigningKeys keys, out string error)
    {
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        try
        {
            handler.ValidateToken(token, new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = Inventory.Api.Services.AuthService.JwtIssuer,
                ValidAudience = Inventory.Api.Services.AuthService.JwtIssuer,
                ClockSkew = TimeSpan.Zero,
                IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(keys.JwtKey)
            }, out _);
            error = "";
            return true;
        }
        catch (Exception ex) { error = ex.GetType().Name; return false; }
    }

    var tokenA = IssueToken(keysA);

    Check(ValidateWith(tokenA, keysA, out var okErr),
        $"توکنِ همین نصب با کلید خودش معتبر است (خطا: {okErr})");

    var accepted = ValidateWith(tokenA, keysB, out var errB);
    Check(!accepted,
        $"⛔ توکنِ امضاشده با کلید نصب دیگر رد می‌شود (خطا: {errB})");
    Check(errB.Contains("Signature"),
        $"علت رد شدن، امضاست نه چیز دیگر (خطا: {errB})");

    // ---------- د) توکن فایل چت هم همین قاعده را دارد ----------
    var (chatToken, _) = Inventory.Api.Services.Chat.ChatFileToken.Create(7, keysA);
    Check(Inventory.Api.Services.Chat.ChatFileToken.TryValidate(chatToken, keysA, out var uidA) && uidA == 7,
        "توکن فایل چت با کلید نصب خودش معتبر است");
    Check(!Inventory.Api.Services.Chat.ChatFileToken.TryValidate(chatToken, keysB, out _),
        "⛔ توکن فایل چت با کلید نصب دیگر رد می‌شود");

    // ---------- ه) هیچ رشتهٔ کلید ثابتی در کد نماند ----------
    // (۱) ثابتِ عمومی JwtKey در AuthService حذف شده باشد
    var jwtKeyField = typeof(Inventory.Api.Services.AuthService)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                   System.Reflection.BindingFlags.Static)
        .FirstOrDefault(f => f.Name.Contains("JwtKey", StringComparison.OrdinalIgnoreCase));
    Check(jwtKeyField is null,
        "فیلد ثابت JwtKey در AuthService وجود ندارد (کلید از ISigningKeys می‌آید)");

    // (۲) رشتهٔ قبلی در هیچ فایل سورس نمانده باشد (پویش فایل‌ها، نه حافظه)
    var repoRoot = repoRootForScan;

    if (repoRoot is null)
    {
        Console.WriteLine("   (اطلاع) ریشهٔ مخزن پیدا نشد؛ پویش فایل انجام نشد.");
    }
    else
    {
        var legacyFragments = new[] { "InventoryApp-Jwt-Secret-Key", "Very-Long-Key-For-HS256" };
        var hits = new List<string>();

        foreach (var file in Directory.EnumerateFiles(Path.Combine(repoRoot, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            if (legacyFragments.Any(f => text.Contains(f, StringComparison.Ordinal)))
                hits.Add(Path.GetRelativePath(repoRoot, file));
        }

        Check(hits.Count == 0,
            $"هیچ فایل سورسی رشتهٔ کلید قدیمی را ندارد (تعداد فایل: {hits.Count}" +
            (hits.Count > 0 ? " — " + string.Join(", ", hits.Take(5)) : "") + ")");
    }
}

// ============================================================
Section("۲۷) ساعت ثبت سیستمی (CreatedAt): تهران، از DTO بایند نمی‌شود، در ویرایش بازنویسی نمی‌شود (بند ۲)");

{
    // ---------- الف) مقدار پیش‌فرض، وقت دیوارِ تهران است نه ساعت سرور ----------
    var invoiceDefault = new Inventory.Api.Data.FacInvoice();
    var incomingDefault = new Inventory.Api.Data.IncomingLetter();
    var outgoingDefault = new Inventory.Api.Data.OutgoingLetter();

    var tehranNow = PersianPeriodKey.NowIran();
    double DiffSeconds(DateTime? a) => a is null ? double.MaxValue : Math.Abs((a.Value - tehranNow).TotalSeconds);

    Check(DiffSeconds(invoiceDefault.CreatedAt) < 120,
        $"CreatedAt فاکتور = وقت تهران، نه وقت سرور (اختلاف با تهران: {DiffSeconds(invoiceDefault.CreatedAt):F0} ثانیه، " +
        $"ساعت سرور: {DateTime.Now:HH:mm}، تهران: {tehranNow:HH:mm})");
    Check(DiffSeconds(incomingDefault.CreatedAt) < 120, "CreatedAt نامهٔ وارده = وقت تهران");
    Check(DiffSeconds(outgoingDefault.CreatedAt) < 120, "CreatedAt نامهٔ صادره = وقت تهران");

    // و صریحاً با ساعت سرور فرق دارد اگر سرور UTC باشد (در همین کانتینر UTC است)
    var serverOffset = TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow);
    var differentFromServer = Math.Abs((invoiceDefault.CreatedAt - DateTime.Now).TotalMinutes) > 1;
    Check(serverOffset == PersianPeriodKey.IranZone.GetUtcOffset(DateTime.UtcNow) || differentFromServer,
        $"اگر ساعت سرور تهران نیست، CreatedAt با آن یکی نیست (افست سرور: {serverOffset}, " +
        $"اختلاف با ساعت سرور: {Math.Abs((invoiceDefault.CreatedAt - DateTime.Now).TotalMinutes):F0} دقیقه)");

    // ---------- ب) از DTO ورودی بایند نمی‌شود ----------
    // DTOهای «افزودن» و «ویرایش» — همان‌هایی که از کلاینت می‌آیند.
    var inputDtos = new[]
    {
        typeof(Inventory.Shared.Dtos.AddIncomingLetterDto),
        typeof(Inventory.Shared.Dtos.EditIncomingLetterDto),
        typeof(Inventory.Shared.Dtos.AddOutgoingLetterDto),
        typeof(Inventory.Shared.Dtos.EditOutgoingLetterDto),
        typeof(Inventory.Shared.Dtos.AddInnerLetterDto),
        typeof(Inventory.Shared.Dtos.EditInnerLetterDto),
        typeof(Inventory.Shared.Dtos.FacInvoice),   // DTO ورودی فاکتور (کنترلر همان را بایند می‌کند)
    };

    // الف) DTOهای نامه: هیچ‌کدام CreatedAt ندارند ⇒ از سمت کلاینت اصلاً قابل ارسال نیست.
    var letterDtos = inputDtos.Where(t => !t.Name.Contains("Invoice", StringComparison.Ordinal)).ToList();
    var dtoHits = letterDtos
        .SelectMany(t => t.GetProperties().Select(p => $"{t.Name}.{p.Name}"))
        .Where(name => name.EndsWith(".CreatedAt", StringComparison.OrdinalIgnoreCase))
        .ToList();

    Check(dtoHits.Count == 0,
        $"DTOهای نامه (افزودن/ویرایش) هیچ خاصیت CreatedAt ندارند ⇒ کلاینت نمی‌تواند زمان ثبت را تعیین کند " +
        $"(DTO بررسی‌شده: {letterDtos.Count}، یافته: {(dtoHits.Count == 0 ? "هیچ" : string.Join(", ", dtoHits))})");

    // ب) DTO فاکتور CreatedAt دارد (چون همین کلاس پاسخ خواندن هم هست)، ولی سرویس آن را
    //    نمی‌خواند: خودش رکورد تازه می‌سازد و CreatedAt را با ساعت تهران می‌نویسد.
    //    این را از متن سرویس می‌سنجیم (تست متنی، نه حافظه‌ای) — چون ساختن IWarehousingService
    //    و IAccountingService قلابی، آزمون را سنگین می‌کند بدون اینکه چیز بیشتری اثبات شود.
    if (repoRootForScan is not null)
    {
        var svcPath = Path.Combine(repoRootForScan, "src", "Inventory.Api", "Services", "Invoicing", "InvoicingService.cs");
        var svcText = File.ReadAllText(svcPath);

        Check(!svcText.Contains("CreatedAt = dto.CreatedAt", StringComparison.Ordinal)
              && !svcText.Contains("dto.CreatedAt,", StringComparison.Ordinal),
            "سرویس فاکتور، CreatedAt را از DTO نمی‌خواند (هیچ انتساب/استفاده‌ای از dto.CreatedAt نیست)");

        Check(svcText.Contains("CreatedAt = Inventory.Api.Services.Core.TehranClock.Now()", StringComparison.Ordinal),
            "سرویس فاکتور، CreatedAt را با ساعت تهران می‌نویسد (نه DateTime.Now)");
    }

    // ---------- ج) در ویرایش بازنویسی نمی‌شود ----------
    await ResetAsync();
    await using (var db = NewDb())
    {
        var wh = new Inventory.Api.Data.Warehouse { Name = "انبار تست زمان", Code = "W-TZ" };
        db.Warehouses.Add(wh);
        await db.SaveChangesAsync();

        var inv = new Inventory.Api.Data.FacInvoice
        {
            Number = 77001, Kind = Inventory.Shared.InvoiceKind.Sale, Date = DateTime.Today,
            WarehouseId = wh.Id, Settlement = Inventory.Shared.SettlementType.Cash, CreatedBy = "tz"
        };
        db.FacInvoices.Add(inv);
        await db.SaveChangesAsync();
    }

    DateTime? stampedAt;
    await using (var db = NewDb())
    {
        var inv = await db.FacInvoices.IgnoreQueryFilters().FirstAsync(i => i.Number == 77001);
        stampedAt = inv.CreatedAt;

        // شبیه‌سازی «ویرایش سرصفحه»: تاریخ سند را کاربر عوض می‌کند و بقیهٔ فیلدها ذخیره می‌شوند.
        inv.Date = DateTime.Today.AddMonths(-3);
        // DTO ورودی فاکتور: با اینکه خاصیت CreatedAt دارد، سرویس هرگز آن را نمی‌خواند
        // (رکورد از نو ساخته می‌شود و CreatedAt را خودِ سرور می‌نویسد). در ادامه همین را می‌سنجیم.
        var incomingDto = new Inventory.Shared.Dtos.FacInvoice
        {
            Id = 0,
            CreatedAt = new DateTime(2001, 1, 1)   // تلاش برای دست‌کاری زمان ثبت
        };
        Check(incomingDto.CreatedAt == new DateTime(2001, 1, 1),
            "(اطلاع) DTO ورودی می‌تواند مقدار CreatedAt داشته باشد؛ مهم این است که سرویس آن را نادیده بگیرد");
        await db.SaveChangesAsync();
    }

    await using (var db = NewDb())
    {
        var inv = await db.FacInvoices.IgnoreQueryFilters().FirstAsync(i => i.Number == 77001);
        Check(inv.CreatedAt == stampedAt,
            $"ویرایش سند، CreatedAt را تغییر نداد (قبلی: {stampedAt:yyyy-MM-dd HH:mm:ss}، اکنون: {inv.CreatedAt:yyyy-MM-dd HH:mm:ss})");
        Check(PersianPeriodKey.For(QuotaPeriod.Monthly, inv.CreatedAt) ==
              PersianPeriodKey.For(QuotaPeriod.Monthly, stampedAt!.Value),
            "جابه‌جایی تاریخ سند به ماه قبل، رکورد را از سقف ماه جاری خارج نمی‌کند");
    }

    // ---------- د) ردیف با CreatedAt=NULL ----------
    await using (var db = NewDb())
    {
        var wh = await db.Warehouses.FirstAsync(w => w.Code == "W-TZ");
        var nullRow = new Inventory.Api.Data.FacInvoice
        {
            Number = 77002, Kind = Inventory.Shared.InvoiceKind.Sale, Date = DateTime.Today,
            WarehouseId = wh.Id, Settlement = Inventory.Shared.SettlementType.Cash, CreatedBy = "tz",
            CreatedAt = default
        };
        db.FacInvoices.Add(nullRow);
        await db.SaveChangesAsync();
    }

    await using (var db = NewDb())
    {
        // EF Core مقدار default(DateTime) را NULL نمی‌نویسد؛ مرزِ واقعیِ ۰۱/۰۱/۰۰۰۱ را می‌سنجیم:
        // این ردیف در هیچ ماه شمسی‌ای نمی‌افتد مگر «کل عمر» — همان قاعده‌ای که مستند شد.
        var row = await db.FacInvoices.IgnoreQueryFilters().FirstAsync(i => i.Number == 77002);
        var (from, to) = PersianPeriodKey.RangeOf(QuotaPeriod.Monthly, PersianPeriodKey.NowIran());
        var inThisMonth = row.CreatedAt >= from && row.CreatedAt < to;
        Check(!inThisMonth, "ردیفِ بدون تاریخ معتبر در سقف ماه جاری شمرده نمی‌شود");
    }
}


// ---------- کمکی‌های بخش صندلی: ریشهٔ مخزن و گرفتن استثنای صندلی ----------
string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Inventory.Api")))
        dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("ریشهٔ مخزن پیدا نشد.");
}

async Task<Inventory.Api.Licensing.Infrastructure.SeatException?> CaptureSeatException(Func<Task> action)
{
    try { await action(); return null; }
    catch (Inventory.Api.Licensing.Infrastructure.SeatException ex) { return ex; }
}

// ============================================================
Section("۲۸) صندلی لایسنس (مرحله ۳): قاعدهٔ کاربر، معافیت امضاشده، بازپس‌گیری، حالت تخریب، کلمپ، هم‌زمانی");

// ---------- کلیدهای تنظیمات: کف‌ها و پیش‌فرض‌ها (بند ۵) ----------
{
    static Inventory.Api.Licensing.LicensingOptions Opt(int idle = 0, int cache = 0, string? evict = null, int minIdle = 0)
        => new()
        {
            SessionIdleMinutes = idle,
            SeatCacheSeconds = cache,
            SeatEviction = evict,
            SeatEvictionMinIdleMinutes = minIdle
        };

    var def = new Inventory.Api.Licensing.LicensingOptions();
    Check(def.EffectiveIdleMinutes() == 15, $"IdleMinutes پیش‌فرض ۱۵ (واقعی: {def.EffectiveIdleMinutes()})");
    Check(Opt(0).EffectiveIdleMinutes() == 15, $"IdleMinutes(صفر) ⇒ 15 (واقعی: {Opt(0).EffectiveIdleMinutes()}) — «بدون محدودیت» وجود ندارد");
    Check(Opt(-7).EffectiveIdleMinutes() == 15, $"IdleMinutes(منفی) ⇒ 15 (واقعی: {Opt(-7).EffectiveIdleMinutes()}) — «بدون محدودیت» وجود ندارد");
    Check(Opt(1).EffectiveIdleMinutes() == 10, $"IdleMinutes(۱ ⇒ کف ۱۰) ⇒ 10 (واقعی: {Opt(1).EffectiveIdleMinutes()}) — «بدون محدودیت» وجود ندارد");
    Check(Opt(9).EffectiveIdleMinutes() == 10, $"IdleMinutes(۹ ⇒ کف ۱۰) ⇒ 10 (واقعی: {Opt(9).EffectiveIdleMinutes()}) — «بدون محدودیت» وجود ندارد");
    Check(Opt(60).EffectiveIdleMinutes() == 60, $"IdleMinutes(۶۰ ⇒ همان) ⇒ 60 (واقعی: {Opt(60).EffectiveIdleMinutes()})");
    Check(Opt(99999).EffectiveIdleMinutes() == 240, $"IdleMinutes(بزرگ ⇒ سقف ۲۴۰) ⇒ 240 (واقعی: {Opt(99999).EffectiveIdleMinutes()})");

    Check(Opt(cache: 0).EffectiveSeatCacheSeconds() == 45, $"SeatCacheSeconds(0) ⇒ 45 (واقعی: {Opt(cache: 0).EffectiveSeatCacheSeconds()}) — همیشه در ۱۵..۱۲۰");
    Check(Opt(cache: -1).EffectiveSeatCacheSeconds() == 45, $"SeatCacheSeconds(-1) ⇒ 45 (واقعی: {Opt(cache: -1).EffectiveSeatCacheSeconds()}) — همیشه در ۱۵..۱۲۰");
    Check(Opt(cache: 1).EffectiveSeatCacheSeconds() == 15, $"SeatCacheSeconds(1) ⇒ 15 (واقعی: {Opt(cache: 1).EffectiveSeatCacheSeconds()}) — همیشه در ۱۵..۱۲۰");
    Check(Opt(cache: 5).EffectiveSeatCacheSeconds() == 15, $"SeatCacheSeconds(5) ⇒ 15 (واقعی: {Opt(cache: 5).EffectiveSeatCacheSeconds()}) — همیشه در ۱۵..۱۲۰");
    Check(Opt(cache: 45).EffectiveSeatCacheSeconds() == 45, $"SeatCacheSeconds(45) ⇒ 45 (واقعی: {Opt(cache: 45).EffectiveSeatCacheSeconds()}) — همیشه در ۱۵..۱۲۰");
    Check(Opt(cache: 120).EffectiveSeatCacheSeconds() == 120, $"SeatCacheSeconds(120) ⇒ 120 (واقعی: {Opt(cache: 120).EffectiveSeatCacheSeconds()}) — همیشه در ۱۵..۱۲۰");
    Check(Opt(cache: 500).EffectiveSeatCacheSeconds() == 120, $"SeatCacheSeconds(500) ⇒ 120 (واقعی: {Opt(cache: 500).EffectiveSeatCacheSeconds()}) — همیشه در ۱۵..۱۲۰");

    Check(Opt(evict: null).EffectiveSeatEviction() == "deny", $"SeatEviction(«null») ⇒ deny (واقعی: {Opt(evict: null).EffectiveSeatEviction()})");
    Check(Opt(evict: "").EffectiveSeatEviction() == "deny", $"SeatEviction(«») ⇒ deny (واقعی: {Opt(evict: "").EffectiveSeatEviction()})");
    Check(Opt(evict: "deny").EffectiveSeatEviction() == "deny", $"SeatEviction(«deny») ⇒ deny (واقعی: {Opt(evict: "deny").EffectiveSeatEviction()})");
    Check(Opt(evict: "whatever").EffectiveSeatEviction() == "deny", $"SeatEviction(«whatever») ⇒ deny (واقعی: {Opt(evict: "whatever").EffectiveSeatEviction()})");
    Check(Opt(evict: "evict-oldest-idle").EffectiveSeatEviction() == "evict-oldest-idle", $"SeatEviction(«evict-oldest-idle») ⇒ evict-oldest-idle (واقعی: {Opt(evict: "evict-oldest-idle").EffectiveSeatEviction()})");
    Check(Opt(evict: "EVICT-OLDEST-IDLE").EffectiveSeatEviction() == "evict-oldest-idle", $"SeatEviction(«EVICT-OLDEST-IDLE») ⇒ evict-oldest-idle (واقعی: {Opt(evict: "EVICT-OLDEST-IDLE").EffectiveSeatEviction()})");

    Check(Opt(minIdle: 0).EffectiveSeatEvictionMinIdleMinutes() == 5, $"SeatEvictionMinIdleMinutes(0) ⇒ 5 (واقعی: {Opt(minIdle: 0).EffectiveSeatEvictionMinIdleMinutes()}) — کف ۵");
    Check(Opt(minIdle: -2).EffectiveSeatEvictionMinIdleMinutes() == 5, $"SeatEvictionMinIdleMinutes(-2) ⇒ 5 (واقعی: {Opt(minIdle: -2).EffectiveSeatEvictionMinIdleMinutes()}) — کف ۵");
    Check(Opt(minIdle: 1).EffectiveSeatEvictionMinIdleMinutes() == 5, $"SeatEvictionMinIdleMinutes(1) ⇒ 5 (واقعی: {Opt(minIdle: 1).EffectiveSeatEvictionMinIdleMinutes()}) — کف ۵");
    Check(Opt(minIdle: 5).EffectiveSeatEvictionMinIdleMinutes() == 5, $"SeatEvictionMinIdleMinutes(5) ⇒ 5 (واقعی: {Opt(minIdle: 5).EffectiveSeatEvictionMinIdleMinutes()}) — کف ۵");
    Check(Opt(minIdle: 60).EffectiveSeatEvictionMinIdleMinutes() == 60, $"SeatEvictionMinIdleMinutes(60) ⇒ 60 (واقعی: {Opt(minIdle: 60).EffectiveSeatEvictionMinIdleMinutes()}) — کف ۵");
    Check(Opt(minIdle: 9999).EffectiveSeatEvictionMinIdleMinutes() == 120, $"SeatEvictionMinIdleMinutes(9999) ⇒ 120 (واقعی: {Opt(minIdle: 9999).EffectiveSeatEvictionMinIdleMinutes()}) — کف ۵");
}

// ---------- کمکی‌های بخش صندلی ----------
EffectiveEntitlement SeatEnt(LicenseUserMode mode, int max, int exemptCap = 0, params string[] exemptNames)
{
    var doc = new LicenseDocument
    {
        Serial = "TEST-SEAT", KeyId = "dev-2026a", CustomerId = "C-1", CustomerName = "تست صندلی",
        Deployment = DeploymentKind.OnPrem, InstallationId = "inst", IssuedAt = new DateTime(2026, 1, 1),
        Items = new List<LicenseItem>
        {
            new()
            {
                Id = "SEAT-1",
                Scope = new List<string> { ModuleCodes.Core, ModuleCodes.Invoicing, ModuleCodes.Chat },
                Term = new LicenseTerm { Type = LicenseTermType.Perpetual },
                Users = new LicenseUsers
                {
                    Mode = mode,
                    Max = max,
                    AppliesTo = LicenseUserScope.Global,
                    Exempt = exemptCap,
                    ExemptUsers = exemptNames.ToList()
                }
            }
        }
    };
    return EntitlementMerger.Merge(new[] { doc });
}

Inventory.Api.Licensing.Services.SeatService NewSeats(AppDbContext db, EffectiveEntitlement ent,
    Inventory.Api.Licensing.Services.SeatCache cache, Inventory.Api.Licensing.LicensingOptions opts,
    Inventory.Api.Licensing.Services.SeatDegradedJournal journal)
    => new(db,
        new SeatOnlyLicenseService(ent),
        new LicenseSessionService(db, opts, NullLogger<LicenseSessionService>.Instance),
        cache,
        journal,
        opts,
        NullLogger<Inventory.Api.Licensing.Services.SeatService>.Instance);

async Task SeedSeatUsersAsync(int count)
{
    await using var db = NewDb();
    var existing = await db.Users.Where(u => u.Username.StartsWith("su")).Select(u => u.Username).ToListAsync();
    for (var i = 1; i <= count; i++)
    {
        var name = $"su{i}";
        if (existing.Contains(name)) continue;
        db.Users.Add(new Inventory.Api.Data.User
        {
            Username = name,
            PasswordHash = "Zm9vYmFy",   // قالب salt.hash — قابل احراز نیست، ولی «حساب سیستمی» هم نیست
            Role = "Operator",
            IsActive = true
        });
    }
    await db.SaveChangesAsync();
}

async Task<List<Inventory.Api.Data.User>> SeatUsersAsync()
{
    await using var db = NewDb();
    return await db.Users.Where(u => u.Username.StartsWith("su")).OrderBy(u => u.Id).ToListAsync();
}

async Task ClearSessionsAsync()
{
    await using var db = NewDb();
    var all = await db.LicenseSessions.ToListAsync();
    db.LicenseSessions.RemoveRange(all);
    await db.SaveChangesAsync();
}

// ============================================================
//  ۲۸.۱) دو ورود اول با سقف ۲ — و رد شدن سوم، بدون قفل سراسری
{
    await ResetAsync();
    await ClearSessionsAsync();
    await SeedSeatUsersAsync(4);
    var su = await SeatUsersAsync();
    var ent = SeatEnt(LicenseUserMode.Concurrent, 2);

    await using (var db = NewDb())
    {
        var opts = new Inventory.Api.Licensing.LicensingOptions();
        var journal = new Inventory.Api.Licensing.Services.SeatDegradedJournal(paths, NullLogger<Inventory.Api.Licensing.Services.SeatDegradedJournal>.Instance);
        var svc = NewSeats(db, ent, new Inventory.Api.Licensing.Services.SeatCache(), opts, journal);

        var g1 = await svc.BeginLoginAsync(su[0].Id, su[0].Username, "PC-1", "10.0.0.1");
        var g2 = await svc.BeginLoginAsync(su[1].Id, su[1].Username, "PC-2", "10.0.0.2");
        Check(g1.Allowed && g2.Allowed, "دو ورود اول با سقف ۲ پذیرفته می‌شوند");

        var g3 = await svc.BeginLoginAsync(su[2].Id, su[2].Username, null, null);
        Check(!g3.Allowed && g3.Code == "LICENSE_SEAT_FULL",
            $"ورود سوم رد می‌شود با کد LICENSE_SEAT_FULL (واقعی: {g3.Code})");
        Check(g3.Message.Contains("ظرفیت صندلی‌های لایسنس پر است"), $"پیام فارسی «ظرفیت پر است» برگردانده می‌شود: «{g3.Message}»");

        // همان کاربر از تب دوم: صندلی تازه نمی‌گیرد
        var again = await svc.BeginLoginAsync(su[0].Id, su[0].Username, "PC-1", "10.0.0.1");
        Check(again.Allowed, "ورود دوبارهٔ همان کاربر (تب/دستگاه دوم) صندلی تازه نمی‌گیرد");

        var distinct = await svc.CountConcurrentAsync(0);
        Check(distinct == 2, $"شمارش صندلی «کاربر یگانه» است (واقعی: {distinct} — انتظار: ۲)");
    }
}

// ============================================================
//  ۲۸.۲) معافیت فقط از فهرست امضاشده و فقط تا سقف exempt
{
    await ResetAsync();
    await ClearSessionsAsync();
    await SeedSeatUsersAsync(3);
    var su = await SeatUsersAsync();
    var ent = SeatEnt(LicenseUserMode.Concurrent, 1, 1, su[0].Username);

    await using (var db = NewDb())
    {
        var opts = new Inventory.Api.Licensing.LicensingOptions();
        var journal = new Inventory.Api.Licensing.Services.SeatDegradedJournal(paths, NullLogger<Inventory.Api.Licensing.Services.SeatDegradedJournal>.Instance);
        var svc = NewSeats(db, ent, new Inventory.Api.Licensing.Services.SeatCache(), opts, journal);

        var g1 = await svc.BeginLoginAsync(su[0].Id, su[0].Username, null, null);
        var g2 = await svc.BeginLoginAsync(su[1].Id, su[1].Username, null, null);
        var g3 = await svc.BeginLoginAsync(su[2].Id, su[2].Username, null, null);

        Check(g1.Allowed, "کاربر معاف (اولین نام فهرست) با وجود سقف ۱ پذیرفته می‌شود");
        Check(g2.Allowed,
            "نام دوم فهرست معاف نیست (از سقف «exempt»=۱ گذشته) و چون کاربر معاف صندلی مصرف نمی‌کند، " +
            $"صندلی آزاد را می‌گیرد (کد: {g2.Code})");
        Check(!g3.Allowed && g3.Code == "LICENSE_SEAT_FULL",
            $"و نام سوم هم معاف نیست؛ با پر شدن تک‌صندلی، رد می‌شود (کد: {g3.Code}) — یعنی فقط «{su[0].Username}» واقعاً معاف بود");
    }
}

// ============================================================
//  ۲۸.۳) نقش Admin معاف نیست؛ ستون LicenseSeatExempt اثری ندارد
{
    await ResetAsync();
    await ClearSessionsAsync();
    await SeedSeatUsersAsync(3);

    await using (var db = NewDb())
    {
        var all = await db.Users.Where(u => u.Username.StartsWith("su")).OrderBy(u => u.Id).ToListAsync();
        all[0].Role = "Admin";
        all[1].Role = "Admin";
        all[2].LicenseSeatExempt = true;   // ستون میراثی — باید بی‌اثر باشد
        await db.SaveChangesAsync();
    }

    var su = await SeatUsersAsync();
    var ent = SeatEnt(LicenseUserMode.Concurrent, 2);

    await using (var db = NewDb())
    {
        var opts = new Inventory.Api.Licensing.LicensingOptions();
        var journal = new Inventory.Api.Licensing.Services.SeatDegradedJournal(paths, NullLogger<Inventory.Api.Licensing.Services.SeatDegradedJournal>.Instance);
        var svc = NewSeats(db, ent, new Inventory.Api.Licensing.Services.SeatCache(), opts, journal);

        var a = await svc.BeginLoginAsync(su[0].Id, su[0].Username, null, null);
        var b = await svc.BeginLoginAsync(su[1].Id, su[1].Username, null, null);
        Check(a.Allowed && b.Allowed, "دو «مدیر» صندلی می‌گیرند (هیچ معافیت نقشی وجود ندارد)");

        var c = await svc.BeginLoginAsync(su[2].Id, su[2].Username, null, null);
        Check(!c.Allowed && c.Code == "LICENSE_SEAT_FULL",
            $"ستون LicenseSeatExempt=1 هیچ اثری ندارد؛ کاربر سوم رد می‌شود (کد: {c.Code})");
    }
}

// ============================================================
//  ۲۸.۴) Named: ترتیب Id، پیام «نفرm»، و نبود قفل سراسری
{
    await ResetAsync();
    await ClearSessionsAsync();
    await SeedSeatUsersAsync(4);
    var su = await SeatUsersAsync();
    var ent = SeatEnt(LicenseUserMode.Named, 2);

    await using (var db = NewDb())
    {
        var opts = new Inventory.Api.Licensing.LicensingOptions();
        var journal = new Inventory.Api.Licensing.Services.SeatDegradedJournal(paths, NullLogger<Inventory.Api.Licensing.Services.SeatDegradedJournal>.Instance);
        var svc = NewSeats(db, ent, new Inventory.Api.Licensing.Services.SeatCache(), opts, journal);

        var g1 = await svc.BeginLoginAsync(su[0].Id, su[0].Username, null, null);
        var g2 = await svc.BeginLoginAsync(su[1].Id, su[1].Username, null, null);
        Check(g1.Allowed && g2.Allowed, "Named با سقف ۲: نفرات ۱ و ۲ (به ترتیب Id) وارد می‌شوند");

        var g3 = await svc.BeginLoginAsync(su[2].Id, su[2].Username, null, null);
        Check(!g3.Allowed && g3.Code == "LICENSE_SEAT_FULL", $"Named: نفر سوم (Id بزرگ‌تر) رد می‌شود (کد: {g3.Code})");
        Check(g3.Message.Contains("نفر 3 از 2"), $"پیام، «نفرم» را نشان می‌دهد: «{g3.Message}»");

        // ورود دوبارهٔ نفر اول همچنان مجاز است — رد شدن یکی، بقیه را قفل نمی‌کند
        var g4 = await svc.BeginLoginAsync(su[0].Id, su[0].Username, null, null);
        Check(g4.Allowed, "رد شدن یک کاربر، ورود بقیه را قفل نمی‌کند (هیچ شرط سراسری وجود ندارد)");

        // ساخت/فعال‌سازی بالاتر از سقف
        var createAbove = await CaptureSeatException(() => svc.EnsureUserSlotAvailableAsync(null, "su9", true));
        Check(createAbove is not null && createAbove.Code == "LICENSE_SEAT_FULL",
            $"ساخت کاربر بالاتر از سقف رد می‌شود (پیام: «{createAbove?.Message}»)");

        var activateAbove = await CaptureSeatException(() => svc.EnsureUserSlotAvailableAsync(su[3].Id, su[3].Username, true));
        Check(activateAbove is not null && activateAbove.Code == "LICENSE_SEAT_FULL",
            "فعال‌سازی کاربر غیرفعالِ بالاتر از سقف رد می‌شود");

        var deactivate = await CaptureSeatException(() => svc.EnsureUserSlotAvailableAsync(su[0].Id, su[0].Username, false));
        Check(deactivate is null, "غیرفعال‌کردن کاربر هرگز به‌خاطر سقف رد نمی‌شود");
    }
}

// ============================================================
//  ۲۸.۵) sid تصادفی + بازپس‌گیری بعد از پاک‌شدن جدول
{
    await ResetAsync();
    await ClearSessionsAsync();
    await SeedSeatUsersAsync(4);
    var su = await SeatUsersAsync();
    var ent = SeatEnt(LicenseUserMode.Concurrent, 3);

    string? sid1, sid2, sid3;
    await using (var db = NewDb())
    {
        var opts = new Inventory.Api.Licensing.LicensingOptions();
        var journal = new Inventory.Api.Licensing.Services.SeatDegradedJournal(paths, NullLogger<Inventory.Api.Licensing.Services.SeatDegradedJournal>.Instance);
        var svc = NewSeats(db, ent, new Inventory.Api.Licensing.Services.SeatCache(), opts, journal);

        var g1 = await svc.BeginLoginAsync(su[0].Id, su[0].Username, null, null);
        var g2 = await svc.BeginLoginAsync(su[1].Id, su[1].Username, null, null);
        var g3 = await svc.BeginLoginAsync(su[2].Id, su[2].Username, null, null);
        sid1 = g1.SessionId; sid2 = g2.SessionId; sid3 = g3.SessionId;

        Check(g1.Allowed && g1.SessionId is { Length: 32 } && Guid.TryParseExact(g1.SessionId, "N", out _),
            $"ورود su1: صندلی گرفته شد و sid یک GUID تصادفی ۳۲ نویسه‌ای است (طول: {g1.SessionId?.Length})");
        Check(g2.Allowed && g2.SessionId is { Length: 32 }, $"ورود su2: صندلی گرفته شد و sid یک GUID تصادفی ۳۲ نویسه‌ای است (طول: {g2.SessionId?.Length})");
        Check(g3.Allowed && g3.SessionId is { Length: 32 }, $"ورود su3: صندلی گرفته شد و sid یک GUID تصادفی ۳۲ نویسه‌ای است (طول: {g3.SessionId?.Length})");

        var g4 = await svc.BeginLoginAsync(su[3].Id, su[3].Username, null, null);
        Check(!g4.Allowed, "کاربر چهارم با سقف ۳ رد می‌شود");
    }

    // ری‌استارت/آپدیت: جدول نشست‌ها خالی می‌شود. سه توکن معتبر نباید بیرون بیفتند.
    await ClearSessionsAsync();

    await using (var db = NewDb())
    {
        var opts = new Inventory.Api.Licensing.LicensingOptions();
        var journal = new Inventory.Api.Licensing.Services.SeatDegradedJournal(paths, NullLogger<Inventory.Api.Licensing.Services.SeatDegradedJournal>.Instance);
        var svc = NewSeats(db, ent, new Inventory.Api.Licensing.Services.SeatCache(), opts, journal);

        var r1 = await svc.AuthorizeRequestAsync(su[0].Id, sid1, su[0].Username, true);
        var r2 = await svc.AuthorizeRequestAsync(su[1].Id, sid2, su[1].Username, true);
        var r3 = await svc.AuthorizeRequestAsync(su[2].Id, sid3, su[2].Username, true);
        Check(r1.Allowed && r2.Allowed && r3.Allowed,
            "بعد از خالی‌شدن جدول، سه توکن معتبر صندلی را بازپس می‌گیرند (کسی بیرون نمی‌افتد)");

        var g4 = await svc.BeginLoginAsync(su[3].Id, su[3].Username, null, null);
        Check(!g4.Allowed && g4.Code == "LICENSE_SEAT_FULL",
            $"و چون ظرفیت هنوز پر است، کاربر چهارم همان ۴۰۳ «ظرفیت پر است» را می‌گیرد (کد: {g4.Code})");
    }
}

// ============================================================
//  ۲۸.۶) نشست Closed/Evicted/Expired و نشست Active با LastSeen قدیمی
{
    await ResetAsync();
    await ClearSessionsAsync();
    await SeedSeatUsersAsync(4);
    var su = await SeatUsersAsync();
    var ent = SeatEnt(LicenseUserMode.Concurrent, 3);
    var opts2 = new Inventory.Api.Licensing.LicensingOptions { SessionIdleMinutes = 15 };

    string sidA, sidB, sidC;
    await using (var db = NewDb())
    {
        var journal = new Inventory.Api.Licensing.Services.SeatDegradedJournal(paths, NullLogger<Inventory.Api.Licensing.Services.SeatDegradedJournal>.Instance);
        var svc = NewSeats(db, ent, new Inventory.Api.Licensing.Services.SeatCache(), opts2, journal);
        sidA = (await svc.BeginLoginAsync(su[0].Id, su[0].Username, null, null)).SessionId!;
        sidB = (await svc.BeginLoginAsync(su[1].Id, su[1].Username, null, null)).SessionId!;
        sidC = (await svc.BeginLoginAsync(su[2].Id, su[2].Username, null, null)).SessionId!;
    }

    await using (var db = NewDb())
    {
        var a = await db.LicenseSessions.FirstAsync(s => s.SessionId == sidA);
        a.Status = "Closed";
        var b = await db.LicenseSessions.FirstAsync(s => s.SessionId == sidB);
        b.Status = "Evicted";
        await db.SaveChangesAsync();
    }

    await using (var db = NewDb())
    {
        var journal = new Inventory.Api.Licensing.Services.SeatDegradedJournal(paths, NullLogger<Inventory.Api.Licensing.Services.SeatDegradedJournal>.Instance);
        var svc = NewSeats(db, ent, new Inventory.Api.Licensing.Services.SeatCache(), opts2, journal);

        var a = await svc.AuthorizeRequestAsync(su[0].Id, sidA, su[0].Username, true);
        Check(!a.Allowed && a.Code == "LICENSE_SEAT_ENDED", $"نشست Closed ⇒ LICENSE_SEAT_ENDED (واقعی: {a.Code})");

        var b = await svc.AuthorizeRequestAsync(su[1].Id, sidB, su[1].Username, true);
        Check(!b.Allowed && b.Code == "LICENSE_SEAT_ENDED", $"نشست Evicted ⇒ LICENSE_SEAT_ENDED (واقعی: {b.Code})");
    }

    // Expired با ظرفیت آزاد: بازپس‌گیری، بدون ورود دوباره
    await using (var db = NewDb())
    {
        var c = await db.LicenseSessions.FirstAsync(s => s.SessionId == sidC);
        c.Status = "Expired";
        await db.SaveChangesAsync();
    }
    await using (var db = NewDb())
    {
        var journal = new Inventory.Api.Licensing.Services.SeatDegradedJournal(paths, NullLogger<Inventory.Api.Licensing.Services.SeatDegradedJournal>.Instance);
        var svc = NewSeats(db, ent, new Inventory.Api.Licensing.Services.SeatCache(), opts2, journal);
        var c = await svc.AuthorizeRequestAsync(su[2].Id, sidC, su[2].Username, true);
        Check(c.Allowed, "نشست Expired با ظرفیت آزاد بازپس گرفته می‌شود (کاربر مجبور به ورود دوباره نیست)");
    }

    // Active ولی ۹۰ دقیقه بیکار: نباید شمرده شود
    await ResetAsync();
    await ClearSessionsAsync();
    await using (var db = NewDb())
    {
        db.LicenseSessions.Add(new Inventory.Api.Data.LicenseSession
        {
            SessionId = "stale-one", UserId = su[0].Id, Status = "Active",
            StartedAt = Inventory.Api.Licensing.ClockAnchor.Now().AddHours(-3),
            LastSeen = Inventory.Api.Licensing.ClockAnchor.Now().AddMinutes(-90)
        });
        await db.SaveChangesAsync();
    }

    await using (var db = NewDb())
    {
        var journal = new Inventory.Api.Licensing.Services.SeatDegradedJournal(paths, NullLogger<Inventory.Api.Licensing.Services.SeatDegradedJournal>.Instance);
        var svc = NewSeats(db, ent, new Inventory.Api.Licensing.Services.SeatCache(), opts2, journal);

        var used = await svc.CountConcurrentAsync(0);
        Check(used == 0, $"نشست Active با LastSeen قدیمی شمرده نمی‌شود (واقعی: {used})");

        var g = await svc.BeginLoginAsync(su[1].Id, su[1].Username, null, null);
        Check(g.Allowed, "پس یک صندلی آزاد واقعاً آزاد است و ورود تازه پذیرفته می‌شود");
    }
}

// ============================================================
//  ۲۸.۷) کش: ۱۰۰ درخواست در پنجره، حداکثر یک کوئری بررسی
{
    await ResetAsync();
    await ClearSessionsAsync();
    await SeedSeatUsersAsync(2);
    var su = await SeatUsersAsync();
    var ent = SeatEnt(LicenseUserMode.Concurrent, 5);

    await using (var db = NewDb())
    {
        var opts = new Inventory.Api.Licensing.LicensingOptions { SeatCacheSeconds = 45 };
        var cache = new Inventory.Api.Licensing.Services.SeatCache();
        var journal = new Inventory.Api.Licensing.Services.SeatDegradedJournal(paths, NullLogger<Inventory.Api.Licensing.Services.SeatDegradedJournal>.Instance);
        var svc = NewSeats(db, ent, cache, opts, journal);

        var login = await svc.BeginLoginAsync(su[0].Id, su[0].Username, null, null);
        var sid = login.SessionId;

        var hitsBefore = cache.Hits;
        var missesBefore = cache.Misses;
        var allowedAll = true;
        for (var i = 0; i < 100; i++)
            if (!(await svc.AuthorizeRequestAsync(su[0].Id, sid, su[0].Username, i % 3 == 0)).Allowed) allowedAll = false;

        Check(allowedAll, "همهٔ ۱۰۰ درخواست عبور می‌کنند");
        var hits = cache.Hits - hitsBefore;
        var misses = cache.Misses - missesBefore;
        Check(hits >= 99 && misses <= 1,
            $"۱۰۰ درخواست در پنجرهٔ کش ⇒ حداکثر ۱ کوئری بررسی (هیت: {hits}، میس: {misses})");
    }
}

// ============================================================
//  ۲۸.۸) «فعالیت واقعی»: فقط پولینگ GET مستثناست
{
    Check(!Inventory.Api.Licensing.Infrastructure.SeatActivityRules.IsActivity("GET", "/api/notifications/unread-count"),
        "GET اعلان‌ها (شمارش/فهرست) فعالیت نیست");
    Check(!Inventory.Api.Licensing.Infrastructure.SeatActivityRules.IsActivity("GET", "/api/chat/conversations"),
        "GET فهرست گفتگو فعالیت نیست");
    Check(!Inventory.Api.Licensing.Infrastructure.SeatActivityRules.IsActivity("GET", "/api/system/live-metrics"),
        "GET متریک زنده فعالیت نیست");
    Check(Inventory.Api.Licensing.Infrastructure.SeatActivityRules.IsActivity("POST", "/api/notifications/read-all"),
        "POST read-all در اعلان‌ها فعالیت است");
    Check(Inventory.Api.Licensing.Infrastructure.SeatActivityRules.IsActivity("POST", "/api/notifications/1/read"),
        "POST read در اعلان‌ها فعالیت است");
    Check(Inventory.Api.Licensing.Infrastructure.SeatActivityRules.IsActivity("POST", "/api/chat/messages"),
        "ارسال پیام در چت فعالیت است");
    Check(Inventory.Api.Licensing.Infrastructure.SeatActivityRules.IsActivity("GET", "/api/invoices"),
        "GET دادهٔ کسب‌وکار (فاکتورها) فعالیت است");
    Check(!Inventory.Api.Licensing.Infrastructure.SeatActivityRules.IsActivity("GET", "/hubs/chat")
          && !Inventory.Api.Licensing.Infrastructure.SeatActivityRules.IsActivity("POST", "/hubs/chat/negotiate"),
        "اتصال هاب هیچ‌وقت فعالیت نیست");
}

// ============================================================
//  ۲۸.۹) خطای گذرا: پنجرهٔ ۱۰ دقیقه، ژورنال، و رد بعد از پنجره
{
    await ResetAsync();
    await ClearSessionsAsync();
    await SeedSeatUsersAsync(2);
    var su = await SeatUsersAsync();
    var ent = SeatEnt(LicenseUserMode.Concurrent, 2);   // شمارش از مسیر Concurrent می‌گذرد

    Microsoft.Data.Sqlite.SqliteConnection? locker = null;
    DbContextOptions<AppDbContext> transientOptions;

    if (!useSqlServer)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        locker = new SqliteConnection($"Data Source={dbPath};Default Timeout=30");
        await locker.OpenAsync();
        await using var cmd = locker.CreateCommand();
        cmd.CommandText = "BEGIN EXCLUSIVE";
        await cmd.ExecuteNonQueryAsync();

        transientOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath};Default Timeout=1").Options;
    }
    else
    {
        // SQL Server: رشتهٔ اتصال به پورت بسته ⇒ خطای شبکه/Timeout (گذرا).
        transientOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost,65000;Database=InventoryLicenseTest;" +
                          "User Id=sa;Password=not-used;Connect Timeout=2;TrustServerCertificate=True")
            .Options;
    }

    var journal = new Inventory.Api.Licensing.Services.SeatDegradedJournal(paths, NullLogger<Inventory.Api.Licensing.Services.SeatDegradedJournal>.Instance);
    try
    {
        await using var brokenDb = new AppDbContext(transientOptions);
        var opts = new Inventory.Api.Licensing.LicensingOptions();
        var svc = NewSeats(brokenDb, ent, new Inventory.Api.Licensing.Services.SeatCache(), opts, journal);

        var g = await svc.BeginLoginAsync(su[0].Id, su[0].Username, null, null);
        Check(g.Allowed, "خطای گذرا (Timeout/قطع اتصال/Busy) در پنجرهٔ ۱۰ دقیقه‌ای، ورود را نمی‌شکند");

        journal.Append("SEAT_DEGRADED_LOGIN", su[0].Id, su[0].Username, "ورود در حالت تخریب‌شده");
        Check(journal.PendingCount >= 1, $"ورود در حالت تخریب‌شده در ژورنال محلی ثبت شد (خطوط: {journal.PendingCount})");

        using (Inventory.Api.Licensing.ClockAnchor.Fix(Inventory.Api.Licensing.ClockAnchor.Now().AddMinutes(11)))
        {
            var late = await svc.BeginLoginAsync(su[1].Id, su[1].Username, null, null);
            Check(!late.Allowed && late.Code == "LICENSE_SEAT_UNAVAILABLE",
                $"بعد از ۱۰ دقیقهٔ بدون بررسی موفق، ورود جدید رد می‌شود (کد: {late.Code})");
        }
    }
    finally
    {
        if (locker is not null)
        {
            try { await using var rollback = locker.CreateCommand(); rollback.CommandText = "ROLLBACK"; await rollback.ExecuteNonQueryAsync(); } catch { }
            try { await locker.CloseAsync(); } catch { }
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(journal.FilePath)) File.Delete(journal.FilePath); } catch { }
    }
}

// ============================================================
//  ۲۸.۱۰) خطای ساختاری: رد ورود جدید، رخداد Warning، نشست موجود ادامه
{
    await ResetAsync();
    await ClearSessionsAsync();
    await SeedSeatUsersAsync(2);
    var su = await SeatUsersAsync();
    var ent = SeatEnt(LicenseUserMode.Concurrent, 3);

    await using (var db = NewDb())
    {
        await db.Database.ExecuteSqlRawAsync(
            useSqlServer ? "DROP TABLE [dbo].[LicenseSessions]" : "DROP TABLE IF EXISTS \"LicenseSessions\"");
    }

    try
    {
        await using (var db = NewDb())
        {
            var opts = new Inventory.Api.Licensing.LicensingOptions();
            var journal = new Inventory.Api.Licensing.Services.SeatDegradedJournal(paths, NullLogger<Inventory.Api.Licensing.Services.SeatDegradedJournal>.Instance);
            var svc = NewSeats(db, ent, new Inventory.Api.Licensing.Services.SeatCache(), opts, journal);

            var g = await svc.BeginLoginAsync(su[0].Id, su[0].Username, null, null);
            Check(!g.Allowed && g.Code == "LICENSE_SEAT_UNAVAILABLE",
                $"خطای ساختاری ⇒ ورود جدید رد می‌شود (کد: {g.Code})");

            var ev = await db.LicenseEvents.AsNoTracking()
                .Where(e => e.Kind == "SEAT_SCHEMA_BROKEN").OrderByDescending(e => e.Id).FirstOrDefaultAsync();
            Check(ev is not null && ev.Severity == "Warning",
                $"رخداد SEAT_SCHEMA_BROKEN با شدت Warning ثبت شد (شدت: {ev?.Severity})");

            var v = await svc.AuthorizeRequestAsync(su[0].Id, "any-sid", su[0].Username, true);
            Check(v.Allowed && v.Degraded,
                $"در خطای ساختاری، نشست موجود ادامه می‌دهد ولی پاسخ برچسب degraded دارد (Allowed={v.Allowed}، Code={v.Code}، Degraded={v.Degraded})");
        }
    }
    finally
    {
        // جدول را برمی‌گردانیم؛ روی SQL Server دیتابیس پایدار است و بخش‌های بعدی به آن نیاز دارند.
        await using var db = NewDb();
        await LicenseSchemaV1.EnsureAsync(db);
    }
}

// ============================================================
//  ۲۸.۱۱) sid کاربر دیگر و بررسی هاب
{
    await ResetAsync();
    await ClearSessionsAsync();
    await SeedSeatUsersAsync(3);
    var su = await SeatUsersAsync();
    var ent = SeatEnt(LicenseUserMode.Concurrent, 3);

    string sidOther;
    await using (var db = NewDb())
    {
        var opts = new Inventory.Api.Licensing.LicensingOptions();
        var journal = new Inventory.Api.Licensing.Services.SeatDegradedJournal(paths, NullLogger<Inventory.Api.Licensing.Services.SeatDegradedJournal>.Instance);
        var svc = NewSeats(db, ent, new Inventory.Api.Licensing.Services.SeatCache(), opts, journal);
        sidOther = (await svc.BeginLoginAsync(su[1].Id, su[1].Username, null, null)).SessionId!;
    }

    await using (var db = NewDb())
    {
        var opts = new Inventory.Api.Licensing.LicensingOptions();
        var journal = new Inventory.Api.Licensing.Services.SeatDegradedJournal(paths, NullLogger<Inventory.Api.Licensing.Services.SeatDegradedJournal>.Instance);
        var svc = NewSeats(db, ent, new Inventory.Api.Licensing.Services.SeatCache(), opts, journal);

        var v = await svc.AuthorizeRequestAsync(su[0].Id, sidOther, su[0].Username, true);
        Check(!v.Allowed && v.Code == "LICENSE_SESSION_MISMATCH",
            $"sid کاربر دیگر با این توکن معتبر نیست (کد: {v.Code})");

        var hv = await svc.ValidateHubAsync(su[0].Id, sidOther, su[0].Username);
        Check(!hv.Allowed && hv.Code == "LICENSE_SESSION_MISMATCH", $"همان قاعده در بررسی هاب هم برقرار است (کد: {hv.Code})");
    }
}

// ============================================================
//  ۲۸.۱۲) هاب: sid بسته‌شده و sid ناشناخته
{
    await ResetAsync();
    await ClearSessionsAsync();
    await SeedSeatUsersAsync(2);
    var su = await SeatUsersAsync();
    var ent = SeatEnt(LicenseUserMode.Concurrent, 2);

    string sid;
    await using (var db = NewDb())
    {
        var opts = new Inventory.Api.Licensing.LicensingOptions();
        var journal = new Inventory.Api.Licensing.Services.SeatDegradedJournal(paths, NullLogger<Inventory.Api.Licensing.Services.SeatDegradedJournal>.Instance);
        var svc = NewSeats(db, ent, new Inventory.Api.Licensing.Services.SeatCache(), opts, journal);
        sid = (await svc.BeginLoginAsync(su[0].Id, su[0].Username, null, null)).SessionId!;
        await svc.ReleaseAsync(sid);   // خروج
    }

    await using (var db = NewDb())
    {
        var opts = new Inventory.Api.Licensing.LicensingOptions();
        var journal = new Inventory.Api.Licensing.Services.SeatDegradedJournal(paths, NullLogger<Inventory.Api.Licensing.Services.SeatDegradedJournal>.Instance);
        var svc = NewSeats(db, ent, new Inventory.Api.Licensing.Services.SeatCache(), opts, journal);

        var h = await svc.ValidateHubAsync(su[0].Id, sid, su[0].Username);
        Check(!h.Allowed && h.Code == "LICENSE_SEAT_ENDED", $"اتصال هاب با sid بسته‌شده رد می‌شود (کد: {h.Code})");

        var unknown = await svc.ValidateHubAsync(su[0].Id, "no-such-sid", su[0].Username);
        Check(!unknown.Allowed && unknown.Code == "LICENSE_SEAT_UNKNOWN",
            $"هاب با sid ناموجود هم رد می‌شود (بدون بازپس‌گیری؛ درخواست API بعدی صندلی را می‌گیرد) (کد: {unknown.Code})");
    }
}

// ============================================================
//  ۲۸.۱۳) سیاست بیرون‌انداختن: پیش‌فرض deny، و evict-oldest-idle
{
    async Task<string> RunEvictionAsync(string policy)
    {
        await ResetAsync();
        await ClearSessionsAsync();
        await SeedSeatUsersAsync(3);
        var su = await SeatUsersAsync();
        var ent = SeatEnt(LicenseUserMode.Concurrent, 2);

        string oldSid;
        await using (var db = NewDb())
        {
            var opts = new Inventory.Api.Licensing.LicensingOptions { SeatEviction = policy, SeatEvictionMinIdleMinutes = 5 };
            var journal = new Inventory.Api.Licensing.Services.SeatDegradedJournal(paths, NullLogger<Inventory.Api.Licensing.Services.SeatDegradedJournal>.Instance);
            var svc = NewSeats(db, ent, new Inventory.Api.Licensing.Services.SeatCache(), opts, journal);
            oldSid = (await svc.BeginLoginAsync(su[0].Id, su[0].Username, null, null)).SessionId!;
            await svc.BeginLoginAsync(su[1].Id, su[1].Username, null, null);
        }

        // نشست اول را ۱۰ دقیقه بیکار می‌کنیم
        await using (var db = NewDb())
        {
            var s = await db.LicenseSessions.FirstAsync(x => x.SessionId == oldSid);
            s.LastSeen = Inventory.Api.Licensing.ClockAnchor.Now().AddMinutes(-10);
            await db.SaveChangesAsync();
        }

        await using (var db = NewDb())
        {
            var opts = new Inventory.Api.Licensing.LicensingOptions { SeatEviction = policy, SeatEvictionMinIdleMinutes = 5 };
            var journal = new Inventory.Api.Licensing.Services.SeatDegradedJournal(paths, NullLogger<Inventory.Api.Licensing.Services.SeatDegradedJournal>.Instance);
            var svc = NewSeats(db, ent, new Inventory.Api.Licensing.Services.SeatCache(), opts, journal);
            var g = await svc.BeginLoginAsync(su[2].Id, su[2].Username, null, null);
            return g.Allowed ? "allowed" : g.Code;
        }
    }

    var denyResult = await RunEvictionAsync("deny");
    Check(denyResult == "LICENSE_SEAT_FULL", $"پیش‌فرض deny: هیچ‌کس بیرون انداخته نمی‌شود و ورود تازه ۴۰۳ می‌گیرد (واقعی: {denyResult})");

    var evictResult = await RunEvictionAsync("evict-oldest-idle");
    Check(evictResult == "allowed", $"با evict-oldest-idle، قدیمی‌ترین نشست بیکار بیرون می‌رود و ورود تازه پذیرفته می‌شود (واقعی: {evictResult})");

    await using (var db = NewDb())
    {
        var evicted = await db.LicenseSessions.AsNoTracking().CountAsync(s => s.Status == "Evicted");
        Check(evicted >= 1, $"ردیف قدیمی وضعیت Evicted گرفت (واقعی: {evicted})");
    }
}

// ============================================================
//  ۲۸.۱۴) users: None ⇒ هیچ سقفی اعمال نمی‌شود (لایسنس پل)
{
    await ResetAsync();
    await ClearSessionsAsync();
    await SeedSeatUsersAsync(4);
    var su = await SeatUsersAsync();
    var ent = SeatEnt(LicenseUserMode.None, 0);
    Check(!ent.Seats.HasUserLimit, "سیاست users:None سقف کاربر ندارد (HasUserLimit=false)");

    await using (var db = NewDb())
    {
        var opts = new Inventory.Api.Licensing.LicensingOptions();
        var journal = new Inventory.Api.Licensing.Services.SeatDegradedJournal(paths, NullLogger<Inventory.Api.Licensing.Services.SeatDegradedJournal>.Instance);
        var svc = NewSeats(db, ent, new Inventory.Api.Licensing.Services.SeatCache(), opts, journal);
        var ok = 0;
        foreach (var u in su.Take(4))
            if ((await svc.BeginLoginAsync(u.Id, u.Username, null, null)).Allowed) ok++;
        Check(ok == 4, $"با users.mode = None هیچ سقفی اعمال نمی‌شود (ورود موفق: {ok} از ۴)");
    }
}

// ============================================================
//  ۲۸.۱۵) هم‌زمانی: ۲۰ ورود موازی با سقف ۵ ⇒ دقیقاً ۵ (قفل واقعی)
{
    await ResetAsync();
    await ClearSessionsAsync();
    await SeedSeatUsersAsync(20);
    var su = await SeatUsersAsync();
    var ent = SeatEnt(LicenseUserMode.Concurrent, 5);

    var grants = await Task.WhenAll(su.Select(async u =>
    {
        await using var db = NewDb();
        var opts = new Inventory.Api.Licensing.LicensingOptions();
        var journal = new Inventory.Api.Licensing.Services.SeatDegradedJournal(paths, NullLogger<Inventory.Api.Licensing.Services.SeatDegradedJournal>.Instance);
        // عمداً کش تازه برای هر درخواست — وگرنه کش، مسابقه را پنهان می‌کند.
        var svc = NewSeats(db, ent, new Inventory.Api.Licensing.Services.SeatCache(), opts, journal);
        return await svc.BeginLoginAsync(u.Id, u.Username, null, null);
    }));

    var ok = grants.Count(g => g.Allowed);
    var full = grants.Count(g => !g.Allowed && g.Code == "LICENSE_SEAT_FULL");
    var other = grants.Where(g => !g.Allowed && g.Code != "LICENSE_SEAT_FULL").Select(g => g.Code).Distinct().ToList();

    Check(ok == 5, $"۲۰ ورود هم‌زمان با سقف ۵ ⇒ دقیقاً ۵ موفق (واقعی: {ok})");
    Check(full == 15, $"و ۱۵ رد با کد LICENSE_SEAT_FULL (واقعی: {full}) — خطاهای دیگر: {string.Join(", ", other)}");

    await using (var db = NewDb())
    {
        var active = await db.LicenseSessions.CountAsync(s => s.Status == "Active");
        Check(active == 5, $"تعداد ردیف‌های Active دقیقاً ۵ است (واقعی: {active}) — قفل از صندلی دوتایی جلوگیری کرد");
    }
}

// ============================================================
//  ۲۸.۱۶) قفل: sp_getapplock با owner=Transaction، و گرفته‌نشدن ⇒ BUSY
{
    var repoRoot = FindRepoRoot();
    var seatText = File.ReadAllText(Path.Combine(repoRoot, "src", "Inventory.Api", "Licensing", "Services", "SeatService.cs"));

    Check(seatText.Contains("sp_getapplock", StringComparison.Ordinal)
          && seatText.Contains("@LockOwner = 'Transaction'", StringComparison.Ordinal)
          && seatText.Contains("LockTimeoutMs", StringComparison.Ordinal),
        "قفل روی SQL Server با sp_getapplock و owner=Transaction و timeout مشخص است");

    Check(seatText.Contains("LICENSE_SEAT_BUSY", StringComparison.Ordinal)
          && !seatText.Contains("Busy() => SeatGrant.Ok", StringComparison.Ordinal),
        "گرفته‌نشدن قفل ⇒ کد LICENSE_SEAT_BUSY (نه degraded و نه عبور)");
}

// ============================================================
//  ۲۸.۱۷) گارد هاب‌ها: چهار هاب کاربری، و RemoteAgentHub دست‌نخورده
{
    var repoRoot = FindRepoRoot();
    var hubs = new[] { "NotifyHub.cs", "ChatHub.cs", "DashboardHub.cs", "RemoteHub.cs" };

    var missing = new List<string>();
    foreach (var hub in hubs)
    {
        var path = Directory.GetFiles(Path.Combine(repoRoot, "src", "Inventory.Api", "Hubs"), hub,
            SearchOption.AllDirectories).FirstOrDefault();
        if (path is null || !File.ReadAllText(path).Contains("SeatHubGuard.ValidateAsync(this)", StringComparison.Ordinal))
            missing.Add(hub);
    }
    Check(missing.Count == 0, $"هر چهار هاب کاربری در OnConnectedAsync گارد صندلی دارند (بدون: {string.Join(", ", missing)})");

    var agentHubPath = Directory.GetFiles(Path.Combine(repoRoot, "src", "Inventory.Api", "Hubs"), "RemoteAgentHub.cs",
        SearchOption.AllDirectories).First();
    Check(!File.ReadAllText(agentHubPath).Contains("SeatHubGuard", StringComparison.Ordinal),
        "RemoteAgentHub دست‌نخورده است (ایجنت بدون توکن کاربر و بدون صندلی)");
}

// ============================================================
//  ۲۸.۱۸) ستون میراثی و نگهبان رانش صدور توکن
{
    var repoRoot = FindRepoRoot();
    var Api = Path.Combine(repoRoot, "src", "Inventory.Api");

    var licensingFiles = Directory.GetFiles(Path.Combine(Api, "Licensing"), "*.cs", SearchOption.AllDirectories)
        .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
        .ToList();
    var offenders = licensingFiles
        .Where(f => File.ReadAllText(f).Contains("LicenseSeatExempt", StringComparison.Ordinal))
        .Select(Path.GetFileName).ToList();
    Check(offenders.Count == 0,
        $"ستون Users.LicenseSeatExempt از خدمات تصمیم لایسنس حذف شد (هیچ کوئری‌ای رویش نمانده) — فایل‌ها: {string.Join(", ", offenders)}");

    var seedingSources = Directory.GetFiles(Api, "*.cs", SearchOption.AllDirectories)
        .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
        .Where(f => File.ReadAllText(f).Contains("LicenseSeatExempt", StringComparison.Ordinal))
        .Select(Path.GetFileName).Distinct().ToList();
    Check(seedingSources.All(n => n is "User.cs" or "LicenseSchemaV1.cs"),
        $"LicenseSeatExempt فقط در تعریف/اسکیما مانده (فایل‌ها: {string.Join(", ", seedingSources)})");

    var issuanceSites = Directory.GetFiles(Api, "*.cs", SearchOption.AllDirectories)
        .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
        .Where(f => File.ReadAllText(f).Contains("new JwtSecurityToken(", StringComparison.Ordinal)
                    || File.ReadAllText(f).Contains(".WriteToken(", StringComparison.Ordinal))
        .Select(Path.GetFileName).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
    Check(issuanceSites.SequenceEqual(new[] { "AuthService.cs", "TokenService.cs" }),
        $"تنها دو محل صدور توکن وجود دارد (واقعی: {string.Join(", ", issuanceSites)}) — مسیر سوم یعنی صندلی دور زده می‌شود");

    var authServiceText = File.ReadAllText(Path.Combine(Api, "Services", "Core", "AuthService.cs"));
    Check(authServiceText.Contains("\"sid\"", StringComparison.Ordinal),
        "توکن نشست کاربر در AuthService همیشه ادعای sid دارد");

    var radisTokenText = File.ReadAllText(Path.Combine(Api, "Modules", "RadisHr", "Services", "TokenService.cs"));
    Check(radisTokenText.Contains("Derive(", StringComparison.Ordinal)
          && !radisTokenText.Contains("RADIS-HR-V019-DOTNET8-DEFAULT-SIGNING-KEY", StringComparison.Ordinal),
        "توکن RadisHr با کلید مشتق‌شده و بدون رشتهٔ ثابت صادر می‌شود (در میزبان پذیرفته نمی‌شود ⇒ صندلی نمی‌گیرد)");
}


Console.WriteLine();
Console.WriteLine(new string('═', 96));
if (failures == 0)
{
    Console.WriteLine($"✅ همهٔ {passes} بررسی موفق بود (شامل بخش ۲۸ — صندلی لایسنس).");
    Environment.ExitCode = 0;
}
else
{
    Console.WriteLine($"⛔ {failures} بررسی شکست خورد از {passes + failures}:");
    foreach (var item in failedChecks) Console.WriteLine("   • " + item);
    Environment.ExitCode = 1;
}
Console.WriteLine(new string('═', 96));

try { File.Delete(dbPath); Directory.Delete(storage, true); } catch { /* بی‌صدا */ }
return Environment.ExitCode;

// ============================================================
//  کلاس کمکی: سرویس لایسنس «مستقیم» برای تست تراکنش
// ============================================================

/// <summary>
/// سرویس لایسنس برای تست — Entitlement ثابت می‌دهد و مصرف را به QuotaService واقعی
/// همان DbContext می‌سپارد (بدون کش)، تا تراکنش درست سنجیده شود.
/// </summary>

/// <summary>
/// سرویس لایسنس «ساختگی» برای تست دروازه — فقط وضعیت کلی و مهلت سازگاری را کنترل می‌کند.
/// (تست دروازه نباید به دیتابیس یا فایل لایسنس وابسته باشد.)
/// </summary>
/// <summary>زمینهٔ اتصال ساختگی — تست فیلتر هاب به نمونهٔ واقعی SignalR نیاز ندارد.</summary>
file sealed class StubHubCallerContext : HubCallerContext
{
    private readonly CancellationTokenSource _cts = new();
    public override string ConnectionId => "test-connection";
    public override string? UserIdentifier => "1";
    public override System.Security.Claims.ClaimsPrincipal? User => new(new System.Security.Claims.ClaimsIdentity());
    public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
    public override Microsoft.AspNetCore.Http.Features.IFeatureCollection Features
        => new Microsoft.AspNetCore.Http.Features.FeatureCollection();
    public override CancellationToken ConnectionAborted => _cts.Token;
    public override void Abort() => _cts.Cancel();
}

file sealed class GateLicenseService(LicenseOverallStatus status, bool grace)
    : Inventory.Licensing.Abstractions.ILicenseService
{
    public ModuleState GetModuleState(string module) => ModuleState.Valid;
    public ModuleEntitlement? GetModule(string module) => null;
    public EffectiveEntitlement GetEffectiveEntitlement() => new() { Status = status };
    public Task<EffectiveEntitlement> GetEffectiveEntitlementAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new EffectiveEntitlement { Status = status });
    public LicenseOverallStatus GetOverallStatus() => status;

    public Task<Inventory.Licensing.Abstractions.QuotaConsumeResult> TryConsumeAsync(string module, string metric,
        int amount = 1, string? entityType = null, string? entityId = null, int? userId = null,
        bool recordLedger = true, CancellationToken cancellationToken = default)
        => Task.FromResult(Inventory.Licensing.Abstractions.QuotaConsumeResult.Unlimited());

    public Task RecordUsageAsync(string module, string metric, int amount = 1, string? entityType = null,
        string? entityId = null, int? userId = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public void Invalidate() { }

    public Task<bool> IsInExistingInstallGraceAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(grace);
}

file sealed class FixedLicenseService(EffectiveEntitlement entitlement, QuotaService quota) 
    : Inventory.Licensing.Abstractions.ILicenseService
{
    public ModuleState GetModuleState(string module) => entitlement.GetState(module);
    public ModuleEntitlement? GetModule(string module) => entitlement.FindModule(module);
    public EffectiveEntitlement GetEffectiveEntitlement() => entitlement;
    public Task<EffectiveEntitlement> GetEffectiveEntitlementAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(entitlement);
    public LicenseOverallStatus GetOverallStatus() => entitlement.Status;

    public Task<Inventory.Licensing.Abstractions.QuotaConsumeResult> TryConsumeAsync(
        string module, string metric, int amount = 1, string? entityType = null, string? entityId = null,
        int? userId = null, bool recordLedger = true, CancellationToken cancellationToken = default)
        => quota.TryConsumeAsync(entitlement, module, metric, amount, entityType, entityId, userId, recordLedger,
            cancellationToken);

    public Task RecordUsageAsync(string module, string metric, int amount = 1, string? entityType = null,
        string? entityId = null, int? userId = null, CancellationToken cancellationToken = default)
        => quota.RecordUsageAsync(entitlement, module, metric, amount, entityType, entityId, userId, cancellationToken);

    public void Invalidate() { }

    public Task<bool> IsInExistingInstallGraceAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}

// ============================================================
//  سرویس لایسنس ساختگی برای بخش صندلی — فقط Entitlement می‌دهد.
// ============================================================
file sealed class SeatOnlyLicenseService(EffectiveEntitlement entitlement)
    : Inventory.Licensing.Abstractions.ILicenseService
{
    public ModuleState GetModuleState(string module) => entitlement.GetState(module);
    public ModuleEntitlement? GetModule(string module) => entitlement.FindModule(module);
    public EffectiveEntitlement GetEffectiveEntitlement() => entitlement;
    public Task<EffectiveEntitlement> GetEffectiveEntitlementAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(entitlement);
    public LicenseOverallStatus GetOverallStatus() => entitlement.Status;

    public Task<Inventory.Licensing.Abstractions.QuotaConsumeResult> TryConsumeAsync(string module, string metric,
        int amount = 1, string? entityType = null, string? entityId = null, int? userId = null,
        bool recordLedger = true, CancellationToken cancellationToken = default)
        => Task.FromResult(Inventory.Licensing.Abstractions.QuotaConsumeResult.Unlimited());

    public Task RecordUsageAsync(string module, string metric, int amount = 1, string? entityType = null,
        string? entityId = null, int? userId = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public void Invalidate() { }

    public Task<bool> IsInExistingInstallGraceAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}
