using System.ComponentModel.DataAnnotations;
using Inventory.Shared;

namespace Inventory.Api.Data;

/// <summary>تاریخچه غیرقابل‌ویرایش وضعیت چک فروش دارای ردیف مالی.</summary>
public class SaleChequeStatusEvent : ICompanyOwned
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public Guid OperationId { get; set; }
    public int ChequeId { get; set; }
    public Cheque Cheque { get; set; } = null!;
    public ChequeStatus PreviousStatus { get; set; }
    public ChequeStatus NewStatus { get; set; }
    public DateTime CreatedAt { get; set; }
    [MaxLength(120)] public string? CreatedBy { get; set; }
    [MaxLength(200)] public string? Reason { get; set; }
}
