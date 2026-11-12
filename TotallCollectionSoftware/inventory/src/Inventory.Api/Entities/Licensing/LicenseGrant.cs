using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// یک فایل لایسنسِ اعمال‌شده (پایه یا Add-on).
///
/// <para>
/// payload و امضا عیناً همان چیزی است که در فایل بوده — یعنی می‌توان در هر لحظه امضا را
/// <b>دوباره</b> بررسی کرد؛ اگر ردیف دیتابیس دست‌کاری شود، امضا نمی‌خواند و سند کنار
/// گذاشته می‌شود. به همین دلیل payload متنی ذخیره می‌شود، نه فقط نتیجهٔ تجزیه‌شده.
/// </para>
/// </summary>
public class LicenseGrant
{
    public int Id { get; set; }

    /// <summary>
    /// شناسهٔ مستأجر (فقط SaaS). در نصب داخلی خالی است؛ چون در نصب داخلی «مستأجر» همان نصب است.
    /// </summary>
    public int? TenantId { get; set; }

    /// <summary>شمارهٔ سریال سند — یکتا؛ هر فایل فقط یک‌بار اعمال می‌شود.</summary>
    [MaxLength(64)]
    public string Serial { get; set; } = "";

    /// <summary>شناسهٔ کلید عمومی امضا.</summary>
    [MaxLength(64)]
    public string KeyId { get; set; } = "";

    /// <summary>RS256 یا ES256.</summary>
    [MaxLength(20)]
    public string Algorithm { get; set; } = "RS256";

    /// <summary>Base64 از بایت‌های UTF-8 سند (همان چیزی که امضا شده).</summary>
    public string Payload { get; set; } = "";

    /// <summary>Base64 امضا.</summary>
    public string Signature { get; set; } = "";

    /// <summary>نام فایل هنگام آپلود — برای پیگیری.</summary>
    [MaxLength(260)]
    public string? FileName { get; set; }

    /// <summary>Active | Revoked. (سندی که کنار گذاشته شود Revoked می‌شود، حذف نمی‌شود.)</summary>
    [MaxLength(20)]
    public string Status { get; set; } = "Active";

    /// <summary>آیا Add-on است؟ (پایه = سندی که مشتری/تاریخ را تعیین می‌کند)</summary>
    public bool IsAddOn { get; set; }

    [MaxLength(200)]
    public string? CustomerName { get; set; }

    /// <summary>شروع و پایان مؤثر سند (denormalized برای نمایش سریع؛ ملاک واقعی، payload است).</summary>
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }

    public DateTime AppliedAt { get; set; } = DateTime.Now;

    /// <summary>کاربری که فایل را آپلود کرده (خالی = فعال‌سازی بدون ورود، وقتی لایسنس نامعتبر بوده).</summary>
    public int? AppliedByUserId { get; set; }

    /// <summary>یادداشت بررسی (خطاها/هشدارهای زمان اعمال).</summary>
    [MaxLength(1200)]
    public string? ValidationNote { get; set; }
}
