using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// تعطیلی شرکتی (مجموعه) — مدیر یک روز را برای کل پرسنل تعطیل می‌کند.
/// در این روز هیچ کاربری کسری نمی‌خورد و در «کل تردد ماه» با وضعیت تعطیلی (سبز) نمایش داده می‌شود.
/// </summary>
public class CompanyHoliday
{
    public int Id { get; set; }

    /// <summary>تاریخ تعطیلی (بدون زمان)</summary>
    public DateTime HolidayDate { get; set; }

    /// <summary>عنوان/دلیل — مثلاً: ایام عزاداری، نشست سازمانی</summary>
    [MaxLength(100)]
    public string Name { get; set; } = "";

    /// <summary>نام مدیری که این تعطیلی را ثبت کرده (اعمال گروهی برای کل پرسنل)</summary>
    [MaxLength(150)]
    public string? CreatedByName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
