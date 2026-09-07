namespace Inventory.Shared.Dtos;

/// <summary>
/// قرارداد مشترک صفحه‌بندی تمام ماژول‌ها. قبلاً دو تعریف هم‌نام در CatalogDtos و
/// ProjectDtos باعث CS0101 در Shared و سپس CS0006 در Api/Client می‌شد.
/// هر دو نام تعداد کل برای سازگاری پاسخ‌های کاتالوگ و مدیریت پروژه حفظ می‌شوند.
/// </summary>
public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }

    /// <summary>نام سازگار با API پروژه‌ها؛ همان مقدار TotalCount، نه یک شمارنده مستقل.</summary>
    public int Total { get => TotalCount; set => TotalCount = value; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int PageCount => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

    /// <summary>جمع تیک‌های زمان کار روی همه ردیف‌های فیلترشده، نه فقط صفحه جاری.</summary>
    public long? SumTicks { get; set; }
}
