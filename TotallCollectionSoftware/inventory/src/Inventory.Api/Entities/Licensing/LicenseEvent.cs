using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// رخدادهای لایسنس — لاگ تغییرناپذیر برای پیگیری.
///
/// <para>
/// چه چیزهایی این‌جا ثبت می‌شود: اعمال/رد فایل، تعارض سیاست کاربر، رسیدن به سقف سهمیه،
/// دست‌کاری ردیف مصرف (اختلاف HMAC)، تغییر شناسهٔ نصب، و انقضا/مهلت.
/// این جدول برای «چه شد که مشتری قفل شد؟» ضروری است.
/// </para>
/// </summary>
public class LicenseEvent
{
    public int Id { get; set; }

    public DateTime At { get; set; } = DateTime.Now;

    /// <summary>کد رخداد (مثل LICENSE_APPLIED، QUOTA_DENIED، USAGE_TAMPERED).</summary>
    [MaxLength(60)]
    public string Kind { get; set; } = "";

    /// <summary>Info | Warning | Error</summary>
    [MaxLength(20)]
    public string Severity { get; set; } = "Info";

    [MaxLength(600)]
    public string Message { get; set; } = "";

    /// <summary>داده‌های تکمیلی JSON (بدون اطلاعات حساس).</summary>
    public string? DataJson { get; set; }

    public int? UserId { get; set; }

    [MaxLength(64)]
    public string? Serial { get; set; }
}
