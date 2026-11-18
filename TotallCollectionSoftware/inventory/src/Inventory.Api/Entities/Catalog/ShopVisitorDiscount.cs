namespace Inventory.Api.Data;

/// <summary>سقف تخفیف هر کاربر ویزیتور در هر شرکت، مستقل از معرف و پورسانت.</summary>
public class ShopVisitorDiscount
{
    public int CompanyId { get; set; }
    public int UserId { get; set; }
    public decimal MaxDiscountPercent { get; set; }
}
