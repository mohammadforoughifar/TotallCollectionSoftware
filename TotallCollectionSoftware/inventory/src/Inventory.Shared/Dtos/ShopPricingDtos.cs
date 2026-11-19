namespace Inventory.Shared.Dtos;

public class ShopPricingPolicyDto
{
    public bool MinimumProfitEnabled { get; set; }
    public decimal MinimumMarkupPercent { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class ShopPriceAdjustmentRequest
{
    public int CategoryId { get; set; }
    public bool IncludeDescendants { get; set; }
    public bool Increase { get; set; } = true;
    public decimal Percent { get; set; }
    public DateTime EffectiveDate { get; set; }
}

public class ShopPriceAdjustmentCommand : ShopPriceAdjustmentRequest
{
    public Guid OperationId { get; set; }
    public string PreviewHash { get; set; } = "";
}

public class ShopPriceAdjustmentRow
{
    public int ProductId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
    public decimal? PreviousPrice { get; set; }
    public decimal? NewPrice { get; set; }
    public string? Reason { get; set; }
}

public class ShopPriceAdjustmentPreview
{
    public List<ShopPriceAdjustmentRow> Rows { get; set; } = new();
    public string PreviewHash { get; set; } = "";
}

public class ShopPriceHistoryItem
{
    public long Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public DateTime EffectiveDate { get; set; }
    public decimal Price { get; set; }
    public decimal? PreviousPrice { get; set; }
    public string Source { get; set; } = "";
    public int? PurchaseTransactionId { get; set; }
    public Guid? OperationId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}

/// <summary>Nullable price distinguishes an unknown historical price from zero.</summary>
public class DatedSalePriceDto
{
    /// <summary>قیمت از تاریخچهٔ قیمت همان روز (یا آخرین قیمت پیش از آن) آمده است.</summary>
    public const string HistorySource = "history";
    /// <summary>برای آن تاریخ قیمتی ثبت نشده و «قیمت فروش تعریف کالا» مبنا قرار گرفته است.</summary>
    public const string CatalogSource = "catalog";

    public decimal? Price { get; set; }
    public DateTime? EffectiveDate { get; set; }
    /// <summary>history | catalog؛ خالی یعنی هیچ قیمتی پیدا نشد.</summary>
    public string? Source { get; set; }
    /// <summary>قیمت از تعریف کالا آمده، نه از تاریخچه؛ کلاینت باید این را به کاربر بگوید.</summary>
    public bool IsCatalogFallback => Source == CatalogSource;
}

/// <summary>Profit-authorized explanation of the exact purchase used at an invoice date.</summary>
public class ShopMinimumProfitBasisDto
{
    public int? CompanyId { get; set; }
    public bool PolicyConfigured { get; set; }
    public bool MinimumProfitEnabled { get; set; }
    public decimal MinimumMarkupPercent { get; set; }
    public int? PurchaseId { get; set; }
    public string? PurchaseNumber { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public decimal? PurchaseQuantity { get; set; }
    public decimal? PurchaseGross { get; set; }
    public decimal? PurchaseDiscount { get; set; }
    public decimal? NetUnitCost { get; set; }
    public decimal? MinimumUnitRevenueBeforeSaleDiscountAndCommission { get; set; }
    public string? Reason { get; set; }
}
