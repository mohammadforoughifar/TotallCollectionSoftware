using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>تاریخچه تغییر سررسید؛ حتی تغییر با تعدیل صفر نیز ثبت می‌شود.</summary>
public class SaleChequeReschedule : ICompanyOwned
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public Guid OperationId { get; set; }
    public int ChequeId { get; set; }
    public Cheque Cheque { get; set; } = null!;
    public int SaleChequeChargeId { get; set; }
    public SaleChequeCharge SaleChequeCharge { get; set; } = null!;
    public int? SaleChequeChargeEventId { get; set; }
    public SaleChequeChargeEvent? SaleChequeChargeEvent { get; set; }
    public DateTime PreviousDueDate { get; set; }
    public DateTime NewDueDate { get; set; }
    public DateTime Date { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal RatePercent { get; set; }
    public int PeriodDays { get; set; }
    public decimal IncomeDelta { get; set; }
    [MaxLength(120)] public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
