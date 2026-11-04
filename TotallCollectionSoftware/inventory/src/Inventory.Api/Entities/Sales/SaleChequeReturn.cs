using System.ComponentModel.DataAnnotations;
using Inventory.Shared;

namespace Inventory.Api.Data;

/// <summary>گردش برگشت کالای فاکتور سوددار؛ سند و اقلام اولیه حفظ می‌شوند.</summary>
public class SaleChequeReturn : ICompanyOwned
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public Guid OperationId { get; set; }
    public int SaleTransactionId { get; set; }
    public Transaction SaleTransaction { get; set; } = null!;
    public int ReturnTransactionId { get; set; }
    public Transaction ReturnTransaction { get; set; } = null!;
    public int SaleChequeChargeId { get; set; }
    public SaleChequeCharge SaleChequeCharge { get; set; } = null!;
    public int AccVoucherId { get; set; }
    public AccVoucher AccVoucher { get; set; } = null!;
    public int? SaleChequeChargeEventId { get; set; }
    public SaleChequeChargeEvent? SaleChequeChargeEvent { get; set; }
    public int SalesAccountId { get; set; }
    public int? VatAccountId { get; set; }
    public int IncomeAccountId { get; set; }
    public int PartyAccountId { get; set; }
    public DateTime Date { get; set; }
    public decimal GoodsAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal IncomeAmount { get; set; }
    /// <summary>دورهٔ محاسبه پس از آخرین تعدیل؛ null یعنی پیش از اولین تعدیل.</summary>
    public int? EpochRescheduleId { get; set; }
    public decimal EpochBase { get; set; }
    public decimal EpochIncome { get; set; }
    public decimal PreviouslyReturnedBaseInEpoch { get; set; }
    public decimal BalanceAfter { get; set; }
    public decimal UncollectedChequeExcess { get; set; }
    public SaleChequeReturnDecision Decision { get; set; }
    [MaxLength(120)] public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<SaleChequeReturnLine> Lines { get; set; } = new();
}

public class SaleChequeReturnLine : ICompanyOwned
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public int SaleChequeReturnId { get; set; }
    public SaleChequeReturn SaleChequeReturn { get; set; } = null!;
    public int OriginalLineId { get; set; }
    public TransactionLine OriginalLine { get; set; } = null!;
    public int ReturnLineId { get; set; }
    public TransactionLine ReturnLine { get; set; } = null!;
    public decimal Quantity { get; set; }
    public decimal GoodsAmount { get; set; }
    public decimal VatAmount { get; set; }
}
