namespace Inventory.Shared.Dtos;

/// <summary>اطلاعات غیرمالی لازم برای ثبت پیش‌فاکتور فروش؛ پرداخت در زمان تبدیل جداگانه دریافت می‌شود.</summary>
public sealed class SalesProformaCommand
{
    public int WarehouseId { get; set; }
    public int PartyId { get; set; }
    public DateTime Date { get; set; }
    public string? Description { get; set; }
    public int? ReferrerId { get; set; }
    public List<SalesProformaLine> Lines { get; set; } = new();
}

/// <summary>مشخصات و مبالغ سطر پیش‌فاکتور؛ مبالغ با قواعد فاکتور فروش محاسبه می‌شوند.</summary>
public sealed class SalesProformaLine
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string Unit { get; set; } = "";
    public bool IsService { get; set; }
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public bool DiscountIsAmount { get; set; }
    public decimal VatValue { get; set; }

    public decimal Total => ShopInvoiceAmounts.Total(Quantity, Price, DiscountPercent, DiscountAmount, DiscountIsAmount, VatValue);
}

/// <summary>نمای پیش‌فاکتور؛ شناسهٔ فاکتور تنها پس از تبدیل موفق ثبت می‌شود.</summary>
public sealed class SalesProformaDto
{
    public Guid Id { get; set; }
    public int CompanyId { get; set; }
    public string Number { get; set; } = "";
    public string PartyName { get; set; } = "";
    public string? PartyPhone { get; set; }
    public string? PartyAddress { get; set; }
    public string? PartyTaxId { get; set; }
    public string WarehouseName { get; set; } = "";
    public SalesProformaCommand Document { get; set; } = new();
    public int? SaleOrderId { get; set; }
}
