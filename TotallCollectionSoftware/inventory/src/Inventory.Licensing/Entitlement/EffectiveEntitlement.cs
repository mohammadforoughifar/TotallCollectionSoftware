using Inventory.Licensing.Model;
using Inventory.Licensing.Modules;

namespace Inventory.Licensing.Entitlement;

/// <summary>شدت یک یافته/تعارض.</summary>
public enum LicenseIssueSeverity
{
    /// <summary>اطلاعاتی — فقط برای نمایش.</summary>
    Info = 0,

    /// <summary>هشدار — کار می‌کند، ولی باید بررسی شود (مثلاً متریک ناشناخته).</summary>
    Warning = 1,

    /// <summary>خطا — لایسنس (یا آن آیتم) نباید اعمال شود.</summary>
    Error = 2
}

/// <summary>یک یافتهٔ اعتبارسنجی یا تعارض با پیام فارسی.</summary>
/// <param name="Code">کد ماشین‌خوان (مثل USER_POLICY_CONFLICT).</param>
/// <param name="Severity">شدت.</param>
/// <param name="Message">پیام فارسی برای نمایش مستقیم به کاربر.</param>
public sealed record LicenseIssue(string Code, LicenseIssueSeverity Severity, string Message);

/// <summary>سقف مؤثر یک متریک در یک دوره.</summary>
public sealed class MetricLimit
{
    public required string Module { get; init; }
    public required string Metric { get; init; }
    public required QuotaPeriod Period { get; init; }

    /// <summary>سقف مؤثر (بیشترین سقف بین آیتم‌های معتبر + بسته‌های افزایشی یکتا).</summary>
    public long Limit { get; set; }

    /// <summary>آیتم‌هایی که این سقف از آن‌ها آمده.</summary>
    public List<string> ItemIds { get; set; } = new();

    /// <summary>آیا بستهٔ افزایشی (stackable) در این سقف نقش داشت؟</summary>
    public bool IncludesStackable { get; set; }

    /// <summary>
    /// «بخشودگی گذشته» برای همین متریک در همین دوره — از <c>LicenseQuota.Grandfather</c>.
    /// اگر <b>هر</b> آیتمِ سازندهٔ این سقف آن را روشن کرده باشد، روشن است (بازگشت‌پذیر
    /// به نفع مشتری و بی‌خطر: فقط گذشتهٔ همین دوره را نادیده می‌گیرد).
    /// </summary>
    public bool Grandfather { get; set; }

    /// <summary>کلید این سقف در جدول مصرف: ماژول + متریک + دوره.</summary>
    public string UsageKey => $"{Module}|{Metric}|{Period}";
}

/// <summary>خلاصهٔ یک آیتم پس از ارزیابی (برای صفحهٔ «وضعیت لایسنس»).</summary>
public sealed class ItemEntitlement
{
    public required string ItemId { get; init; }
    public required string Serial { get; init; }
    public required LicenseTermType TermType { get; init; }
    public DateTime? ValidFrom { get; init; }
    public DateTime? ValidUntil { get; init; }
    public DateTime? UpdatesUntil { get; init; }
    public LicenseUserMode UserMode { get; init; }
    public int UserMax { get; init; }
    public int UserExempt { get; init; }
    public LicenseUserScope UserScope { get; init; }
    public IReadOnlyList<string> Modules { get; init; } = Array.Empty<string>();

    /// <summary>هنوز شروع نشده / در جریان / منقضی.</summary>
    public DateTime? FirstExpiry { get; init; }
    public bool IsActive { get; set; }
    public bool IsExpired { get; set; }
    public bool IsNotStarted { get; set; }
}

/// <summary>سیاست کاربرِ مؤثر (پس از ادغام همهٔ آیتم‌ها).</summary>
public sealed class UserSeatPolicy
{
    public LicenseUserMode Mode { get; init; } = LicenseUserMode.None;

    /// <summary>سقف سراسری (آیتم‌های appliesTo = Global).</summary>
    public int? GlobalMax { get; init; }

    /// <summary>سقف به‌ازای ماژول (آیتم‌های appliesTo = Modules).</summary>
    public IReadOnlyDictionary<string, int> ModuleMax { get; init; } = new Dictionary<string, int>();

    /// <summary>حداکثر تعداد حساب معاف از صندلی — از سند امضاشده.</summary>
    public int MaxExempt { get; init; }

    public bool HasUserLimit => Mode != LicenseUserMode.None;

    /// <summary>
    /// سقف مؤثر برای کاربری که به این ماژول‌ها دسترسی دارد.
    /// اگر هم سقف سراسری و هم سقف ماژولی باشد، سقف بزرگ‌تر ملاک است؛ ولی چون هر دو از
    /// یک سیاست می‌آیند و فقط دامنه‌شان فرق دارد، سقف هر ماژول جداگانه هم گزارش می‌شود.
    /// </summary>
    public int? MaxForModules(IEnumerable<string> modules)
    {
        var list = modules.Select(ModuleCodes.Normalize).Where(m => m.Length > 0).ToList();
        var best = GlobalMax;
        foreach (var m in list)
            if (ModuleMax.TryGetValue(m, out var v) && (best is null || v > best)) best = v;
        return best;
    }
}

/// <summary>وضعیت کلی نصب.</summary>
public enum LicenseOverallStatus
{
    /// <summary>هیچ سندی اعمال نشده — فقط ورود مدیر و صفحهٔ فعال‌سازی.</summary>
    Missing = 0,

    /// <summary>سند وجود دارد ولی معتبر نیست (امضا/ساختار/InstallationId).</summary>
    Invalid = 1,

    /// <summary>دست‌کم یک سند معتبر وجود دارد.</summary>
    Valid = 2
}

/// <summary>
/// Entitlement مؤثر — نتیجهٔ ادغام همهٔ اسناد معتبر.
/// <b>همهٔ</b> بررسی‌ها (سهمیه، کاربر، آپدیت، اعتبار ماژول) باید روی همین شیء انجام شوند،
/// نه روی یک سند تنها.
/// </summary>
public sealed class EffectiveEntitlement
{
    public LicenseOverallStatus Status { get; init; } = LicenseOverallStatus.Missing;

    public string? CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public DeploymentKind? Deployment { get; init; }
    public DateTime ComputedAt { get; init; } = DateTime.Now;

    /// <summary>همهٔ آیتم‌ها (فعال، منقضی و شروع‌نشده).</summary>
    public IReadOnlyList<ItemEntitlement> Items { get; init; } = Array.Empty<ItemEntitlement>();

    /// <summary>وضعیت هر ماژول.</summary>
    public IReadOnlyDictionary<string, ModuleEntitlement> Modules { get; init; } =
        new Dictionary<string, ModuleEntitlement>(StringComparer.OrdinalIgnoreCase);

    /// <summary>سقف‌های مؤثر، به تفکیک (ماژول، متریک، دوره).</summary>
    public IReadOnlyList<MetricLimit> Limits { get; init; } = Array.Empty<MetricLimit>();

    public UserSeatPolicy Seats { get; init; } = new();

    public IReadOnlyList<LicenseIssue> Issues { get; init; } = Array.Empty<LicenseIssue>();

    /// <summary>آخرین نسخهٔ مجاز برای آیتم‌های دائمی؛ null = نامحدود (یا هیچ آیتم دائمی نیست).</summary>
    public DateTime? UpdatesUntil { get; init; }

    /// <summary>آیا دست‌کم یک آیتم دائمی وجود دارد؟ (تنها این‌ها Build را محدود می‌کنند)</summary>
    public bool HasPerpetualItem { get; init; }

    /// <summary>سریال اسنادی که در این ادغام شرکت کردند.</summary>
    public IReadOnlyList<string> AppliedSerials { get; init; } = Array.Empty<string>();

    public bool HasLicense => Status == LicenseOverallStatus.Valid;

    public bool HasErrors => Issues.Any(i => i.Severity == LicenseIssueSeverity.Error);

    /// <summary>وضعیت یک ماژول؛ ماژول همیشه‌فعال همیشه Valid است.</summary>
    public ModuleState GetState(string module)
    {
        if (ModuleCodes.IsAlwaysOn(module)) return ModuleState.Valid;
        return Modules.TryGetValue(ModuleCodes.Normalize(module), out var m) ? m.State : ModuleState.Disabled;
    }

    /// <summary>آیا این ماژول اطلاعات نمایشی دارد؟</summary>
    public ModuleEntitlement? FindModule(string module)
        => Modules.TryGetValue(ModuleCodes.Normalize(module), out var m) ? m : null;

    /// <summary>آیا عملیات نوشتن روی این ماژول مجاز است؟</summary>
    public bool IsWritable(string module) => GetState(module) is ModuleState.Valid or ModuleState.ExpiringSoon or ModuleState.Grace;

    /// <summary>آیا مشاهدهٔ این ماژول مجاز است؟</summary>
    public bool IsReadable(string module) => GetState(module) != ModuleState.Disabled;

    /// <summary>سقف‌های یک متریک روی یک ماژول (ممکن است چند دوره داشته باشد).</summary>
    public IReadOnlyList<MetricLimit> LimitsFor(string module, string metric)
    {
        var mod = ModuleCodes.Normalize(module);
        var met = Metrics.MetricCodes.Normalize(metric);
        return Limits.Where(l => string.Equals(l.Module, mod, StringComparison.OrdinalIgnoreCase)
                                 && string.Equals(l.Metric, met, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// آیا نسخه‌ای با تاریخ Build داده‌شده مجاز است؟
    /// آیتم‌های اشتراکی به نسخه وابسته نیستند؛ فقط <c>updatesUntil</c> آیتم‌های دائمی ملاک است.
    /// </summary>
    public bool AllowsBuild(DateTime buildDate)
    {
        if (!HasPerpetualItem) return true;          // فقط اشتراکی ⇒ وابستگی به نسخه ندارد
        if (UpdatesUntil is null) return true;       // آیتم دائمی بدون سقف نسخه
        return buildDate.Date <= UpdatesUntil.Value.Date;
    }

    /// <summary>ماژول‌هایی که وضعیت‌شان اجازهٔ نمایش منو را می‌دهد.</summary>
    public IEnumerable<string> VisibleModules => Modules.Where(kv => kv.Value.IsReadable).Select(kv => kv.Key);
}
