using Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Db = Inventory.Api.Data;

namespace Inventory.Api.Services;

/// <summary>Server-side guard, called within the same serializable transaction as the shop sale.</summary>
public static class ShopMinimumProfitGuard
{
    internal sealed class PurchaseBasis
    {
        public int ProductId { get; set; }
        public int TransactionId { get; set; }
        public string Number { get; set; } = "";
        public DateTime Date { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal? GrossAmount { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal Gross => GrossAmount ?? OrderLineAmounts.Gross(Quantity, Price);
        public bool IsValid => Quantity > 0m && Price > 0m && DiscountAmount >= 0m && DiscountAmount < Gross;
        public decimal Net => Gross - DiscountAmount;
    }

    internal static async Task<Dictionary<int, PurchaseBasis>> FindPurchasesAsync(AppDbContext db, int? company,
        IEnumerable<int> productIds, DateTime date, int cutoffId)
    {
        var ids = productIds.Distinct().ToList();
        if (ids.Count == 0) return new();
        var start = date.Date;
        if (start == DateTime.MaxValue.Date) throw new InvalidOperationException("تاریخ فاکتور معتبر نیست.");
        var end = start.AddDays(1);
        // Same-day ties use document ID. Both the guard and diagnostic use this query.
        var purchases = await (from l in db.TransactionLines.AsNoTracking()
                               join t in db.Transactions.AsNoTracking() on l.TransactionId equals t.Id
                               where ids.Contains(l.ProductId) && t.CompanyId == company && l.CompanyId == company &&
                                     t.Type == TransactionType.Purchase && t.Date < end &&
                                     (t.Date < start || t.Id < cutoffId)
                               orderby t.Date descending, t.Id descending, l.Id descending
                               select new PurchaseBasis { ProductId = l.ProductId, TransactionId = t.Id, Number = t.Number,
                                   Date = t.Date, Quantity = l.Quantity, Price = l.Price, GrossAmount = l.GrossAmount,
                                   DiscountAmount = l.DiscountAmount }).ToListAsync();
        return purchases.GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.First());
    }

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
                old.DiscountAmount == ShopInvoiceLineMath.Calculate(line, true, old).DiscountAmount)
                result.Add(i);
        }
        return result;
    }

    public static async Task ValidateAsync(AppDbContext db, Db.Transaction sale, Dictionary<int, Db.Product> products,
        HashSet<int>? unchangedIndexes = null, int? chronologySaleId = null)
    {
        var company = sale.CompanyId ?? db.ActiveCompanyId ?? db.ResolveDefaultCompanyId();
        if (db.ActiveCompanyId is > 0 && sale.CompanyId is > 0 && db.ActiveCompanyId != sale.CompanyId)
            throw new InvalidOperationException("شرکت فاکتور با شرکت فعال مطابقت ندارد.");
        var policy = await db.ShopPricingPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.CompanyId == company);
        if (policy is null || !policy.MinimumProfitEnabled) return;
        if (policy.MinimumMarkupPercent < 0m || policy.MinimumMarkupPercent > 10000m)
            throw new InvalidOperationException("تنظیم حداقل سود معتبر نیست؛ تنظیمات قیمت‌گذاری بررسی شود.");
        var indexed = sale.Lines.Select((line, index) => (line, index))
            .Where(x => unchangedIndexes?.Contains(x.index) != true && products.TryGetValue(x.line.ProductId, out var p) && !p.IsService).ToList();
        if (indexed.Count == 0) return;
        if (sale.ReferrerId is > 0 && !sale.ReferrerGoodsPercentSnapshot.HasValue)
            throw new InvalidOperationException("نرخ تاریخی پورسانت برای کنترل سود این تغییر مشخص نیست؛ ابتدا سابقه معرف بررسی شود.");
        var rate = sale.ReferrerId is > 0 ? sale.ReferrerGoodsPercentSnapshot!.Value : 0m;
        if (rate < 0 || rate > 100) throw new InvalidOperationException("نرخ پورسانت معرف معتبر نیست.");
        var last = await FindPurchasesAsync(db, company, indexed.Select(x => x.line.ProductId), sale.Date,
            chronologySaleId ?? sale.Id);
        foreach (var (line, _) in indexed)
        {
            var name = products[line.ProductId].Name;
            if (!last.TryGetValue(line.ProductId, out var purchase) || !purchase.IsValid)
                throw new InvalidOperationException($"برای «{name}» آخرین خرید معتبر تا تاریخ فاکتور مشخص نیست؛ فروش با کنترل حداقل سود مجاز نیست.");
            if (rate > 0 && !line.UnitCostSnapshot.HasValue)
                throw new InvalidOperationException($"بهای لازم برای محاسبه پورسانت «{name}» مشخص نیست؛ ابتدا سابقه کالا بررسی شود.");
            var revenue = Math.Max(0m, (line.GrossAmount ?? OrderLineAmounts.Gross(line.Quantity, line.Price)) - line.DiscountAmount);
            bool meets;
            try
            {
                meets = ShopMinimumProfit.MeetsPurchase(revenue, line.Quantity, purchase.Net, purchase.Quantity,
                    policy.MinimumMarkupPercent, rate, rate > 0 ? line.UnitCostSnapshot!.Value : 0m);
            }
            catch (OverflowException)
            {
                throw new InvalidOperationException("مبالغ یا مقدار سطر برای کنترل حداقل سود خارج از محدوده مجاز است.");
            }
            if (!meets)
                throw new InvalidOperationException($"قیمت «{name}» پس از تخفیف و پورسانت با حداقل سود فروشگاه سازگار نیست. مبنای خرید را با F7 بررسی کنید.");
        }
    }
}
