namespace Inventory.Shared.Dtos;

public enum TradeInUseKind { ApplyToSale = 1, Refund = 2 }

public class ShopTradeInDto
{
    public int Id { get; set; }
    public Guid OperationId { get; set; }
    public string Number { get; set; } = "";
    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public string DeviceType { get; set; } = "";
    public string? DeviceModel { get; set; }
    public string? SerialNumber { get; set; }
    public string? Note { get; set; }
    public decimal Value { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public decimal UsedAmount { get; set; }
    public decimal AvailableCredit => FinalizedAt.HasValue ? Value - UsedAmount : 0;
    public List<ShopTradeInUseDto> Uses { get; set; } = new();
}

public class ShopTradeInUseDto
{
    public int Id { get; set; }
    public TradeInUseKind Kind { get; set; }
    public int? TargetSaleId { get; set; }
    public string? TargetSaleNumber { get; set; }
    public string? TrsAccountName { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public string? CreatedBy { get; set; }
}

public class ShopTradeInFinalizationCommand { public DateTime Date { get; set; } }

public class ShopTradeInUseCommand
{
    public Guid OperationId { get; set; }
    public TradeInUseKind Kind { get; set; }
    public int? TargetSaleId { get; set; }
    public int? TrsAccountId { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public string? Note { get; set; }
}

public class ShopTradeInTargetSaleDto
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public DateTime Date { get; set; }
    public decimal TotalAmount { get; set; }
}
