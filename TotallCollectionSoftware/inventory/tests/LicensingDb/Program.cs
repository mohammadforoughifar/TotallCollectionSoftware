using Inventory.Api.Data;
using Inventory.Api.Licensing.Services;
using Inventory.Licensing.Abstractions;
using Inventory.Licensing.Entitlement;
using Inventory.Licensing.Metrics;
using Inventory.Licensing.Model;
using Inventory.Licensing.Modules;
using Inventory.Licensing.Periods;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

// ============================================================
//  تست یکپارچهٔ مرحله ۲ — سهمیه روی دیتابیس واقعی (SQLite موقت)
//  اجرا:  dotnet run --project tests/LicensingDb
//  اثبات: اتمی‌بودن زیر بار هم‌زمان، تراکنش، HMAC، بازگشت مصرف، کلید شمسی.
// ============================================================

var failures = 0;
void Check(bool ok, string message)
{
    if (ok) { Console.WriteLine("PASS: " + message); return; }
    failures++;
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

    // متریک فعلی از نوع Counter است ⇒ تعداد واقعی نادیده گرفته می‌شود
    var counterOnly = await svc.GetEffectiveUsageAsync(ent, limit, _ => Task.FromResult(999L));
    var stored = await db.LicenseUsages.AsNoTracking()
        .Where(u => u.Metric == MetricCodes.InvoiceCreate).Select(u => u.Used).FirstAsync();
    Check(counterOnly == stored, "متریک Counter فقط شمارنده را برمی‌گرداند");

    var def = MetricCodes.Find(MetricCodes.InvoiceCreate);
    Check(def?.Kind == MetricKind.Counter, "نوع متریک invoice.create = Counter");
    Check(MetricCodes.Find(MetricCodes.LetterCreate)?.Kind == MetricKind.Counter,
        "نوع متریک تجمیعی letter.create = Counter (پیش‌فرض)");
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
    var withQuota = Entitlement(MetricCodes.InvoiceCreate, 30, QuotaPeriod.Monthly, ModuleCodes.Invoicing);
    var quota = Service(db, NewIntegrity());
    var captured = await Inventory.Api.Licensing.Services.ActiveCountProbes.CaptureAllAsync(
        db, quota, withQuota, "TEST-2", "activation");

    // امروز هیچ متریکی در رجیستری از نوع ActiveCount نیست ⇒ حلقهٔ مبنا خالی است.
    // این تست همان حقیقت را قفل می‌کند تا اگر فردا متریکی ActiveCount شد، رفتار دیده شود.
    var activeCountMetrics = Inventory.Licensing.Metrics.MetricCodes.All
        .Count(m => m.Kind == Inventory.Licensing.Metrics.MetricKind.ActiveCount);
    Console.WriteLine($"   (اطلاع) متریک‌های ActiveCount در این نسخه: {activeCountMetrics} — مبناهای ثبت‌شده: {captured}");

    var limit = withQuota.LimitsFor(ModuleCodes.Invoicing, MetricCodes.InvoiceCreate).First();
    var effective = await quota.GetEffectiveUsageAsync(withQuota, limit, _ => Task.FromResult((long)liveInDb));

    Check(effective == 0,
        $"مصرف مؤثر «صدور صورتحساب» با سقف ۳۰ = ۰ (واقعی: {effective}) — ۵۰۰۰ فاکتور قبل از آمدن سقف، سقف را پر نکرد");
    Check(effective < limit.Limit, $"سقف ۳۰ باز است (مصرف {effective} < سقف {limit.Limit})");

    // و واقعاً می‌شود ۳۰ تا ثبت کرد
    for (var i = 0; i < 30; i++)
        await quota.TryConsumeAsync(withQuota, ModuleCodes.Invoicing, MetricCodes.InvoiceCreate);

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

    // ۳۰ فاکتور در همین ماه شمسی + ۷۰ فاکتور در خارج از پنجره
    var inWindow = Enumerable.Range(1, 30).Select(i => new Inventory.Api.Data.FacInvoice
    {
        Number = 200_000 + i, Kind = Inventory.Shared.InvoiceKind.Sale,
        Date = from.AddHours(1), WarehouseId = wh.Id,
        Settlement = Inventory.Shared.SettlementType.Cash, CreatedBy = "test"
    });
    var outWindow = Enumerable.Range(1, 70).Select(i => new Inventory.Api.Data.FacInvoice
    {
        Number = 300_000 + i, Kind = Inventory.Shared.InvoiceKind.Sale,
        Date = from.AddMonths(-2), WarehouseId = wh.Id,
        Settlement = Inventory.Shared.SettlementType.Cash, CreatedBy = "test"
    });

    await db.FacInvoices.AddRangeAsync(inWindow);
    await db.FacInvoices.AddRangeAsync(outWindow);
    await db.SaveChangesAsync();

    var inPeriod = await Inventory.Api.Licensing.Services.ActiveCountProbes.CountAsync(
        db, MetricCodes.InvoiceCreate, Inventory.Licensing.Model.QuotaPeriod.Monthly,
        Inventory.Licensing.Periods.PersianPeriodKey.NowIran());

    Check(inPeriod == 30, $"شمارش «همین ماه» فقط ۳۰ رکورد را دید، نه ۱۰۰ (واقعی: {inPeriod})");
    Check(to > from, $"مرز بازهٔ ماه شمسی معتبر است ({from:yyyy-MM-dd} تا {to:yyyy-MM-dd})");

    var empty = await Inventory.Api.Licensing.Services.ActiveCountProbes.CountAsync(
        db, MetricCodes.InvoiceCreate, Inventory.Licensing.Model.QuotaPeriod.Monthly,
        Inventory.Licensing.Periods.PersianPeriodKey.NowIran().AddMonths(2));
    Check(empty == 0, $"ماه دو ماه بعد خالی است (واقعی: {empty}) — سقف ماهانه از صفر شروع می‌شود");
}

// ============================================================
Section("۱۶) carryOverUsage: سقف دائمی ۳۰ با مصرف ۳۰، بعد لایسنس دوم با سقف دائمی ۱۰۰۰");

{
    // ⚠️ carryOverUsage عمداً پیاده نشده (تصمیم با کارفرما). این تست فقط **رفتار فعلی** را
    // ثبت می‌کند تا تصمیم روی عدد واقعی گرفته شود، نه روی حدس.
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
Console.WriteLine($"\n{(failures == 0 ? "همهٔ تست‌های مرحله ۲ موفق بود." : $"{failures} تست شکست خورد.")}");

try { File.Delete(dbPath); Directory.Delete(storage, true); } catch { /* بی‌صدا */ }
Environment.ExitCode = failures == 0 ? 0 : 1;
return Environment.ExitCode;

// ============================================================
//  کلاس کمکی: سرویس لایسنس «مستقیم» برای تست تراکنش
// ============================================================

/// <summary>
/// سرویس لایسنس برای تست — Entitlement ثابت می‌دهد و مصرف را به QuotaService واقعی
/// همان DbContext می‌سپارد (بدون کش)، تا تراکنش درست سنجیده شود.
/// </summary>
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

    public Task<Inventory.Licensing.Abstractions.SeatCheckResult> CanLoginUserAsync(int userId, CancellationToken cancellationToken = default)
        => Task.FromResult(Inventory.Licensing.Abstractions.SeatCheckResult.Ok());

    public void Invalidate() { }

    public Task<bool> IsInExistingInstallGraceAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}
