namespace Inventory.Shared;

/// <summary>Minimum markup is based on the last purchase; VAT and cheque interest are excluded.</summary>
public static class ShopMinimumProfit
{
    /// <summary>
    /// Compare against the actual net purchase line as a ratio, without rounding a
    /// recurring per-unit cost down. The stored gross already embodies invoice rounding.
    /// </summary>
    public static bool MeetsPurchase(decimal revenueAfterDiscount, decimal quantity, decimal purchaseNet,
        decimal purchaseQuantity, decimal markupPercent, decimal commissionPercent, decimal commissionUnitCost)
    {
        if (quantity <= 0 || purchaseNet <= 0 || purchaseQuantity <= 0 || markupPercent < 0 ||
            commissionPercent < 0 || commissionPercent > 100 || commissionUnitCost < 0) return false;
        var commissionProfit = decimal.Round(revenueAfterDiscount - quantity * commissionUnitCost, 2);
        var commission = SaleReferrerCommission.Goods(commissionProfit, commissionPercent);
        // Division is intentionally avoided: e.g. 200 / 3 remains exact at the floor.
        return (revenueAfterDiscount - commission) * purchaseQuantity * 100m >=
               purchaseNet * quantity * (100m + markupPercent);
    }

    public static bool Meets(decimal revenueAfterDiscount, decimal quantity, decimal lastPurchaseUnitCost,
        decimal markupPercent, decimal commissionPercent, decimal commissionUnitCost)
    {
        if (quantity <= 0 || lastPurchaseUnitCost <= 0 || markupPercent < 0 || commissionPercent < 0 || commissionPercent > 100 || commissionUnitCost < 0)
            return false;
        // Purchase discounts may yield a recurring unit cost. Keep micro-rial precision
        // until the full line is calculated, then round the required money up, never down.
        var basis = decimal.Round(quantity * lastPurchaseUnitCost, 6);
        var requiredRevenue = decimal.Ceiling((basis + basis * markupPercent / 100m) * 100m) / 100m;
        // Match the existing invoice report: line profit is rounded before commission.
        var commissionProfit = decimal.Round(revenueAfterDiscount - quantity * commissionUnitCost, 2);
        var commission = SaleReferrerCommission.Goods(commissionProfit, commissionPercent);
        return revenueAfterDiscount - commission >= requiredRevenue;
    }
}
