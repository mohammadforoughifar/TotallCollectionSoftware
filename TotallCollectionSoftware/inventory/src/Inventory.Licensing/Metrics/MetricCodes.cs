using Inventory.Licensing.Modules;

namespace Inventory.Licensing.Metrics;

/// <summary>
/// نوع شمارش یک متریک.
/// </summary>
public enum MetricKind
{
    /// <summary>شمارنده: هر ثبت، یک واحد مصرف می‌کند (پیش‌فرض).</summary>
    Counter = 0,

    /// <summary>
    /// «تعداد فعال»: ملاک، تعداد واقعی رکوردهای موجود است، نه تعداد ثبت‌ها.
    /// مصرف مؤثر = بیشترینِ (شمارنده، تعداد واقعی ردیف‌ها) — چون رکوردهایی که قبل از
    /// فعال‌شدن سامانهٔ لایسنس ساخته شده‌اند هم باید شمرده شوند.
    /// </summary>
    ActiveCount = 1
}

/// <summary>
/// تعریف یک متریک قابل سهمیه.
/// </summary>
/// <param name="Code">کد پایدار در فایل لایسنس.</param>
/// <param name="Module">ماژولی که این متریک به آن تعلق دارد (مبنای پیدا کردن سقف).</param>
/// <param name="Title">نام فارسی برای UI.</param>
/// <param name="Unit">واحد شمارش برای نمایش («فاکتور»، «نامه»، …).</param>
/// <param name="IsAggregate">
/// متریک تجمیعی: خودش سهمیهٔ مستقل ندارد، بلکه جمع چند متریک دیگر است.
/// مصرف هر متریک عضو، این متریک را هم مصرف می‌کند.
/// </param>
/// <param name="Components">متریک‌های عضو (برای تجمیعی).</param>
public sealed record MetricDefinition(
    string Code,
    string Module,
    string Title,
    string Unit,
    bool IsAggregate = false,
    IReadOnlyList<string>? Components = null,
    MetricKind Kind = MetricKind.Counter);

/// <summary>
/// رجیستری متریک‌ها — منبع واحد حقیقت.
///
/// <para>
/// افزودن متریک تازه فقط یک ردیف در <see cref="All"/> و یک فراخوانی
/// <c>QuotaGuard.EnsureAsync</c> در نقطهٔ ثبت رکورد است. هیچ جای دیگری
/// لازم نیست عوض شود (نه سوییچ، نه if، نه تنظیمات).
/// </para>
///
/// <para>
/// ⚠️ این فهرست در کد است؛ متریکی که در لایسنس آمده ولی اینجا ثبت نشده باشد
/// «ناشناخته» است و فقط به‌عنوان هشدار گزارش می‌شود (نه خطا) تا لایسنس معتبر بماند.
/// </para>
/// </summary>
public static class MetricCodes
{
    // ---------- صورت‌حساب و فروش ----------
    public const string InvoiceCreate = "invoice.create";
    public const string MoadianInvoiceSend = "moadian.invoice.send";

    // ---------- نامه‌ها ----------
    public const string LetterInnerCreate = "letter.inner.create";
    public const string LetterOutgoingCreate = "letter.outgoing.create";
    public const string LetterIncomingCreate = "letter.incoming.create";

    /// <summary>متریک تجمیعی هر سه نوع نامه — «نامه با سقف ماهانه» روی این بسته می‌شود.</summary>
    public const string LetterCreate = "letter.create";

    /// <summary>همهٔ متریک‌های ثبت‌شده.</summary>
    public static readonly IReadOnlyList<MetricDefinition> All = new List<MetricDefinition>
    {
        new(InvoiceCreate, ModuleCodes.Invoicing, "صدور صورتحساب", "فاکتور"),

        new(MoadianInvoiceSend, ModuleCodes.Moadian, "ارسال صورتحساب به مودیان", "صورتحساب"),

        new(LetterInnerCreate,    ModuleCodes.Letters, "ثبت نامهٔ داخلی", "نامه"),
        new(LetterOutgoingCreate, ModuleCodes.Letters, "ثبت نامهٔ صادره", "نامه"),
        new(LetterIncomingCreate, ModuleCodes.Letters, "ثبت نامهٔ وارده", "نامه"),

        new(LetterCreate, ModuleCodes.Letters, "ثبت نامه (همهٔ انواع)", "نامه",
            IsAggregate: true,
            Components: new[] { LetterInnerCreate, LetterOutgoingCreate, LetterIncomingCreate })
    };

    /// <summary>تعریف یک متریک؛ برای کد ناشناس null.</summary>
    public static MetricDefinition? Find(string? metric)
    {
        var key = Normalize(metric);
        return All.FirstOrDefault(m => string.Equals(m.Code, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>آیا کد در رجیستری ثبت شده است؟</summary>
    public static bool IsKnown(string? metric) => Find(metric) is not null;

    /// <summary>
    /// ماژولِ صاحب متریک. برای کد ناشناس، پیشوند قبل از نقطه به‌عنوان حدس برگردانده
    /// می‌شود تا رفتار سیستم «رد کردن» نباشد.
    /// </summary>
    public static string ModuleOf(string? metric)
    {
        var def = Find(metric);
        if (def is not null) return def.Module;
        var key = Normalize(metric);
        var dot = key.IndexOf('.');
        return dot > 0 ? key[..dot] : key;
    }

    /// <summary>نام فارسی متریک.</summary>
    public static string TitleOf(string? metric) => Find(metric)?.Title ?? Normalize(metric);

    /// <summary>واحد شمارش متریک.</summary>
    public static string UnitOf(string? metric) => Find(metric)?.Unit ?? "مورد";

    /// <summary>
    /// متریک‌هایی که مصرف یک رکورد باید هم‌زمان بشمارد:
    /// خودِ متریک + هر متریک تجمیعی که آن را در بر می‌گیرد.
    /// مثال: ثبت نامهٔ داخلی ⇒ { letter.inner.create, letter.create }.
    /// </summary>
    public static IReadOnlyList<string> ConsumeTargets(string? metric)
    {
        var key = Normalize(metric);
        if (key.Length == 0) return Array.Empty<string>();

        var targets = new List<string> { key };
        foreach (var aggregate in All.Where(m => m.IsAggregate && m.Components is not null))
        {
            if (aggregate.Components!.Any(c => string.Equals(c, key, StringComparison.OrdinalIgnoreCase))
                && !targets.Contains(aggregate.Code, StringComparer.OrdinalIgnoreCase))
                targets.Add(aggregate.Code);
        }
        return targets;
    }

    /// <summary>نام‌گذاری متعارف: حروف کوچک، بدون فاصلهٔ اضافه.</summary>
    public static string Normalize(string? metric) => (metric ?? "").Trim().ToLowerInvariant();
}
