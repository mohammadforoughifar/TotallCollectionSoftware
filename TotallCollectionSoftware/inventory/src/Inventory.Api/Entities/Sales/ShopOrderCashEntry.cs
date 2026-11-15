using System.ComponentModel.DataAnnotations;
using Inventory.Shared;

namespace Inventory.Api.Data;

/// <summary>
/// گردش نقدی فاکتور فروشگاهی. TransactionId عمداً کلید خارجی نیست: پس از حذف
/// فاکتور هم اصل پرداخت و رویداد معکوس آن برای حسابرسی باقی می‌مانند.
/// </summary>
public class ShopOrderCashEntry : ICompanyOwned
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public int TransactionId { get; set; }
    [MaxLength(30)] public string OrderNumber { get; set; } = "";
    public TransactionType OrderType { get; set; }
    public int? PartyId { get; set; }
    [MaxLength(200)] public string? PartyName { get; set; }
    public bool IsOpening { get; set; }
    public int TrsAccountId { get; set; }
    public TrsAccount TrsAccount { get; set; } = null!;
    /// <summary>در تطبیق قدیمی، سند دستی موجود؛ اصل مبلغ از آن سند شمرده می‌شود و در اینجا دوباره شمرده نمی‌شود.</summary>
    public int? MatchedTrsVoucherId { get; set; }
    public TrsVoucher? MatchedTrsVoucher { get; set; }
    public CashType CashType { get; set; }
    [MaxLength(100)] public string? Reference { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public Guid OperationId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    [MaxLength(120)] public string? CreatedBy { get; set; }
    public DateTime? ReversalDate { get; set; }
    public Guid? ReversalOperationId { get; set; }
    public DateTime? ReversedAt { get; set; }
    [MaxLength(120)] public string? ReversedBy { get; set; }
    [MaxLength(200)] public string? ReversalReason { get; set; }
}
