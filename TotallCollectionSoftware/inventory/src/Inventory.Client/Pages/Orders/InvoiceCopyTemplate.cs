using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Pages.Orders;

/// <summary>
/// Only the reusable commercial details of an invoice. Transaction/line IDs,
/// payment instruments, settlements and cheque-income history never enter a copy.
/// </summary>
public sealed class InvoiceCopyTemplate
{
    public TransactionType Type { get; init; }
    public int WarehouseId { get; init; }
    public string? WarehouseName { get; init; }
    public int PartyId { get; init; }
    public string? PartyName { get; init; }
    public string Description { get; init; } = "";
    public List<InvoiceCopyLine> Lines { get; init; } = new();

    public static InvoiceCopyTemplate From(Order source) => new()
    {
        Type = source.Type,
        WarehouseId = source.WarehouseId,
        WarehouseName = source.WarehouseName,
        PartyId = source.PartyId,
        PartyName = source.PartyName,
        Description = source.Description,
        Lines = source.Lines.Select(line => new InvoiceCopyLine
        {
            ProductId = line.ProductId,
            ProductName = line.ProductName,
            Unit = line.Unit,
            IsService = line.IsService,
            Quantity = line.Quantity,
            Price = line.Price,
            DiscountPercent = line.DiscountPercent,
            DiscountAmount = line.DiscountAmount,
            DiscountIsAmount = line.DiscountIsAmount,
            VatValue = line.VatValue
        }).ToList()
    };
}

public sealed class InvoiceCopyLine
{
    public int ProductId { get; init; }
    public string ProductName { get; init; } = "";
    public string Unit { get; init; } = "";
    public bool IsService { get; init; }
    public decimal Quantity { get; init; }
    public decimal Price { get; init; }
    public decimal DiscountPercent { get; init; }
    public decimal DiscountAmount { get; init; }
    public bool DiscountIsAmount { get; init; }
    public decimal VatValue { get; init; }
}
