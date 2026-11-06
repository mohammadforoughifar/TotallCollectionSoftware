using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Db = Inventory.Api.Data;

namespace Inventory.Api.Services;

/// <summary>Company-scoped prices. Effective-date ordering never rewrites invoice lines.</summary>
public class ShopPricingService
{
    private readonly AppDbContext _db;
    public ShopPricingService(AppDbContext db) => _db = db;
    private int? Company => _db.ActiveCompanyId ?? _db.ResolveDefaultCompanyId();

    public async Task<ShopPricingPolicyDto> GetPolicyAsync()
    {
        var company = Company;
        var row = await _db.ShopPricingPolicies.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == company);
        return row == null ? new() : new() { MinimumProfitEnabled = row.MinimumProfitEnabled,
            MinimumMarkupPercent = row.MinimumMarkupPercent, UpdatedAt = row.UpdatedAt, UpdatedBy = row.UpdatedBy };
    }

    /// <summary>Read-only diagnostic; the order controller checks the separate profit permission.</summary>
    public async Task<ShopMinimumProfitBasisDto> GetMinimumProfitBasisAsync(int productId, DateTime date, int? saleId)
    {
        if (date == default || date.Date == DateTime.MaxValue.Date)
            throw new InvalidOperationException("تاریخ فاکتور معتبر نیست.");
        var company = Company;
        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId && p.CompanyId == company)
            ?? throw new InvalidOperationException("کالا در شرکت فعال یافت نشد.");
        var policy = await _db.ShopPricingPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.CompanyId == company);
        var result = new ShopMinimumProfitBasisDto { CompanyId = company, PolicyConfigured = policy is not null,
            MinimumProfitEnabled = policy?.MinimumProfitEnabled == true, MinimumMarkupPercent = policy?.MinimumMarkupPercent ?? 0m };
        if (product.IsService) { result.Reason = "خدمات مشمول کنترل حداقل سود کالا نیستند."; return result; }
        var cutoff = int.MaxValue;
        if (saleId is > 0)
        {
            var original = await _db.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == saleId &&
                t.CompanyId == company && t.Type == TransactionType.Sale)
                ?? throw new InvalidOperationException("فاکتور فروش در شرکت فعال یافت نشد.");
            var isChequeCorrection = await _db.SaleChequeCharges.AsNoTracking().AnyAsync(c => c.TransactionId == original.Id);
            if (!isChequeCorrection || date.Date == original.Date.Date) cutoff = original.Id;
        }
        var purchases = await ShopMinimumProfitGuard.FindPurchasesAsync(_db, company, new[] { productId }, date, cutoff);
        if (!purchases.TryGetValue(productId, out var purchase))
        { result.Reason = "تا تاریخ و ترتیب این فاکتور، خریدی برای این کالا پیدا نشد."; return result; }
        result.PurchaseId = purchase.TransactionId;
        result.PurchaseNumber = purchase.Number;
        result.PurchaseDate = purchase.Date;
        result.PurchaseQuantity = purchase.Quantity;
        result.PurchaseGross = purchase.Gross;
        result.PurchaseDiscount = purchase.DiscountAmount;
        if (!purchase.IsValid)
        { result.Reason = "مقدار، قیمت یا تخفیف آخرین خرید معتبر نیست."; return result; }
        result.NetUnitCost = purchase.Net / purchase.Quantity;
        if (result.MinimumProfitEnabled)
        {
            try { result.MinimumUnitRevenueBeforeSaleDiscountAndCommission = decimal.Ceiling(
                purchase.Net * (100m + result.MinimumMarkupPercent) / (purchase.Quantity * 100m)); }
            catch (OverflowException) { result.Reason = "محاسبه حداقل به دلیل بزرگی مبلغ ممکن نیست."; }
        }
        return result;
    }

    public async Task<ShopPricingPolicyDto> SavePolicyAsync(ShopPricingPolicyDto input, string? user)
    {
        if (input.MinimumMarkupPercent < 0 || input.MinimumMarkupPercent > 10000 || input.MinimumMarkupPercent != decimal.Round(input.MinimumMarkupPercent, 2))
            throw new InvalidOperationException("درصد حداقل سود باید بین صفر و ۱۰۰۰۰ و حداکثر با دو رقم اعشار باشد.");
        var company = Company;
        if (company == 0) throw new InvalidOperationException("شرکت فعال انتخاب نشده است.");
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var policy = await _db.ShopPricingPolicies.FirstOrDefaultAsync(p => p.CompanyId == company);
        if (policy == null) { policy = new Db.ShopPricingPolicy { CompanyId = company }; _db.ShopPricingPolicies.Add(policy); }
        policy.MinimumProfitEnabled = input.MinimumProfitEnabled;
        policy.MinimumMarkupPercent = input.MinimumMarkupPercent;
        policy.UpdatedAt = DateTime.Now;
        policy.UpdatedBy = Actor(user);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return await GetPolicyAsync();
    }

    public static string? Actor(string? user) => string.IsNullOrWhiteSpace(user) ? null : user.Trim()[..Math.Min(user.Trim().Length, 120)];

    /// <summary>Returns only known history; no current-price fallback for older dates.</summary>
    public static async Task<Dictionary<int, Db.ShopSalePrice>> ResolveAsync(AppDbContext db, IEnumerable<int> productIds, DateTime date)
    {
        var ids = productIds.Distinct().ToList();
        if (ids.Count == 0) return new();
        var end = date.Date.AddDays(1);
        var rows = await db.ShopSalePrices.AsNoTracking().Where(x => ids.Contains(x.ProductId) && x.EffectiveDate < end)
            .OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.Id).ToListAsync();
        return rows.GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.First());
    }

    public async Task<DatedSalePriceDto> GetPriceAsync(int productId, DateTime date)
    {
        if (date == default || date.Date == DateTime.MaxValue.Date) throw new InvalidOperationException("تاریخ قیمت معتبر نیست.");
        if (!await _db.Products.AnyAsync(p => p.Id == productId)) throw new InvalidOperationException("کالا یافت نشد.");
        var prices = await ResolveAsync(_db, new[] { productId }, date);
        return prices.TryGetValue(productId, out var value)
            ? new() { Price = value.Price, EffectiveDate = value.EffectiveDate } : new();
    }

    public static void ValidatePrice(decimal price)
    {
        if (!ShopInvoiceAmounts.IsWholePrice(price))
            throw new InvalidOperationException("قیمت باید غیرمنفی و بدون اعشار، به ریال صحیح باشد.");
    }

    /// <summary>Called before the same SaveChanges as the product, including new products.</summary>
    public static void RecordCatalogPrice(AppDbContext db, Db.Product product, decimal? previous, string? user)
    {
        ValidatePrice(product.SalePrice);
        db.ShopSalePrices.Add(new Db.ShopSalePrice { CompanyId = product.CompanyId, Product = product,
            EffectiveDate = DateTime.Today, Price = product.SalePrice, PreviousPrice = previous,
            Source = "catalog", CreatedAt = DateTime.Now, CreatedBy = Actor(user) });
    }

    public static void ValidatePurchasePriceInputs(OrderCommand cmd)
    {
        if (cmd.Type != TransactionType.Purchase && cmd.Lines.Any(l => l.NewSalePrice.HasValue))
            throw new InvalidOperationException("قیمت فروش جدید فقط در خرید قابل ثبت است.");
        foreach (var price in cmd.Lines.Where(l => l.NewSalePrice.HasValue).Select(l => l.NewSalePrice!.Value)) ValidatePrice(price);
        if (cmd.Lines.Where(l => l.NewSalePrice.HasValue).GroupBy(l => l.ProductId)
            .Any(g => g.Select(l => l.NewSalePrice!.Value).Distinct().Count() > 1))
            throw new InvalidOperationException("برای یک کالا در یک خرید، فقط یک قیمت فروش جدید وارد کنید.");
    }

    /// <summary>Must be called inside the invoice transaction, after its ID has been saved.</summary>
    public static async Task RecordPurchasePricesAsync(AppDbContext db, Db.Transaction transaction, OrderCommand cmd, string? user)
    {
        ValidatePurchasePriceInputs(cmd);
        if (transaction.Type != TransactionType.Purchase) return;
        var changes = cmd.Lines.Where(l => l.NewSalePrice.HasValue).GroupBy(l => l.ProductId)
            .ToDictionary(g => g.Key, g => g.First().NewSalePrice!.Value);
        if (changes.Count == 0) return;
        var changedIds = changes.Keys.ToList();
        var previous = await ResolveAsync(db, changedIds, transaction.Date);
        var products = await db.Products.Where(p => changedIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        foreach (var (productId, price) in changes)
        {
            if (!products.TryGetValue(productId, out var product) || product.IsService)
                throw new InvalidOperationException("قیمت فروش فقط برای کالای خریداری‌شده قابل ثبت است.");
            db.ShopSalePrices.Add(new Db.ShopSalePrice { CompanyId = transaction.CompanyId, ProductId = productId,
                EffectiveDate = transaction.Date.Date, Price = price,
                PreviousPrice = previous.TryGetValue(productId, out var prior) ? prior.Price : null,
                Source = "purchase", PurchaseTransactionId = transaction.Id, CreatedAt = DateTime.Now, CreatedBy = Actor(user) });
        }
        await db.SaveChangesAsync();
        await RefreshCurrentPriceCacheAsync(db, products.Values);
    }

    /// <summary>Scalar compatibility cache never jumps to a future or superseded price.</summary>
    public static async Task RefreshCurrentPriceCacheAsync(AppDbContext db, IEnumerable<Db.Product> products)
    {
        var list = products.ToList();
        var current = await ResolveAsync(db, list.Select(p => p.Id), DateTime.Today);
        foreach (var p in list) if (current.TryGetValue(p.Id, out var price)) p.SalePrice = price.Price;
        await db.SaveChangesAsync();
    }

    public async Task<ShopPriceAdjustmentPreview> PreviewAsync(ShopPriceAdjustmentRequest request)
    {
        if (request.CategoryId <= 0 || request.EffectiveDate == default || request.EffectiveDate.Date == DateTime.MaxValue.Date ||
            request.Percent <= 0 || request.Percent > (request.Increase ? 10000 : 100) || request.Percent != decimal.Round(request.Percent, 2))
            throw new InvalidOperationException("گروه، تاریخ و درصد تغییر معتبر را وارد کنید.");
        var company = Company;
        var cats = await _db.ProductCategories.AsNoTracking().Where(c => c.CompanyId == company).ToListAsync();
        if (cats.All(c => c.Id != request.CategoryId)) throw new InvalidOperationException("گروه کالا یافت نشد.");
        var selected = new HashSet<int> { request.CategoryId };
        if (request.IncludeDescendants)
        {
            bool changed;
            do { changed = false; foreach (var c in cats) if (c.ParentId.HasValue && selected.Contains(c.ParentId.Value)) changed |= selected.Add(c.Id); } while (changed);
        }
        var names = cats.Where(c => selected.Contains(c.Id)).Select(c => c.Name).ToList();
        var ids = selected.ToList();
        // ID takes precedence over legacy text: mismatched text must not select a product from another group.
        var products = await _db.Products.AsNoTracking().Where(p => p.CompanyId == company && !p.IsService &&
            ((p.CategoryId != null && ids.Contains(p.CategoryId.Value)) || (p.CategoryId == null && p.Category != null && names.Contains(p.Category))))
            .OrderBy(p => p.Name).ThenBy(p => p.Id).ToListAsync();
        var prices = await ResolveAsync(_db, products.Select(p => p.Id), request.EffectiveDate);
        var result = new ShopPriceAdjustmentPreview();
        var fingerprint = new StringBuilder().Append(request.CategoryId).Append('|').Append(request.IncludeDescendants)
            .Append('|').Append(request.Increase).Append('|').Append(request.Percent.ToString(CultureInfo.InvariantCulture))
            .Append('|').Append(request.EffectiveDate.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('|').Append(company);
        foreach (var p in products)
        {
            prices.TryGetValue(p.Id, out var old);
            var row = new ShopPriceAdjustmentRow { ProductId = p.Id, Name = p.Name, Code = p.Code, IsActive = p.IsActive,
                PreviousPrice = old?.Price };
            if (p.CategoryId == null && p.Category != null && cats.Count(c => c.Name == p.Category) > 1)
                row.Reason = "گروه متنی مبهم است؛ ابتدا گروه کالا را مشخص کنید";
            else if (old == null || old.Price <= 0) row.Reason = "قیمت معتبر در این تاریخ موجود نیست";
            else
            {
                try { row.NewPrice = decimal.Round(old.Price * (1m + (request.Increase ? request.Percent : -request.Percent) / 100m), 0, MidpointRounding.AwayFromZero); }
                catch (OverflowException) { throw new InvalidOperationException("قیمت جدید خارج از محدوده قابل ثبت است."); }
                ValidatePrice(row.NewPrice.Value);
            }
            result.Rows.Add(row);
            fingerprint.Append('|').Append(p.Id).Append(':').Append(old?.Id ?? 0).Append(':')
                .Append(old?.Price.ToString(CultureInfo.InvariantCulture) ?? "?");
        }
        result.PreviewHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint.ToString())));
        return result;
    }

    public async Task<ShopPriceAdjustmentPreview> ApplyAsync(ShopPriceAdjustmentCommand cmd, string? user)
    {
        if (cmd.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(cmd.PreviewHash))
            throw new InvalidOperationException("ابتدا پیش‌نمایش را بررسی کنید.");
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var already = await _db.ShopSalePrices.AsNoTracking().Where(h => h.OperationId == cmd.OperationId).ToListAsync();
        if (already.Count > 0)
        {
            if (already.Any(h => h.PreviewHash != cmd.PreviewHash)) throw new InvalidOperationException("شناسه عملیات با پیش‌نمایش دیگری استفاده شده است.");
            await tx.CommitAsync();
            return new ShopPriceAdjustmentPreview { PreviewHash = cmd.PreviewHash };
        }
        var preview = await PreviewAsync(cmd);
        if (!string.Equals(preview.PreviewHash, cmd.PreviewHash, StringComparison.Ordinal))
            throw new InvalidOperationException("قیمت‌ها یا کالاهای گروه تغییر کرده‌اند؛ پیش‌نمایش را دوباره دریافت کنید.");
        var changed = preview.Rows.Where(r => r.NewPrice.HasValue).ToList();
        if (changed.Count == 0) throw new InvalidOperationException("برای این گروه قیمت قابل تغییری در تاریخ انتخاب‌شده وجود ندارد.");
        foreach (var row in changed)
            _db.ShopSalePrices.Add(new Db.ShopSalePrice { CompanyId = Company, ProductId = row.ProductId,
                EffectiveDate = cmd.EffectiveDate.Date, Price = row.NewPrice!.Value, PreviousPrice = row.PreviousPrice,
                Source = "group", OperationId = cmd.OperationId, PreviewHash = cmd.PreviewHash,
                CreatedAt = DateTime.Now, CreatedBy = Actor(user) });
        await _db.SaveChangesAsync();
        var ids = changed.Select(r => r.ProductId).ToList();
        var products = await _db.Products.Where(p => ids.Contains(p.Id)).ToListAsync();
        await RefreshCurrentPriceCacheAsync(_db, products);
        await tx.CommitAsync();
        return preview;
    }

    public async Task<List<LookupItem>> CategoriesAsync()
    {
        var company = Company;
        var cats = await _db.ProductCategories.AsNoTracking().Where(c => c.CompanyId == company).ToListAsync();
        var byId = cats.ToDictionary(c => c.Id);
        return cats.OrderBy(c => c.Name).Select(c =>
        {
            var parts = new List<string> { c.Name };
            var seen = new HashSet<int> { c.Id };
            var parent = c.ParentId;
            while (parent is > 0 && seen.Add(parent.Value) && byId.TryGetValue(parent.Value, out var node))
                { parts.Insert(0, node.Name); parent = node.ParentId; }
            return new LookupItem { Id = c.Id, Name = string.Join(" / ", parts) };
        }).ToList();
    }

    public async Task<List<ShopPriceHistoryItem>> HistoryAsync(int? productId, int take = 100)
    {
        var company = Company;
        var query = from h in _db.ShopSalePrices.AsNoTracking()
                    join p in _db.Products.AsNoTracking() on h.ProductId equals p.Id
                    where h.CompanyId == company && p.CompanyId == company
                    select new ShopPriceHistoryItem { Id = h.Id, ProductId = h.ProductId, ProductName = p.Name,
                        ProductCode = p.Code, EffectiveDate = h.EffectiveDate, Price = h.Price, PreviousPrice = h.PreviousPrice,
                        Source = h.Source, PurchaseTransactionId = h.PurchaseTransactionId, OperationId = h.OperationId,
                        CreatedAt = h.CreatedAt, CreatedBy = h.CreatedBy };
        if (productId is > 0) query = query.Where(h => h.ProductId == productId.Value);
        return await query.OrderByDescending(h => h.CreatedAt).ThenByDescending(h => h.Id).Take(Math.Clamp(take, 1, 500)).ToListAsync();
    }
}
