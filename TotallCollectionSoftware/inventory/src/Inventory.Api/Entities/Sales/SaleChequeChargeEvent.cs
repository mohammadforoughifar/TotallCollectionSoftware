using Inventory.Shared;

namespace Inventory.Api.Data;

/// <summary>
/// گردش عملیاتی تغییر درآمد چک؛ پیوند سند ERP فقط برای سوابق تاریخی نگه داشته می‌شود.
/// </summary>
public class SaleChequeChargeEvent : ICompanyOwned
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public Guid OperationId { get; set; }
    public int SaleChequeChargeId { get; set; }
    public SaleChequeCharge SaleChequeCharge { get; set; } = null!;
    /// <summary>پیوند اختیاری سند تاریخی ERP؛ گردش‌های جدید فروشگاهی به آن وابسته نیستند.</summary>
    public int? AccVoucherId { get; set; }
    public AccVoucher? AccVoucher { get; set; }
    public SaleChequeChargeEventKind Kind { get; set; }
    public DateTime Date { get; set; }
    /// <summary>تغییر امضادار درآمد؛ هنگام کاهش و برگشت منفی است.</summary>
    public decimal IncomeDelta { get; set; }
}
