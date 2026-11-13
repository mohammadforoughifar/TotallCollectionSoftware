namespace Inventory.Api.Data;
/// <summary>Independent shop print settings, never office/company-profile settings.</summary>
public class ShopPrintSetting : ICompanyOwned
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public string BanksJson { get; set; } = "[]";
}
