using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// یک ماژول نرم‌افزاری و مالک آن — نقشهٔ «چه کسی مسئول چه بخشی است».
/// <para>
/// مقدار <c>Key</c> عمداً هم‌نام با پوشهٔ کد است (Hr، Office، DocArchive) تا
/// بتوان این جدول را از خروجی <c>tools/ownership-map.csv</c> و فایل
/// <c>.github/CODEOWNERS</c> پر کرد و برعکس.
/// </para>
/// </summary>
public class DevModule
{
    public int Id { get; set; }

    /// <summary>کلید یکتا، هم‌نام با پوشهٔ کد.</summary>
    [MaxLength(60)]
    public string Key { get; set; } = "";

    /// <summary>نام فارسی برای نمایش در منو و بورد.</summary>
    [MaxLength(120)]
    public string Title { get; set; } = "";

    /// <summary>مالک ماژول — همان کسی که بازبین اجباری PRهای این ماژول است.</summary>
    public int? OwnerId { get; set; }

    [MaxLength(60)]
    public string? Icon { get; set; }

    [MaxLength(9)]
    public string ColorHex { get; set; } = "#0d6efd";

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>مسیرهای کد این ماژول، جداشده با «;» — برای گزارش و تطبیق با CODEOWNERS.</summary>
    [MaxLength(1000)]
    public string? RepoPaths { get; set; }

    /// <summary>حجم کد سمت سرور — برای اولویت‌بندی تعیین مالک.</summary>
    public int ServiceLines { get; set; }

    /// <summary>تعداد صفحه‌های کلاینت.</summary>
    public int PageCount { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
