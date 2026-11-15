using Inventory.Licensing.Periods;

namespace Inventory.Api.Services.Core;

/// <summary>
/// ساعت «ثبت سیستمی» برای ستون‌های <c>CreatedAt</c> جدول‌های کسب‌وکاری.
///
/// <para>
/// <b>تصمیم (بند ۲ بازبینی): یکدست «ساعت دیوارِ تهران».</b> هر چهار ستون
/// (<c>FacInvoices.CreatedAt</c>، <c>IncomingLetters.CreatedAt</c>،
/// <c>OutgoingLetters.CreatedAt</c>، <c>InnerLetters.CreatedAt</c>) و همچنین مرزهای
/// ماهِ سهمیه، همه بر پایهٔ <b>ساعت دیوارِ تهران</b> نوشته و مقایسه می‌شوند.
/// </para>
///
/// <para>
/// <b>چرا این و نه UTC؟</b> سه دلیل:
/// </para>
/// <list type="number">
///   <item><b>قاعدهٔ کسب‌وکار در تقویم شمسی است.</b> «۲۰۰۰ نامه در ماه» یعنی ماه شمسی
///   به وقت تهران؛ همان چیزی که در <see cref="PersianPeriodKey.RangeOf"/> ساخته می‌شود.
///   اگر ستون UTC باشد، هر مقایسه باید در دو جا تبدیل شود و هر جای فراموش‌شده یک باگ
///   خاموش می‌سازد.</item>
///   <item><b>دادهٔ موجود قابل تفسیر است.</b> ردیف‌هایی که تا امروز نوشته شده‌اند با
///   <c>DateTime.Now</c> (ساعت محلی سرور) نوشته شده‌اند. روی سرورهای ایرانی (مورد
///   واقعی مشتری) ساعت محلی همان تهران است، پس این ردیف‌ها از قبل «تهران» هستند و
///   نیازی به تبدیل ندارند. اگر UTC را انتخاب می‌کردیم، این ردیف‌ها باید با حدسِ
///   «سرورِ قبلی چه منطقه‌ای بود» جابه‌جا می‌شدند — یعنی یک تغییر دادهٔ حدسی روی
///   اطلاعات مالی مشتری. حالا هیچ ردیفی دست نمی‌خورد.</item>
///   <item><b>مستقل از تنظیم سرور.</b> مقدار از <see cref="PersianPeriodKey.NowIran"/>
///   می‌آید که صریحاً منطقهٔ <c>Asia/Tehran</c> را اعمال می‌کند (و اگر tzdata نبود،
///   افست ثابت +03:30). یعنی چه سرور UTC باشد چه آمستردام چه تهران، عدد یکسان است.</item>
/// </list>
///
/// <para>
/// ⚠️ پس این ستون‌ها <b>«لحظهٔ مطلق UTC» نیستند</b> و نباید با <c>DateTime.UtcNow</c>
/// مقایسه شوند. هر مقایسه‌ای باید از بازه‌های <see cref="PersianPeriodKey"/> بیاید.
/// تست مرز ماه در چهار منطقهٔ زمانی همین را قفل می‌کند.
/// </para>
/// </summary>
public static class TehranClock
{
    /// <summary>
    /// «اکنون» به وقت دیوارِ تهران (<c>DateTimeKind.Unspecified</c>) — همان فرمتی که
    /// ستون‌های <c>CreatedAt</c> ذخیره می‌کنند.
    /// </summary>
    public static DateTime Now() => DateTime.SpecifyKind(PersianPeriodKey.NowIran(), DateTimeKind.Unspecified);

    /// <summary>
    /// مقدار <c>CreatedAt</c> برای ردیفی که از سیستم دیگری <b>وارد</b> می‌شود (ایمپورت).
    ///
    /// <para>
    /// قاعدهٔ ایمپورت: تاریخ اصلی سند، نه «الان». اگر ورود انبوه، <c>CreatedAt</c> را
    /// با «الان» بنویسد، هزار نامهٔ سال ۱۴۰۰ همه در ماه جاری شمرده می‌شوند و سقف
    /// ماهانهٔ مشتری را در همان روز اول پر می‌کنند.
    /// </para>
    /// </summary>
    /// <param name="originalDate">تاریخ سند در سیستم مبدأ (همان دیوارِ تهران).</param>
    public static DateTime ForImported(DateTime originalDate)
        => DateTime.SpecifyKind(originalDate, DateTimeKind.Unspecified);

    /// <summary>آیا مقدار، در ماه شمسیِ همین لحظه می‌افتد؟ (برای گزارش و تست)</summary>
    public static bool IsCurrentPersianMonth(DateTime? createdAt)
        => createdAt is { } t && PersianPeriodKey.For(Inventory.Licensing.Model.QuotaPeriod.Monthly, t)
            == PersianPeriodKey.Now(Inventory.Licensing.Model.QuotaPeriod.Monthly);
}
