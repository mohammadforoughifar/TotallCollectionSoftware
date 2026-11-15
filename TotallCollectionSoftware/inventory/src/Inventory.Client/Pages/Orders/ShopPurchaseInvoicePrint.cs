using Inventory.Shared.Dtos;
namespace Inventory.Client.Pages.Orders;

/// <summary>Full approved purchase layout, not the legacy sales/purchase print builder.</summary>
public static class ShopPurchaseInvoicePrint
{
    public static string Build(Order order, CompanyProfileDto? profile, string? paperSize = null)
        => ShopSaleInvoicePrint.BuildPurchase(order, profile, paperSize);
}
