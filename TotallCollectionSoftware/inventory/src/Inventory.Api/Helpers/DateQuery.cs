using System.Globalization;
using Inventory.Shared;

namespace Inventory.Api.Helpers;

/// <summary>
/// تجزیهٔ تاریخ‌های آمده در کوئری‌استرینگ (مثل <c>asOf</c> موجودی فاکتور) مستقل از فرهنگ سیستم.
/// <para>
/// چرا لازم است: قالب <c>yyyy-MM-dd</c> در فرهنگی که تقویم جاری‌اش هجری شمسی است (مثل
/// <c>fa-IR</c> روی ویندوز سرور) به «سال شمسی» تفسیر می‌شود؛ نتیجه این بود که فیلتر
/// «موجودی تا تاریخ سند» بی‌سروصدا به یک تاریخ دورِ آینده تبدیل می‌شد و خرید فردا در
/// موجودی فاکتور دیروز دیده می‌شد. این کلاس همیشه میلادی می‌خواند و اگر ورودی به‌هرحال
/// شمسی برسد (کلاینت با فرهنگ فارسی) هم آن را به میلادی برمی‌گرداند.
/// </para>
/// </summary>
public static class DateQuery
{
    /// <summary>بازهٔ سال شمسی که در تاریخ سندِ فروشگاه معنا دارد (۱۴۰۵ ≈ اکنون).</summary>
    private const int MinPersianYear = 1300;
    private const int MaxPersianYear = 1500;

    private static readonly string[] Formats =
    {
        "yyyy-MM-dd", "yyyy/MM/dd", "yyyy-M-d", "yyyy/M/d",
        "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFF",
        "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ssK", "yyyy-MM-ddTHH:mm:ss.FFFFFFFK"
    };

    /// <summary>تاریخ را از متن کوئری می‌خواند؛ null یعنی ورودی خالی یا نامعتبر.</summary>
    public static DateTime? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var value = Fa.ToEn(text).Trim();

        if (DateTime.TryParseExact(value, Formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var exact))
            return Normalize(exact);

        // ISO با منطقهٔ زمانی (مثلاً خروجی سریال‌ساز مرورگر)
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var offset))
            return Normalize(offset.Date);

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return Normalize(parsed);

        return null;
    }

    /// <summary>
    /// تاریخ را به «روز» تبدیل می‌کند و اگر سالش در بازهٔ سال شمسی باشد، آن را میلادی می‌کند.
    /// </summary>
    public static DateTime Normalize(DateTime value)
    {
        var day = value.Date;
        if (day.Year is >= MinPersianYear and <= MaxPersianYear)
        {
            var gregorian = PersianDate.ToGregorian(day.Year, day.Month, day.Day);
            if (gregorian != DateTime.MinValue) return gregorian.Date;
        }
        return day;
    }
}
