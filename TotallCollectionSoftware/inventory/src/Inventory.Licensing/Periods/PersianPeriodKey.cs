using System.Globalization;
using Inventory.Licensing.Model;

namespace Inventory.Licensing.Periods;

/// <summary>
/// کلید دورهٔ سهمیه بر اساس <b>تقویم شمسی</b>.
///
/// <para>
/// چرا شمسی؟ چون همهٔ گزارش‌ها و انتظار مشتری ایرانی بر پایهٔ ماه و سال شمسی است؛
/// «سقف ۲۰۰۰ نامه در ماه» یعنی ماه شمسی، نه ماه میلادی. اگر کلید میلادی بود،
/// سهمیه در میانهٔ ماه شمسی ریست می‌شد و برای کاربر قابل توضیح نبود.
/// </para>
///
/// <para>
/// شکل کلیدها: <c>-</c> برای Lifetime، <c>1405-08</c> برای ماهانه و <c>1405</c> برای سالانه.
/// این کلید در جدول <c>LicenseUsage.PeriodKey</c> ذخیره می‌شود و بخشی از کلید یکتای
/// (ماژول، متریک، دوره) است.
/// </para>
/// </summary>
public static class PersianPeriodKey
{
    /// <summary>کلید دورهٔ «یک‌بار برای همیشه».</summary>
    public const string Lifetime = "-";

    /// <summary>تقویم شمسی — یک نمونهٔ مشترک (PersianCalendar تغییرناپذیر است).</summary>
    private static readonly PersianCalendar Calendar = new();

    /// <summary>
    /// منطقهٔ زمانی ایران — <b>مبنای قطعی</b> محاسبهٔ دوره.
    ///
    /// <para>
    /// چرا صریح؟ چون سرور می‌تواند UTC باشد، وقت محلی اروپا باشد یا هر چیز دیگر؛
    /// «ماه شمسی» باید همیشه بر اساس ساعت دیوارِ تهران حساب شود. نمونهٔ واقعی:
    /// ساعت ۰۰:۳۰ بامداد ۱ آذر به وقت تهران = ۲۱:۰۰ روز ۳۰ آبان به وقت UTC.
    /// اگر مبنای محاسبه UTC باشد، مصرف روز اول ماه تازه در سهمیهٔ ماه قبل می‌افتد.
    /// </para>
    ///
    /// <para>
    /// از سال ۱۴۰۱ ساعت تابستانی در ایران حذف شده و افست ثابت +۰۳:۳۰ است؛ ولی
    /// از base zone استفاده می‌کنیم تا اگر روزی برگشت، خودکار درست بماند. اگر
    /// دیتابیس منطقهٔ زمانی نصب نبود، افست ثابت +۰۳:۳۰ جایگزین می‌شود.
    /// </para>
    /// </summary>
    // ⚠️ ترتیب مهم است: این دو **پیش از** IranZone تعریف شده‌اند، چون
    // مقدارگیری فیلدهای static از بالا به پایین است و ResolveIranZone به هر دو نیاز دارد.
    /// <summary>شناسه‌های منطقه‌زمانی ایران به ترتیب تلاش (لینوکس، سپس ویندوز).</summary>
    public static readonly string[] IranZoneIds = { "Asia/Tehran", "Iran Standard Time" };

    /// <summary>افست ثابت جایگزین — <b>+03:30</b>، نه UTC.</summary>
    public static readonly TimeSpan IranFallbackOffset = TimeSpan.FromMinutes(210);

    public static readonly TimeZoneInfo IranZone = ResolveIranZone();

    private static TimeZoneInfo ResolveIranZone() => ResolveZone(FindZone);

    /// <summary>
    /// حل منطقهٔ زمانی ایران با یک تابع جست‌وجوی تزریق‌شدنی (برای تست).
    ///
    /// <para>
    /// ⚠️ <b>چرا جایگزین +03:30 و نه UTC؟</b> اگر دیتابیس منطقه‌زمانی سیستم ناقص باشد
    /// (کانتینرهای لاغر، ویندوز بدون tzdata)، افتادن به UTC یعنی ساعت تهران ۳:۳۰ عقب
    /// می‌رود و مرز ماه شمسی، انقضا و مهلت تنفس همه جابه‌جا می‌شوند. ایران از ۱۴۰۱
    /// ساعت تابستانی ندارد، پس +03:30 ثابت <b>امروز دقیقاً درست</b> است؛ UTC هیچ‌وقت
    /// درست نیست.
    /// </para>
    /// </summary>
    public static TimeZoneInfo ResolveZone(Func<string, TimeZoneInfo?> find)
    {
        foreach (var id in IranZoneIds)
        {
            try
            {
                var zone = find(id);
                if (zone is not null) return zone;
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { }
        }

        return TimeZoneInfo.CreateCustomTimeZone("Iran-Fallback", IranFallbackOffset,
            "Iran (UTC+03:30)", "Iran (UTC+03:30)");
    }

    private static TimeZoneInfo? FindZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return null; }
    }

    /// <summary>تبدیل یک لحظهٔ مطلق به ساعت دیوارِ تهران (بدون Kind محلی).</summary>
    public static DateTime ToIran(DateTimeOffset instant)
        => TimeZoneInfo.ConvertTime(instant, IranZone).DateTime;

    /// <summary>
    /// «اکنون» به وقت تهران، با <b>افست صریح</b> — برای هر APIای که زمان را به بیرون می‌دهد.
    ///
    /// <para>
    /// <b>چرا این متد لازم شد؟</b> <see cref="NowIran"/> یک <see cref="DateTime"/> با
    /// Kind ناشناخته برمی‌گرداند؛ وقتی به JSON سریال می‌شود، رشته‌ای <b>بدون افست</b>
    /// تولید می‌کند (مثلاً <c>2026-11-08 14:30:00</c>) که هر مصرف‌کنندهٔ بیرونی ممکن است
    /// UTC فرض کند — ۳ ساعت و ۳۰ دقیقه خطا در پایش و همگام‌سازی.
    /// </para>
    ///
    /// <para>
    /// این متد همان ساعت دیوارِ تهران را با افست واقعی منطقه برمی‌گرداند؛ در JSON به‌صورت
    /// <c>2026-11-08T14:30:00+03:30</c> درمی‌آید که بدون ابهام تجزیه می‌شود.
    /// </para>
    /// </summary>
    public static DateTimeOffset NowIranOffset()
    {
        var instant = DateTimeOffset.UtcNow;
        var wall = ToIran(instant);
        return new DateTimeOffset(DateTime.SpecifyKind(wall, DateTimeKind.Unspecified),
            IranZone.GetUtcOffset(instant));
    }

    /// <summary>کلید دورهٔ یک لحظهٔ مطلق — <b>مبنای درست</b> برای هر محاسبه.</summary>
    public static string ForInstant(DateTimeOffset instant, QuotaPeriod period)
        => For(period, ToIran(instant));

    /// <summary>
    /// کلید دورهٔ یک <see cref="DateTime"/>.
    /// ⚠️ این ساعت باید <b>وقت تهران</b> باشد؛ برای لحظهٔ مطلق از
    /// <see cref="ForInstant"/> استفاده کنید.
    /// </summary>
    public static string For(QuotaPeriod period, DateTime when) => period switch
    {
        QuotaPeriod.Monthly => $"{Year(when):0000}-{Month(when):00}",
        QuotaPeriod.Yearly => $"{Year(when):0000}",
        _ => Lifetime
    };

    /// <summary>
    /// بازهٔ <b>میلادی</b> متناظر با یک دورهٔ شمسی — برای پرس‌وجو روی ستون تاریخ.
    ///
    /// <para>
    /// چرا لازم است؟ برای متریک‌های «تعداد فعال» با دورهٔ ماهانه/سالانه، تعداد رکوردهای
    /// <b>همین دوره</b> ملاک است، نه کل تاریخچه. ستون تاریخ دیتابیس میلادی است، پس
    /// مرز ماه شمسی باید به بازهٔ میلادی برگردانده شود.
    /// </para>
    ///
    /// <para>پایان بازه <b>انحصاری</b> است: <c>[From, To)</c>.</para>
    /// </summary>
    public static (DateTime From, DateTime To) RangeOf(QuotaPeriod period, DateTime iranWhen)
    {
        var (year, month) = (Year(iranWhen), Month(iranWhen));

        switch (period)
        {
            case QuotaPeriod.Monthly:
            {
                var from = Calendar.ToDateTime(year, month, 1, 0, 0, 0, 0);
                var to = month == 12
                    ? Calendar.ToDateTime(year + 1, 1, 1, 0, 0, 0, 0)
                    : Calendar.ToDateTime(year, month + 1, 1, 0, 0, 0, 0);
                return (from, to);
            }
            case QuotaPeriod.Yearly:
                return (Calendar.ToDateTime(year, 1, 1, 0, 0, 0, 0),
                        Calendar.ToDateTime(year + 1, 1, 1, 0, 0, 0, 0));
            default:
                return (DateTime.MinValue, DateTime.MaxValue);
        }
    }

    /// <summary>بازهٔ میلادی دورهٔ همین لحظه (ساعت تهران).</summary>
    public static (DateTime From, DateTime To) CurrentRange(QuotaPeriod period)
        => RangeOf(period, NowIran());

    /// <summary>کلید دورهٔ همین لحظه — بر اساس ساعت تهران، نه ساعت سرور.</summary>
    public static string Now(QuotaPeriod period) => ForInstant(DateTimeOffset.UtcNow, period);

    /// <summary>ساعت دیوارِ تهران در همین لحظه (برای نمایش/ذخیره).</summary>
    public static DateTime NowIran() => ToIran(DateTimeOffset.UtcNow);

    /// <summary>سال شمسی یک تاریخ.</summary>
    public static int Year(DateTime when) => Calendar.GetYear(when);

    /// <summary>ماه شمسی یک تاریخ (۱ تا ۱۲).</summary>
    public static int Month(DateTime when) => Calendar.GetMonth(when);

    /// <summary>اولین روز بازهٔ یک کلید دوره (برای تطبیق مصرف با رکوردهای واقعی).</summary>
    /// <exception cref="FormatException">کلید نامعتبر.</exception>
    public static DateTime StartOf(QuotaPeriod period, string key)
    {
        if (period == QuotaPeriod.Lifetime) return DateTime.MinValue;

        var parts = (key ?? "").Split('-');
        if (parts.Length != (period == QuotaPeriod.Monthly ? 2 : 1))
            throw new FormatException($"کلید دورهٔ «{key}» با نوع دورهٔ {period} سازگار نیست.");

        if (!int.TryParse(parts[0], out var year)) throw new FormatException($"سال کلید دورهٔ «{key}» نامعتبر است.");
        var month = 1;
        if (period == QuotaPeriod.Monthly && !int.TryParse(parts[1], out month))
            throw new FormatException($"ماه کلید دورهٔ «{key}» نامعتبر است.");

        if (year is < 1200 or > 1700) throw new FormatException($"سال شمسی «{year}» خارج از بازهٔ قابل قبول است.");
        if (month is < 1 or > 12) throw new FormatException($"ماه شمسی «{month}» نامعتبر است.");

        return Calendar.ToDateTime(year, month, 1, 0, 0, 0, 0);
    }

    /// <summary>آخرین روز بازهٔ یک کلید دوره (۲۳:۵۹:۵۹ همان روز).</summary>
    public static DateTime EndOf(QuotaPeriod period, string key)
    {
        if (period == QuotaPeriod.Lifetime) return DateTime.MaxValue;
        var start = StartOf(period, key);
        var end = period == QuotaPeriod.Monthly ? start.AddMonths(1).AddDays(-1) : start.AddYears(1).AddDays(-1);
        return end.Date.AddDays(1).AddTicks(-1);
    }

    /// <summary>وصف فارسی کوتاه دوره — برای نمایش «مصرف مرداد ۱۴۰۵».</summary>
    public static string Describe(QuotaPeriod period, string key) => period switch
    {
        QuotaPeriod.Monthly => key,
        QuotaPeriod.Yearly => $"سال {key}",
        _ => "کل عمر"
    };
}
