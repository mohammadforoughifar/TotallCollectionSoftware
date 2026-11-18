using System.Text.Json.Serialization;

namespace Inventory.Shared.Dtos;

// =====================================================================
//  گزارش «تجمیع درخواست‌های ویزیتوری بر اساس کالا» — مشترک API/کلاینت
//
//  قاعدهٔ گزارش (خواستهٔ کارفرما):
//   • بازهٔ زمانی، مبنای انتخاب درخواست‌ها است.
//   • هر کالا فقط **یک سطر** دارد (تجمیع همهٔ درخواست‌های همان بازه).
//   • در انتها جمع مقدار سفارش‌شده نمایش داده می‌شود.
//   • ستون «کسری» = جمع مقدار سفارش‌شده − موجودی کل شرکت برای همان کالا؛
//     فقط وقتی بزرگ‌تر از صفر باشد مقدار دارد.
// =====================================================================

/// <summary>یک سطر گزارش: یک کالا + مقدار سفارش‌شده در بازه + موجودی + کسری.</summary>
public class VisRequestsReportRow
{
    public int ProductId { get; set; }

    public string ProductCode { get; set; } = "";

    public string ProductName { get; set; } = "";

    public string? Unit { get; set; }

    /// <summary>جمع مقدار درخواست‌شده از این کالا در بازهٔ گزارش.</summary>
    public decimal OrderedQty { get; set; }

    /// <summary>تعداد درخواست‌هایی (سفارش‌های متمایز) که این کالا در آن‌ها آمده است.</summary>
    public int OrderCount { get; set; }

    /// <summary>موجودی کل شرکت برای این کالا (جمع همهٔ انبارها).</summary>
    public decimal StockQty { get; set; }

    /// <summary>کسری = سفارش − موجودی؛ اگر مثبت نشد صفر می‌ماند و نمایش داده نمی‌شود.</summary>
    public decimal ShortageQty { get; set; }

    /// <summary>کالای خدماتی موجودی ندارد؛ ستون کسری برایش «—» است.</summary>
    public bool IsService { get; set; }

    /// <summary>آیا این سطر کسری دارد؟ (برای هایلایت و شمارش)</summary>
    [JsonIgnore]
    public bool HasShortage => !IsService && ShortageQty > 0;

    /// <summary>متن آمادهٔ ستون کسری.</summary>
    [JsonIgnore]
    public string ShortageText => IsService ? "—" : (HasShortage ? Fa.Digits(ShortageQty) : "—");
}

/// <summary>خروجی کامل گزارش تجمیع درخواست‌های ویزیتوری.</summary>
public class VisRequestsReportDto
{
    public DateTime From { get; set; }

    public DateTime To { get; set; }

    /// <summary>تعداد درخواست‌های وارد‌شده در بازه (پس از اعمال فیلترها).</summary>
    public int OrderCount { get; set; }

    /// <summary>تعداد کالاهای متمایز (تعداد سطرهای گزارش).</summary>
    public int ProductCount => Rows.Count;

    public List<VisRequestsReportRow> Rows { get; set; } = new();

    /// <summary>جمع کل مقدار سفارش‌شده — ردیف پایانی جدول.</summary>
    public decimal TotalOrderedQty { get; set; }

    /// <summary>جمع کسری‌ها (فقط سطرهای دارای کسری).</summary>
    public decimal TotalShortageQty { get; set; }

    /// <summary>تعداد سطرهای دارای کسری.</summary>
    public int ShortageRowCount { get; set; }

    /// <summary>درخواست‌های ابطال/ردشده در این گزارش حساب شده‌اند؟</summary>
    public bool IncludesCanceled { get; set; }

    /// <summary>گزارش فقط دادهٔ خود کاربر (ویزیتور) را نشان می‌دهد.</summary>
    public bool ScopedToMe { get; set; }

    public DateTime GeneratedAt { get; set; } = DateTime.Now;

    /// <summary>توضیح یک‌خطی دامنهٔ گزارش برای نمایش در سرصفحه/چاپ.</summary>
    [JsonIgnore]
    public string ScopeText => ScopedToMe
        ? "فقط درخواست‌های ثبت‌شده توسط شما"
        : "همهٔ درخواست‌های شرکت";

    [JsonIgnore]
    public string CanceledText => IncludesCanceled
        ? "درخواست‌های ردشده/ابطال‌شده هم حساب شده‌اند"
        : "درخواست‌های ردشده و ابطال‌شده در گزارش نیستند";

    [JsonIgnore]
    public bool HasData => Rows.Count > 0;
}
