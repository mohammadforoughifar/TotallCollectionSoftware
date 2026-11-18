namespace Inventory.Shared.Dtos;
public class ShopVisitorDiscountDto
{
    public int CompanyId { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public decimal MaxDiscountPercent { get; set; }
}
