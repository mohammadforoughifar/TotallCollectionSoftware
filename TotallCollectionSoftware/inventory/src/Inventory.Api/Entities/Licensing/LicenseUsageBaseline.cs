namespace Inventory.Api.Data;

/// <summary>
/// «مبنای مصرف» — تعداد رکوردهایی که <b>پیش از فعال‌سازی لایسنس</b> وجود داشته‌اند.
///
/// <para>
/// <b>مسئله:</b> برای متریک‌های نوع «تعداد فعال» (<c>MetricKind.ActiveCount</c>) ملاک،
/// تعداد واقعی ردیف‌های دیتابیس است. مشتری‌ای که ۲۰٬۰۰۰ نامهٔ قدیمی را از سیستم قبلی
/// <b>وارد می‌کند</b> (اسکریپت مهاجرت) نباید یک‌شبه از سقف عبور کند و ثبتش بسته شود؛
/// چون آن نامه‌ها با نرم‌افزار ما ساخته نشده‌اند.
/// </para>
///
/// <para>
/// <b>راه‌حل:</b> هنگام فعال‌سازی لایسنس، تعداد رکوردهای موجود و زمان همان لحظه
/// در همین جدول ثبت می‌شود (یک‌بار، idempotent). مصرف مؤثر می‌شود:
/// <code>effective = max(counter, liveCount − baseline)</code>
/// یعنی رکوردهای «قبل از مبنا» از شمارش کسر می‌شوند، ولی رکوردهای تازه (حتی اگر
/// با ایمپورت ساخته شوند) شمرده می‌شوند — چون بعد از فعال‌سازی، ایمپورت هم مصرف
/// واقعی مشتری است و باید با سهمیه هم‌خوان باشد.
/// </para>
///
/// <para>
/// برای متریک‌های شمارنده (Counter) این جدول لازم نیست و ردیفی ساخته نمی‌شود؛
/// چون ایمپورت اصلاً شمارنده را زیاد نمی‌کند و شمارنده از صفر شروع می‌شود.
/// </para>
/// </summary>
public class LicenseUsageBaseline
{
    public long Id { get; set; }

    /// <summary>ماژول صاحب متریک</summary>
    public string Module { get; set; } = "";

    /// <summary>کد متریک (مثلاً <c>letter.create</c>)</summary>
    public string Metric { get; set; } = "";

    /// <summary>تعداد رکوردهایی که در لحظهٔ مبنا وجود داشتند.</summary>
    public long BaselineCount { get; set; }

    /// <summary>لحظهٔ ثبت مبنا (ساعت تهران) — مبنای پرس‌وجوی «رکوردهای قبلی».</summary>
    public DateTime CapturedAt { get; set; }

    /// <summary>چه رویدادی مبنا را ثبت کرد: <c>activation</c>، <c>upgrade</c>، <c>manual</c>.</summary>
    public string Reason { get; set; } = "activation";

    /// <summary>سریال لایسنسی که در آن لحظه اعمال شد (برای ممیزی).</summary>
    public string? Serial { get; set; }
}
