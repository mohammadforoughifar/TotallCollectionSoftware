using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Inventory.Licensing.Crypto;
using Inventory.Licensing.Entitlement;
using Inventory.Licensing.Metrics;
using Inventory.Licensing.Model;
using Inventory.Licensing.Modules;
using Inventory.Licensing.Periods;
using Inventory.Licensing.Validation;

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
    LicenseQuota[]? quotas = null, bool carryOver = false) => new()
{
    Id = id,
    Scope = scope.ToList(),
    Term = term,
    Users = users ?? new LicenseUsers(),
    Quotas = quotas?.ToList() ?? new List<LicenseQuota>(),
    CarryOverUsage = carryOver
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
Console.WriteLine($"\n{(failures == 0 ? "همهٔ تست‌ها موفق بود." : $"{failures} تست شکست خورد.")}");
return failures == 0 ? 0 : 1;
