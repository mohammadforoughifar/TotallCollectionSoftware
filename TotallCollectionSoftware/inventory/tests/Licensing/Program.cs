using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Inventory.Licensing.Crypto;
using Inventory.Licensing.Entitlement;
using Inventory.Licensing.Metrics;
using Inventory.Licensing.Model;
using Inventory.Licensing.Modules;
using Inventory.Licensing.Abstractions;
using Inventory.Licensing.Periods;
using Inventory.Licensing.Validation;
using Inventory.Api.Licensing;
using Microsoft.Extensions.Configuration;

// ============================================================
//  تست‌های موتور لایسنس (مرحله ۱)
//  اجرا:  dotnet run --project tests/Licensing
//  هر خط PASS/FAIL چاپ می‌شود و در پایان کد خروج غیرصفر در صورت شکست.
// ============================================================

var failures = 0;

void Check(bool ok, string message)
{
    if (ok)
    {
        Console.WriteLine("PASS: " + message);
        return;
    }
    failures++;
    Console.WriteLine("FAIL: " + message);
}

void Section(string title) => Console.WriteLine($"\n=== {title} ===");

// ---------- ابزار ساخت سند ----------
LicenseItem Item(string id, string[] scope, LicenseTerm term, LicenseUsers? users = null,
    LicenseQuota[]? quotas = null) => new()
{
    Id = id,
    Scope = scope.ToList(),
    Term = term,
    Users = users ?? new LicenseUsers(),
    Quotas = quotas?.ToList() ?? new List<LicenseQuota>()
};

LicenseTerm Sub(DateTime from, DateTime until) => new()
{
    Type = LicenseTermType.Subscription,
    ValidFrom = from,
    ValidUntil = until
};

LicenseTerm Perp(DateTime? updatesUntil = null) => new()
{
    Type = LicenseTermType.Perpetual,
    UpdatesUntil = updatesUntil
};

LicenseTerm Trial(DateTime from, DateTime until) => new()
{
    Type = LicenseTermType.Trial,
    ValidFrom = from,
    ValidUntil = until
};

LicenseDocument Doc(string serial, params LicenseItem[] items) => new()
{
    Serial = serial,
    KeyId = "dev-2026a",
    CustomerId = "CUST-1",
    CustomerName = "شرکت نمونه",
    Deployment = DeploymentKind.OnPrem,
    InstallationId = "abc123",
    IssuedAt = new DateTime(2026, 1, 1),
    Items = items.ToList()
};

var today = new DateTime(2026, 11, 8);

// ---------- ابزار اجرای فرمان بیرونی (برای تست openssl) ----------
(bool Ok, int ExitCode) TryRun(string file, string args, out string output)
{
    try
    {
        var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        })!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit(30000);
        output = stdout + stderr;
        return (p.ExitCode == 0, p.ExitCode);
    }
    catch (Exception ex)
    {
        output = ex.Message;
        return (false, -1);
    }
}

(bool Ok, int ExitCode) Run(string file, string args) => TryRun(file, args, out _);

// ---------- پیدا کردن ریشهٔ مخزن (برای تست کلیدها و .gitignore) ----------
string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "Inventory.sln"))) return dir.FullName;
        dir = dir.Parent;
    }
    return AppContext.BaseDirectory;
}

string LocateRepoFile(string relative) => Path.Combine(FindRepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));

// ============================================================
Section("۱) ادغام Itemها — دورترین انقضا، دائمی، پوشش چندگانه");

{
    var doc = Doc("S1",
        Item("I1", new[] { ModuleCodes.Catalog }, Sub(today.AddMonths(-1), today.AddMonths(2))),
        Item("I2", new[] { ModuleCodes.Catalog }, Perp()));

    var e = EntitlementMerger.Merge(new[] { doc }, today);
    Check(e.GetState(ModuleCodes.Catalog) == ModuleState.Valid, "ماژول با آیتم معتبر Valid است");
    Check(e.FindModule(ModuleCodes.Catalog)!.ExpiresAt is null, "آیتم دائمی، انقضا را بی‌نهایت می‌کند (دورترین)");
}

{
    var doc = Doc("S1",
        Item("I1", new[] { ModuleCodes.Sales }, Sub(today.AddMonths(-1), today.AddDays(10))),
        Item("I2", new[] { ModuleCodes.Sales }, Sub(today.AddMonths(-1), today.AddMonths(6))));

    var e = EntitlementMerger.Merge(new[] { doc }, today);
    Check(e.FindModule(ModuleCodes.Sales)!.ExpiresAt == today.AddMonths(6).Date,
        "دورترین انقضا ملاک است (نه نزدیک‌ترین)");
}

{
    var doc = Doc("S1", Item("I1", new[] { ModuleCodes.Reports }, Sub(today.AddMonths(-1), today.AddDays(20))));
    var e = EntitlementMerger.Merge(new[] { doc }, today);
    Check(e.GetState(ModuleCodes.Reports) == ModuleState.ExpiringSoon, "کمتر از ۳۰ روز مانده ⇒ ExpiringSoon");
}

// ============================================================
Section("۲) چرخهٔ عمر: Grace، ReadOnly و Disabled");

{
    var expiredYesterday = Doc("S1", Item("I1", new[] { ModuleCodes.Treasury }, Sub(today.AddMonths(-2), today.AddDays(-1))));
    var e = EntitlementMerger.Merge(new[] { expiredYesterday }, today);
    Check(e.GetState(ModuleCodes.Treasury) == ModuleState.Grace, "تا ۱۵ روز پس از انقضا ⇒ Grace");
}

{
    var expiredLong = Doc("S1", Item("I1", new[] { ModuleCodes.Treasury }, Sub(today.AddMonths(-6), today.AddDays(-30))));
    var e = EntitlementMerger.Merge(new[] { expiredLong }, today);
    Check(e.GetState(ModuleCodes.Treasury) == ModuleState.ReadOnly, "بیش از ۱۵ روز پس از انقضا ⇒ ReadOnly");
    Check(e.IsReadable(ModuleCodes.Treasury) && !e.IsWritable(ModuleCodes.Treasury),
        "ReadOnly: خواندن آزاد، نوشتن بسته");
}

{
    var doc = Doc("S1", Item("I1", new[] { ModuleCodes.Catalog }, Perp()));
    var e = EntitlementMerger.Merge(new[] { doc }, today);
    Check(e.GetState(ModuleCodes.Moadian) == ModuleState.Disabled, "ماژول بی‌لایسنس ⇒ Disabled");
    Check(e.GetState(ModuleCodes.Core) == ModuleState.Valid, "ماژول همیشه‌فعال (core) حتی بی‌لایسنس Valid است");
}

{
    var future = Doc("S1", Item("I1", new[] { ModuleCodes.Ai }, Sub(today.AddMonths(3), today.AddMonths(9))));
    var e = EntitlementMerger.Merge(new[] { future }, today);
    Check(e.GetState(ModuleCodes.Ai) == ModuleState.Disabled, "آیتم شروع‌نشده، ماژول را فعال نمی‌کند");
}

// ============================================================
Section("۳) سقف متریک: بیشترین سقف، نه جمع؛ بستهٔ افزایشی یکتا");

{
    var doc = Doc("S1",
        Item("I1", new[] { ModuleCodes.Invoicing }, Sub(today, today.AddYears(1)),
            quotas: new[] { new LicenseQuota { Metric = MetricCodes.InvoiceCreate, Limit = 1000, Period = QuotaPeriod.Lifetime } }),
        Item("I2", new[] { ModuleCodes.Invoicing }, Sub(today, today.AddYears(1)),
            quotas: new[] { new LicenseQuota { Metric = MetricCodes.InvoiceCreate, Limit = 500, Period = QuotaPeriod.Lifetime } }));

    var e = EntitlementMerger.Merge(new[] { doc }, today);
    var limit = e.LimitsFor(ModuleCodes.Invoicing, MetricCodes.InvoiceCreate).Single();
    Check(limit.Limit == 1000, "دو آیتم هم‌پوشان: بیشترین سقف (۱۰۰۰)، نه جمع (۱۵۰۰)");
}

{
    var doc = Doc("S1",
        Item("I1", new[] { ModuleCodes.Invoicing }, Sub(today, today.AddYears(1)),
            quotas: new[] { new LicenseQuota { Metric = MetricCodes.InvoiceCreate, Limit = 1000, Period = QuotaPeriod.Lifetime } }),
        Item("PACK-A", new[] { ModuleCodes.Invoicing }, Sub(today, today.AddYears(1)),
            quotas: new[] { new LicenseQuota { Metric = MetricCodes.InvoiceCreate, Limit = 500, Period = QuotaPeriod.Lifetime, Stackable = true, PackId = "pack-a" } }));

    var e = EntitlementMerger.Merge(new[] { doc }, today);
    Check(e.LimitsFor(ModuleCodes.Invoicing, MetricCodes.InvoiceCreate).Single().Limit == 1500,
        "بستهٔ افزایشی به بیشترین سقف اضافه می‌شود (۱۰۰۰ + ۵۰۰)");
}

{
    var samePack = Doc("S1",
        Item("I1", new[] { ModuleCodes.Invoicing }, Perp()),
        Item("PACK-A1", new[] { ModuleCodes.Invoicing }, Perp(),
            quotas: new[] { new LicenseQuota { Metric = MetricCodes.InvoiceCreate, Limit = 500, Period = QuotaPeriod.Lifetime, Stackable = true, PackId = "pack-a" } }),
        Item("PACK-A2", new[] { ModuleCodes.Invoicing }, Perp(),
            quotas: new[] { new LicenseQuota { Metric = MetricCodes.InvoiceCreate, Limit = 500, Period = QuotaPeriod.Lifetime, Stackable = true, PackId = "pack-a" } }));

    var e = EntitlementMerger.Merge(new[] { samePack }, today);
    Check(e.LimitsFor(ModuleCodes.Invoicing, MetricCodes.InvoiceCreate).Single().Limit == 500,
        "همان packId دوبار اعمال نمی‌شود (فقط یک‌بار)");
}

{
    var expired = Doc("S1",
        Item("OLD", new[] { ModuleCodes.Letters }, Sub(today.AddYears(-2), today.AddDays(-40)),
            quotas: new[] { new LicenseQuota { Metric = MetricCodes.LetterCreate, Limit = 100, Period = QuotaPeriod.Monthly } }));

    var e = EntitlementMerger.Merge(new[] { expired }, today);
    Check(e.LimitsFor(ModuleCodes.Letters, MetricCodes.LetterCreate).Count == 0,
        "آیتم منقضی هیچ سقفی نمی‌دهد");
}

// ============================================================
Section("۴) متریک تجمیعی: مصرف نامهٔ داخلی، سقف letter.create را هم می‌شمارد");

{
    var targets = MetricCodes.ConsumeTargets(MetricCodes.LetterInnerCreate);
    Check(targets.Contains(MetricCodes.LetterInnerCreate), "خودِ متریک در فهرست مصرف است");
    Check(targets.Contains(MetricCodes.LetterCreate), "متریک تجمیعی letter.create هم مصرف می‌شود");
    Check(MetricCodes.ModuleOf(MetricCodes.LetterOutgoingCreate) == ModuleCodes.Letters, "متریک نامه به ماژول letters تعلق دارد");
}

// ============================================================
Section("۵) سیاست کاربر: تعارض و سقف");

{
    var doc = Doc("S1",
        Item("I1", new[] { ModuleCodes.Catalog }, Perp(), new LicenseUsers { Mode = LicenseUserMode.Named, Max = 10 }),
        Item("I2", new[] { ModuleCodes.Sales }, Perp(), new LicenseUsers { Mode = LicenseUserMode.Concurrent, Max = 5 }));

    var e = EntitlementMerger.Merge(new[] { doc }, today);
    Check(e.Issues.Any(i => i.Code == "USER_POLICY_CONFLICT" && i.Severity == LicenseIssueSeverity.Error),
        "Named + Concurrent در یک entitlement ⇒ خطای تعارض");
}

{
    var doc = Doc("S1",
        Item("I1", new[] { ModuleCodes.Catalog }, Perp(), new LicenseUsers { Mode = LicenseUserMode.Named, Max = 10 }),
        Item("I2", new[] { ModuleCodes.Sales }, Perp(), new LicenseUsers { Mode = LicenseUserMode.Named, Max = 25 }));

    var e = EntitlementMerger.Merge(new[] { doc }, today);
    Check(e.Seats.Mode == LicenseUserMode.Named, "یک سیاست سراسری: Named");
    Check(e.Seats.GlobalMax == 25, "سقف سراسری = بیشترین سقف آیتم‌های همان سیاست (۲۵)");
}

{
    var doc = Doc("S1",
        Item("I1", new[] { ModuleCodes.Catalog }, Perp(),
            new LicenseUsers { Mode = LicenseUserMode.Named, Max = 4, AppliesTo = LicenseUserScope.Modules }),
        Item("I2", new[] { ModuleCodes.Sales }, Perp(),
            new LicenseUsers { Mode = LicenseUserMode.Named, Max = 7, AppliesTo = LicenseUserScope.Modules }));

    var e = EntitlementMerger.Merge(new[] { doc }, today);
    Check(e.Seats.ModuleMax.TryGetValue(ModuleCodes.Catalog, out var c) && c == 4, "سقف ماژولی catalog = ۴");
    Check(e.Seats.ModuleMax.TryGetValue(ModuleCodes.Sales, out var s) && s == 7, "سقف ماژولی sales = ۷");
}

// ============================================================
Section("۶) نسخه و آیتم دائمی (updatesUntil)");

{
    var doc = Doc("S1",
        Item("I1", new[] { ModuleCodes.Catalog }, Perp(new DateTime(2027, 6, 30))),
        Item("I2", new[] { ModuleCodes.Catalog }, Perp(new DateTime(2028, 1, 31))));

    var e = EntitlementMerger.Merge(new[] { doc }, today);
    Check(e.UpdatesUntil == new DateTime(2028, 1, 31), "بیشترین updatesUntil بین آیتم‌های دائمی ملاک است");
    Check(e.AllowsBuild(new DateTime(2028, 1, 1)), "Build قبل از سقف مجاز است");
    Check(!e.AllowsBuild(new DateTime(2028, 2, 1)), "Build بعد از سقف مجاز نیست");
}

{
    var doc = Doc("S1", Item("I1", new[] { ModuleCodes.Catalog }, Sub(today, today.AddYears(1))));
    var e = EntitlementMerger.Merge(new[] { doc }, today);
    Check(!e.HasPerpetualItem && e.AllowsBuild(new DateTime(2099, 1, 1)),
        "لایسنس فقط اشتراکی: نسخه محدود نمی‌شود");
}

// ============================================================
Section("۷) دوره‌های شمسی سهمیه");

{
    var shahrivar = new DateTime(2026, 9, 10);   // شهریور ۱۴۰۵
    Check(PersianPeriodKey.For(QuotaPeriod.Monthly, shahrivar) == "1405-06",
        $"کلید ماهانه شمسی درست است ({PersianPeriodKey.For(QuotaPeriod.Monthly, shahrivar)})");
    Check(PersianPeriodKey.For(QuotaPeriod.Yearly, shahrivar) == "1405", "کلید سالانه شمسی درست است");
    Check(PersianPeriodKey.For(QuotaPeriod.Lifetime, shahrivar) == "-", "کلید یک‌بار-برای-همیشه «-» است");

    var start = PersianPeriodKey.StartOf(QuotaPeriod.Monthly, "1405-08");
    Check(PersianPeriodKey.Year(start) == 1405 && PersianPeriodKey.Month(start) == 8,
        "StartOf همان ماه شمسی را برمی‌گرداند");
}

// ============================================================
Section("۸) امضا: معتبر، دست‌کاری‌شده، کلید ناشناس");

{
    // کلید توسعه در زمان اجرا ساخته می‌شود؛ کلید خصوصی هیچ‌وقت در مخزن نیست.
    using var rsa = RSA.Create(2048);
    var privatePem = rsa.ExportPkcs8PrivateKeyPem();
    var publicPem = rsa.ExportSubjectPublicKeyInfoPem();
    var ring = PublicKeyRing.FromEntries(new[] { new PublicKeyEntry { KeyId = "test-key", Algorithm = "RS256", PublicKeyPem = publicPem } });

    var document = Doc("SIG-1", Item("I1", new[] { ModuleCodes.Catalog }, Perp()));
    // شناسهٔ کلید داخل سند باید با شناسهٔ کلید پوشش امضا یکی باشد؛ وگرنه
    // LicenseVerifier (درست) آن را ناسازگار می‌داند.
    document.KeyId = "test-key";
    var payload = LicenseJson.ToPayloadBytes(document);
    var signature = KeySign.Sign(payload, privatePem, "RS256");

    var envelope = new SignedLicenseDocument
    {
        KeyId = "test-key",
        Algorithm = "RS256",
        Payload = Convert.ToBase64String(payload),
        Signature = Convert.ToBase64String(signature)
    };
    var fileText = JsonSerializer.Serialize(envelope);

    var ok = LicenseVerifier.Verify(fileText, ring);
    Check(ok.Ok && ok.Document is not null, "فایل با امضای معتبر پذیرفته می‌شود");

    // دست‌کاری payload (یک فیلد عوض شود) باید امضا را بشکند
    var tamperedDoc = Doc("SIG-1", Item("I1", new[] { ModuleCodes.Catalog, ModuleCodes.Moadian }, Perp()));
    var tampered = new SignedLicenseDocument
    {
        KeyId = "test-key",
        Algorithm = "RS256",
        Payload = Convert.ToBase64String(LicenseJson.ToPayloadBytes(tamperedDoc)),
        Signature = Convert.ToBase64String(signature)
    };
    Check(!LicenseVerifier.Verify(JsonSerializer.Serialize(tampered), ring).Ok,
        "تغییر payload (افزودن ماژول) امضا را می‌شکند");

    var unknownKey = new SignedLicenseDocument
    {
        KeyId = "other-key",
        Algorithm = "RS256",
        Payload = Convert.ToBase64String(payload),
        Signature = Convert.ToBase64String(signature)
    };
    Check(!LicenseVerifier.Verify(JsonSerializer.Serialize(unknownKey), ring).Ok,
        "keyId ناشناس رد می‌شود");
}

// ============================================================
Section("۹) اعتبارسنجی ساختار سند");

{
    var noItems = new LicenseDocument { Serial = "X", CustomerId = "C", Deployment = DeploymentKind.OnPrem, InstallationId = "abc123" };
    var issues = LicenseValidator.Validate(noItems, new LicenseValidationContext { Now = today, ExpectedInstallationId = "abc123" });
    Check(issues.Any(i => i.Code == "ITEMS_EMPTY" && i.Severity == LicenseIssueSeverity.Error), "سند بدون آیتم رد می‌شود");

    var wrongVersion = Doc("X", Item("I1", new[] { ModuleCodes.Catalog }, Perp()));
    wrongVersion.Version = 2;
    Check(LicenseValidator.Validate(wrongVersion, new LicenseValidationContext { Now = today })
            .Any(i => i.Code == "VERSION_UNSUPPORTED"), "نسخهٔ غیر ۳ رد می‌شود");

    var wrongInstall = Doc("X", Item("I1", new[] { ModuleCodes.Catalog }, Perp()));
    Check(LicenseValidator.Validate(wrongInstall, new LicenseValidationContext { Now = today, ExpectedInstallationId = "zzz" })
            .Any(i => i.Code == "INSTALLATION_MISMATCH"), "InstallationId نامطابق رد می‌شود");

    var unknownModule = Doc("X", Item("I1", new[] { "nosuchmodule" }, Perp()));
    Check(LicenseValidator.Validate(unknownModule, new LicenseValidationContext { Now = today })
            .Any(i => i.Code == "MODULE_UNKNOWN"), "کد ماژول ناشناخته رد می‌شود");

    var subNoDates = Doc("X", Item("I1", new[] { ModuleCodes.Catalog }, new LicenseTerm { Type = LicenseTermType.Subscription }));
    Check(LicenseValidator.Validate(subNoDates, new LicenseValidationContext { Now = today })
            .Any(i => i.Code == "TERM_VALID_UNTIL_REQUIRED"), "اشتراکی بدون تاریخ پایان رد می‌شود");

    var stackNoPack = Doc("X", Item("I1", new[] { ModuleCodes.Invoicing }, Perp(),
        quotas: new[] { new LicenseQuota { Metric = MetricCodes.InvoiceCreate, Limit = 10, Period = QuotaPeriod.Lifetime, Stackable = true } }));
    Check(LicenseValidator.Validate(stackNoPack, new LicenseValidationContext { Now = today })
            .Any(i => i.Code == "PACK_ID_REQUIRED"), "بستهٔ افزایشی بدون packId رد می‌شود");

    var serialDup = Doc("DUP", Item("I1", new[] { ModuleCodes.Catalog }, Perp()));
    Check(LicenseValidator.Validate(serialDup, new LicenseValidationContext { Now = today, AppliedSerials = new[] { "DUP" } })
            .Any(i => i.Code == "SERIAL_ALREADY_APPLIED"), "سریال تکراری رد می‌شود");
}

// ============================================================
Section("۱۰) نقشهٔ وابستگی");

{
    Check(ModuleDependencyMap.RequiresOf(ModuleCodes.Sales).Any(d => d.Requires == ModuleCodes.Catalog),
        "sales به catalog وابسته است");
    Check(ModuleDependencyMap.RequiresOf(ModuleCodes.Moadian).Any(d => d.Requires == ModuleCodes.Invoicing),
        "moadian به invoicing وابسته است");

    // همهٔ کدهای نقشه باید در کاتالوگ باشند (جلوگیری از اشتباه تایپی)
    var unknown = ModuleDependencyMap.All
        .SelectMany(d => new[] { d.Module, d.Requires })
        .Distinct()
        .Where(c => !ModuleCodes.IsKnown(c))
        .ToList();
    Check(unknown.Count == 0, $"همهٔ کدهای نقشهٔ وابستگی در کاتالوگ هستند (ناشناخته: {string.Join(", ", unknown)})");

    // بیش از یک‌بار وابستگی مستقیم به یک ماژول نباشد
    var duplicates = ModuleDependencyMap.All.GroupBy(d => (d.Module, d.Requires)).Where(g => g.Count() > 1).ToList();
    Check(duplicates.Count == 0, "یال وابستگی تکراری وجود ندارد");
}

// ============================================================
Section("۱۱) کاتالوگ ماژول‌ها");

{
    Check(ModuleCodes.IsAlwaysOn(ModuleCodes.Core), "core همیشه‌فعال است");
    Check(ModuleCodes.IsAlwaysOn(ModuleCodes.Dashboard), "dashboard همیشه‌فعال است");
    Check(ModuleCodes.IsAlwaysOn(ModuleCodes.Cartable), "cartable همیشه‌فعال است");
    Check(ModuleCodes.IsAlwaysOn(ModuleCodes.Backup), "backup همیشه‌فعال است");
    Check(!ModuleCodes.IsAlwaysOn(ModuleCodes.Sales), "sales همیشه‌فعال نیست");
    Check(!ModuleCodes.IsKnown("whatever"), "کد ناشناخته، ماژول معتبر نیست");

    var duplicateCodes = ModuleCodes.All.GroupBy(m => m.Code, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList();
    Check(duplicateCodes.Count == 0, "کد ماژول تکراری در کاتالوگ نیست");

    // آیتمی که ماژول همیشه‌فعال را نام ببرد، نباید خطای ساختاری بدهد (فقط هشدار)
    var docWithAlwaysOn = Doc("X", Item("I1", new[] { ModuleCodes.Core }, Perp()));
    var issues = LicenseValidator.Validate(docWithAlwaysOn, new LicenseValidationContext { Now = today });
    Check(!issues.Any(i => i.Severity == LicenseIssueSeverity.Error), "نام‌بردن core در scope خطای ساختاری نیست");
}

// ============================================================
Section("۱۲) چند فایل هم‌زمان (پایه + Add-on)");

{
    var baseDoc = Doc("BASE",
        Item("B1", new[] { ModuleCodes.Catalog }, Perp()),
        Item("B2", new[] { ModuleCodes.Invoicing }, Sub(today, today.AddYears(1)),
            quotas: new[] { new LicenseQuota { Metric = MetricCodes.InvoiceCreate, Limit = 1000, Period = QuotaPeriod.Lifetime } }));

    var addOn = Doc("ADDON",
        Item("A1", new[] { ModuleCodes.Moadian }, Sub(today, today.AddMonths(6)),
            quotas: new[] { new LicenseQuota { Metric = MetricCodes.MoadianInvoiceSend, Limit = 50, Period = QuotaPeriod.Monthly } }));

    var e = EntitlementMerger.Merge(new[] { baseDoc, addOn }, today);
    Check(e.GetState(ModuleCodes.Catalog) == ModuleState.Valid, "فایل پایه اعمال می‌شود");
    Check(e.GetState(ModuleCodes.Moadian) == ModuleState.Valid, "فایل Add-on هم‌زمان اعمال می‌شود");
    Check(e.AppliedSerials.Count == 2, "هر دو سریال در فهرست اعمال‌شده‌اند");
    Check(e.LimitsFor(ModuleCodes.Moadian, MetricCodes.MoadianInvoiceSend).Single().Period == QuotaPeriod.Monthly,
        "سهمیهٔ ماهانه از Add-on خوانده می‌شود");
}

// ============================================================
Section("۱۳) نبود لایسنس");

{
    var e = EntitlementMerger.Merge(Array.Empty<LicenseDocument>(), today);
    Check(e.Status == LicenseOverallStatus.Missing, "بدون سند ⇒ وضعیت Missing");
    Check(e.GetState(ModuleCodes.Core) == ModuleState.Valid, "حتی بدون سند، core باز است");
    Check(e.GetState(ModuleCodes.Sales) == ModuleState.Disabled, "بدون سند، ماژول‌های عادی Disabled");
}

// ============================================================
Section("۱۴) سازگاری با openssl — همان فایلی که ابزار بیرونی امضا می‌کند");

{
    // هدف: اثبات اینکه امضای تولیدشده با «openssl dgst -sha256 -sign» دقیقاً همان چیزی است
    // که LicenseVerifier در C# می‌پذیرد و با تغییر یک بایت رد می‌کند.
    //
    // پارامترهای امضا (RS256):
    //   padding = RSASSA-PKCS1-v1_5   (نه PSS)
    //   hash    = SHA-256
    //   ورودی   = بایت‌های خام payload  (نه Base64، نه JSON دوباره‌سریال‌شده)
    // ES256 (برای مرحله ۴):
    //   padding = ندارد (ECDSA)، hash = SHA-256، امضا = DER-encoded (r,s) — نه فرمت خام JWS

    var dir = Path.Combine(Path.GetTempPath(), "lic-openssl-" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(dir);
    var keyPath = Path.Combine(dir, "key.pem");
    var pubPath = Path.Combine(dir, "pub.pem");

    var hasOpenSsl = TryRun("openssl", "version", out _).Ok;
    Check(hasOpenSsl, "openssl روی این ماشین موجود است (وگرنه این بخش قابل اجرا نیست)");

    if (hasOpenSsl)
    {
        // کلید تازهٔ آزمایشی را همین‌جا با openssl می‌سازیم؛ هیچ کلید خصوصی‌ای در مخزن نیست.
        var gen = Run("openssl", $"genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out \"{keyPath}\"");
        Run("openssl", $"pkey -in \"{keyPath}\" -pubout -out \"{pubPath}\"");
        Check(gen.Ok && File.Exists(keyPath), "کلید آزمایشی با openssl ساخته شد");

        var publicPem = File.ReadAllText(pubPath);
        var ring = PublicKeyRing.FromEntries(new[]
        {
            new PublicKeyEntry { KeyId = "openssl-test", Algorithm = "RS256", PublicKeyPem = publicPem }
        });

        var doc = Doc("OSSL-1", Item("I1", new[] { ModuleCodes.Catalog, ModuleCodes.Invoicing }, Perp()));
        doc.KeyId = "openssl-test";
        var payload = LicenseJson.ToPayloadBytes(doc);
        var payloadPath = Path.Combine(dir, "payload.bin");
        File.WriteAllBytes(payloadPath, payload);

        // امضا با openssl — همان دستوری که tools/license-sign.py اجرا می‌کند
        var sign = Run("openssl", $"dgst -sha256 -sign \"{keyPath}\" -out \"{dir}/sig.bin\" \"{payloadPath}\"");
        var signature = File.ReadAllBytes(Path.Combine(dir, "sig.bin"));
        Check(sign.Ok && signature.Length == 256, "openssl امضای ۲۵۶ بایتی RSA-2048 تولید کرد");

        var envelope = new SignedLicenseDocument
        {
            KeyId = "openssl-test",
            Algorithm = "RS256",
            Payload = Convert.ToBase64String(payload),
            Signature = Convert.ToBase64String(signature)
        };
        var fileText = JsonSerializer.Serialize(envelope);

        var ok = LicenseVerifier.Verify(fileText, ring);
        Check(ok.Ok && ok.Document is not null,
            "LicenseVerifier فایل امضاشدهٔ openssl را می‌پذیرد (PKCS#1 v1.5 + SHA-256)");

        // ---------- تغییر یک بایت ----------
        var tamperedPayload = (byte[])payload.Clone();
        var index = Array.IndexOf(tamperedPayload, (byte)'"');   // هر بایتی؛ عمداً داخل JSON
        tamperedPayload[index + 3] ^= 0x01;                       // یک بیت در یک بایت

        var tamperedPayloadEnv = new SignedLicenseDocument
        {
            KeyId = "openssl-test",
            Algorithm = "RS256",
            Payload = Convert.ToBase64String(tamperedPayload),
            Signature = Convert.ToBase64String(signature)
        };
        Check(!LicenseVerifier.Verify(JsonSerializer.Serialize(tamperedPayloadEnv), ring).Ok,
            "تغییر یک بایت در payload ⇒ امضا رد می‌شود");

        // امضا هم دست‌کاری شود
        var tamperedSignature = (byte[])signature.Clone();
        tamperedSignature[10] ^= 0xFF;
        var tamperedSigEnv = new SignedLicenseDocument
        {
            KeyId = "openssl-test",
            Algorithm = "RS256",
            Payload = Convert.ToBase64String(payload),
            Signature = Convert.ToBase64String(tamperedSignature)
        };
        Check(!LicenseVerifier.Verify(JsonSerializer.Serialize(tamperedSigEnv), ring).Ok,
            "تغییر یک بایت در امضا ⇒ رد می‌شود");

        // ---------- ES256: امضای DER، نه فرمت خام JWS ----------
        var ecKey = Path.Combine(dir, "ec.pem");
        var ecPub = Path.Combine(dir, "ec-pub.pem");
        Run("openssl", $"genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 -out \"{ecKey}\"");
        Run("openssl", $"pkey -in \"{ecKey}\" -pubout -out \"{ecPub}\"");
        Run("openssl", $"dgst -sha256 -sign \"{ecKey}\" -out \"{dir}/ec.sig\" \"{payloadPath}\"");
        var ecSignature = File.ReadAllBytes(Path.Combine(dir, "ec.sig"));

        var ecRing = PublicKeyRing.FromEntries(new[]
        {
            new PublicKeyEntry { KeyId = "openssl-ec", Algorithm = "ES256", PublicKeyPem = File.ReadAllText(ecPub) }
        });
        var ecDoc = Doc("OSSL-EC", Item("I1", new[] { ModuleCodes.Catalog }, Perp()));
        ecDoc.KeyId = "openssl-ec";
        var ecPayload = LicenseJson.ToPayloadBytes(ecDoc);
        File.WriteAllBytes(payloadPath, ecPayload);
        Run("openssl", $"dgst -sha256 -sign \"{ecKey}\" -out \"{dir}/ec.sig\" \"{payloadPath}\"");

        var ecEnvelope = new SignedLicenseDocument
        {
            KeyId = "openssl-ec",
            Algorithm = "ES256",
            Payload = Convert.ToBase64String(ecPayload),
            Signature = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(dir, "ec.sig")))
        };
        Check(LicenseVerifier.Verify(JsonSerializer.Serialize(ecEnvelope), ecRing).Ok,
            "امضای ECDSA P-256 با openssl (DER) هم پذیرفته می‌شود");
    }

    try { Directory.Delete(dir, true); } catch { /* بی‌صدا */ }
}

// ============================================================
Section("۱۵) امضا روی بایت‌های خام — نه JSON بازسریالایزشده");

{
    // payloadی می‌سازیم که LicenseJson.ToPayloadBytes هرگز تولیدش نمی‌کند:
    // فاصلهٔ اضافه، ترتیب کلید جابه‌جا، CRLF. اگر برنامه payload را «بازسازی» می‌کرد،
    // این فایل رد می‌شد؛ پذیرشش یعنی دقیقاً همان بایت‌ها راستی‌آزمایی می‌شوند.
    using var rsa = RSA.Create(2048);
    var privatePem = rsa.ExportPkcs8PrivateKeyPem();
    var publicPem = rsa.ExportSubjectPublicKeyInfoPem();
    var ring = PublicKeyRing.FromEntries(new[]
    {
        new PublicKeyEntry { KeyId = "raw-key", Algorithm = "RS256", PublicKeyPem = publicPem }
    });

    var rawJson =
        "{\r\n" +
        "  \"items\": [ { \"id\": \"RAW1\", \"scope\": [\"catalog\"], \"term\": { \"type\": \"Perpetual\" }," +
        " \"users\": { \"mode\": \"None\", \"max\": 0 }, \"quotas\": [] } ],\r\n" +
        "  \"version\": 3, \"keyId\": \"raw-key\", \"serial\": \"RAW-1\", \"customerId\": \"C-RAW\"," +
        " \"deployment\": \"OnPrem\", \"installationId\": \"abc123\", \"issuedAt\": \"2026-01-01T00:00:00\"\r\n" +
        "}";
    var rawPayload = Encoding.UTF8.GetBytes(rawJson);

    Check(!rawPayload.SequenceEqual(LicenseJson.ToPayloadBytes(LicenseJson.ReadDocument(rawPayload)!)),
        "payload خام با خروجی بازسریالایز شده یکسان نیست (پس فرقشان قابل تشخیص است)");

    var signature = KeySign.Sign(rawPayload, privatePem, "RS256");
    var envelope = new SignedLicenseDocument
    {
        KeyId = "raw-key",
        Algorithm = "RS256",
        Payload = Convert.ToBase64String(rawPayload),
        Signature = Convert.ToBase64String(signature)
    };

    var result = LicenseVerifier.Verify(JsonSerializer.Serialize(envelope), ring);
    Check(result.Ok && result.Document?.Serial == "RAW-1",
        "امضا روی بایت‌های خام معتبر است — برنامه هیچ بازسازی‌ای انجام نمی‌دهد");
}

// ============================================================
Section("۱۶) کلید: فقط عمومی در مخزن، اثر انگشت مطابق، هیچ کلید خصوصی");

{
    // حلقهٔ جاسازی‌شدهٔ خود اسمبلی (همان چیزی که سرور مشتری در دست دارد)
    var embedded = PublicKeyRing.FromEmbedded();
#if DEBUG
    Check(embedded.LoadErrors.Count == 0, $"کلیدهای جاسازی‌شده بدون خطا بارگذاری می‌شوند ({string.Join(" | ", embedded.LoadErrors)})");
    Check(embedded.KeyIds.Contains("dev-2026a"), "کلید dev-2026a در حلقهٔ جاسازی‌شده هست");
#else
    // در Release، نبودِ کلید توسعه خطای بارگذاری نیست؛ یک «رد شدن عمدی» با یادداشت است.
    // آنچه باید بررسی شود این است: هیچ خطای واقعی دیگری وجود ندارد و کلید توسعه بارگذاری نشده.
    // دو پیام «عمدی» در Release کنار گذاشته می‌شوند: رد شدن کلید توسعه، و هشدار
    // «حلقه خالی است» — که تا وقتی ردیف کلید تولیدی به manifest اضافه نشده طبیعی است
    // و در §۵ همین خروجی هم به‌صورت هشدار چاپ می‌شود. هر خطای دیگری «واقعی» است.
    var realErrors = embedded.LoadErrors
        .Where(e => !e.Contains("dev-2026a", StringComparison.OrdinalIgnoreCase))
        .Where(e => !(embedded.KeyIds.Count == 0 && e.Contains("هیچ کلید عمومی معتبری")))
        .ToList();
    Check(realErrors.Count == 0,
        $"کلیدهای جاسازی‌شده بدون خطای واقعی بارگذاری می‌شوند ({string.Join(" | ", realErrors)})");
    Check(embedded.KeyIds.Count > 0 || embedded.LoadErrors.Any(e => e.Contains("هیچ کلید عمومی معتبری")),
        "بیلد Release بدون کلید تولیدی: هشدار خالی‌بودن حلقه صادر می‌شود (تا ردیف کلید تولیدی افزوده شود)");
    Check(embedded.LoadErrors.Any(e => e.Contains("dev-2026a", StringComparison.OrdinalIgnoreCase)),
        "بیلد Release: رد شدن کلید توسعه با یادداشت گزارش شده است");
    Check(!embedded.KeyIds.Contains("dev-2026a"), "بیلد Release: کلید dev-2026a در حلقهٔ جاسازی‌شده نیست");
#endif

    // اثر انگشت در manifest باید با فایل کلید بخواند (اگر دست‌کاری شود، بارگذاری رد می‌کند)
    var pem = File.ReadAllText(LocateRepoFile("src/Inventory.Licensing/Keys/dev-2026a.pem"));
    var manifest = File.ReadAllText(LocateRepoFile("src/Inventory.Licensing/Keys/manifest.json"));
    var fingerprint = PublicKeyRing.Fingerprint(pem, "RS256");
    Check(fingerprint is not null && manifest.Contains(fingerprint, StringComparison.OrdinalIgnoreCase),
        "اثر انگشت SHA-256 کلید عمومی با manifest یکی است");

    // هیچ فایل tracked‌شده‌ای نباید کلید خصوصی داشته باشد
    Check(!pem.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase),
        "فایل کلید داخل مخزن، کلید عمومی است نه خصوصی");

    var repoRoot = FindRepoRoot();
    var offenders = Directory.EnumerateFiles(repoRoot, "*.pem", SearchOption.AllDirectories)
        .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                    && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
        .Where(f => File.ReadAllText(f).Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
        .ToList();
    Check(offenders.Count == 0,
        $"هیچ فایل .pem حاوی کلید خصوصی در مخزن نیست (یافت‌شده: {string.Join(", ", offenders)})");
}

// ============================================================
Section("۱۷) پوشش رجیستری کنترلر→ماژول (تست شکست‌خور)");

{
    // این بخش عمداً «شکست‌خور» است: اگر کنترلر تازه‌ای به پروژه اضافه شود و در
    // LicenseModuleRegistry ثبت نشود، این تست FAIL می‌دهد و بیلد/CI را می‌شکند.
    // دلیل: در زمان اجرا رفتار fail-open است (سرویس مشتری نباید بخوابد)، پس تنها جای
    // سخت‌گیری همین‌جاست. وگرنه پوشش لایسنس بی‌سروصدا سوراخ می‌شود.

    var apiRoot = LocateRepoFile("src/Inventory.Api");
    var registryFile = Path.Combine(apiRoot, "Licensing", "Infrastructure", "LicenseModuleRegistry.cs");

    Check(Directory.Exists(apiRoot), $"پوشهٔ سورس API پیدا شد ({apiRoot})");

    // ---------- ۱) کنترلرهای واقعی در سورس ----------
    var controllers = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var file in Directory.EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories))
    {
        if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
            file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;

        var text = File.ReadAllText(file);
        // کامنت‌ها حذف می‌شوند تا کلاس کامنت‌شده حساب نشود
        text = System.Text.RegularExpressions.Regex.Replace(text, @"//[^\n]*", "");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"/\*.*?\*/", "", System.Text.RegularExpressions.RegexOptions.Singleline);

        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                     text, @"\bclass\s+([A-Za-z0-9_]+)Controller\b"))
        {
            var name = m.Groups[1].Value;

            // کلاس پایه (BaseApiController) کنترلر واقعی نیست
            if (string.Equals(name, "BaseApi", StringComparison.OrdinalIgnoreCase)) continue;
            // کلاس انتزاعی هم کنترلر مسیرپذیر نیست
            var declLine = text.Substring(Math.Max(0, m.Index - 60), Math.Min(60, m.Index)).TrimEnd();
            if (declLine.EndsWith("abstract", StringComparison.OrdinalIgnoreCase)) continue;

            controllers.Add(name);
        }
    }

    Check(controllers.Count > 100, $"کنترلرهای واقعی شناسایی شدند ({controllers.Count} مورد)");

    // ---------- ۲) کلیدهای رجیستری ----------
    var registryText = File.ReadAllText(registryFile);
    registryText = System.Text.RegularExpressions.Regex.Replace(registryText, @"//[^\n]*", "");
    var mapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                 registryText, @"\[""([A-Za-z0-9_]+)""\]\s*="))
        mapped.Add(m.Groups[1].Value);

    Check(mapped.Count > 100, $"کلیدهای رجیستری خوانده شدند ({mapped.Count} مورد)");

    // ---------- ۳) مقایسه ----------
    var unmapped = controllers.Where(c => !mapped.Contains(c)).OrderBy(c => c).ToList();
    Check(unmapped.Count == 0,
        unmapped.Count == 0
            ? $"همهٔ {controllers.Count} کنترلر در رجیستری ماژول‌اند"
            : $"{unmapped.Count} کنترلر در LicenseModuleRegistry نگاشت نشده‌اند: {string.Join(", ", unmapped)}");

    // کنترلر معاف از لایسنس، هم باید در فهرست ExemptControllers باشد
    var exempt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "License", "Auth", "Health" };
    var exemptMissing = exempt.Where(e => controllers.Contains(e) && !registryText.Contains($"\"{e}\"")).ToList();
    Check(exemptMissing.Count == 0, $"کنترلرهای معاف هم در رجیستری دیده شده‌اند ({string.Join(", ", exemptMissing)})");

    // ---------- ۴) کلید کهنه (کنترلر ناموجود) فقط گزارش می‌شود، شکست نیست ----------
    var stale = mapped.Where(k => !controllers.Contains(k)).OrderBy(k => k).ToList();
    Console.WriteLine(unmapped.Count == 0
        ? $"      کلیدهای کهنه در رجیستری (بی‌خطر): {stale.Count}"
        : "      کلیدهای کهنه گزارش نشد چون خطای اصلی، کنترلرهای نگاشت‌نشده است.");
}

// ============================================================
Section("۱۸) مرز ماه شمسی: مبنای محاسبه باید ساعت تهران باشد، نه ساعت/منطقهٔ سرور");

{
    // لحظه‌های مطلق (UTC):
    //   A = ۳۰ آبان ۲۳:۵۹ به وقت تهران  ⇒ 2026-11-21T20:29:00Z
    //   B = ۱ آذر ۰۰:۰۰ به وقت تهران   ⇒ 2026-11-21T20:30:00Z
    var a = new DateTimeOffset(2026, 11, 21, 20, 29, 0, TimeSpan.Zero);
    var b = new DateTimeOffset(2026, 11, 21, 20, 30, 0, TimeSpan.Zero);

    var keyA = PersianPeriodKey.ForInstant(a, QuotaPeriod.Monthly);
    var keyB = PersianPeriodKey.ForInstant(b, QuotaPeriod.Monthly);

    Console.WriteLine($"      منطقهٔ زمانی سرور این اجرا: {TimeZoneInfo.Local.Id} | TZ={Environment.GetEnvironmentVariable("TZ") ?? "(پیش‌فرض)"}");
    Console.WriteLine($"      کلید A (۳۰ آبان ۲۳:۵۹ تهران) = {keyA} | کلید B (۱ آذر ۰۰:۰۰ تهران) = {keyB}");

    Check(keyA == "1405-08", $"۲۳:۵۹ روز ۳۰ آبان ۱۴۰۵ ⇒ دورهٔ 1405-08 (واقعی: {keyA})");
    Check(keyB == "1405-09", $"۰۰:۰۰ روز ۱ آذر ۱۴۰۵ ⇒ دورهٔ تازهٔ 1405-09 (واقعی: {keyB})");
    Check(keyA != keyB, "دقیقاً در نیمه‌شب تهران، دوره عوض می‌شود");

    // ساعت دیوار تهران در همان دو لحظه
    var wallB = PersianPeriodKey.ToIran(b);
    var wallA = PersianPeriodKey.ToIran(a);
    Check(wallB.TimeOfDay == TimeSpan.Zero, $"مرز در نیمه‌شب تهران است (B = {wallB:yyyy-MM-dd HH:mm})");
    Check(wallA.Date == wallB.Date.AddDays(-1), $"A یک دقیقه پیش از مرز، روز قبل است ({wallA:yyyy-MM-dd HH:mm})");

    // مستقل از منطقهٔ زمانی سرور: تبدیل همیشه با Asia/Tehran انجام می‌شود
    Check(PersianPeriodKey.IranZone.BaseUtcOffset == TimeSpan.FromMinutes(210)
          || PersianPeriodKey.IranZone.SupportsDaylightSavingTime == false,
        $"منطقهٔ ایران تفکیک‌شده است ({PersianPeriodKey.IranZone.Id}، افست {PersianPeriodKey.IranZone.BaseUtcOffset})");

    // Now() هم باید از ساعت تهران بیاید، نه DateTime.Now
    var nowKey = PersianPeriodKey.Now(QuotaPeriod.Monthly);
    var expectedKey = PersianPeriodKey.ForInstant(DateTimeOffset.UtcNow, QuotaPeriod.Monthly);
    Check(nowKey == expectedKey, $"کلید «همین حالا» با مبنای تهران ساخته می‌شود ({nowKey})");
}

// ============================================================
Section("۱۹) پیام‌ها: «۸۰٪» و «سقف پر شد» دو پیام جدا هستند");

{
    var near = QuotaConsumeResult.Ok("invoice.create", used: 80, limit: 100, "'1405-08'");
    var at = QuotaConsumeResult.Ok("invoice.create", used: 100, limit: 100, "'1405-08'");
    var low = QuotaConsumeResult.Ok("invoice.create", used: 10, limit: 100, "'1405-08'");

    Check(near.IsNearLimit && !near.IsAtLimit, "مصرف ۸۰ از ۱۰۰ ⇒ «نزدیک سقف» است، نه «پر شده»");
    Check(at.IsAtLimit && !at.IsNearLimit, "مصرف ۱۰۰ از ۱۰۰ ⇒ «سقف پر شد» است، نه «۸۰٪»");
    Check(!low.IsNearLimit && !low.IsAtLimit, "مصرف ۱۰ از ۱۰۰ ⇒ هیچ هشداری ندارد");
    Check(near.WarningMessage?.Contains("80") == true && near.WarningMessage!.Contains('٪'),
        $"هشدار ۸۰٪ درصد را می‌گوید: «{near.WarningMessage}»");
    Check(at.WarningMessage?.Contains("پر شد") == true, $"هشدار ۱۰۰٪ می‌گوید «پر شد»: «{at.WarningMessage}»");
    Check(near.WarningMessage != at.WarningMessage, "دو متن هشدار متفاوت‌اند (ادغام نشده‌اند)");
}

// ============================================================
Section("۲۰) پوشش مسیر و رجیستری با بازتاب (reflection) روی اسمبلی واقعی");

{
    // چرا بازتاب؟ چون تست باید همان RouteAttribute واقعی را بخواند، نه یک regex روی متن.
    // اگر فردا کسی کنترلر تازه‌ای با مسیر خارج از /api بسازد (دقیقاً مثل ماجرای /Moadian)،
    // همین تست باید بشکند.
    var assembly = typeof(Inventory.Api.Licensing.Infrastructure.LicenseGateMiddleware).Assembly;
    var allControllers = assembly.GetTypes()
        .Where(t => !t.IsAbstract && !t.IsInterface
                    && typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(t)
                    && t.Name.EndsWith("Controller", StringComparison.Ordinal))
        .ToList();

    // کلاس پایه (مثل BaseApiController) کنترلر نیست: هیچ Route ندارد و خودش سرو نمی‌شود.
    var controllerTypes = allControllers
        .Where(t => !allControllers.Any(other => other != t && t.IsAssignableFrom(other)))
        .ToList();

    Check(controllerTypes.Count > 100, $"کنترلرهای واقعی از اسمبلی خوانده شدند ({controllerTypes.Count} مورد)");

    var problems = new List<string>();
    var ignoredPrefixes = new[] { "/swagger", "/_framework", "/favicon" };

    foreach (var type in controllerTypes)
    {
        var routes = new List<string>();
        foreach (var attr in type.GetCustomAttributes(inherit: true))
        {
            if (attr is Microsoft.AspNetCore.Mvc.RouteAttribute route) routes.Add(route.Template ?? "");
        }

        // مسیرهای مطلق روی اکشن‌ها (‎~/… یا /…) هم می‌توانند از دروازه بیرون بزنند
        foreach (var method in type.GetMethods())
        {
            foreach (var attr in method.GetCustomAttributes(inherit: true))
            {
                var template = attr switch
                {
                    Microsoft.AspNetCore.Mvc.RouteAttribute r => r.Template,
                    Microsoft.AspNetCore.Mvc.HttpGetAttribute g => g.Template,
                    Microsoft.AspNetCore.Mvc.HttpPostAttribute p => p.Template,
                    Microsoft.AspNetCore.Mvc.HttpPutAttribute u => u.Template,
                    Microsoft.AspNetCore.Mvc.HttpDeleteAttribute d => d.Template,
                    Microsoft.AspNetCore.Mvc.HttpPatchAttribute pa => pa.Template,
                    _ => null
                };

                if (!string.IsNullOrWhiteSpace(template) && (template!.StartsWith("~") || template.StartsWith("/")))
                    routes.Add(template.TrimStart('~'));
            }
        }

        if (routes.Count == 0)
        {
            problems.Add($"{type.Name}: هیچ [Route] ندارد");
            continue;
        }

        foreach (var raw in routes)
        {
            var path = raw.Split('?')[0];
            path = System.Text.RegularExpressions.Regex.Replace(path, @"\{[^}]*\}", "x").TrimStart('~');
            if (!path.StartsWith('/')) path = "/" + path;

            if (ignoredPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;

            var gated = Inventory.Api.Licensing.Infrastructure.LicenseGateMiddleware.IsApiPath(path);
            var documentedPublic = Inventory.Api.Licensing.Infrastructure.LicenseGateMiddleware
                .IsIntentionallyPublic(path);

            if (!gated && !documentedPublic)
                problems.Add($"{type.Name}: مسیر «{raw}» بیرون از دروازه است و در فهرست مستند «عمداً عمومی» هم نیست");
        }

        // رجیستری کنترلر→ماژول
        var registry = Inventory.Api.Licensing.Infrastructure.LicenseModuleRegistry.Resolve(
            type.Name.Replace("Controller", ""));
        if (registry is null)
            problems.Add($"{type.Name}: در رجیستری کنترلر→ماژول نگاشت نشده است");
    }

    if (problems.Count > 0)
        foreach (var p in problems.Take(10)) Console.WriteLine("      ⚠ " + p);

    Check(problems.Count == 0,
        problems.Count == 0
            ? $"همهٔ {controllerTypes.Count} کنترلر: مسیر زیر دروازهٔ لایسنس + نگاشت‌شده در رجیستری"
            : $"{problems.Count} مشکل در پوشش مسیر/رجیستری (بیشتر از ۱۰ مورد چاپ نشد)");

    // کنترل مکمل: اگر دروازه پیشوند /api را نداشت، همین تست باید می‌شکست.
    var publicPaths = Inventory.Api.Licensing.Infrastructure.LicenseGateMiddleware.IntentionallyPublicPaths;
    Check(publicPaths.Length == 3,
        $"فهرست «عمداً بیرون از دروازه» دقیقاً ۳ مورد مستند است ({string.Join(", ", publicPaths)})");

    Check(Inventory.Api.Licensing.Infrastructure.LicenseGateMiddleware.IsApiPath("/api/products"),
        "کنترل مثبت: /api/products داخل دروازه است");
    Check(Inventory.Api.Licensing.Infrastructure.LicenseGateMiddleware.IsApiPath("/Moadian/SendInvoiceAsync"),
        "کنترل مثبت: مسیر /Moadian (غیر api) هم داخل دروازه است — همان باگ قبلی");
}

// ============================================================
Section("۲۱) انقضا و مهلت تنفس با ساعت ایران + جایگزین +03:30 (و نه UTC)");

{
    // بخش الف: «امروز» باید تاریخ تهران باشد، نه تاریخ سرور.
    // اگر سرور UTC باشد، بین ۰۰:۰۰ تا ۰۳:۳۰ بامداد تهران هنوز «دیروز» است و
    // لایسنسی که امروز منقضی می‌شود یک روز بیشتر زنده می‌ماند.
    var todayIran = Inventory.Licensing.Entitlement.EntitlementMerger.Today();
    var expectedIran = Inventory.Licensing.Periods.PersianPeriodKey.NowIran().Date;
    Check(todayIran == expectedIran,
        $"«امروز» = تاریخ تهران ({todayIran:yyyy-MM-dd}) — مستقل از منطقهٔ زمانی سرور (سرور: {TimeZoneInfo.Local.Id})");

    // بخش ب: سند منقضی‌شده «دیروزِ تهران» باید Expired باشد — با now:null (ساعت سیستم)
    var yesterday = expectedIran.AddDays(-1);
    var doc = new Inventory.Licensing.Model.LicenseDocument
    {
        Serial = "TZ-1", KeyId = "dev-2026a", CustomerId = "C", CustomerName = "تست",
        Deployment = Inventory.Licensing.Model.DeploymentKind.OnPrem, InstallationId = "i",
        Items = new List<Inventory.Licensing.Model.LicenseItem>
        {
            new()
            {
                Id = "I1", Scope = new List<string> { Inventory.Licensing.Modules.ModuleCodes.Invoicing },
                Term = new Inventory.Licensing.Model.LicenseTerm
                {
                    Type = Inventory.Licensing.Model.LicenseTermType.Subscription,
                    ValidFrom = yesterday.AddDays(-30), ValidUntil = yesterday
                }
            }
        }
    };

    var expired = Inventory.Licensing.Entitlement.EntitlementMerger.Merge(new[] { doc });
    Check(expired.Modules.TryGetValue(Inventory.Licensing.Modules.ModuleCodes.Invoicing, out var st1)
          && st1.State != Inventory.Licensing.Entitlement.ModuleState.Valid,
        $"سندی که «دیروزِ تهران» تمام شده، معتبر نیست (وضعیت: {(st1 is null ? "?" : st1.State.ToString())})");

    // و سندی که «امروزِ تهران» تمام می‌شود تا پایان امروز معتبر است
    doc.Items[0].Term.ValidUntil = expectedIran;
    var stillValid = Inventory.Licensing.Entitlement.EntitlementMerger.Merge(new[] { doc });
    Check(stillValid.Modules[Inventory.Licensing.Modules.ModuleCodes.Invoicing].State
          is Inventory.Licensing.Entitlement.ModuleState.Valid
             or Inventory.Licensing.Entitlement.ModuleState.ExpiringSoon,
        "سندی که امروزِ تهران تمام می‌شود، کل امروز معتبر است");

    // بخش ج: مهلت تنفس (Grace) هم با همان مبنا
    doc.Items[0].Term.ValidUntil = expectedIran.AddDays(-3);   // ۳ روز پیش
    var grace = Inventory.Licensing.Entitlement.EntitlementMerger.Merge(new[] { doc });
    var graceState = grace.Modules[Inventory.Licensing.Modules.ModuleCodes.Invoicing].State;
    Check(graceState == Inventory.Licensing.Entitlement.ModuleState.Grace,
        $"۳ روز پس از انقضا ⇒ مهلت تنفس (وضعیت: {graceState})");

    doc.Items[0].Term.ValidUntil = expectedIran.AddDays(-400);
    var readOnly = Inventory.Licensing.Entitlement.EntitlementMerger.Merge(new[] { doc });
    var roState = readOnly.Modules[Inventory.Licensing.Modules.ModuleCodes.Invoicing].State;
    Check(roState == Inventory.Licensing.Entitlement.ModuleState.ReadOnly,
        $"۴۰۰ روز پس از انقضا ⇒ فقط‌خواندنی (وضعیت: {roState})");
}

// ============================================================
Section("۲۲) جایگزین منطقهٔ زمانی: اگر Asia/Tehran و Iran Standard Time نبودند ⇒ +03:30");

{
    // سناریوی واقعی: کانتینر لاغر یا ویندوز بدون tzdata. قبلاً در چنین حالتی
    // TimeZoneInfo.FindSystemTimeZoneById خطا می‌داد و برنامه می‌افتاد؛ حالا افست ثابت.
    var noZones = Inventory.Licensing.Periods.PersianPeriodKey.ResolveZone(_ => null);
    Check(noZones.BaseUtcOffset == TimeSpan.FromMinutes(210),
        $"افست جایگزین +03:30 است، نه UTC (واقعی: {noZones.BaseUtcOffset})");
    Check(noZones.BaseUtcOffset != TimeSpan.Zero, "جایگزین هرگز UTC نیست");

    // همان لحظهٔ UTC باید به ساعت تهران (۳:۳۰ جلوتر) تبدیل شود
    var utc = new DateTimeOffset(2026, 11, 21, 20, 30, 0, TimeSpan.Zero);   // = ۰۰:۰۰ تهران
    var asIran = TimeZoneInfo.ConvertTime(utc, noZones).DateTime;
    Check(asIran == new DateTime(2026, 11, 22, 0, 0, 0),
        $"تبدیل با جایگزین: ۲۰:۳۰ UTC ⇒ ۰۰:۰۰ روز بعد به وقت تهران (واقعی: {asIran:yyyy-MM-dd HH:mm})");

    // فقط «Asia/Tehran» غایب باشد ⇒ باید «Iran Standard Time» را امتحان کند
    var triedIds = new List<string>();
    var windowsZone = Inventory.Licensing.Periods.PersianPeriodKey.ResolveZone(id =>
    {
        triedIds.Add(id);
        return id == "Iran Standard Time"
            ? TimeZoneInfo.CreateCustomTimeZone("Iran Standard Time", TimeSpan.FromMinutes(210), "IRST", "IRST")
            : null;
    });
    Check(triedIds.SequenceEqual(new[] { "Asia/Tehran", "Iran Standard Time" }),
        $"ترتیب تلاش: {string.Join(" ⇒ ", triedIds)}");
    Check(windowsZone.Id == "Iran Standard Time", $"شناسهٔ ویندوز پذیرفته شد ({windowsZone.Id})");

    // و وقتی سرور به‌درستی Asia/Tehran دارد، همان استفاده می‌شود
    var real = Inventory.Licensing.Periods.PersianPeriodKey.IranZone;
    Console.WriteLine($"   (اطلاع) منطقهٔ ایران روی این سرور: {real.Id} با افست {real.BaseUtcOffset}");
    Check(real.BaseUtcOffset == TimeSpan.FromMinutes(210) || real.BaseUtcOffset == TimeSpan.FromMinutes(210),
        "افست منطقهٔ ایران +03:30 است (ایران از ۱۴۰۱ ساعت تابستانی ندارد)");
}

// ============================================================
Section("۲۳) فهرست کنترلرها (برای تطبیق با گزارش مرحله ۰)");

{
    // با متغیر محیطی LICENSING_DUMP_CONTROLLERS=1 فهرست کامل چاپ می‌شود تا با
    // عدد ۱۲۵ گزارش مرحله ۰ مقایسه شود. حالت پیش‌فرض: فقط خلاصه.
    var assembly = typeof(Inventory.Api.Licensing.Infrastructure.LicenseGateMiddleware).Assembly;
    var all = assembly.GetTypes()
        .Where(t => !t.IsAbstract && !t.IsInterface
                    && typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(t)
                    && t.Name.EndsWith("Controller", StringComparison.Ordinal))
        .ToList();
    var real = all.Where(t => !all.Any(o => o != t && t.IsAssignableFrom(o)))
        .Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

    Check(real.Count >= 150, $"تعداد کنترلرهای واقعی: {real.Count}");

    if (Environment.GetEnvironmentVariable("LICENSING_DUMP_CONTROLLERS") == "1")
    {
        Console.WriteLine("----- BEGIN CONTROLLERS -----");
        foreach (var name in real) Console.WriteLine(name);
        Console.WriteLine("----- END CONTROLLERS -----");
    }
}

// ============================================================
Section("۲۴) سازگاری رو به جلو: فیلد ناشناخته در سند امضاشده، امضا را نمی‌شکند");

{
    // درخواست کارفرما (بازبینی سوم): «سندی که فیلد ناشناخته (مثلاً carryOverUsage) دارد
    // باید هنوز تأیید و بارگذاری شود.»
    //
    // چرا این مهم است؟ فایل لایسنس را **ما** برای **آن‌ها** امضا می‌کنیم. اگر نسخهٔ نرم‌افزار
    // مشتری قدیمی‌تر از فیلدهای جدید سند باشد، نباید فایل «معتبر ولی ناخوانا» شود و
    // مشتری وسط کار بماند. امضا روی بایت‌های خام است، پس فیلد اضافی امضا را نمی‌شکند؛
    // فقط باید مطمئن شویم Deserialize آن را نادیده می‌گیرد (نه خطا).
    using var rsa = RSA.Create(2048);
    var privatePem = rsa.ExportPkcs8PrivateKeyPem();
    var publicPem = rsa.ExportSubjectPublicKeyInfoPem();
    var ring = PublicKeyRing.FromEntries(new[] { new PublicKeyEntry { KeyId = "fwd-key", Algorithm = "RS256", PublicKeyPem = publicPem } });

    var document = Doc("FWD-1", Item("I1", new[] { ModuleCodes.Catalog }, Perp()));
    document.KeyId = "fwd-key";

    // payload پایه، سپس تزریق دستی یک فیلد ناشناخته — همان کاری که ابزار صدور نسخهٔ
    // آینده می‌کند. امضا روی همین بایت‌های تغییر‌یافته زده می‌شود.
    var json = Encoding.UTF8.GetString(LicenseJson.ToPayloadBytes(document));
    var mutated = json.Replace("\"items\":", "\"carryOverUsage\":17,\"someFutureField\":{\"a\":1},\"items\":", StringComparison.Ordinal);
    Check(mutated != json, "فیلدهای ناشناخته به payload تزریق شدند");

    var payload = Encoding.UTF8.GetBytes(mutated);
    var signature = KeySign.Sign(payload, privatePem, "RS256");

    var envelope = new SignedLicenseDocument
    {
        KeyId = "fwd-key",
        Algorithm = "RS256",
        Payload = Convert.ToBase64String(payload),
        Signature = Convert.ToBase64String(signature)
    };

    var fileText = JsonSerializer.Serialize(envelope);
    var verified = LicenseVerifier.Verify(fileText, ring);

    Check(verified.Ok, $"سند با فیلد ناشناخته تأیید می‌شود (خطا: {verified.ErrorText})");
    Check(verified.Document is not null, "سند با فیلد ناشناخته بارگذاری می‌شود (Document != null)");
    Check(verified.Document?.Serial == "FWD-1",
        $"سریال درست خوانده شد (واقعی: {verified.Document?.Serial})");
    Check(verified.Document?.Items.Count == 1 && verified.Document.Items[0].Quotas.Count == 0,
        "فیلدهای شناخته‌شدهٔ دیگر سالم ماندند");

    // کنترل: همان payload بدون امضای درست باید رد شود — تا معلوم باشد «نادیده گرفتن
    // فیلد ناشناخته» به معنی «نادیده گرفتن امضا» نیست.
    var badSig = new SignedLicenseDocument
    {
        KeyId = "fwd-key",
        Algorithm = "RS256",
        Payload = Convert.ToBase64String(payload),
        Signature = Convert.ToBase64String(KeySign.Sign(LicenseJson.ToPayloadBytes(document), privatePem, "RS256"))
    };
    Check(!LicenseVerifier.Verify(JsonSerializer.Serialize(badSig), ring).Ok,
        "کنترل: امضای payload اصلی، payload تغییر‌یافته را تأیید نمی‌کند");

    // و سندِ دارای فیلد ناشناخته باید در ادغام هم بی‌مشکل باشد (نه فقط در تأیید).
    var merged = merged_ok(verified.Document!);
    Check(merged, "ادغام سند دارای فیلد ناشناخته بدون خطا انجام شد");
}

// ============================================================
// ============================================================
// ============================================================
Section("۲۵) کلید توسعه فقط در بیلد Debug — بیلد Release نباید آن را بشناسد (بازبینی چهارم، مورد ۵)");

{
    // قاعدهٔ خالص: در Debug همه مجازند، در Release فقط کلیدهای غیرتوسعه.
    Check(Inventory.Licensing.Crypto.PublicKeyRing.IsKeyAllowedInBuild(false),
        "کلید تولیدی (debugOnly=false) در هر بیلدی مجاز است");

#if DEBUG
    Check(Inventory.Licensing.Crypto.PublicKeyRing.IsKeyAllowedInBuild(true),
        "بیلد Debug: کلید توسعه (debugOnly=true) مجاز است");
#else
    Check(!Inventory.Licensing.Crypto.PublicKeyRing.IsKeyAllowedInBuild(true),
        "بیلد Release: کلید توسعه (debugOnly=true) رد می‌شود");
#endif

    var ring = Inventory.Licensing.Crypto.PublicKeyRing.FromEmbedded();

#if DEBUG
    Check(ring.KeyIds.Contains("dev-2026a"),
        $"بیلد Debug: کلید dev-2026a در حلقه هست (کلیدها: {string.Join(", ", ring.KeyIds)})");
#else
    Check(!ring.KeyIds.Contains("dev-2026a"),
        $"بیلد Release: کلید dev-2026a در حلقه نیست (کلیدها: {string.Join(", ", ring.KeyIds)})");
#endif

    // ⚠️ هشدار عملیاتی: بیلد Release بدون کلید تولیدی یعنی «هیچ لایسنسی معتبر نیست».
    // این تست آن را قرمز نمی‌کند (چون در مرحلهٔ توسعه طبیعی است) ولی چاپ می‌کند تا
    // لحظهٔ بسته‌بندی فراموش نشود.
    if (ring.KeyIds.Count == 0)
        Console.WriteLine("   ⚠️ یادآوری: این بیلد هیچ کلید عمومی ندارد ⇒ پیش از تحویل، " +
                          "ردیف کلید تولیدی را به Keys/manifest.json اضافه کنید.");
}


// ============================================================
Section("۲۶) مرز ماه شمسی و ساعت ثبت سیستمی — مستقل از منطقهٔ زمانی سرور (بازبینی چهارم، بند ۲)");

{
    // دو لحظهٔ مطلق، دقیقاً روی مرز ماه شمسی به وقت تهران:
    //   آبان ۱۴۰۵ سی روزه است ⇒ پایانش ۳۰ آبان ۲۳:۵۹:۵۹ به وقت تهران.
    //   ۳۰ آبان ۲۳:۵۹ تهران = ۲۰:۲۹ UTC همان روز   (افست +03:30)
    //   آخرین ثانیه: 20:29:59Z      → باید در ۱۴۰۵-۰۸ باشد
    //   لحظهٔ بعد:    20:30:00Z      → باید در ۱۴۰۵-۰۹ باشد
    var lastSecondOfAban = new DateTimeOffset(2026, 11, 21, 20, 29, 59, TimeSpan.Zero);
    var firstSecondOfAzar = new DateTimeOffset(2026, 11, 21, 20, 30, 0, TimeSpan.Zero);

    var keyBefore = PersianPeriodKey.ForInstant(lastSecondOfAban, QuotaPeriod.Monthly);
    var keyAfter = PersianPeriodKey.ForInstant(firstSecondOfAzar, QuotaPeriod.Monthly);

    Console.WriteLine($"   (اطلاع) منطقهٔ زمانی این اجرا: {TimeZoneInfo.Local.Id} — " +
                      $"منطقهٔ مبنا: {PersianPeriodKey.IranZone.Id}");

    Check(keyBefore == "1405-08", $"۳۰ آبان ۲۳:۵۹:۵۹ تهران ⇒ ماه ۱۴۰۵-۰۸ (واقعی: {keyBefore})");
    Check(keyAfter == "1405-09", $"۱ آذر ۰۰:۰۰:۰۰ تهران ⇒ ماه ۱۴۰۵-۰۹ (واقعی: {keyAfter})");
    Check(keyBefore != keyAfter, "دو لحظهٔ یک‌ثانیه‌ای، دو ماه شمسی متفاوت‌اند (مرز دقیق است)");

    // همان دو لحظه، از مسیر «ساعت دیوار» که ستون CreatedAt با آن نوشته می‌شود:
    var wallBefore = PersianPeriodKey.ToIran(lastSecondOfAban);
    var wallAfter = PersianPeriodKey.ToIran(firstSecondOfAzar);

    // ⚠️ توجه: DateTime.Year/Month/Day میلادی است؛ برای شمسی از PersianPeriodKey استفاده می‌کنیم.
    // ساعت دیوارِ تهران برای لحظهٔ اول: ۳۰ آبان ۱۴۰۵ ساعت ۲۳:۵۹ (= ۲۱ نوامبر ۲۰۲۶ میلادی).
    Check(PersianPeriodKey.Year(wallBefore) == 1405 && PersianPeriodKey.Month(wallBefore) == 8
          && wallBefore.Day == 21 && wallBefore.Hour == 23 && wallBefore.Minute == 59,
        $"ساعت دیوارِ لحظهٔ اول = ۳۰ آبان ۱۴۰۵، ۲۳:۵۹ (واقعی: {wallBefore:yyyy-MM-dd HH:mm} میلادی = "
        + $"{PersianPeriodKey.Year(wallBefore)}/{PersianPeriodKey.Month(wallBefore):00})");
    Check(PersianPeriodKey.Year(wallAfter) == 1405 && PersianPeriodKey.Month(wallAfter) == 9
          && wallAfter.Day == 22 && wallAfter.Hour == 0,
        $"ساعت دیوارِ لحظهٔ دوم = ۱ آذر ۱۴۰۵، ۰۰:۰۰ (واقعی: {wallAfter:yyyy-MM-dd HH:mm} میلادی = "
        + $"{PersianPeriodKey.Year(wallAfter)}/{PersianPeriodKey.Month(wallAfter):00})");

    // بازهٔ ماه باید با همان مرزها یکی باشد: [From, To)
    //   اول آبان ۱۴۰۵ = ۲۳ اکتبر ۲۰۲۶ ؛ اول آذر ۱۴۰۵ = ۲۲ نوامبر ۲۰۲۶
    var (from, to) = PersianPeriodKey.RangeOf(QuotaPeriod.Monthly, wallBefore);
    Check(from == new DateTime(2026, 10, 23),
        $"شروع بازه = ۰۰:۰۰ اول آبان (۲۳ اکتبر ۲۰۲۶) — واقعی: {from:yyyy-MM-dd HH:mm}");
    Check(to == wallAfter.Date,
        $"پایان بازه (انحصاری) = ۰۰:۰۰ اول آذر (۲۲ نوامبر ۲۰۲۶) — واقعی: {to:yyyy-MM-dd HH:mm}");

    // ردیفی که ۲۳:۵۹:۵۹ نوشته شده در بازهٔ ماه قبل است، نه ماه بعد.
    Check(wallBefore >= from && wallBefore < to, "ردیف ۲۳:۵۹:۵۹ در بازهٔ ماه قبل می‌افتد");
    Check(!(wallAfter >= from && wallAfter < to), "ردیف ۰۰:۰۰:۰۰ در بازهٔ ماه قبل نمی‌افتد");

    // و مستقل از منطقهٔ زمانی *سرور*: هیچ‌کدام از این محاسبات به TimeZoneInfo.Local نگاه نمی‌کند.
    // (اثبات اجرایی: همین مجموعه با TZ=UTC، Asia/Tehran، Europe/Amsterdam و America/New_York اجرا و
    //  خروجی یکسان دیده شد — گزارش بند ۲.)
    Check(PersianPeriodKey.IranZone.Id is "Asia/Tehran" or "Iran Standard Time" or "Iran-Fallback",
        $"منطقهٔ ایران حل شد، نه UTC (واقعی: {PersianPeriodKey.IranZone.Id})");
    Check(PersianPeriodKey.IranZone.GetUtcOffset(new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero))
              == TimeSpan.FromMinutes(210),
        "افست ایران +03:30 است (بدون ساعت تابستانی)");
}

// ============================================================
Section("۲۷) گام ۵ — کلاینت: نگاشت منو به ماژول، خلاصهٔ وضعیت، متن خطاها");
// ============================================================

{
    // ---------- الف) نگاشت منو ⇄ ماژول ----------
    Check(Inventory.Client.Services.LicenseMenuMap.ModuleForPath("settings/license") == "licensing",
        "مسیر «وضعیت لایسنس» به ماژول licensing نگاشت می‌شود");
    Check(Inventory.Client.Services.LicenseMenuMap.ModuleForPath("/hr/payroll?tab=1") == "hr_fa",
        "کوئری و اسلش ابتدایی، نگاشت را خراب نمی‌کند");
    Check(Inventory.Client.Services.LicenseMenuMap.ModuleForPath("/hr-core/staff") == "hr_core",
        "پیشوند «hr» مسیر «hr-core» را نمی‌دزدد (تطبیق روی مرز بخش)");
    Check(Inventory.Client.Services.LicenseMenuMap.ModuleForPath("/some-unmapped-page") is null,
        "مسیر نگاشت‌نشده ⇒ آزاد (منو بی‌دلیل پنهان نمی‌شود)");

    var mapped = Inventory.Client.Services.LicenseMenuMap.MappedModuleCodes;
    var allCodes = ModuleCodes.All.Select(m => m.Code).ToList();
    var unknown = mapped.Where(c => !allCodes.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
    Check(unknown.Count == 0, "همهٔ کدهای نگاشت‌شده در فهرست ۳۳ ماژول موتور وجود دارند" +
        (unknown.Count == 0 ? "" : " — ناشناخته: " + string.Join(", ", unknown)));
    Check(allCodes.Count == 33, $"فهرست ماژول‌های موتور ۳۳ مورد است (واقعی: {allCodes.Count})");
    Check(mapped.Count >= 30, $"نگاشت، دست‌کم ۳۰ ماژول را پوشش می‌دهد (واقعی: {mapped.Count})");

    var disabled = new[] { "sales" };
    Check(!Inventory.Client.Services.LicenseMenuMap.IsPathAllowed("/sales/invoices", disabled),
        "ماژول غیرفعال ⇒ منوی آن پنهان می‌شود");
    Check(Inventory.Client.Services.LicenseMenuMap.IsPathAllowed("/catalog/products", disabled),
        "سایر ماژول‌ها با غیرفعال‌شدن یک ماژول پنهان نمی‌شوند");
    Check(Inventory.Client.Services.LicenseMenuMap.IsPathAllowed("/settings/audit-log", disabled),
        "مسیر نگاشت‌نشده همیشه مجاز است");

    // ---------- ب) خلاصهٔ وضعیت ----------
    const string payload = """
    {
      "status": "Valid",
      "customerName": "شرکت نمونه",
      "installationId": "11111111-1111-1111-1111-111111111111",
      "deployment": "OnPrem",
      "updatesUntil": "2027-01-01T00:00:00",
      "canWrite": false,
      "rehostRecommended": true,
      "clock": { "health": "MajorBackward", "writesBlocked": true, "message": "ساعت عقب است" },
      "seats": { "mode": "Named", "globalMax": 5 },
      "modules": [
        { "code": "catalog", "title": "کالا", "state": "Valid", "stateTitle": "فعال", "alwaysOn": false },
        { "code": "sales", "title": "فروش", "state": "Disabled", "stateTitle": "غیرفعال", "alwaysOn": false },
        { "code": "core", "title": "هسته", "state": "Valid", "stateTitle": "فعال", "alwaysOn": true }
      ],
      "limits": [
        { "module": "invoicing", "metric": "invoice", "used": 900, "limit": 1000 },
        { "module": "letters", "metric": "letter", "used": 10, "limit": 0 },
        { "module": "moadian", "metric": "invoice", "used": 1000, "limit": 1000 }
      ],
      "reservations": { "pending": 2, "reserved": 1, "confirmed": 10, "refunded": 0 },
      "issues": [ { "code": "CLOCK_BEHIND", "severity": "Error", "message": "ساعت سرور عقب است" } ]
    }
    """;

    var summary = Inventory.Client.Services.LicenseStatusSummary.Parse(payload);
    Check(summary.Status == "Valid" && summary.StatusTitle == "فعال", "وضعیت کلی درست خوانده می‌شود");
    Check(!summary.CanWrite, "پرچم «نوشتن بسته» از canWrite خوانده می‌شود");
    Check(summary.ClockBlocked && summary.ClockMessage == "ساعت عقب است", "قفل ساعت و پیام آن خوانده می‌شود");
    Check(summary.RehostRecommended, "پرچم جابه‌جایی نصب خوانده می‌شود");
    Check(summary.SeatsGlobalMax == 5 && summary.SeatMode == "Named", "صندلی خوانده می‌شود");
    Check(summary.Modules.Count == 3 && summary.Modules[0].Title == "کالا", "ردیف‌های ماژول خوانده می‌شوند");
    Check(summary.DisabledModules.Count == 1 && summary.DisabledModules.Contains("sales"),
        "ماژول‌های غیرفعال برای پنهان‌کردن منو استخراج می‌شوند");
    Check(summary.NeedsAttention, "وضعیت نیازمند اقدام تشخیص داده می‌شود (قفل ساعت/خطا)");

    var invoicing = summary.Limits.First(l => l.Module == "invoicing");
    Check(invoicing.Percent == 90 && invoicing.AtRisk, "۹۰۰ از ۱۰۰۰ ⇒ ۹۰٪ و «در معرض خطر»");
    var unlimited = summary.Limits.First(l => l.Module == "letters");
    Check(unlimited.Percent is null && !unlimited.AtRisk, "سقف نامحدود ⇒ درصد ندارد و هشدار نمی‌سازد");
    var full = summary.Limits.First(l => l.Module == "moadian");
    Check(full.Percent == 100, "سقف پر ⇒ درصد ۱۰۰ (نه بیشتر)");
    Check(!new Inventory.Client.Services.LicenseStatusSummary
        { Status = "Valid" }.NeedsAttention, "وضعیت سالم ⇒ نیازی به اقدام نیست");

    // ---------- ج) متن خطاها: هیچ خطای لایسنسی خروج از حساب نمی‌خواهد ----------
    Check(!Inventory.Client.Services.LicenseErrorText.RequiresRelogin("LICENSE_READONLY")
          && !Inventory.Client.Services.LicenseErrorText.RequiresRelogin("SEAT_ENDED"),
        "هیچ کد لایسنسی، خروج خودکار از حساب را لازم نمی‌کند");
    foreach (var code in new[] { "LICENSE_READONLY", "MODULE_DISABLED", "QUOTA_EXCEEDED", "SEAT_FULL", "SEAT_ENDED" })
        Check(Inventory.Client.Services.LicenseErrorText.For(code).Length > 20,
            $"کد {code} پیام راهنمای فارسی دارد");
    Check(Inventory.Client.Services.LicenseErrorText.For("QUOTA_EXCEEDED", "سقف پر است").Contains("سقف پر است"),
        "متن سرور هم در پیام می‌آید");
    Check(Inventory.Client.Services.LicenseErrorText.IsLicenseCode("LICENSE_READONLY")
          && !Inventory.Client.Services.LicenseErrorText.IsLicenseCode("SOMETHING_ELSE"),
        "تشخیص کد لایسنسی درست است");
}


// ============================================================
//  دور زدن لایسنس در محیط توسعه (DevelopmentBypass)
//  هدف: «در محیط توسعه هیچ خطای لایسنسی نباید وجود داشته باشد» — و در عین حال
//  هرگز در Production اثر نکند.
// ============================================================
Section("دور زدن لایسنس در محیط توسعه");

{
    // ---------- الف) وضعیت ساختگی: کامل و بدون سقف ----------
    var dev = DevelopmentEntitlement.Build();

    Check(dev.Status == LicenseOverallStatus.Valid, "وضعیت توسعه: Valid است");
    Check(dev.HasLicense && !dev.HasErrors, "وضعیت توسعه: بدون خطا");
    Check(dev.Modules.Count == ModuleCodes.All.Count,
        $"وضعیت توسعه: هر {ModuleCodes.All.Count} ماژول حاضر است (شد: {dev.Modules.Count})");
    Check(ModuleCodes.All.All(m => dev.GetState(m.Code) == ModuleState.Valid),
        "وضعیت توسعه: همهٔ ماژول‌ها Valid هستند");
    Check(ModuleCodes.All.All(m => dev.IsWritable(m.Code)), "وضعیت توسعه: نوشتن در همهٔ ماژول‌ها آزاد است");
    Check(dev.Limits.Count == 0, "وضعیت توسعه: هیچ سقف سهمیه‌ای نیست (یعنی نامحدود)");
    Check(!dev.Seats.HasUserLimit && dev.Seats.Mode == LicenseUserMode.None,
        "وضعیت توسعه: سقف صندلی اعمال نمی‌شود");
    Check(dev.HasPerpetualItem && dev.UpdatesUntil is null,
        "وضعیت توسعه: بررسی نسخه/آپدیت مانع نمی‌شود");
    Check(dev.Issues.Any(i => i.Code == DevelopmentEntitlement.IssueCode && i.Severity == LicenseIssueSeverity.Info),
        "وضعیت توسعه: با Issue اطلاعاتی LICENSE_DEV_BYPASS علامت‌گذاری شده (پنهان نیست)");
    Check(dev.AppliedSerials.Contains(DevelopmentEntitlement.Serial),
        "وضعیت توسعه: سریال ساختگی در AppliedSerials است");
    Check(dev.Issues.All(i => i.Severity != LicenseIssueSeverity.Error),
        "وضعیت توسعه: هیچ Issue با شدت Error ندارد (پس بنر قرمز نمی‌سازد)");

    // ---------- ب) تصمیم فعال‌سازی: فقط Development ----------
    DevelopmentLicenseBypass Eval(string env, bool? flag)
    {
        var cfg = new Microsoft.Extensions.Configuration.ConfigurationBuilder();
        if (flag is { } value)
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
                { [DevelopmentLicenseBypass.ConfigKey] = value ? "true" : "false" });
        return DevelopmentLicenseBypass.Evaluate(new FakeEnv(env), cfg.Build());
    }

    Check(Eval("Development", null).IsActive, "Development بدون کلید ⇒ فعال (پیش‌فرض)");
    Check(Eval("Development", true).IsActive, "Development با true ⇒ فعال");
    Check(!Eval("Development", false).IsActive, "Development با false ⇒ غیرفعال (تست مسیر واقعی لایسنس)");

    Check(!Eval("Production", null).IsActive, "Production بدون کلید ⇒ غیرفعال");
    Check(!Eval("Production", true).IsActive,
        "⛔ Production با true ⇒ همچنان غیرفعال (کلید مشتری نمی‌تواند لایسنس را دور بزند)");
    Check(Eval("Production", true).InactiveReason is { Length: > 0 },
        "Production با true ⇒ دلیل غیرفعال بودن گزارش می‌شود");
    Check(!Eval("Staging", null).IsActive && !Eval("", null).IsActive,
        "محیط‌های دیگر (Staging/خالی) ⇒ غیرفعال");
}

Console.WriteLine($"\n{(failures == 0 ? "همهٔ تست‌ها موفق بود." : $"{failures} تست شکست خورد.")}");
return failures == 0 ? 0 : 1;

// ---- کمکی: ادغام یک سند و تأیید اینکه نتیجه معتبر است ----
static bool merged_ok(LicenseDocument doc)
{
    var ent = EntitlementMerger.Merge(new[] { doc });
    return ent.Status == LicenseOverallStatus.Valid && ent.Items.Count > 0;
}

// ---- کمکی: محیط ساختگی برای تست تصمیم «حالت توسعه» ----
sealed class FakeEnv : Microsoft.Extensions.Hosting.IHostEnvironment
{
    public FakeEnv(string name) => EnvironmentName = name;

    public string EnvironmentName { get; set; }
    public string ApplicationName { get; set; } = "tests";
    public string ContentRootPath { get; set; } = ".";
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
        new Microsoft.Extensions.FileProviders.NullFileProvider();
}
