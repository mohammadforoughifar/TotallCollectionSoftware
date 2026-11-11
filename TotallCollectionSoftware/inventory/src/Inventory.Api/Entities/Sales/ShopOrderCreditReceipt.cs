using System.ComponentModel.DataAnnotations;
using Inventory.Shared;

namespace Inventory.Api.Data;

/// <summary>دریافت یا پرداخت نسیهٔ فاکتور و لغو آن در دفتر مستقل فروشگاه.</summary>
public class ShopOrderCreditReceipt : ICompanyOwned
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public Guid OperationId { get; set; }
    public int TransactionId { get; set; }
    public Transaction Transaction { get; set; } = null!;
    [MaxLength(30)] public string OrderNumber { get; set; } = "";
    public TransactionType OrderType { get; set; }
    public int PartyId { get; set; }
    [MaxLength(200)] public string? PartyName { get; set; }
    public int TrsAccountId { get; set; }
    public TrsAccount TrsAccount { get; set; } = null!;
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    [MaxLength(120)] public string? CreatedBy { get; set; }
    public Guid? ReversalOperationId { get; set; }
    public DateTime? ReversalDate { get; set; }
    public DateTime? ReversedAt { get; set; }
    [MaxLength(120)] public string? ReversedBy { get; set; }
}
