using System.ComponentModel.DataAnnotations;
using Inventory.Shared;

namespace Inventory.Api.Data;

/// <summary>تعیین‌تکلیف بستانکاری مشتری از طریق پرداخت وجه یا مصرف در فاکتور فروش دیگر.</summary>
public class SaleChequeCreditSettlement : ICompanyOwned
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public Guid OperationId { get; set; }
    public SaleChequeCreditSettlementKind Kind { get; set; }
    public int SaleTransactionId { get; set; }
    public Transaction SaleTransaction { get; set; } = null!;
    public int? TargetSaleTransactionId { get; set; }
    public Transaction? TargetSaleTransaction { get; set; }
    public int SaleChequeChargeId { get; set; }
    public SaleChequeCharge SaleChequeCharge { get; set; } = null!;
    public int? TrsAccountId { get; set; }
    public TrsAccount? TrsAccount { get; set; }
    public int? TreasuryLedgerAccountId { get; set; }
    public int PartyAccountId { get; set; }
    public int? TargetPartyAccountId { get; set; }
    public int AccVoucherId { get; set; }
    public AccVoucher AccVoucher { get; set; } = null!;
    public int? TrsVoucherId { get; set; }
    public TrsVoucher? TrsVoucher { get; set; }
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    [MaxLength(200)] public string? Note { get; set; }
    [MaxLength(120)] public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
