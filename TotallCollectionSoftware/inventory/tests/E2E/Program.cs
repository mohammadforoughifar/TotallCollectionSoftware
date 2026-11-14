using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Inventory.Api.Data;
using Inventory.Api.Licensing;
using Inventory.Api.Licensing.Infrastructure;
using Inventory.Api.Licensing.Services;
using Inventory.Licensing.Crypto;
using Inventory.Licensing.Entitlement;
using Inventory.Licensing.Abstractions;
using Inventory.Licensing.Metrics;
using Inventory.Licensing.Model;
using Inventory.Licensing.Modules;
using Inventory.Licensing.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

// ============================================================
//  تست یکپارچهٔ سرتاسری (E2E) — لایسنس روی SQLite واقعی
//  اجرا:  dotnet run --project tests/E2E
//
//  مسیر سنجیده‌شده در هر سناریو:
//      ردیف LicenseGrants در دیتابیس
//        ⇒ بازخوانی و بررسی امضا (همان قاعدهٔ SignedFileLicenseProvider)
//        ⇒ اعتبارسنجی ساختار/نصب/زمان (LicenseValidator)
//        ⇒ ادغام (EntitlementMerger) و وضعیت هر ماژول
//        ⇒ نتیجهٔ عملی: دروازهٔ فقط‌خواندنی، سقف سهمیه، صندلی، انقضا
//
//  ⚠️ فرض ثبت‌شده: این تست از یک Provider کوچک محلی استفاده می‌کند که **همان**
//     قواعد امضا/اعتبارسنجی را اجرا می‌کند (امضا با RSA-SHA256 و حلقهٔ کلید تستی).
//     دلیل: PublicKeyRingProvider فقط کلیدهای تعبیه‌شده در بیلد را می‌شناسد و کلید
//     خصوصی هیچ‌وقت در مخزن/بیلد نیست. خودِ SignedFileLicenseProvider در تست‌های
//     مرحلهٔ ۴ پوشش دارد؛ هدف این‌جا «رفتار سرتاسری» است، نه تکرار آن تست.
// ============================================================

var failures = 0;
var passes = 0;
var failed = new List<string>();

void Check(bool ok, string message)
{
    if (ok) { passes++; Console.WriteLine("PASS: " + message); return; }
    failures++;
    failed.Add(message);
    Console.WriteLine("FAIL: " + message);
}
void Section(string title) => Console.WriteLine($"\n=== {title} ===");

// ---------- زیرساخت: دیتابیس موقت + کلید تستی ----------
var dbFile = Path.Combine(Path.GetTempPath(), $"e2e-{Guid.NewGuid():N}.db");
var connString = $"Data Source={dbFile}";
DbContextOptions<AppDbContext> Options() => new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlite(connString).Options;
AppDbContext NewDb() => new(Options());

await using (var boot = NewDb())
{
    await boot.Database.EnsureCreatedAsync();
    await LicenseSchemaV1.EnsureAsync(boot);
}

using var rsa = RSA.Create(2048);
var privatePem = rsa.ExportRSAPrivateKeyPem();
var publicPem = rsa.ExportSubjectPublicKeyInfoPem();
var ring = PublicKeyRing.FromEntries(new[]
{
    new PublicKeyEntry { KeyId = "e2e-key", Algorithm = "RS256", PublicKeyPem = publicPem }
});

var installationId = "22222222-2222-2222-2222-222222222222";
// زمان مرجع = زمان واقعی: موتور ادغام، «امروز» را از ساعت واقعی می‌گیرد؛ اگر
// تاریخ ساختگی بدهیم، سناریوهای انقضا/شروع بی‌معنا می‌شوند.
var now = DateTime.Now;

// ---------- سازندهٔ سند امضاشده ----------
LicenseItem Item(string id, string[] scope, LicenseTerm term, LicenseQuota[]? quotas = null, LicenseUsers? users = null)
    => new()
    {
        Id = id,
        Scope = scope.ToList(),
        Term = term,
        Quotas = quotas?.ToList() ?? new List<LicenseQuota>(),
        Users = users ?? new LicenseUsers()
    };

LicenseTerm Sub(DateTime from, DateTime until) => new()
{
    Type = LicenseTermType.Subscription, ValidFrom = from, ValidUntil = until
};

LicenseTerm Perp(DateTime? updatesUntil = null) => new()
{ Type = LicenseTermType.Perpetual, UpdatesUntil = updatesUntil };

/// <summary>سند را امضا و به‌شکل envelope (همان چیزی که در دیتابیس ذخیره می‌شود) برمی‌گرداند.</summary>
(LicenseDocument Doc, string Payload, string Signature) Sign(LicenseDocument doc)
{
    doc.KeyId = "e2e-key";
    var payload = LicenseJson.ToPayloadBytes(doc);
    var signature = KeySign.Sign(payload, privatePem, "RS256");
    return (doc, Convert.ToBase64String(payload), Convert.ToBase64String(signature));
}

/// <summary>ذخیرهٔ سند در دیتابیس (همان ردیفی که activate می‌نویسد).</summary>
async Task StoreAsync(LicenseDocument doc, string payload, string signature, string status = "Active", bool addOn = false)
{
    await using var db = NewDb();
    db.LicenseGrants.Add(new LicenseGrant
    {
        Serial = doc.Serial,
        KeyId = "e2e-key",
        Algorithm = "RS256",
        Payload = payload,
        Signature = signature,
        FileName = doc.Serial + ".lic",
        Status = status,
        IsAddOn = addOn,
        AppliedAt = now,
        ValidFrom = doc.Items.Select(i => i.Term.ValidFrom).Where(v => v.HasValue).Min(),
        ValidUntil = doc.Items.Select(i => i.Term.ValidUntil).Where(v => v.HasValue).Max(),
        CustomerName = doc.CustomerName
    });
    await db.SaveChangesAsync();
}

/// <summary>
/// بازخوانی + امضا + اعتبارسنجی + ادغام — همان زنجیرهٔ تولیدی، ولی با حلقهٔ کلید تستی.
/// سند نامعتبر (امضا/ساختار/نصب) **کنار گذاشته می‌شود**، نه اینکه کل سیستم را بیندازد.
/// </summary>
async Task<(EffectiveEntitlement Entitlement, List<LicenseIssue> Rejected)> EvaluateAsync(DateTime clockNow)
{
    await using var db = NewDb();
    var grants = await db.LicenseGrants.AsNoTracking()
        .Where(g => g.Status == "Active").OrderBy(g => g.AppliedAt).ToListAsync();

    var documents = new List<LicenseDocument>();
    var rejected = new List<LicenseIssue>();

    foreach (var grant in grants)
    {
        var text = JsonSerializer.Serialize(new SignedLicenseDocument
        {
            KeyId = grant.KeyId,
            Algorithm = grant.Algorithm,
            Payload = grant.Payload,
            Signature = grant.Signature
        });

        var verification = LicenseVerifier.Verify(text, ring);
        if (!verification.Ok || verification.Document is null)
        {
            rejected.Add(new LicenseIssue("LICENSE_SIGNATURE_INVALID",
                LicenseIssueSeverity.Error, verification.ErrorText ?? "امضا معتبر نیست."));
            continue;
        }

        var issues = LicenseValidator.Validate(verification.Document, new LicenseValidationContext
        {
            ExpectedInstallationId = installationId,
            ExpectedDeployment = DeploymentKind.OnPrem,
            Now = clockNow,
            AppliedSerials = documents.Select(d => d.Serial).ToList()
        });

        if (issues.Any(i => i.Severity == LicenseIssueSeverity.Error))
        {
            rejected.AddRange(issues.Where(i => i.Severity == LicenseIssueSeverity.Error));
            continue;
        }

        documents.Add(verification.Document);
    }

    return (EntitlementMerger.Merge(documents), rejected);
}

// ============================================================
Section("۱) بدون لایسنس: وضعیت Missing و فقط‌خواندنی");
// ============================================================
{
    var (entitlement, rejected) = await EvaluateAsync(now);
    Check(entitlement.Status == LicenseOverallStatus.Missing,
        $"بدون هیچ سند، وضعیت Missing است (واقعی: {entitlement.Status})");
    Check(LicenseAccessState.IsReadOnly(entitlement.Status),
        "دروازهٔ نوشتن، Missing را فقط‌خواندنی می‌داند");
    Check(rejected.Count == 0, "فهرست رد‌شده‌ها خالی است");
    Check(entitlement.GetState(ModuleCodes.Sales) == ModuleState.Disabled,
        "ماژول فروش در حالت بدون لایسنس بسته است");
    Check(entitlement.GetState(ModuleCodes.Core) != ModuleState.Disabled,
        "ماژول همیشه‌فعال (هسته) حتی بدون لایسنس باز است");
}

// ============================================================
Section("۲) لایسنس «پل»: ۳۳ ماژول، اشتراک کوتاه، بدون محدودیت کاربر");
// ============================================================
var bridgeUntil = now.AddDays(30);
{
    var allCodes = ModuleCodes.All.Select(m => m.Code).ToArray();
    var doc = new LicenseDocument
    {
        Serial = "LIC-E2E-BRIDGE",
        CustomerId = "CUST-E2E",
        CustomerName = "شرکت آزمایشی",
        Deployment = DeploymentKind.OnPrem,
        InstallationId = installationId,
        IssuedAt = now,
        Items = new List<LicenseItem> { Item("BRIDGE", allCodes, Sub(now.AddDays(-1), bridgeUntil)) }
    };
    var (signed, payload, signature) = Sign(doc);
    await StoreAsync(signed, payload, signature);

    var (entitlement, rejected) = await EvaluateAsync(now);
    Check(entitlement.Status == LicenseOverallStatus.Valid,
        $"لایسنس پل معتبر است (واقعی: {entitlement.Status})");
    Check(rejected.Count == 0, "هیچ سندی رد نشد");
    Check(!LicenseAccessState.IsReadOnly(entitlement.Status), "دروازهٔ نوشتن باز است");

    var disabled = ModuleCodes.All.Where(m => entitlement.GetState(m.Code) == ModuleState.Disabled).ToList();
    Check(disabled.Count == 0,
        "همهٔ ۳۳ ماژول باز هستند" + (disabled.Count == 0 ? "" : " — بسته: " + string.Join(", ", disabled.Select(m => m.Code))));
    Check(entitlement.Seats.GlobalMax is null or <= 0,
        $"لایسنس پل سقف کاربر ندارد (واقعی: {entitlement.Seats.GlobalMax?.ToString() ?? "بدون سقف"})");
}

// ============================================================
Section("۳) سقف سهمیه: مصرف تا سقف، بعد رد با QUOTA_EXCEEDED");
// ============================================================
{
    var doc = new LicenseDocument
    {
        Serial = "LIC-E2E-QUOTA",
        CustomerId = "CUST-E2E",
        CustomerName = "شرکت آزمایشی",
        Deployment = DeploymentKind.OnPrem,
        InstallationId = installationId,
        IssuedAt = now,
        Items = new List<LicenseItem>
        {
            Item("Q1", new[] { ModuleCodes.Invoicing },
                Sub(now.AddDays(-1), now.AddDays(30)),
                quotas: new[] { new LicenseQuota { Metric = MetricCodes.InvoiceCreate, Period = QuotaPeriod.Monthly, Limit = 3 } })
        }
    };
    var (signedQ, payloadQ, signatureQ) = Sign(doc);

    // سهمیهٔ سند پل را کنار می‌گذاریم تا فقط سند سهمیه‌دار بماند.
    await using (var db = NewDb())
    {
        var bridge = await db.LicenseGrants.FirstAsync(g => g.Serial == "LIC-E2E-BRIDGE");
        bridge.Status = "Suspended";
        await db.SaveChangesAsync();
    }
    await StoreAsync(signedQ, payloadQ, signatureQ);

    var (entitlement, quotaRejected) = await EvaluateAsync(now);
    Check(entitlement.Status == LicenseOverallStatus.Valid,
        $"سند سهمیه‌دار معتبر است (واقعی: {entitlement.Status})، رد‌شده: {quotaRejected.Count}");
    Check(entitlement.LimitsFor(ModuleCodes.Invoicing, MetricCodes.InvoiceCreate).Count == 1,
        "سقف ماهانهٔ «ایجاد فاکتور» از سند خوانده شد");
    await using var db2 = NewDb();
    var quota = new QuotaService(db2,
        new QuotaIntegrity(TestPaths(), NullLogger<QuotaIntegrity>.Instance),
        new LicenseEventLogger(db2, NullLogger<LicenseEventLogger>.Instance),
        NullLogger<QuotaService>.Instance);

    var results = new List<QuotaConsumeResult>();
    for (var i = 0; i < 4; i++)
        results.Add(await quota.TryConsumeAsync(entitlement, ModuleCodes.Invoicing, MetricCodes.InvoiceCreate, 1,
            "FaInvoice", (i + 1).ToString(), userId: 1));

    Check(results.Take(3).All(r => r.Allowed), "سه مصرف اول مجاز است (سقف ۳)");
    Check(!results[3].Allowed, "مصرف چهارم رد می‌شود");
    Check(!results[3].Allowed && results[3].Limit == 3 && results[3].Used == 3,
        $"رد شدن با سقف ۳ و مصرف ۳ گزارش می‌شود (Used={results[3].Used}, Limit={results[3].Limit})");
    Check(results[3].Message.Contains("سقف", StringComparison.Ordinal),
        "پیام رد شدن، «سقف» را به کاربر می‌گوید: " + results[3].Message);

    var rows = await db2.LicenseUsages.AsNoTracking()
        .Where(u => u.Module == ModuleCodes.Invoicing && u.Metric == MetricCodes.InvoiceCreate)
        .Select(u => u.Used).ToListAsync();
    Check(rows.Count == 1 && rows[0] == 3, $"مصرف ثبت‌شده دقیقاً ۳ است، نه ۴ (واقعی: {string.Join("/", rows)})");
    Check(results[2].IsAtLimit, "مصرف سوم، «سقف پر شد» را علامت می‌زند");
    // رد شدن سهمیه عمداً «رخداد» نمی‌سازد: این حالت پرتکرار است و جدول LicenseEvents
    // با آن پر می‌شد. سندِ رد شدن در همان ردیف مصرف (LicenseUsages) و پیام کاربر است.
    var usageRow = await db2.LicenseUsages.AsNoTracking()
        .FirstAsync(u => u.Module == ModuleCodes.Invoicing && u.Metric == MetricCodes.InvoiceCreate);
    Check(usageRow.Used == 3, $"رد شدن، مصرف را بالا نمی‌برد (واقعی: {usageRow.Used})");
}

// ============================================================
Section("۴) صندلی: حالت Named با یک صندلی، نفر دوم رد می‌شود");
// ============================================================
{
    await using var db = NewDb();
    var options = new LicensingOptions();
    var entitlement = SeatEntitlement(LicenseUserMode.Named, 1);

    // حساب «قابل‌ورود» = فعال و دارای گذرواژهٔ واقعی (هش خالی/غیرفعال، صندلی نمی‌گیرد).
    var user1 = new Inventory.Api.Data.User { Username = "u1", PasswordHash = "hash-1" };
    var user2 = new Inventory.Api.Data.User { Username = "u2", PasswordHash = "hash-2" };
    db.Users.AddRange(user1, user2);
    await db.SaveChangesAsync();

    var journal = new SeatDegradedJournal(TestPaths(), NullLogger<SeatDegradedJournal>.Instance);
    var seats = new SeatService(db, new FixedLicenseService(entitlement),
        new LicenseSessionService(db, options, NullLogger<LicenseSessionService>.Instance),
        new SeatCache(), journal, options,
        new LicenseRuntimeState(db, NullLogger<LicenseRuntimeState>.Instance), NullLogger<SeatService>.Instance);

    using (ClockAnchor.Fix(now))
    {
        var login1 = await seats.BeginLoginAsync(user1.Id, user1.Username, "PC-1", "10.0.0.1");
        Check(login1.Allowed, $"کاربر اول صندلی می‌گیرد (کد: {login1.Code})");
        var login2 = await seats.BeginLoginAsync(user2.Id, user2.Username, "PC-2", "10.0.0.2");
        Check(!login2.Allowed, "کاربر دوم با صندلی پر رد می‌شود");
        Check(login2.Code == "LICENSE_SEAT_FULL", $"کد رد شدن LICENSE_SEAT_FULL است (واقعی: {login2.Code})");
    }
}

// ============================================================
Section("۵) انقضا با ساعت لنگر: سند تا ۲۴ ساعت دیگر معتبر است، بعدش نه");
// ============================================================
{
    var until = now.Date.AddDays(1);
    var doc = new LicenseDocument
    {
        Serial = "LIC-E2E-EXPIRY",
        CustomerId = "CUST-E2E",
        CustomerName = "شرکت آزمایشی",
        Deployment = DeploymentKind.OnPrem,
        InstallationId = installationId,
        IssuedAt = now.AddDays(-30),
        Items = new List<LicenseItem>
        {
            Item("E1", new[] { ModuleCodes.Catalog, ModuleCodes.Sales }, Sub(now.AddDays(-30), until))
        }
    };
    var (signedE, payloadE, signatureE) = Sign(doc);

    await using (var db = NewDb())
    {
        foreach (var grant in await db.LicenseGrants.Where(g => g.Status == "Active").ToListAsync())
            grant.Status = "Suspended";
        await db.SaveChangesAsync();
    }
    await StoreAsync(signedE, payloadE, signatureE);

    var (inside, rejectedInside) = await EvaluateAsync(now);
    Check(inside.Status == LicenseOverallStatus.Valid,
        $"یک ساعت مانده به انقضا، سند معتبر است (واقعی: {inside.Status})، رد‌شده: {rejectedInside.Count}");
    Check(inside.GetState(ModuleCodes.Catalog) is ModuleState.Valid or ModuleState.ExpiringSoon,
        $"ماژول کالا باز است (واقعی: {inside.GetState(ModuleCodes.Catalog)})");

    // سند منقضی: پایهٔ زمانی «دیروز» تا سند از امروز گذشته باشد
    var expiredDoc = new LicenseDocument
    {
        Serial = "LIC-E2E-EXPIRED",
        CustomerId = "CUST-E2E",
        CustomerName = "شرکت آزمایشی",
        Deployment = DeploymentKind.OnPrem,
        InstallationId = installationId,
        IssuedAt = now.AddDays(-120),
        Items = new List<LicenseItem>
        {
            // ۶۰ روز گذشته از انقضا ⇒ فراتر از پنجرهٔ ۱۵ روزهٔ مهلت
            Item("E2", new[] { ModuleCodes.Catalog, ModuleCodes.Sales },
                Sub(now.AddDays(-120), now.AddDays(-60)))
        }
    };
    var (expiredSigned, expiredPayload, expiredSignature) = Sign(expiredDoc);

    await using (var db = NewDb())
    {
        foreach (var grant in await db.LicenseGrants.Where(g => g.Status == "Active").ToListAsync())
            grant.Status = "Suspended";
        await db.SaveChangesAsync();
    }
    await StoreAsync(expiredSigned, expiredPayload, expiredSignature);

    var (afterExpiry, _) = await EvaluateAsync(now);
    Check(afterExpiry.GetState(ModuleCodes.Catalog) is ModuleState.ReadOnly or ModuleState.Disabled,
        $"۶۰ روز پس از انقضا، ماژول کالا فقط‌خواندنی/بسته است (واقعی: {afterExpiry.GetState(ModuleCodes.Catalog)})");
    // وضعیت کلی می‌تواند Valid بماند (سند معتبر است، ولی ماژول‌ها فقط‌خواندنی‌اند)؛
    // آنچه اهمیت دارد این است که نوشتن بسته باشد.
    Check(!afterExpiry.IsWritable(ModuleCodes.Catalog),
        "با سند منقضی، نوشتن در ماژول کالا بسته است (حتی اگر وضعیت کلی Valid بماند)");

    // مهلت ۱۵ روزه: سند ۱۰ روز منقضی ⇒ Grace (نوشتن باز می‌ماند) — طراحی‌شده، نه خطا
    var graceDoc = new LicenseDocument
    {
        Serial = "LIC-E2E-GRACE",
        CustomerId = "CUST-E2E",
        CustomerName = "شرکت آزمایشی",
        Deployment = DeploymentKind.OnPrem,
        InstallationId = installationId,
        IssuedAt = now.AddDays(-90),
        Items = new List<LicenseItem>
        {
            Item("E3", new[] { ModuleCodes.Catalog }, Sub(now.AddDays(-90), now.AddDays(-10)))
        }
    };
    var (graceSigned, gracePayload, graceSignature) = Sign(graceDoc);
    await using (var db = NewDb())
    {
        foreach (var grant in await db.LicenseGrants.Where(g => g.Status == "Active").ToListAsync())
            grant.Status = "Suspended";
        await db.SaveChangesAsync();
    }
    await StoreAsync(graceSigned, gracePayload, graceSignature);
    var (inGrace, _) = await EvaluateAsync(now);
    Check(inGrace.GetState(ModuleCodes.Catalog) == ModuleState.Grace,
        $"۱۰ روز پس از انقضا، مهلت ۱۵ روزه فعال است (واقعی: {inGrace.GetState(ModuleCodes.Catalog)})");
    Check(inGrace.IsWritable(ModuleCodes.Catalog), "در مهلت، نوشتن هنوز مجاز است");

    // ساعت عقب‌رفته مجوز را زنده نمی‌کند: «اکنون مؤثر» = max(ساعت سیستم، لنگر)
    var clockPath = TestPaths();
    await using var clockDb = NewDb();
    var clockEvents = new LicenseEventLogger(clockDb, NullLogger<LicenseEventLogger>.Instance);
    var key = RandomNumberGenerator.GetBytes(32);
    var storeA = new MemoryClockStore("e2e-a");
    var storeB = new MemoryClockStore("e2e-b");
    ClockGuard Guard() => new(clockDb, clockPath, clockEvents,
        NullLogger<ClockGuard>.Instance, new IClockStore[] { storeA, storeB }, key);

    using (ClockAnchor.Fix(now))
        await Guard().EvaluateAsync();
    using (ClockAnchor.Fix(now.AddHours(-30)))
    {
        var verdict = await Guard().EvaluateAsync();
        Check(verdict.Health == ClockHealth.MajorBackward && verdict.WritesBlocked,
            "۳۰ ساعت عقب بردن ساعت ⇒ فقط‌خواندنی");
        Check(LicenseAccessState.IsReadOnly(LicenseOverallStatus.Valid),
            "دروازهٔ نوشتن با ساعت عقب‌رفته بسته می‌شود، حتی اگر لایسنس معتبر باشد");
        Check(verdict.EffectiveNow >= verdict.Anchor,
            "«اکنون مؤثر» هرگز عقب‌تر از لنگر نمی‌رود");
    }
    ClockGuardState.Reset();
    try { Directory.Delete(clockPath.Root, true); } catch { }
}

// ============================================================
Section("۶) Add-on: سند پایه + افزودنی ⇒ ماژول تازه باز می‌شود");
// ============================================================
{
    var baseDoc = new LicenseDocument
    {
        Serial = "LIC-E2E-BASE",
        CustomerId = "CUST-E2E",
        CustomerName = "شرکت آزمایشی",
        Deployment = DeploymentKind.OnPrem,
        InstallationId = installationId,
        IssuedAt = now,
        Items = new List<LicenseItem>
        {
            Item("B1", new[] { ModuleCodes.Catalog }, Sub(now.AddDays(-1), now.AddDays(90)))
        }
    };
    var addOnDoc = new LicenseDocument
    {
        Serial = "LIC-E2E-ADDON",
        CustomerId = "CUST-E2E",
        CustomerName = "شرکت آزمایشی",
        Deployment = DeploymentKind.OnPrem,
        InstallationId = installationId,
        IssuedAt = now,
        Items = new List<LicenseItem>
        {
            Item("A1", new[] { ModuleCodes.HrFa }, Sub(now.AddDays(-1), now.AddDays(30)))
        }
    };

    await using (var db = NewDb())
    {
        foreach (var grant in await db.LicenseGrants.Where(g => g.Status == "Active").ToListAsync())
            grant.Status = "Suspended";
        await db.SaveChangesAsync();
    }

    var (b, bp, bs) = Sign(baseDoc);
    await StoreAsync(b, bp, bs);
    var before = await EvaluateAsync(now);
    Check(before.Entitlement.GetState(ModuleCodes.HrFa) == ModuleState.Disabled,
        "پیش از افزودنی، منابع انسانی بسته است");

    var (a, ap, asig) = Sign(addOnDoc);
    await StoreAsync(a, ap, asig, addOn: true);
    var after = await EvaluateAsync(now);

    Check(after.Entitlement.Status == LicenseOverallStatus.Valid, "پس از افزودنی، وضعیت معتبر است");
    Check(after.Entitlement.GetState(ModuleCodes.HrFa) is ModuleState.Valid or ModuleState.ExpiringSoon,
        $"ماژول منابع انسانی با افزودنی باز شد (واقعی: {after.Entitlement.GetState(ModuleCodes.HrFa)})");
    Check(after.Entitlement.GetState(ModuleCodes.Catalog) is ModuleState.Valid or ModuleState.ExpiringSoon,
        "سند پایه هم‌چنان معتبر است (افزودنی جای سند پایه را نمی‌گیرد)");
    Check(after.Entitlement.Items.Count == 2, $"هر دو سند در ادغام حاضرند (واقعی: {after.Entitlement.Items.Count})");
}

// ============================================================
Section("۷) لایسنس خراب: امضای دست‌کاری‌شده کنار گذاشته می‌شود");
// ============================================================
{
    await using (var db = NewDb())
    {
        foreach (var grant in await db.LicenseGrants.Where(g => g.Status == "Active").ToListAsync())
            grant.Status = "Suspended";
        await db.SaveChangesAsync();
    }

    var corruptDoc = new LicenseDocument
    {
        Serial = "LIC-E2E-CORRUPT",
        CustomerId = "CUST-E2E",
        CustomerName = "شرکت آزمایشی",
        Deployment = DeploymentKind.OnPrem,
        InstallationId = installationId,
        IssuedAt = now,
        Items = new List<LicenseItem>
        {
            Item("C1", new[] { ModuleCodes.Accounting }, Sub(now.AddDays(-1), now.AddDays(90)))
        }
    };
    var (c, cp, cs) = Sign(corruptDoc);

    // یک بیت در امضا (Base64) عوض می‌شود — همان چیزی که «فایل خراب/دست‌کاری‌شده» می‌دهد.
    var sigBytes = Convert.FromBase64String(cs);
    sigBytes[sigBytes.Length - 5] ^= 0x01;
    await StoreAsync(c, cp, Convert.ToBase64String(sigBytes));

    var (entitlement, rejected) = await EvaluateAsync(now);
    Check(entitlement.Status != LicenseOverallStatus.Valid,
        $"سند با امضای خراب، وضعیت را معتبر نمی‌کند (واقعی: {entitlement.Status})");
    Check(rejected.Any(r => r.Code == "LICENSE_SIGNATURE_INVALID"),
        "دلیل رد شدن، امضای نامعتبر است");
    Check(entitlement.GetState(ModuleCodes.Accounting) == ModuleState.Disabled,
        "ماژول حسابداری با سند خراب باز نمی‌شود");

    // سند سالم دوباره معتبر می‌شود (خودترمیمی: خرابی، دادهٔ سالم را از بین نمی‌برد)
    await using (var db = NewDb())
    {
        var bad = await db.LicenseGrants.FirstAsync(g => g.Serial == "LIC-E2E-CORRUPT");
        bad.Status = "Suspended";
        await db.SaveChangesAsync();
    }
    var goodDoc = new LicenseDocument
    {
        Serial = "LIC-E2E-GOOD",
        CustomerId = "CUST-E2E",
        CustomerName = "شرکت آزمایشی",
        Deployment = DeploymentKind.OnPrem,
        InstallationId = installationId,
        IssuedAt = now,
        Items = new List<LicenseItem>
        {
            Item("G1", new[] { ModuleCodes.Accounting }, Sub(now.AddDays(-1), now.AddDays(90)))
        }
    };
    var (g, gp, gs) = Sign(goodDoc);
    await StoreAsync(g, gp, gs);
    var (recovered, _) = await EvaluateAsync(now);
    Check(recovered.Status == LicenseOverallStatus.Valid,
        "با اعمال سند سالم، وضعیت خودکار معتبر می‌شود");
}

// ============================================================
Section("۸) شناسهٔ نصب ناهمخوان: سند پذیرفته نمی‌شود");
// ============================================================
{
    await using (var db = NewDb())
    {
        foreach (var grant in await db.LicenseGrants.Where(g => g.Status == "Active").ToListAsync())
            grant.Status = "Suspended";
        await db.SaveChangesAsync();
    }

    var wrongInstall = new LicenseDocument
    {
        Serial = "LIC-E2E-WRONGINSTALL",
        CustomerId = "CUST-OTHER",
        CustomerName = "شرکت دیگر",
        Deployment = DeploymentKind.OnPrem,
        InstallationId = "99999999-9999-9999-9999-999999999999",
        IssuedAt = now,
        Items = new List<LicenseItem>
        {
            Item("W1", new[] { ModuleCodes.Catalog }, Sub(now.AddDays(-1), now.AddDays(90)))
        }
    };
    var (w, wp, ws) = Sign(wrongInstall);
    await StoreAsync(w, wp, ws);

    var (entitlement, rejected) = await EvaluateAsync(now);
    Check(entitlement.Status != LicenseOverallStatus.Valid,
        $"سند با شناسهٔ نصب دیگر، این نصب را معتبر نمی‌کند (واقعی: {entitlement.Status})");
    Check(rejected.Count > 0, "سند با پیام مشخص رد می‌شود");
    Check(LicenseAccessState.IsReadOnly(entitlement.Status),
        "وضعیت غیرمعتبر ⇒ فقط‌خواندنی (بدون استثنا)");
}

// ============================================================
Console.WriteLine();
Console.WriteLine(new string('═', 96));
if (failures == 0)
{
    Console.WriteLine($"✅ همهٔ {passes} بررسی E2E موفق بود.");
    Environment.ExitCode = 0;
}
else
{
    Console.WriteLine($"⛔ {failures} بررسی شکست خورد از {passes + failures}:");
    foreach (var item in failed) Console.WriteLine("   • " + item);
    Environment.ExitCode = 1;
}
Console.WriteLine(new string('═', 96));

try { File.Delete(dbFile); } catch { }
return Environment.ExitCode;

// ============================================================
//  کمکی‌ها
// ============================================================

/// <summary>مسیرهای آزمایشی برای فایل‌های جانبی (کلید HMAC، ژورنال صندلی).</summary>
static LicensePaths TestPaths()
{
    var root = Path.Combine(Path.GetTempPath(), "e2e-paths-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var options = new LicensingOptions { StorageFolder = root };
    return new LicensePaths(options, NullLogger<LicensePaths>.Instance);
}

/// <summary>Entitlement با حالت صندلی مشخص — برای تست دروازهٔ صندلی.</summary>
static EffectiveEntitlement SeatEntitlement(LicenseUserMode mode, int max) => BuildSeatEntitlement(mode, max);

static EffectiveEntitlement BuildSeatEntitlement(LicenseUserMode mode, int max)
{
    var doc = new LicenseDocument
    {
        Serial = "LIC-E2E-SEAT",
        KeyId = "e2e-key",
        CustomerId = "CUST-E2E",
        CustomerName = "شرکت آزمایشی",
        Deployment = DeploymentKind.OnPrem,
        InstallationId = "22222222-2222-2222-2222-222222222222",
        IssuedAt = new DateTime(2026, 11, 14),
        Items = new List<LicenseItem>
        {
            new()
            {
                Id = "S1",
                Scope = ModuleCodes.All.Select(m => m.Code).ToList(),
                Term = new LicenseTerm
                {
                    Type = LicenseTermType.Subscription,
                    ValidFrom = new DateTime(2026, 11, 1),
                    ValidUntil = new DateTime(2027, 11, 1)
                },
                Users = new LicenseUsers { Mode = mode, Max = max }
            }
        }
    };
    return EntitlementMerger.Merge(new[] { doc });
}

/// <summary>سرویس لایسنس ساختگی برای دروازهٔ صندلی (فقط Entitlement می‌دهد).</summary>
file sealed class FixedLicenseService(EffectiveEntitlement entitlement) : ILicenseService
{
    public ModuleState GetModuleState(string module) => entitlement.GetState(module);
    public ModuleEntitlement? GetModule(string module) => entitlement.FindModule(module);
    public EffectiveEntitlement GetEffectiveEntitlement() => entitlement;
    public Task<EffectiveEntitlement> GetEffectiveEntitlementAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(entitlement);
    public LicenseOverallStatus GetOverallStatus() => entitlement.Status;

    public Task<QuotaConsumeResult> TryConsumeAsync(string module, string metric, int amount = 1,
        string? entityType = null, string? entityId = null, int? userId = null, bool recordLedger = true,
        CancellationToken cancellationToken = default)
        => Task.FromResult(QuotaConsumeResult.Unlimited());

    public Task RecordUsageAsync(string module, string metric, int amount = 1, string? entityType = null,
        string? entityId = null, int? userId = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public void Invalidate() { }

    public Task<bool> IsInExistingInstallGraceAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}

/// <summary>ذخیره‌گاه لنگر ساعت در حافظه (جای فایل/رجیستری).</summary>
file sealed class MemoryClockStore(string name) : IClockStore
{
    public string Name { get; } = name;
    public string? Value { get; set; }
    public string? Read() => Value;
    public void Write(string value) => Value = value;
}
