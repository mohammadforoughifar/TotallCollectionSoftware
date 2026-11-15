namespace Inventory.Api.Data;

/// <summary>
/// <b>رزرو سهمیه</b> — رکورد درجریانِ یک عملیات شبکه‌ای (ارسال به مودیان) که سهمیه‌اش
/// پیشاپیش کم شده ولی نتیجه‌اش هنوز مشخص نیست.
///
/// <para>
/// <b>چرا جدول جدا و نه فقط ردیف دفتر؟</b> در بازبینی مرحله ۲ معلوم شد «رزرو» فقط با
/// چند ردیف در <c>LicenseUsageLedger</c> نشان داده می‌شد و <b>وضعیت</b> نداشت. نتیجه:
/// اگر برنامه درست بعد از ارسال و قبل از تسویه خاموش می‌شد، آن رزرو برای همیشه
/// «در هوا» می‌ماند و هیچ‌کس نمی‌دانست باید تأیید شود یا برگردد. این جدول همان
/// «وضعیت + زمان» را می‌دهد:
/// </para>
///
/// <para><b>چرخهٔ عمر:</b></para>
/// <list type="number">
///   <item><see cref="States.Reserved"/> — همین حالا رزرو شد؛ تماس شبکه در جریان است.</item>
///   <item><see cref="States.Confirmed"/> — ارسال قطعاً موفق بود؛ واحد مصرف‌شده می‌ماند.</item>
///   <item><see cref="States.Refunded"/> — ارسال قطعاً شکست خورد؛ واحد برگشت.</item>
///   <item><see cref="States.Pending"/> — نتیجه نامعلوم، یا رزروی که بیش از حد مجاز
///   <see cref="ReservedAt"/> قدیمی شده است. <b>واحد برنمی‌گردد</b> تا پشتیبانی
///   تصمیم بگیرد (تسویهٔ دستی).</item>
/// </list>
///
/// <para>
/// ⚠️ قاعدهٔ سند: «نتیجهٔ نامعلوم ⇒ بازگشت نکن». پس <c>Pending</c> عمداً مصرف را
/// آزاد نمی‌کند؛ فقط قابل پیگیری و تسویه‌پذیر می‌شود.
/// </para>
/// </summary>
public class LicenseReservation
{
    /// <summary>نام‌های وضعیت — رشته‌ای نگه داشته می‌شود تا خواندن ردیف‌های قدیمی نشکند.</summary>
    public static class States
    {
        public const string Reserved = "Reserved";
        public const string Confirmed = "Confirmed";
        public const string Refunded = "Refunded";
        public const string Pending = "Pending";
    }

    public long Id { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(32)]
    public string Module { get; set; } = "";

    [System.ComponentModel.DataAnnotations.MaxLength(64)]
    public string Metric { get; set; } = "";

    /// <summary>کلید دورهٔ سهمیه در لحظهٔ رزرو (شمسی) — تسویه باید همان دوره را بزند.</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(16)]
    public string PeriodKey { get; set; } = "-";

    /// <summary>مقدار رزروشده (همیشه ۱ در مرحلهٔ ۲).</summary>
    public int Amount { get; set; } = 1;

    [System.ComponentModel.DataAnnotations.MaxLength(64)]
    public string? EntityType { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(64)]
    public string? EntityId { get; set; }

    /// <summary>Reserved | Confirmed | Refunded | Pending</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(20)]
    public string State { get; set; } = States.Reserved;

    /// <summary>لحظهٔ رزرو — مبنای «رزرو کهنه» در راه‌اندازی برنامه.</summary>
    public DateTime ReservedAt { get; set; } = DateTime.Now;

    /// <summary>لحظهٔ تسویه (نال تا وقتی در وضعیت Reserved/Pending است).</summary>
    public DateTime? SettledAt { get; set; }

    /// <summary>چه کسی تسویه کرد — «system» برای خودکار، نام کاربری برای تسویهٔ دستی.</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(100)]
    public string? SettledBy { get; set; }

    /// <summary>یادداشت تسویه (مثلاً «تأیید پس از بررسی گزارش مودیان»).</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(600)]
    public string? Note { get; set; }

    /// <summary>HMAC ردیف — مثل بقیهٔ جدول‌های مصرف، ضد دست‌کاری مستقیم در دیتابیس.</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(88)]
    public string? Hmac { get; set; }
}
