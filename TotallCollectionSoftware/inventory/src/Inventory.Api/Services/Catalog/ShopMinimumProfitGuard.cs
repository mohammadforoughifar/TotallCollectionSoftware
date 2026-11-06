using Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Db = Inventory.Api.Data;

namespace Inventory.Api.Services;

/// <summary>Server-side guard, called within the same serializable transaction as the shop sale.</summary>
public static class ShopMinimumProfitGuard
{
    public static HashSet<int> UnchangedLineIndexes(Db.Transaction original, OrderCommand command, int? newReferrerId)
    {
        var existingIds = command.Lines.Where(l => l.Id > 0).Select(l => l.Id).ToList();
        if (existingIds.Count != existingIds.Distinct().Count())
            throw new InvalidOperationException("شناسه سطر فاکتور تکراری است.");
        var result = new HashSet<int>();
        var date = command.Date == default ? original.Date : command.Date;
        if (date.Date != original.Date.Date || command.WarehouseId != original.WarehouseId ||
            command.PartyId != original.PartyId || newReferrerId != original.ReferrerId) return result;
        var originals = original.Lines.ToDictionary(x => x.Id);
        for (var i = 0; i < command.Lines.Count; i++)
        {
            var line = command.Lines[i];
            if (line.Id <= 0 || !originals.TryGetValue(line.Id, out var old)) continue;
            if (old.ProductId == line.ProductId && old.Quantity == line.Quantity && old.Price == line.Price &&
                old.DiscountAmount == OrderLineAmounts.Discount(line.Quantity, line.Price, line.DiscountPercent, line.DiscountAmount, line.DiscountIsAmount))
                result.Add(i);
        }
        return result;
    }

    public static async Task ValidateAsync(AppDbContext db, Db.Transaction sale, Dictionary<int, Db.Product> products,
        HashSet<int>? unchangedIndexes = null, int? chronologySaleId = null)
    {
        var company = sale.CompanyId ?? db.ActiveCompanyId ?? db.ResolveDefaultCompanyId();
        var policy = await db.ShopPricingPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.CompanyId == company);
        if (policy is null || !policy.MinimumProfitEnabled) return;
        var indexed = sale.Lines.Select((line, index) => (line, index))
            .Where(x => unchangedIndexes?.Contains(x.index) != true && products.TryGetValue(x.line.ProductId, out var p) && !p.IsService).ToList();
        if (indexed.Count == 0) return;
        if (sale.ReferrerId is > 0 && !sale.ReferrerGoodsPercentSnapshot.HasValue)
            throw new InvalidOperationException("نرخ تاریخی پورسانت برای کنترل سود این تغییر مشخص نیست؛ ابتدا سابقه معرف بررسی شود.");
        var rate = sale.ReferrerId is > 0 ? sale.ReferrerGoodsPercentSnapshot!.Value : 0m;
        if (rate < 0 || rate > 100) throw new InvalidOperationException("نرخ پورسانت معرف معتبر نیست.");
        var ids = indexed.Select(x => x.line.ProductId).Distinct().ToList();
        var start = sale.Date.Date;
        if (start == DateTime.MaxValue.Date) throw new InvalidOperationException("تاریخ فاکتور معتبر نیست.");
        var end = start.AddDays(1);
        var cutoffId = chronologySaleId ?? sale.Id;
        // Same-day ties use document ID, so a purchase entered after an old sale is
        // not silently presented as the purchase already known at that sale.
        var purchases = await (from l in db.TransactionLines.AsNoTracking()
                               join t in db.Transactions.AsNoTracking() on l.TransactionId equals t.Id
                               where ids.Contains(l.ProductId) && t.CompanyId == company && l.CompanyId == company &&
                                     t.Type == TransactionType.Purchase && t.Date < end &&
                                     (t.Date < start || t.Id < cutoffId)
                               orderby t.Date descending, t.Id descending, l.Id descending
                               select new { l.ProductId, l.Quantity, l.Price, l.DiscountAmount }).ToListAsync();
        var last = purchases.GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.First());
        foreach (var (line, _) in indexed)
        {
            var name = products[line.ProductId].Name;
            if (!last.TryGetValue(line.ProductId, out var purchase) || purchase.Quantity <= 0 || purchase.Price <= 0)
                throw new InvalidOperationException($"برای «{name}» آخرین خرید معتبر تا تاریخ فاکتور مشخص نیست؛ فروش با کنترل حداقل سود مجاز نیست.");
            var purchaseGross = OrderLineAmounts.Gross(purchase.Quantity, purchase.Price);
            if (purchase.DiscountAmount < 0 || purchase.DiscountAmount >= purchaseGross)
                throw new InvalidOperationException($"بهای آخرین خرید «{name}» معتبر نیست؛ ابتدا سابقه خرید بررسی شود.");
            var unitCost = (purchaseGross - purchase.DiscountAmount) / purchase.Quantity;
            if (rate > 0 && !line.UnitCostSnapshot.HasValue)
                throw new InvalidOperationException($"بهای لازم برای محاسبه پورسانت «{name}» مشخص نیست؛ ابتدا سابقه کالا بررسی شود.");
            var revenue = Math.Max(0m, OrderLineAmounts.Gross(line.Quantity, line.Price) - line.DiscountAmount);
            if (!ShopMinimumProfit.Meets(revenue, line.Quantity, unitCost, policy.MinimumMarkupPercent, rate,
                    rate > 0 ? line.UnitCostSnapshot!.Value : 0m))
                throw new InvalidOperationException($"قیمت «{name}» پس از تخفیف و پورسانت با حداقل سود فروشگاه سازگار نیست.");
        }
    }
}
