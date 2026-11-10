using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// آیتم‌های مجزاشدهٔ یک سند — <b>فقط برای سرویس SaaS</b>.
///
/// <para>
/// در SaaS، پنل مدیریت لایسنس باید بتواند بدون باز کردن payload، آیتم‌ها را فهرست/ویرایش
/// کند. در نصب داخلی (OnPrem) سند امضاشده تنها منبع است و این جدول خالی می‌ماند؛
/// هرگز به‌جای payload تصمیم‌گیر نیست.
/// </para>
/// </summary>
public class LicenseItemRow
{
    public int Id { get; set; }

    public int GrantId { get; set; }

    /// <summary>شناسهٔ آیتم داخل سند.</summary>
    [MaxLength(64)]
    public string ItemId { get; set; } = "";

    /// <summary>کد ماژول‌ها با کاما — برای فیلتر سریع در پنل.</summary>
    [MaxLength(600)]
    public string ScopeCsv { get; set; } = "";

    [MaxLength(20)]
    public string TermType { get; set; } = "Subscription";

    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
    public DateTime? UpdatesUntil { get; set; }

    [MaxLength(20)]
    public string UserMode { get; set; } = "None";
    public int UserMax { get; set; }
    public int UserExempt { get; set; }

    /// <summary>سهمیه‌ها به‌صورت JSON (لیست LicenseQuota).</summary>
    public string? QuotasJson { get; set; }

    public bool CarryOverUsage { get; set; }
}
