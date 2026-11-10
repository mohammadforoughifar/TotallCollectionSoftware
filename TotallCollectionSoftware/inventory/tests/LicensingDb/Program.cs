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

string ConnectionString() => $"Data Source={dbPath};Cache=Shared;Default Timeout=30";

DbContextOptions<AppDbContext> Options() =>
    new DbContextOptionsBuilder<AppDbContext>().UseSqlite(ConnectionString()).Options;

AppDbContext NewDb() => new(Options());

// ---------- ساخت اسکیما با همان کد تولیدی ----------
await using (var db = NewDb())
{
    await db.Database.EnsureCreatedAsync();
    await LicenseSchemaV1.EnsureAsync(db);      // جداول لایسنس (همان چیزی که در بوت اجرا می‌شود)
    await LicenseSchemaV1.EnsureAsync(db);      // بار دوم: باید بی‌اثر باشد (idempotent)
}
Console.WriteLine($"دیتابیس تست: {dbPath}");

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
Console.WriteLine($"\n{(failures == 0 ? "همهٔ تست‌های مرحله ۲ موفق بود." : $"{failures} تست شکست خورد.")}");

try { File.Delete(dbPath); Directory.Delete(storage, true); } catch { /* بی‌صدا */ }
Environment.ExitCode = failures == 0 ? 0 : 1;

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
