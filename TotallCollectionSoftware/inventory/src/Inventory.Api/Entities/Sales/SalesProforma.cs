using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

// A quotation is deliberately separate from Transaction and its stock/ledger effects.
public sealed class SalesProforma
{
    [Key, MaxLength(32)] public string Id { get; set; } = "";
    public int CompanyId { get; set; }
    [MaxLength(40)] public string Number { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string DocumentJson { get; set; } = "";
    public int? SaleOrderId { get; set; }
}
