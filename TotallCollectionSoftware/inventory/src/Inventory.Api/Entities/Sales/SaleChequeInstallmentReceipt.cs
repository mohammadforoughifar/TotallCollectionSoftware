namespace Inventory.Api.Data;

/// <summary>گردش دریافت/پرداخت و لغو قسط خرید یا فروش در دفتر فروشگاهی.</summary>
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
    public int? BankAccountId { get; set; }
    public int? PartyAccountId { get; set; }
    /// <summary>پیوند اختیاری سند تاریخی ERP؛ گردش‌های جدید فروشگاهی به آن وابسته نیستند.</summary>
    public int? AccVoucherId { get; set; }
    public AccVoucher? AccVoucher { get; set; }
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public Guid? ReversalOperationId { get; set; }
    public int? ReversalVoucherId { get; set; }
    public AccVoucher? ReversalVoucher { get; set; }
    public DateTime? ReversalDate { get; set; }
}
