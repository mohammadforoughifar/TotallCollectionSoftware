using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// پروفایل شرکت/فروشگاه — فقط یک ردیف (Id=1).
/// تعریف شرکت (نام، شماره حساب، شماره تماس، …) + تنظیمات چاپ فاکتورهای فروش/خرید.
/// جدول توسط <see cref="CompanyProfileSchemaV1"/> به‌صورت خودتعمیر ساخته می‌شود.
/// </summary>
public class CompanyProfile
{
    public int Id { get; set; }

    /// <summary>نام فروشگاه / شرکت — روی سربرگ فاکتور چاپی می‌آید</summary>
    [MaxLength(200)] public string Name { get; set; } = "";

    /// <summary>شماره حساب بانکی — روی فاکتور چاپی نمایش داده می‌شود</summary>
    [MaxLength(100)] public string? AccountNumber { get; set; }

    /// <summary>شماره تماس — روی فاکتور چاپی نمایش داده می‌شود</summary>
    [MaxLength(100)] public string? Phone { get; set; }

    [MaxLength(500)] public string? Address { get; set; }

    /// <summary>کد اقتصادی</summary>
    [MaxLength(50)] public string? EconomicCode { get; set; }

    /// <summary>شناسه ملی / مالیاتی</summary>
    [MaxLength(50)] public string? TaxId { get; set; }

    [MaxLength(200)] public string? Email { get; set; }

    [MaxLength(200)] public string? Website { get; set; }

    // ---------- تنظیمات چاپ فاکتور ----------
    /// <summary>متن‌های بالای فاکتور (هر خط جداگانه) — بالای سربرگ چاپ می‌شود</summary>
    [MaxLength(1000)] public string? PrintHeaderLines { get; set; }

    /// <summary>متن‌های پایین فاکتور (هر خط جداگانه) — جایگزین متن پیش‌فرض footer</summary>
    [MaxLength(1000)] public string? PrintFooterLines { get; set; }

    /// <summary>اندازه کاغذ فاکتور: A4 یا A5 (پیش‌فرض A4)</summary>
    [MaxLength(10)] public string PrintPaperSize { get; set; } = "A4";

    /// <summary>نمایش کادر امضا در پایین فاکتور</summary>
    public bool PrintShowSignatures { get; set; } = true;

    public DateTime? UpdatedAt { get; set; }
}
