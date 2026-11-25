namespace Inventory.Shared.Dtos;

public enum WarrantyDecision { Unspecified = 0, Accepted = 1, Rejected = 2 }

public class ShopWarrantyDto
{
    public int Id { get; set; }
    public Guid OperationId { get; set; }
    public string Number { get; set; } = "";
    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public int? TechnicianId { get; set; }
    public string? TechnicianName { get; set; }
    public int WarehouseId { get; set; }
    public string DeviceType { get; set; } = "";
    public string? DeviceModel { get; set; }
    public string? SerialNumber { get; set; }
    public string? ProblemDescription { get; set; }
    public string? Accessories { get; set; }
    public string? Note { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public int? StockTransactionId { get; set; }
    public int ChargePolicyVersion { get; set; }
    public int? ChargeTransactionId { get; set; }
    public string? ChargeTransactionNumber { get; set; }
    public decimal SettledAmount { get; set; }
    public decimal CustomerAmount => FinalizedAt.HasValue && ChargePolicyVersion == 0 ? 0m :
        Items.Sum(i => i.CustomerAmount);
    public decimal UnpaidAmount => FinalizedAt.HasValue ? Math.Max(0m, CustomerAmount - SettledAmount) : 0m;
    // The legacy top-level device fields mirror the first device for older clients.
    public List<ShopWarrantyDeviceDto> Devices { get; set; } = new();
    public List<ShopWarrantyItemDto> Items { get; set; } = new();
    public decimal InternalCost => Items.Where(i => i.ProductId is null or 0 ? i.Performed : i.Supplied)
        .Sum(i => decimal.Round(i.Quantity * i.UnitCost, 0, MidpointRounding.AwayFromZero));
}

public class ShopWarrantyDeviceDto
{
    public int Id { get; set; }
    public string DeviceType { get; set; } = "";
    public string? DeviceModel { get; set; }
    public string? SerialNumber { get; set; }
    public string? ProblemDescription { get; set; }
    public string? Accessories { get; set; }
}

public class ShopWarrantyItemDto
{
    public int Id { get; set; }
    public string Description { get; set; } = "";
    public int? ProductId { get; set; }
    public string? ProductName { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal UnitCost { get; set; }
    public decimal? CustomerUnitPrice { get; set; }
    public WarrantyDecision Decision { get; set; }
    public bool Supplied { get; set; }
    public bool Performed { get; set; } = true;
    public bool IsChargeable => Decision == WarrantyDecision.Rejected && (ProductId is null or 0 ? Performed : Supplied);
    public decimal CustomerAmount => IsChargeable && CustomerUnitPrice.HasValue
        ? decimal.Round(Quantity * CustomerUnitPrice.Value, 0, MidpointRounding.AwayFromZero) : 0m;
}

public class ShopWarrantyLookupsDto
{
    public List<LookupItem> Customers { get; set; } = new();
    public List<LookupItem> Products { get; set; } = new();
    public List<LookupItem> Warehouses { get; set; } = new();
    public List<LookupItem> Technicians { get; set; } = new();

    /// <summary>
    /// قاعدهٔ واحد هر کالا — کلاینت با آن «نوع مقدار» را نشان می‌دهد و مقدار اعشاری را
    /// برای واحدهای صحیح (مثل «عدد») مسدود می‌کند.
    /// </summary>
    public List<ShopWarrantyProductRuleDto> ProductRules { get; set; } = new();
}

/// <summary>قاعدهٔ مقدار کالا در پذیرش گارانتی: واحد و مجاز/غیرمجاز بودن اعشار.</summary>
public class ShopWarrantyProductRuleDto
{
    public int ProductId { get; set; }
    public string Unit { get; set; } = "";

    /// <summary>null یعنی نوع مقدار این واحد هنوز در «واحدهای شمارش» تعیین نشده است.</summary>
    public bool? AllowsFractionalQuantity { get; set; }

    /// <summary>متن فارسی نوع مقدار برای نمایش در فرم پذیرش.</summary>
    public string QuantityKindText => AllowsFractionalQuantity switch
    {
        true => "مقدار اعشاری مجاز",
        false => "فقط مقدار صحیح",
        null => "نوع مقدار نامشخص"
    };
}

/// <summary>موجودی یک کالا در انبار انتخاب‌شدهٔ پذیرش گارانتی (برای نمایش و هشدار کمبود).</summary>
public class ShopWarrantyStockItemDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal Quantity { get; set; }
}

/// <summary>هشدار کمبود موجودی یک ردیف گارانتی در برابر موجودی انبار.</summary>
public class ShopWarrantyStockWarningDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal Required { get; set; }
    public decimal Available { get; set; }
    public decimal Shortage => Required > Available ? Required - Available : 0m;

    public string Message =>
        $"موجودی «{ProductName}» در انبار کافی نیست — موردنیاز {Required:0.##}، موجود {Available:0.##}، کمبود {Shortage:0.##}.";
}

/// <summary>درخواست بررسی موجودی ردیف‌های یک پذیرش گارانتی در انبار انتخاب‌شده.</summary>
public class ShopWarrantyStockCheckCommand
{
    public int WarehouseId { get; set; }
    public List<ShopWarrantyItemDto>? Items { get; set; } = new();
}
