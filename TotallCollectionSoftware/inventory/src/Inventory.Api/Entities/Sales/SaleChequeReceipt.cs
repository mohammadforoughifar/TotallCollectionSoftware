namespace Inventory.Api.Data;

/// <summary>وصول واقعی چک فروش به صندوق/بانک انتخاب‌شده؛ پیوندهای حسابداری قدیمی برای تاریخچه نگهداری می‌شوند.</summary>
public class SaleChequeReceipt : ICompanyOwned
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public Guid OperationId { get; set; }
    public int ChequeId { get; set; }
    public Cheque Cheque { get; set; } = null!;
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
    /// <summary>لغو وصول با گردش معکوس مستقل؛ سابقه وصول اولیه حذف نمی‌شود.</summary>
    public Guid? ReversalOperationId { get; set; }
    public int? ReversalVoucherId { get; set; }
    public AccVoucher? ReversalVoucher { get; set; }
    public DateTime? ReversalDate { get; set; }
    public bool PreviousAccountKnown { get; set; }
    public int? PreviousTrsAccountId { get; set; }
}
