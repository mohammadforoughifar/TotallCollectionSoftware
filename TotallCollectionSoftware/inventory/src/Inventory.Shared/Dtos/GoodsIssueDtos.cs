namespace Inventory.Shared.Dtos;

/// <summary>سطر حواله تحویل کالا — فقط مقدار، بدون هیچ مبلغی.</summary>
public class GoodsIssueLineDto
{
    public int Id { get; set; }

    /// <summary>
    /// شناسهٔ کالا وقتی سطر «از انبار» انتخاب شده باشد؛ صفر یعنی سطر «دستی» با نام آزاد.
    /// سرور برای سطرِ دارای شناسه، نام کالا را از خود کالا snapshot می‌کند.
    /// </summary>
    public int ProductId { get; set; }

    /// <summary>نام کالا — در حالت انبار از فهرست کالاها و در حالت دستی متن آزاد تایپ‌شده.</summary>
    public string ProductName { get; set; } = "";

    /// <summary>مقدار — واحد ندارد و ریالی نیست</summary>
    public decimal Quantity { get; set; }
    public string? Note { get; set; }
}

/// <summary>حواله تحویل کالا — سند سبک «فقط مقدار» برای یک مشتری.</summary>
public class GoodsIssueDto
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public DateTime Date { get; set; }
    public int PartyId { get; set; }
    public int? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public string PartyName { get; set; } = "";
    public string? Description { get; set; }
    public List<GoodsIssueLineDto> Lines { get; set; } = new();

    /// <summary>جمع مقدار سطرها</summary>
    public decimal TotalQuantity => Lines.Sum(l => l.Quantity);

    public int LineCount => Lines.Count;
}

/// <summary>
/// گزینهٔ «کالای انباری» برای انتخاب در سطرهای بیجک — همان فهرست کالاها با موجودی.
/// انتخاب از این فهرست فقط شناسه/نام کالا را در سطر می‌نشاند؛ ورود نام آزاد (دستی)
/// همچنان مجاز است و به موجودی یا حسابداری اثری ندارد.
/// </summary>
public class GoodsIssueProductOptionDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "عدد";

    /// <summary>موجودی در همهٔ انبارهای شرکت — فقط برای نمایش/راهنمای انتخاب.</summary>
    public decimal TotalStock { get; set; }

    /// <summary>انبار اختصاصی کالا (خالی = همهٔ انبارها).</summary>
    public string? WarehouseName { get; set; }

    public bool? AllowsFractionalQuantity { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>متن نمایش در انتخاب‌گر: «کد — نام» (بدون کد، فقط نام).</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Code) ? Name : $"{Code} — {Name}";
}

/// <summary>درخواست ثبت/ویرایش حواله.</summary>
public class GoodsIssueCommand
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public int PartyId { get; set; }
    public int? WarehouseId { get; set; }
    public string? Description { get; set; }
    public List<GoodsIssueLineDto> Lines { get; set; } = new();
}
