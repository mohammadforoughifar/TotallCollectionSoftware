// ---------------------------------------------------------------------------
// آزمون «قابلیت‌های تفکیک‌شده» — مثل «نامهٔ وارده» زیر ماژول «اتوماسیون اداری».
//
// چه چیزی را ثابت می‌کند؟
//   ۱) قابلیت با ماژول والد در scope ⇒ سند سالم است.
//   ۲) قابلیت بدون ماژول والد            ⇒ FEATURE_MODULE_OUT_OF_SCOPE (خطا).
//   ۳) کد قابلیت ناشناخته                ⇒ FEATURE_UNKNOWN (خطا).
//   ۴) قابلیت تکراری                     ⇒ FEATURE_DUPLICATE (هشدار، سند معتبر).
//   ۵) در دسترسی مؤثر، قابلیت فقط وقتی مجاز است که ماژول والدش هم مجاز باشد.
//   ۶) سازگاری عقب‌رو: سند قدیمی بدون features ⇒ قابلیت تابع ماژول والد.
//
// اجرا:  dotnet run -c Release --project test/support/feature-guard
// ---------------------------------------------------------------------------
using Inventory.Licensing.Model;
using Inventory.Licensing.Validation;
using Inventory.Licensing.Entitlement;

int fails = 0;
void Check(string name, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) fails++; }

LicenseDocument Doc(List<string> scope, List<string> features) => new()
{
    KeyId = "k", Serial = "S-1", CustomerId = "C1", Deployment = DeploymentKind.OnPrem,
    InstallationId = "inst-1", IssuedAt = DateTime.UtcNow,
    Items = { new LicenseItem { Id = "base", Scope = scope, Features = features,
        Term = new LicenseTerm { Type = LicenseTermType.Subscription, ValidFrom = DateTime.UtcNow.Date, ValidUntil = DateTime.UtcNow.Date.AddYears(1) },
        Users = new LicenseUsers { Mode = LicenseUserMode.Named, Max = 5 } } }
};

var ctx = new LicenseValidationContext { ExpectedInstallationId = "inst-1", ExpectedDeployment = DeploymentKind.OnPrem, Now = DateTime.UtcNow };

var okDoc = Doc(new() { "letters", "sales" }, new() { "letter.incoming" });
var okIssues = LicenseValidator.Validate(okDoc, ctx);
Check("قابلیت با ماژول والد در scope ⇒ بدون خطا", okIssues.Count(i => i.Severity == LicenseIssueSeverity.Error) == 0);

var outOfScope = Doc(new() { "sales" }, new() { "letter.incoming" });
var badIssues = LicenseValidator.Validate(outOfScope, ctx);
Check("قابلیت بدون ماژول والد ⇒ FEATURE_MODULE_OUT_OF_SCOPE", badIssues.Any(i => i.Code == "FEATURE_MODULE_OUT_OF_SCOPE" && i.Severity == LicenseIssueSeverity.Error));

var unknown = Doc(new() { "letters" }, new() { "letter.telegram" });
Check("کد قابلیت ناشناخته ⇒ FEATURE_UNKNOWN", LicenseValidator.Validate(unknown, ctx).Any(i => i.Code == "FEATURE_UNKNOWN"));

var dup = Doc(new() { "letters" }, new() { "letter.incoming", "LETTER.INCOMING" });
Check("قابلیت تکراری ⇒ FEATURE_DUPLICATE (هشدار)", LicenseValidator.Validate(dup, ctx).Any(i => i.Code == "FEATURE_DUPLICATE"));

// Entitlement: تفکیک قابلیت‌ها
var eff = new EffectiveEntitlement();
var ent = EntitlementMerger.Merge(new[] { okDoc });
Check("ادغام: قابلیت تفکیک‌شده در دسترسی مؤثر هست", ent.HasFeature("letter.incoming") && ent.FeaturesRestricted);
Check("ماژول letters مجاز است", ent.IsReadable("letters") && ent.VisibleModules.Contains("letters"));
Check("قابلیت داخلی روی ماژول خارج از scope مجاز نیست", !ent.HasFeature("letter.outgoing"));

// سازگاری عقب‌رو: سند بدون features ⇒ قابلیت تابع ماژول والد
var legacy = Doc(new() { "letters" }, new());
var ent2 = EntitlementMerger.Merge(new[] { legacy });
Check("سند قدیمی بدون features ⇒ نامهٔ وارده مجاز (بک‌ورد)", ent2.HasFeature("letter.incoming") && !ent2.FeaturesRestricted);

Console.WriteLine(fails == 0 ? "ALL PASS" : $"{fails} FAILED");
return fails == 0 ? 0 : 1;
