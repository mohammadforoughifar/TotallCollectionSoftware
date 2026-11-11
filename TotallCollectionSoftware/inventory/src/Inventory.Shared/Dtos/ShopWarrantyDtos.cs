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
    // The legacy top-level device fields mirror the first device for older clients.
    public List<ShopWarrantyDeviceDto> Devices { get; set; } = new();
    public List<ShopWarrantyItemDto> Items { get; set; } = new();
    public decimal InternalCost => Items.Where(i => i.ProductId is null or 0 || i.Supplied)
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
    public WarrantyDecision Decision { get; set; }
    public bool Supplied { get; set; }
}

public class ShopWarrantyLookupsDto
{
    public List<LookupItem> Customers { get; set; } = new();
    public List<LookupItem> Products { get; set; } = new();
    public List<LookupItem> Warehouses { get; set; } = new();
    public List<LookupItem> Technicians { get; set; } = new();
}
