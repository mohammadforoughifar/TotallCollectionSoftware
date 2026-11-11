using System.ComponentModel.DataAnnotations;
using Inventory.Shared.Dtos;

namespace Inventory.Api.Data;

/// <summary>پذیرش گارانتی مستقل از فاکتور فروش و مبلغ مشتری.</summary>
public class ShopWarranty : ICompanyOwned
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public Guid OperationId { get; set; }
    [MaxLength(30)] public string Number { get; set; } = "";
    public int PartyId { get; set; }
    public Party Party { get; set; } = null!;
    public int? TechnicianId { get; set; }
    public Technician? Technician { get; set; }
    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;
    [MaxLength(100)] public string DeviceType { get; set; } = "";
    [MaxLength(200)] public string? DeviceModel { get; set; }
    [MaxLength(100)] public string? SerialNumber { get; set; }
    [MaxLength(1000)] public string? ProblemDescription { get; set; }
    [MaxLength(500)] public string? Accessories { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public int? StockTransactionId { get; set; }
    public Transaction? StockTransaction { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    [MaxLength(120)] public string? CreatedBy { get; set; }
    [MaxLength(120)] public string? FinalizedBy { get; set; }
    public List<ShopWarrantyDevice> Devices { get; set; } = new();
    public List<ShopWarrantyItem> Items { get; set; } = new();
}

/// <summary>دستگاه‌های متعلق به یک پذیرش؛ قطعات بین کل پذیرش مشترک‌اند.</summary>
public class ShopWarrantyDevice : ICompanyOwned
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public int ShopWarrantyId { get; set; }
    public ShopWarranty ShopWarranty { get; set; } = null!;
    public int Position { get; set; }
    [MaxLength(100)] public string DeviceType { get; set; } = "";
    [MaxLength(200)] public string? DeviceModel { get; set; }
    [MaxLength(100)] public string? SerialNumber { get; set; }
    [MaxLength(1000)] public string? ProblemDescription { get; set; }
    [MaxLength(500)] public string? Accessories { get; set; }
}

public class ShopWarrantyItem : ICompanyOwned
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public int ShopWarrantyId { get; set; }
    public ShopWarranty ShopWarranty { get; set; } = null!;
    [MaxLength(300)] public string Description { get; set; } = "";
    public int? ProductId { get; set; }
    public Product? Product { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public WarrantyDecision Decision { get; set; }
    public bool Supplied { get; set; }
}
