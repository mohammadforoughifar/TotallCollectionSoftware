using Inventory.Licensing.Entitlement;
using Inventory.Licensing.Metrics;
using Inventory.Licensing.Model;
using Inventory.Licensing.Modules;

namespace Inventory.Licensing.Validation;

/// <summary>زمینهٔ اعتبارسنجی — چیزهایی که سند باید با نصب جاری بخواند.</summary>
public sealed class LicenseValidationContext
{
    /// <summary>شناسهٔ نصب این سرور (برای OnPrem اجباری است).</summary>
    public string? ExpectedInstallationId { get; init; }

    /// <summary>نوع استقرار همین نسخه (از #if SAAS یا تنظیمات نصب).</summary>
    public DeploymentKind? ExpectedDeployment { get; init; }

    /// <summary>زمان مرجع (برای پیام‌های «تاریخ شروع در آینده است»).</summary>
    public DateTime? Now { get; init; }

    /// <summary>سریال‌هایی که قبلاً اعمال شده‌اند — برای تشخیص «قبلاً نصب شده».</summary>
    public IReadOnlyCollection<string>? AppliedSerials { get; init; }
}

/// <summary>
/// اعتبارسنجی ساختاری سند لایسنس.
/// این لایه <b>پس از</b> تأیید امضا اجرا می‌شود و فقط بررسی می‌کند که سند
/// «معنی‌دار و کامل» باشد؛ اگر خطا بدهد، سند اعمال نمی‌شود.
/// </summary>
public static class LicenseValidator
{
    public static List<LicenseIssue> Validate(LicenseDocument? document, LicenseValidationContext context)
    {
        var issues = new List<LicenseIssue>();
        if (document is null)
        {
            issues.Add(Error("DOCUMENT_EMPTY", "محتوای سند لایسنس خالی است."));
            return issues;
        }

        var now = (context.Now ?? DateTime.Now).Date;

        // ---------- سطح سند ----------
        if (document.Version != LicenseFormat.CurrentVersion)
            issues.Add(Error("VERSION_UNSUPPORTED",
                $"نسخهٔ سند لایسنس {document.Version} است؛ این نسخه از نرم‌افزار فقط نسخهٔ {LicenseFormat.CurrentVersion} را می‌شناسد."));

        if (string.IsNullOrWhiteSpace(document.Serial))
            issues.Add(Error("SERIAL_MISSING", "سند لایسنس شمارهٔ سریال ندارد."));

        if (string.IsNullOrWhiteSpace(document.CustomerId))
            issues.Add(Error("CUSTOMER_MISSING", "سند لایسنس شناسهٔ مشتری ندارد."));

        if (context.AppliedSerials is not null && !string.IsNullOrWhiteSpace(document.Serial)
            && context.AppliedSerials.Contains(document.Serial, StringComparer.OrdinalIgnoreCase))
            issues.Add(Error("SERIAL_ALREADY_APPLIED", $"فایل با شمارهٔ سریال «{document.Serial}» قبلاً روی این نصب اعمال شده است."));

        if (context.ExpectedDeployment is { } expected && document.Deployment != expected)
            issues.Add(Error("DEPLOYMENT_MISMATCH",
                expected == DeploymentKind.OnPrem
                    ? "این فایل برای استقرار SaaS صادر شده و روی نصب داخلی (OnPrem) قابل استفاده نیست."
                    : "این فایل برای نصب داخلی (OnPrem) صادر شده و روی سرویس SaaS قابل استفاده نیست."));

        if (document.Deployment == DeploymentKind.OnPrem)
        {
            if (string.IsNullOrWhiteSpace(document.InstallationId))
                issues.Add(Error("INSTALLATION_ID_MISSING", "فایل لایسنس نصب داخلی باید شناسهٔ نصب (installationId) داشته باشد."));
            else if (!string.IsNullOrWhiteSpace(context.ExpectedInstallationId)
                     && !string.Equals(document.InstallationId!.Trim(), context.ExpectedInstallationId!.Trim(), StringComparison.OrdinalIgnoreCase))
                issues.Add(Error("INSTALLATION_MISMATCH",
                    "این فایل برای نصب دیگری صادر شده است. شناسهٔ نصب این سرور با شناسهٔ داخل فایل یکی نیست."));
        }

        if (document.IssuedAt == default)
            issues.Add(Warning("ISSUED_AT_MISSING", "تاریخ صدور سند خالی است."));
        else if (document.IssuedAt.Date > now.AddDays(1))
            issues.Add(Warning("ISSUED_AT_FUTURE", "تاریخ صدور سند در آینده است؛ ساعت سرور را بررسی کنید."));

        // ---------- آیتم‌ها ----------
        if (document.Items.Count == 0)
        {
            issues.Add(Error("ITEMS_EMPTY", "سند لایسنس هیچ آیتمی ندارد."));
            return issues;
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in document.Items)
        {
            var tag = string.IsNullOrWhiteSpace(item.Id) ? "(بی‌شناسه)" : item.Id;

            if (string.IsNullOrWhiteSpace(item.Id))
                issues.Add(Error("ITEM_ID_MISSING", "یکی از آیتم‌های لایسنس شناسه ندارد."));
            else if (!seenIds.Add(item.Id))
                issues.Add(Error("ITEM_ID_DUPLICATE", $"شناسهٔ آیتم «{item.Id}» تکراری است."));

            // ---- scope ----
            if (item.Scope.Count == 0)
                issues.Add(Error("SCOPE_EMPTY", $"آیتم «{tag}» هیچ ماژولی را پوشش نمی‌دهد."));
            else
            {
                foreach (var raw in item.Scope)
                {
                    if (!ModuleCodes.IsKnown(raw))
                        issues.Add(Error("MODULE_UNKNOWN",
                            $"آیتم «{tag}» ماژول ناشناختهٔ «{raw}» دارد. کدهای مجاز در نرم‌افزار ثابت‌اند."));
                }

                var alwaysOnInScope = item.Scope.Where(ModuleCodes.IsAlwaysOn).Select(ModuleCodes.TitleOf).Distinct().ToList();
                if (alwaysOnInScope.Count > 0)
                    issues.Add(Warning("ALWAYS_ON_IN_SCOPE",
                        $"آیتم «{tag}» ماژول‌های همیشه‌فعال ({string.Join("، ", alwaysOnInScope)}) را هم نام برده؛ این‌ها نیازی به لایسنس ندارند و نادیده گرفته می‌شوند."));
            }

            // ---- term ----
            ValidateTerm(item, tag, now, issues);

            // ---- users ----
            if (item.Users.Max < 0)
                issues.Add(Error("USER_MAX_NEGATIVE", $"سقف کاربر آیتم «{tag}» منفی است."));

            if (item.Users.Exempt < 0)
                issues.Add(Error("USER_EXEMPT_NEGATIVE", $"تعداد حساب‌های معاف آیتم «{tag}» منفی است."));

            if (item.Users.Mode != LicenseUserMode.None && item.Users.Max <= 0)
                issues.Add(Error("USER_MAX_REQUIRED", $"آیتم «{tag}» سیاست کاربر دارد ولی سقف آن صفر است."));

            if (item.Users.Exempt > item.Users.Max && item.Users.Mode != LicenseUserMode.None)
                issues.Add(Error("USER_EXEMPT_EXCEEDS_MAX",
                    $"تعداد حساب‌های معاف آیتم «{tag}» ({item.Users.Exempt}) از سقف کاربران آن ({item.Users.Max}) بیشتر است."));

            if (item.Users.Mode == LicenseUserMode.None && item.Users.Exempt > 0)
                issues.Add(Warning("USER_EXEMPT_WITHOUT_POLICY",
                    $"آیتم «{tag}» سیاست کاربر ندارد ولی تعداد معاف تعریف کرده؛ این مقدار نادیده گرفته می‌شود."));

            // ---- quotas ----
            var seenMetrics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var quota in item.Quotas)
            {
                var metric = MetricCodes.Normalize(quota.Metric);
                if (metric.Length == 0)
                {
                    issues.Add(Error("QUOTA_METRIC_MISSING", $"یکی از سهمیه‌های آیتم «{tag}» کد متریک ندارد."));
                    continue;
                }

                if (quota.Limit <= 0)
                    issues.Add(Error("QUOTA_LIMIT_INVALID", $"سقف سهمیهٔ «{quota.Metric}» در آیتم «{tag}» باید بزرگ‌تر از صفر باشد."));

                if (!seenMetrics.Add(metric))
                    issues.Add(Warning("QUOTA_DUPLICATE",
                        $"متریک «{quota.Metric}» در آیتم «{tag}» بیش از یک‌بار تعریف شده؛ در عمل بیشترین سقف اعمال می‌شود."));

                if (quota.Stackable && string.IsNullOrWhiteSpace(quota.PackId))
                    issues.Add(Error("PACK_ID_REQUIRED",
                        $"سهمیهٔ «{quota.Metric}» در آیتم «{tag}» بستهٔ افزایشی است ولی packId ندارد."));
            }

            // ---- متریک متعلق به ماژولِ داخل scope باشد ----
            foreach (var quota in item.Quotas)
            {
                var def = MetricCodes.Find(quota.Metric);
                if (def is null) continue; // ناشناخته — در لایهٔ ادغام هشدار داده می‌شود
                if (item.Scope.Count > 0 && !item.Scope.Any(s => string.Equals(ModuleCodes.Normalize(s), def.Module, StringComparison.OrdinalIgnoreCase)))
                    issues.Add(Warning("QUOTA_MODULE_OUT_OF_SCOPE",
                        $"سهمیهٔ «{quota.Metric}» به ماژول «{ModuleCodes.TitleOf(def.Module)}» تعلق دارد که در scope آیتم «{tag}» نیست؛ سهمیه اعمال می‌شود ولی ماژول ممکن است غیرفعال بماند."));
            }
        }

        // ---------- تعارض سیاست کاربر در همین سند ----------
        var modes = document.Items
            .Where(i => IsActiveNow(i.Term, now) && i.Users.Mode != LicenseUserMode.None)
            .Select(i => i.Users.Mode).Distinct().ToList();
        if (modes.Count > 1)
            issues.Add(Error("USER_POLICY_CONFLICT",
                "این فایل هم «کاربر نام‌دار» و هم «کاربر هم‌زمان» دارد. در یک نصب فقط یکی مجاز است؛ فایل را اصلاح کنید."));

        return issues;
    }

    private static void ValidateTerm(LicenseItem item, string tag, DateTime now, List<LicenseIssue> issues)
    {
        var term = item.Term;

        switch (term.Type)
        {
            case LicenseTermType.Subscription:
            case LicenseTermType.Trial:
                if (term.ValidFrom is null)
                    issues.Add(Error("TERM_VALID_FROM_REQUIRED", $"آیتم «{tag}» از نوع {TypeTitle(term.Type)} است ولی تاریخ شروع ندارد."));
                if (term.ValidUntil is null)
                    issues.Add(Error("TERM_VALID_UNTIL_REQUIRED", $"آیتم «{tag}» از نوع {TypeTitle(term.Type)} است ولی تاریخ پایان ندارد."));
                if (term.ValidFrom is not null && term.ValidUntil is not null && term.ValidUntil.Value.Date < term.ValidFrom.Value.Date)
                    issues.Add(Error("TERM_RANGE_INVALID", $"تاریخ پایان آیتم «{tag}» قبل از تاریخ شروع آن است."));
                if (term.ValidUntil is not null && term.ValidUntil.Value.Date < now)
                    issues.Add(Warning("TERM_ALREADY_EXPIRED", $"آیتم «{tag}» از قبل منقضی شده است (پایان: {term.ValidUntil:yyyy-MM-dd})."));
                if (term.Type == LicenseTermType.Trial && term.ValidFrom is not null && term.ValidUntil is not null
                    && (term.ValidUntil.Value.Date - term.ValidFrom.Value.Date).TotalDays > 180)
                    issues.Add(Warning("TRIAL_TOO_LONG", $"دورهٔ آزمایشی آیتم «{tag}» بیش از ۱۸۰ روز است."));
                if (term.UpdatesUntil is not null)
                    issues.Add(Warning("UPDATES_UNTIL_IGNORED",
                        $"آیتم «{tag}» اشتراکی/آزمایشی است؛ مقدار updatesUntil آن نقشی در مقایسهٔ نسخه ندارد و نادیده گرفته می‌شود."));
                break;

            case LicenseTermType.Perpetual:
                if (term.ValidUntil is not null)
                    issues.Add(Error("TERM_PERPETUAL_HAS_END",
                        $"آیتم «{tag}» دائمی است و نباید تاریخ پایان داشته باشد."));
                if (term.UpdatesUntil is not null && term.ValidFrom is not null && term.UpdatesUntil.Value.Date < term.ValidFrom.Value.Date)
                    issues.Add(Error("TERM_UPDATES_BEFORE_START",
                        $"آخرین تاریخ به‌روزرسانی آیتم «{tag}» قبل از شروع آن است."));
                if (term.UpdatesUntil is null)
                    issues.Add(Warning("TERM_UPDATES_UNLIMITED",
                        $"آیتم دائمی «{tag}» سقف تاریخ نسخه ندارد؛ مشتری همهٔ نسخه‌های آینده را مجاز خواهد بود."));
                break;

            default:
                issues.Add(Error("TERM_TYPE_UNKNOWN", $"نوع مدل زمانی آیتم «{tag}» نامعتبر است."));
                break;
        }
    }

    private static bool IsActiveNow(LicenseTerm term, DateTime now) => term.Type switch
    {
        LicenseTermType.Perpetual => true,
        _ => (term.ValidFrom is null || term.ValidFrom.Value.Date <= now)
             && (term.ValidUntil is null || term.ValidUntil.Value.Date >= now)
    };

    private static string TypeTitle(LicenseTermType type) => type switch
    {
        LicenseTermType.Subscription => "اشتراکی",
        LicenseTermType.Trial => "آزمایشی",
        _ => "دائمی"
    };

    private static LicenseIssue Error(string code, string message) => new(code, LicenseIssueSeverity.Error, message);
    private static LicenseIssue Warning(string code, string message) => new(code, LicenseIssueSeverity.Warning, message);
}
