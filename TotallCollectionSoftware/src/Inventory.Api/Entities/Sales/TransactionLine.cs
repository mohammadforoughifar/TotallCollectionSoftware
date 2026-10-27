using System.ComponentModel.DataAnnotations;
using Inventory.Shared;
namespace Inventory.Api.Data;

public class TransactionLine : ICompanyOwned
{
    public int Id { get; set; }
    /// <summary>شرکت مالک رکورد — دادهٔ هر شرکت مستقل نگه‌داری می‌شود (فیلتر خودکار).</summary>
    public int? CompanyId { get; set; }
    public int TransactionId { get; set; }
    public int ProductId { get; set; }
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }

    /// <summary>بهای تمام‌شدهٔ قطعیِ واحد برای سطرهایی مثل فاکتور تعمیر؛ برای فروش عادی خالی می‌ماند.</summary>
    public decimal? UnitCostSnapshot { get; set; }

    [MaxLength(300)]
    public string? Description { get; set; }
}
