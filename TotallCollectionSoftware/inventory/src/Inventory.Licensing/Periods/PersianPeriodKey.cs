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
    public static readonly TimeZoneInfo IranZone = ResolveIranZone();

    private static TimeZoneInfo ResolveIranZone()
    {
        foreach (var id in new[] { "Asia/Tehran", "Iran Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { }
        }

        return TimeZoneInfo.CreateCustomTimeZone("Iran-Fallback", TimeSpan.FromMinutes(210),
            "Iran (UTC+03:30)", "Iran (UTC+03:30)");
    }

    /// <summary>تبدیل یک لحظهٔ مطلق به ساعت دیوارِ تهران (بدون Kind محلی).</summary>
    public static DateTime ToIran(DateTimeOffset instant)
        => TimeZoneInfo.ConvertTime(instant, IranZone).DateTime;

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
