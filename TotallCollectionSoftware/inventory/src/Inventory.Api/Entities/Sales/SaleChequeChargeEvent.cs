using Inventory.Shared;

namespace Inventory.Api.Data;

/// <summary>
/// پیوند تغییر درآمد چک به سند قطعی حسابداری. هر رویداد فقط یک سند دارد؛
/// این جدول خود سند اصلاح، دریافت یا برگشت ایجاد نمی‌کند.
/// </summary>
public class SaleChequeChargeEvent : ICompanyOwned
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public Guid OperationId { get; set; }
    public int SaleChequeChargeId { get; set; }
    public SaleChequeCharge SaleChequeCharge { get; set; } = null!;
    public int AccVoucherId { get; set; }
    public AccVoucher AccVoucher { get; set; } = null!;
    public SaleChequeChargeEventKind Kind { get; set; }
    public DateTime Date { get; set; }
    /// <summary>تغییر امضادار درآمد؛ هنگام کاهش و برگشت منفی است.</summary>
    public decimal IncomeDelta { get; set; }
}
