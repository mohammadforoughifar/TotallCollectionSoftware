namespace Inventory.Shared.Dtos;

public enum ExpenseChequeKind { Issued = 1, Received = 2 }
public enum ExpenseChequeState { Outstanding = 1, Cleared = 2, Returned = 3 }

public class ExpenseChequeDto
{
    public int Id { get; set; }
    public ExpenseChequeKind Kind { get; set; }
    public ExpenseChequeState State { get; set; }
    public int? SourceChequeId { get; set; }
    public int? IssuingAccountId { get; set; }
    public string? IssuingAccountName { get; set; }
    public string Number { get; set; } = "";
    public string? BankName { get; set; }
    public DateTime DueDate { get; set; }
    public decimal Amount { get; set; }
    public string Recipient { get; set; } = "";
    public DateTime HandedAt { get; set; }
    public DateTime? SettledAt { get; set; }
    public string? Reason { get; set; }
}

public class ExpenseChequeCreateCommand
{
    public Guid OperationId { get; set; }
    public ExpenseChequeKind Kind { get; set; }
    public ExpenseDto Expense { get; set; } = new();
    public int? SourceChequeId { get; set; }
    public int? IssuingAccountId { get; set; }
    public string Number { get; set; } = "";
    public DateTime DueDate { get; set; }
    public DateTime HandedAt { get; set; }
    public string Recipient { get; set; } = "";
}

public class ExpenseChequeStatusCommand
{
    public Guid OperationId { get; set; }
    public DateTime Date { get; set; }
    public ExpenseChequeState State { get; set; }
    public string? Reason { get; set; }
}

public class ExpenseChequeCreatedResult { public int Id { get; set; } }

/// <summary>تسویه کامل هزینه پس از برگشت همه چک‌ها.</summary>
public class ExpenseReplacementPaymentCommand
{
    public Guid OperationId { get; set; }
    public DateTime Date { get; set; }
    public Inventory.Shared.CashType PayType { get; set; }
    public int TrsAccountId { get; set; }
}
