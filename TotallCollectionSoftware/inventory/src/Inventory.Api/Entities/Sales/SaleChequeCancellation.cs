using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>ابطال کامل فاکتور دارای تاخیر تادیه چک با سند معکوس، بازگشت موجودی و ابطال چک‌ها بدون حذف سابقه.</summary>
public class SaleChequeCancellation : ICompanyOwned
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public Guid OperationId { get; set; }
    public int SaleTransactionId { get; set; }
    public Transaction SaleTransaction { get; set; } = null!;
    public int SaleChequeChargeId { get; set; }
    public SaleChequeCharge SaleChequeCharge { get; set; } = null!;
    public int? ReturnTransactionId { get; set; }
    public Transaction? ReturnTransaction { get; set; }
    public int AccVoucherId { get; set; }
    public AccVoucher AccVoucher { get; set; } = null!;
    public int? SaleChequeChargeEventId { get; set; }
    public SaleChequeChargeEvent? SaleChequeChargeEvent { get; set; }
    public int? RefundTrsVoucherId { get; set; }
    public TrsVoucher? RefundTrsVoucher { get; set; }
    public int SalesAccountId { get; set; }
    public int? VatAccountId { get; set; }
    public int IncomeAccountId { get; set; }
    public int PartyAccountId { get; set; }
    public int? CashLedgerAccountId { get; set; }
    public DateTime Date { get; set; }
    public decimal ReversedGoodsAmount { get; set; }
    public decimal ReversedVatAmount { get; set; }
    /// <summary>تغییر امضادار درآمد چک در ابطال (برای حذف سود باقی‌مانده منفی، و در صورت بستانکاری اصلاحی مثبت است).</summary>
    public decimal ReversedIncomeDelta { get; set; }
    public decimal RefundedCashAmount { get; set; }
    [MaxLength(200)] public string? Reason { get; set; }
    [MaxLength(120)] public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
