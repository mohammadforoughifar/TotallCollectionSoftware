namespace Inventory.Api.Data;

/// <summary>دریافت و لغو دریافت قسط فاکتور دارای تاخیر تادیه چک با سند حسابداری مستقل.</summary>
public class SaleChequeInstallmentReceipt : ICompanyOwned
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public Guid OperationId { get; set; }
    public int InstallmentId { get; set; }
    public InstallmentLine Installment { get; set; } = null!;
    public int TransactionId { get; set; }
    public Transaction Transaction { get; set; } = null!;
    public int TrsAccountId { get; set; }
    public TrsAccount TrsAccount { get; set; } = null!;
    public int BankAccountId { get; set; }
    public int PartyAccountId { get; set; }
    public int AccVoucherId { get; set; }
    public AccVoucher AccVoucher { get; set; } = null!;
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public Guid? ReversalOperationId { get; set; }
    public int? ReversalVoucherId { get; set; }
    public AccVoucher? ReversalVoucher { get; set; }
    public DateTime? ReversalDate { get; set; }
}
