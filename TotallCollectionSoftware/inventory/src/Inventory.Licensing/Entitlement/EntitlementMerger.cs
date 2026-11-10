using Inventory.Licensing.Metrics;
using Inventory.Licensing.Model;
using Inventory.Licensing.Modules;

namespace Inventory.Licensing.Entitlement;

/// <summary>
/// قواعد زمانی — <b>ثابت در کد</b> (نه appsettings).
/// اگر از تنظیمات خوانده می‌شد، کسی می‌توانست «۳۰ روز» را به ۳۰۰۰ روز تغییر دهد و
/// هشدار انقضا را بی‌اثر کند.
/// </summary>
public static class LicenseTimingRules
{
    /// <summary>پنجرهٔ هشدار پیش از انقضا.</summary>
    public const int ExpiringSoonDays = 30;

    /// <summary>مهلت تنفس پس از انقضا که در آن ماژول هنوز کامل کار می‌کند.</summary>
    public const int GraceDays = 15;
}

/// <summary>
/// ادغام چند سند لایسنس به یک Entitlement مؤثر.
///
/// <para>قواعد (طبق سند کارفرما):</para>
/// <list type="bullet">
/// <item>ماژول مجاز است اگر <b>حداقل یک آیتم معتبر</b> آن را پوشش دهد.</item>
/// <item>اعتبار ماژول = <b>دورترین</b> انقضای آیتم‌های معتبر آن (دائمی = بدون انقضا).</item>
/// <item>سقف هر متریک = <b>بیشترین</b> سقف بین آیتم‌های معتبر؛ جمع نمی‌شود —
/// مگر آیتم <c>stackable</c> با <c>packId</c> یکتا که فقط یک‌بار اعمال می‌شود.</item>
/// <item>آیتم منقضی سقفی نمی‌دهد، ولی مصرف ثبت‌شده‌اش در دیتابیس می‌ماند (اینجا کاری با مصرف نداریم).</item>
/// <item>برای Build از بیشترین <c>updatesUntil</c> بین آیتم‌های <b>دائمی</b> استفاده می‌شود.</item>
/// </list>
///
/// <para>
/// این کلاس خالص است: نه دیتابیس، نه ساعت سیستم (زمان به‌عنوان پارامتر می‌آید) — تا در
/// تست بتوان ساعت را جابه‌جا کرد و نتیجه تکرارپذیر باشد.
/// </para>
/// </summary>
public static class EntitlementMerger
{
    /// <summary>
    /// ادغام اسناد. ورودی باید <b>فقط اسناد تأییدشده</b> باشد (امضا و ساختار).
    /// </summary>
    /// <param name="documents">اسناد اعمال‌شده (ممکن است خالی باشد).</param>
    /// <param name="now">زمان مرجع محاسبه (پیش‌فرض: اکنون).</param>
    public static EffectiveEntitlement Merge(IReadOnlyCollection<LicenseDocument>? documents, DateTime? now = null)
    {
        var at = (now ?? DateTime.Now).Date;
        if (documents is null || documents.Count == 0)
            return new EffectiveEntitlement
            {
                Status = LicenseOverallStatus.Missing,
                ComputedAt = at,
                Issues = new[] { new LicenseIssue("LICENSE_MISSING", LicenseIssueSeverity.Error, "هیچ فایل لایسنسی روی این نصب اعمال نشده است.") }
            };

        var issues = new List<LicenseIssue>();

        // ---------- سازگاری بین اسناد ----------
        var customers = documents.Select(d => d.CustomerId).Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (customers.Count > 1)
            issues.Add(new LicenseIssue("CUSTOMER_MISMATCH", LicenseIssueSeverity.Error,
                $"فایل‌های لایسنس به مشتریان مختلف تعلق دارند ({string.Join("، ", customers)}). همهٔ فایل‌ها باید مال یک مشتری باشند."));

        var deployments = documents.Select(d => d.Deployment).Distinct().ToList();
        if (deployments.Count > 1)
            issues.Add(new LicenseIssue("DEPLOYMENT_MISMATCH", LicenseIssueSeverity.Error,
                "فایل‌های لایسنس هم‌زمان OnPrem و SaaS هستند و نمی‌توانند روی یک نصب اعمال شوند."));

        // ---------- ارزیابی آیتم‌ها ----------
        var items = new List<ItemEntitlement>();
        var quotaSources = new List<(ItemEntitlement Entity, LicenseItem Source)>();
        var moduleItems = new Dictionary<string, List<ItemEntitlement>>(StringComparer.OrdinalIgnoreCase);

        foreach (var doc in documents)
        {
            foreach (var item in doc.Items)
            {
                var (state, from, until) = EvaluateTerm(item.Term, at);

                var modules = item.Scope
                    .Select(ModuleCodes.Normalize)
                    .Where(c => c.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var entity = new ItemEntitlement
                {
                    ItemId = item.Id,
                    Serial = doc.Serial,
                    TermType = item.Term.Type,
                    ValidFrom = from,
                    ValidUntil = until,
                    UpdatesUntil = item.Term.Type == LicenseTermType.Perpetual ? item.Term.UpdatesUntil : null,
                    UserMode = item.Users.Mode,
                    UserMax = item.Users.Max,
                    UserExempt = item.Users.Exempt,
                    UserScope = item.Users.AppliesTo,
                    Modules = modules,
                    CarryOverUsage = item.CarryOverUsage,
                    FirstExpiry = until,
                    IsActive = state == TermState.Active,
                    IsExpired = state == TermState.Expired,
                    IsNotStarted = state == TermState.NotStarted
                };
                items.Add(entity);
                quotaSources.Add((entity, item));

                foreach (var code in modules)
                {
                    if (!moduleItems.TryGetValue(code, out var list))
                        moduleItems[code] = list = new List<ItemEntitlement>();
                    list.Add(entity);
                }

                if (item.Users.Mode != LicenseUserMode.None && item.Users.Max <= 0)
                    issues.Add(new LicenseIssue("USER_MAX_INVALID", LicenseIssueSeverity.Warning,
                        $"آیتم «{item.Id}» سیاست کاربر دارد ولی سقف آن صفر است؛ این آیتم هیچ کاربری نمی‌پذیرد."));
            }
        }

        // ---------- وضعیت ماژول‌ها ----------
        var modules = new Dictionary<string, ModuleEntitlement>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, covering) in moduleItems)
        {
            var active = covering.Where(i => i.IsActive).ToList();

            if (active.Count > 0)
            {
                // دورترین انقضا؛ null (دائمی) بر همه چیره است
                var hasPerpetual = active.Any(i => i.ValidUntil is null);
                DateTime? farthest = hasPerpetual ? null : active.Max(i => i.ValidUntil);
                var primary = hasPerpetual
                    ? active.First(i => i.ValidUntil is null)
                    : active.OrderByDescending(i => i.ValidUntil).First();

                var soon = farthest is not null
                           && (farthest.Value.Date - at).TotalDays <= LicenseTimingRules.ExpiringSoonDays;

                modules[code] = new ModuleEntitlement
                {
                    Module = code,
                    State = soon ? ModuleState.ExpiringSoon : ModuleState.Valid,
                    ExpiresAt = farthest,
                    PrimaryItemId = primary.ItemId,
                    PrimaryTermType = primary.TermType.ToString(),
                    ItemIds = covering.Select(i => i.ItemId).Distinct().ToList(),
                    IsAlwaysOn = ModuleCodes.IsAlwaysOn(code)
                };
            }
            else
            {
                // هیچ آیتم فعالی نیست: مهلت تنفس، سپس فقط‌خواندنی
                var expired = covering.Where(i => i.IsExpired && i.ValidUntil is not null).ToList();
                DateTime? lastExpiry = expired.Count == 0 ? null : expired.Max(i => i.ValidUntil);

                var state = ModuleState.Disabled;
                if (lastExpiry is not null)
                {
                    var daysPast = (at - lastExpiry.Value.Date).TotalDays;
                    state = daysPast <= LicenseTimingRules.GraceDays ? ModuleState.Grace : ModuleState.ReadOnly;
                }

                modules[code] = new ModuleEntitlement
                {
                    Module = code,
                    State = state,
                    ExpiresAt = lastExpiry,
                    PrimaryItemId = covering.OrderByDescending(i => i.ValidUntil).FirstOrDefault()?.ItemId,
                    PrimaryTermType = covering.FirstOrDefault()?.TermType.ToString(),
                    ItemIds = covering.Select(i => i.ItemId).Distinct().ToList(),
                    IsAlwaysOn = ModuleCodes.IsAlwaysOn(code)
                };
            }
        }

        // ---------- سقف متریک‌ها ----------
        var limits = BuildLimits(quotaSources, issues);

        // ---------- سیاست کاربر ----------
        var seats = BuildSeats(items, issues);

        // ---------- نسخه و آیتم دائمی ----------
        var perpetual = items.Where(i => i.TermType == LicenseTermType.Perpetual).ToList();
        var hasPerpetual = perpetual.Count > 0;
        DateTime? updatesUntil = null;
        if (hasPerpetual && perpetual.Any(p => p.UpdatesUntil is not null))
            updatesUntil = perpetual.Where(p => p.UpdatesUntil is not null).Max(p => p.UpdatesUntil);

        // ماژول‌های همیشه‌فعال باید در همهٔ حالت‌ها Valid باشند
        foreach (var alwaysOn in ModuleCodes.AlwaysOnCodes)
            modules[alwaysOn] = new ModuleEntitlement { Module = alwaysOn, State = ModuleState.Valid, IsAlwaysOn = true };

        return new EffectiveEntitlement
        {
            Status = documents.Count > 0 ? LicenseOverallStatus.Valid : LicenseOverallStatus.Invalid,
            CustomerId = documents.Select(d => d.CustomerId).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)),
            CustomerName = documents.Select(d => d.CustomerName).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)),
            Deployment = deployments.Count == 1 ? deployments[0] : null,
            ComputedAt = at,
            Items = items,
            Modules = modules,
            Limits = limits,
            Seats = seats,
            Issues = issues,
            UpdatesUntil = updatesUntil,
            HasPerpetualItem = hasPerpetual,
            AppliedSerials = documents.Select(d => d.Serial).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList()
        };
    }

    // ==================== کمکی‌ها ====================

    private enum TermState { NotStarted, Active, Expired }

    /// <summary>
    /// ارزیابی بازهٔ زمانی یک آیتم.
    /// پایان اعتبار «روز کامل» است: تا ۲۳:۵۹:۵۹ همان روز معتبر می‌ماند — این همان
    /// انتظار کاربر است («تا تاریخ ۱۴۰۵/۰۸/۳۰» یعنی کل آن روز).
    /// </summary>
    private static (TermState State, DateTime? From, DateTime? Until) EvaluateTerm(LicenseTerm term, DateTime at)
    {
        if (term.Type == LicenseTermType.Perpetual)
            return (TermState.Active, term.ValidFrom?.Date, null);

        var from = term.ValidFrom?.Date;
        var until = term.ValidUntil?.Date;

        if (from is not null && at < from.Value) return (TermState.NotStarted, from, until);
        if (until is not null && at > until.Value) return (TermState.Expired, from, until);
        return (TermState.Active, from, until);
    }

    /// <summary>
    /// سقف مؤثر هر متریک:
    /// <list type="number">
    /// <item>پایه = بیشترین سقف بین آیتم‌های معتبرِ <b>غیر stackable</b> (جمع نمی‌شود).</item>
    /// <item>هر <c>packId</c> یکتا از آیتم‌های stackable فقط <b>یک‌بار</b> اضافه می‌شود
    /// (اگر دو آیتم همان packId را داشتند، بزرگ‌ترینشان حساب می‌شود — نه جمعشان).</item>
    /// </list>
    /// آیتم منقضی هیچ سقفی نمی‌دهد؛ آیتم شروع‌نشده هم همین‌طور.
    /// </summary>
    private static List<MetricLimit> BuildLimits(
        List<(ItemEntitlement Entity, LicenseItem Source)> sources,
        List<LicenseIssue> issues)
    {
        var groups = new Dictionary<(string Module, string Metric, QuotaPeriod Period), List<(ItemEntitlement Entity, LicenseQuota Quota)>>();

        foreach (var (entity, source) in sources.Where(s => s.Entity.IsActive))
        {
            foreach (var quota in source.Quotas)
            {
                var metric = MetricCodes.Normalize(quota.Metric);
                if (metric.Length == 0) continue;

                if (!MetricCodes.IsKnown(metric))
                    issues.Add(new LicenseIssue("UNKNOWN_METRIC", LicenseIssueSeverity.Warning,
                        $"متریک «{quota.Metric}» در این نسخهٔ نرم‌افزار شناخته‌شده نیست و سهمیهٔ آن اعمال نمی‌شود؛ " +
                        "برای استفاده، نسخهٔ نرم‌افزار را به‌روز کنید."));

                if (quota.Stackable && string.IsNullOrWhiteSpace(quota.PackId))
                    issues.Add(new LicenseIssue("PACK_ID_REQUIRED", LicenseIssueSeverity.Warning,
                        $"سهمیهٔ «{quota.Metric}» در آیتم «{source.Id}» به‌عنوان بستهٔ افزایشی علامت خورده ولی packId ندارد؛ " +
                        "مثل سقف معمولی (بیشترین سقف) رفتار می‌شود."));

                var module = MetricCodes.ModuleOf(metric);
                var key = (Module: module, Metric: metric, Period: quota.Period);
                if (!groups.TryGetValue(key, out var list))
                    groups[key] = list = new List<(ItemEntitlement, LicenseQuota)>();
                list.Add((entity, quota));
            }
        }

        var result = new List<MetricLimit>(groups.Count);
        foreach (var (key, entries) in groups)
        {
            var normal = entries.Where(e => !e.Quota.Stackable).Select(e => e.Quota.Limit).DefaultIfEmpty(0).Max();

            var stackableGroups = entries
                .Where(e => e.Quota.Stackable && !string.IsNullOrWhiteSpace(e.Quota.PackId))
                .GroupBy(e => e.Quota.PackId!.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Max(e => e.Quota.Limit))
                .DefaultIfEmpty(0)
                .Sum();

            // بستهٔ افزایشی بدون packId با سقف معمولی جمع نمی‌شود؛ در «بیشترین» شرکت داده می‌شود
            var stackableWithoutPack = entries
                .Where(e => e.Quota.Stackable && string.IsNullOrWhiteSpace(e.Quota.PackId))
                .Select(e => e.Quota.Limit)
                .DefaultIfEmpty(0)
                .Max();

            var effective = Math.Max(normal, stackableWithoutPack) + stackableGroups;

            result.Add(new MetricLimit
            {
                Module = key.Module,
                Metric = key.Metric,
                Period = key.Period,
                Limit = effective,
                ItemIds = entries.Select(e => e.Entity.ItemId).Distinct().ToList(),
                IncludesStackable = stackableGroups > 0
            });
        }

        return result.OrderBy(l => l.Module).ThenBy(l => l.Metric).ThenBy(l => l.Period).ToList();
    }

    /// <summary>سیاست کاربر مؤثر + تشخیص تعارض.</summary>
    private static UserSeatPolicy BuildSeats(List<ItemEntitlement> items, List<LicenseIssue> issues)
    {
        var withPolicy = items.Where(i => i.IsActive && i.UserMode != LicenseUserMode.None).ToList();
        var modes = withPolicy.Select(i => i.UserMode).Distinct().ToList();

        if (modes.Count > 1)
        {
            var named = withPolicy.Where(i => i.UserMode == LicenseUserMode.Named).Sum(i => i.UserMax);
            var concurrent = withPolicy.Where(i => i.UserMode == LicenseUserMode.Concurrent).Sum(i => i.UserMax);
            issues.Add(new LicenseIssue("USER_POLICY_CONFLICT", LicenseIssueSeverity.Error,
                "تعارض سیاست کاربر: در یک نصب فقط یکی از «کاربر نام‌دار» یا «کاربر هم‌زمان» مجاز است؛ " +
                $"این لایسنس هر دو را دارد (نام‌دار: {named} نفر، هم‌زمان: {concurrent} نفر). " +
                "فایل را با سیاست واحد اصلاح کنید."));
        }

        var mode = modes.Count == 1 ? modes[0] : LicenseUserMode.None;
        if (mode == LicenseUserMode.None)
            return new UserSeatPolicy();

        var globals = withPolicy.Where(i => i.UserMode == mode && i.UserScope == LicenseUserScope.Global).ToList();
        int? globalMax = globals.Count > 0 ? globals.Max(i => i.UserMax) : null;

        var moduleMax = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in withPolicy.Where(i => i.UserMode == mode && i.UserScope == LicenseUserScope.Modules))
            foreach (var m in item.Modules)
                moduleMax[m] = moduleMax.TryGetValue(m, out var current) ? Math.Max(current, item.UserMax) : item.UserMax;

        return new UserSeatPolicy
        {
            Mode = mode,
            GlobalMax = globalMax,
            ModuleMax = moduleMax,
            MaxExempt = withPolicy.Max(i => i.UserExempt)
        };
    }
}
