using Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Api.Services.Repairs;

/// <summary>هزینه و درآمد قطعی گزارش تعمیرات، با حفظ محاسبه سابق اسناد قدیمی.</summary>
internal static class RepairReportTotals
{
    public static (decimal Revenue, decimal PartsCost, decimal LaborCost) Calculate(RepairOrder repair, HashSet<int> serviceIds)
    {
        // Historical ordinary repairs retain their exact old calculation.
        if (!repair.WarrantyProcessed)
            return (repair.Items.Sum(i => i.Price * i.Quantity),
                repair.Items.Where(i => i.ProductId is > 0).Sum(i => i.Cost * i.Quantity),
                repair.Items.Where(i => i.ProductId is null or 0).Sum(i => i.Cost * i.Quantity));
        decimal revenue = 0, parts = 0, labor = 0;
        foreach (var item in repair.Items)
        {
            var isPart = item.ProductId is > 0 && !serviceIds.Contains(item.ProductId.Value);
            var done = item.WarrantyDecision == WarrantyDecision.Unspecified ||
                (isPart ? item.WarrantySupplied : item.WarrantyPerformed);
            if (!done) continue;
            if (item.WarrantyDecision != WarrantyDecision.Accepted)
                revenue += ShopInvoiceAmounts.Gross(item.Quantity, item.Price);
            var cost = ShopInvoiceAmounts.Gross(item.Quantity, item.Cost);
            if (isPart) parts += cost; else labor += cost;
        }
        return (revenue, parts, labor);
    }
}
