using System.ComponentModel.DataAnnotations;
using Inventory.Shared.Dtos;

namespace Inventory.Api.Data;

/// <summary>چک هزینه مستقل از فاکتور؛ منبع دریافتی، پرداخت و وضعیت همگی قابل ردیابی‌اند.</summary>
public class ShopExpenseCheque : ICompanyOwned
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public Guid OperationId { get; set; }
    public int ExpenseId { get; set; }
    public Expense Expense { get; set; } = null!;
    public ExpenseChequeKind Kind { get; set; }
    public ExpenseChequeState State { get; set; }
    public int? SourceChequeId { get; set; }
    public Cheque? SourceCheque { get; set; }
    public int? SourceTransactionId { get; set; }
    public Transaction? SourceTransaction { get; set; }
    public int? IssuingAccountId { get; set; }
    public TrsAccount? IssuingAccount { get; set; }
    public int? SettlementVoucherId { get; set; }
    public TrsVoucher? SettlementVoucher { get; set; }
    [MaxLength(50)] public string Number { get; set; } = "";
    [MaxLength(200)] public string? BankName { get; set; }
    [MaxLength(150)] public string Recipient { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime HandedAt { get; set; }
    public Guid? StatusOperationId { get; set; }
    public DateTime? SettledAt { get; set; }
    [MaxLength(200)] public string? Reason { get; set; }
    [MaxLength(120)] public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    [MaxLength(120)] public string? StatusBy { get; set; }
    public DateTime? StatusAt { get; set; }
}
