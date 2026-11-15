namespace Inventory.Api.Data;

/// <summary>
/// «مبنای بخشودگی گذشته» — تعداد رکوردهایی که در <b>یک دورهٔ مشخص</b> در لحظهٔ فعال‌سازی
/// وجود داشتند و باید از مصرف مؤثر <b>همان دوره</b> کسر شوند.
///
/// <para>
/// <b>چرا جدول جدا و نه ستون در <c>LicenseUsageBaselines</c>؟</b> طبق قرارداد
/// <c>LicenseSchemaV1</c>، اسکریپت ساخت جداول لایسنس <b>فقط افزایشی</b> است: هیچ
/// <c>DROP INDEX</c> یا بازسازی‌ای در آن نیست. برای نگه‌داشتن چند دوره برای یک متریک،
/// ایندکس یکتای موجود <c>(Module, Metric)</c> باید می‌شکست — که یعنی Drop + ساخت دوباره.
/// پس به‌جای دست‌زدن به جدول موجود، یک جدول تازه با کلید سه‌گانه ساخته می‌شود. نتیجه:
/// <b>هیچ تغییری</b> در جدول مبناهای فعلی و هیچ ریسکی برای نصب‌های در حال کار.
/// </para>
///
/// <para>
/// <b>قاعدهٔ رفتاری:</b> فقط دوره‌ای که ردیف دارد بخشوده می‌شود. وقتی ماه بعد شروع شود،
/// کلید دورهٔ تازه است و ردیفی ندارد ⇒ شمارش از صفر. این همان چیزی است که «فقط همین
/// دوره» را تضمین می‌کند.
/// </para>
/// </summary>
public class LicenseGrandfatherBaseline
{
    public long Id { get; set; }

    /// <summary>ماژول صاحب متریک</summary>
    public string Module { get; set; } = "";

    /// <summary>کد متریک (مثلاً <c>letter.create</c>)</summary>
    public string Metric { get; set; } = "";

    /// <summary>
    /// کلید دوره (مثلاً <c>1404-08</c> برای ماهانه، <c>1404</c> برای سالانه، <c>-</c> برای دائمی).
    /// این ستون بخشی از کلید یکتاست؛ پس هر دوره فقط یک مبنا دارد.
    /// </summary>
    public string PeriodKey { get; set; } = "";

    /// <summary>تعداد رکوردهایی که در لحظهٔ فعال‌سازی در همین دوره وجود داشتند.</summary>
    public long BaselineCount { get; set; }

    /// <summary>لحظهٔ ثبت (ساعت تهران).</summary>
    public DateTime CapturedAt { get; set; }

    /// <summary>سریال لایسنسی که این بخشودگی را آورد (ممیزی).</summary>
    public string? Serial { get; set; }
}
