using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>سابقه تعویض چک وصول‌نشده با تأیید صریح مشتری؛ چک قبلی حذف نمی‌شود.</summary>
public class SaleChequeReplacement : ICompanyOwned
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public Guid OperationId { get; set; }
    public int TransactionId { get; set; }
    public Transaction Transaction { get; set; } = null!;
    public int SaleChequeChargeId { get; set; }
    public SaleChequeCharge SaleChequeCharge { get; set; } = null!;
    public int OldChequeId { get; set; }
    public Cheque OldCheque { get; set; } = null!;
    public int NewChequeId { get; set; }
    public Cheque NewCheque { get; set; } = null!;
    public DateTime Date { get; set; }
    public decimal OldAmount { get; set; }
    public decimal NewAmount { get; set; }
    [MaxLength(200)] public string? Reason { get; set; }
    [MaxLength(120)] public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
