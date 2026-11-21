using System.Globalization;

namespace Inventory.Api.Services.Core;

/// <summary>
/// ساعت «ثبت سیستمی» برای ستون‌های <c>CreatedAt</c> جدول‌های کسب‌وکاری.
///
/// <para>
/// همهٔ این ستون‌ها با <b>ساعت دیوارِ تهران</b> (<c>DateTimeKind.Unspecified</c>) نوشته
/// و مقایسه می‌شوند؛ مستقل از این‌که سرور روی چه منطقهٔ زمانی تنظیم شده باشد.
/// </para>
///
/// <para>
/// ⚠️ پس این ستون‌ها «لحظهٔ مطلق UTC» نیستند و نباید با <c>DateTime.UtcNow</c> مقایسه شوند.
/// </para>
/// </summary>
public static class TehranClock
{
    /// <summary>منطقهٔ زمانی تهران — با tzdata، و اگر نبود با افست ثابت +03:30.</summary>
    private static readonly TimeZoneInfo TehranZone = ResolveTehranZone();

    private static TimeZoneInfo ResolveTehranZone()
    {
        foreach (var id in new[] { "Asia/Tehran", "Iran Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        return TimeZoneInfo.CreateCustomTimeZone(
            "Tehran(+03:30)", TimeSpan.FromMinutes(210), "Tehran", "Tehran");
    }

    /// <summary>
    /// «اکنون» به وقت دیوارِ تهران (<c>DateTimeKind.Unspecified</c>) — همان فرمتی که
    /// ستون‌های <c>CreatedAt</c> ذخیره می‌کنند.
    /// </summary>
    public static DateTime Now() => DateTime.SpecifyKind(
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TehranZone), DateTimeKind.Unspecified);

    /// <summary>
    /// مقدار <c>CreatedAt</c> برای ردیفی که از سیستم دیگری <b>وارد</b> می‌شود (ایمپورت).
    /// قاعدهٔ ایمپورت: تاریخ اصلی سند، نه «الان».
    /// </summary>
    public static DateTime ForImported(DateTime originalDate)
        => DateTime.SpecifyKind(originalDate, DateTimeKind.Unspecified);

    /// <summary>آیا مقدار، در ماه شمسیِ همین لحظه می‌افتد؟ (برای گزارش و تست)</summary>
    public static bool IsCurrentPersianMonth(DateTime? createdAt)
    {
        if (createdAt is not { } value) return false;

        var pc = new PersianCalendar();
        var now = Now();
        return pc.GetYear(value) == pc.GetYear(now) && pc.GetMonth(value) == pc.GetMonth(now);
    }
}
