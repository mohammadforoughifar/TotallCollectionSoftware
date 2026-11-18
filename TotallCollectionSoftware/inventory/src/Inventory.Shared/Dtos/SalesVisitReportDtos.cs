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

    /// <summary>مبلغ کل سفارش‌شدهٔ این کالا در بازه (پس از تخفیف سطرها).</summary>
    public decimal Amount { get; set; }

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

/// <summary>نوع تجمیع گزارش — کالا (پیش‌فرض)، ویزیتور یا مشتری.</summary>
public enum VisReportGroup
{
    /// <summary>یک سطر برای هر کالا + موجودی و کسری.</summary>
    Product = 0,

    /// <summary>یک سطر برای هر ویزیتور.</summary>
    Referrer = 1,

    /// <summary>یک سطر برای هر مشتری.</summary>
    Customer = 2
}

/// <summary>
/// یک سطر تجمیع «به تفکیک ویزیتور» یا «به تفکیک مشتری».
/// موجودی/کسری در این دو نما معنا ندارد؛ مقدار و مبلغ و تعداد درخواست ملاک است.
/// </summary>
public class VisRequestsGroupRow
{
    /// <summary>شناسهٔ ویزیتور (ReferrerId) یا مشتری (PartyId).</summary>
    public int Id { get; set; }

    public string Title { get; set; } = "";

    /// <summary>تعداد درخواست‌های متمایز.</summary>
    public int OrderCount { get; set; }

    /// <summary>تعداد سطرهای کالا در این درخواست‌ها.</summary>
    public int LineCount { get; set; }

    /// <summary>تعداد کالاهای متمایز.</summary>
    public int ProductCount { get; set; }

    /// <summary>جمع مقدار سفارش‌شده.</summary>
    public decimal TotalQty { get; set; }

    /// <summary>جمع مبلغ خالص سفارش‌ها (پس از تخفیف).</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>جمع تخفیف‌های داده‌شده.</summary>
    public decimal TotalDiscount { get; set; }

    /// <summary>کسری‌های همین گروه (جمع کسری کالاهای همین ویزیتور/مشتری روی موجودی فعلی).</summary>
    public decimal ShortageQty { get; set; }
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

    /// <summary>سطرهای تجمیع به تفکیک ویزیتور.</summary>
    public List<VisRequestsGroupRow> ReferrerRows { get; set; } = new();

    /// <summary>سطرهای تجمیع به تفکیک مشتری.</summary>
    public List<VisRequestsGroupRow> CustomerRows { get; set; } = new();

    /// <summary>جمع مبلغ خالص همهٔ درخواست‌های بازه (ریال).</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>جمع تخفیف‌های داده‌شده در بازه (ریال).</summary>
    public decimal TotalDiscount { get; set; }

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
