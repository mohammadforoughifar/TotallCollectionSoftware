using System.Globalization;
using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Pages.Orders;

/// <summary>A complete, warehouse-specific snapshot for the invoice product menu.</summary>
public sealed class InvoiceWarehouseStock
{
    private const int PageSize = 10000;
    private int version;

    public int WarehouseId { get; private set; }
    /// <summary>
    /// تاریخی که عکس فوری موجودی برای آن گرفته شده است (مبنای عکس فوری).
    /// موجودی فاکتور همیشه تاریخ‌دار است؛ پیش از انتخاب انبار مقدارش null است.
    /// </summary>
    public DateTime? AsOf { get; private set; }
    public bool IsLoading { get; private set; }
    public bool IsLoaded { get; private set; }
    /// <summary>موجودی تا پایان تاریخ سند (مبنای نمایش).</summary>
    public IReadOnlyDictionary<int, decimal> Quantities { get; private set; } = new Dictionary<int, decimal>();

    /// <summary>
    /// مقدار قابل ثبت در همان تاریخ: کمینهٔ موجودی از آن تاریخ به بعد. کمتر از
    /// <see cref="Quantities"/> یعنی بخشی از موجودی بعد از تاریخ سند فروش رفته و
    /// فاکتور عقب‌تاریخ‌خورده همان قلم را دوباره می‌فروشد.
    /// </summary>
    public IReadOnlyDictionary<int, decimal> Sellables { get; private set; } = new Dictionary<int, decimal>();

    /// <summary>مقدار قابل ثبت در تاریخ عکس فوری برای یک کالا (۰ اگر در این انبار موجودی ندارد).</summary>
    public decimal SellableFor(int productId)
        => Sellables.TryGetValue(productId, out var value) ? value : 0m;

    /// <summary>سازگاری با فراخوانیهای قدیمی: عکس فوری «تا امروز».</summary>
    public Task<bool> LoadAsync(int warehouseId, Func<int, int, int, Task<PagedResult<InvoiceStockItem>>> getPage)
        => LoadAsync(warehouseId, DateTime.Today, (id, page, size, _) => getPage(id, page, size));

    /// <returns>False if a newer warehouse/date request superseded this one.</returns>
    public async Task<bool> LoadAsync(int warehouseId, DateTime? asOf,
        Func<int, int, int, DateTime?, Task<PagedResult<InvoiceStockItem>>> getPage)
    {
        var requestVersion = ++version;
        WarehouseId = warehouseId;
        // عکس فوری موجودی هرگز «بی‌تاریخ» نیست: نبودِ تاریخ یعنی امروز، نه تجمیع کل زمان.
        AsOf = (asOf ?? DateTime.Today).Date;
        IsLoaded = false;
        IsLoading = warehouseId > 0;
        Quantities = new Dictionary<int, decimal>();
        Sellables = new Dictionary<int, decimal>();
        if (warehouseId <= 0) return true;

        try
        {
            var quantities = new Dictionary<int, decimal>();
            var sellables = new Dictionary<int, decimal>();
            var page = 1;
            int? expectedTotal = null;
            long received = 0;
            while (true)
            {
                // همان تاریخ برای همهٔ صفحهها ارسال میشود تا عکس فوری یکپارچه بماند.
                var result = await getPage(warehouseId, page, PageSize, AsOf);
                if (requestVersion != version) return false;
                if (result?.Items is null) throw new InvalidOperationException("Stock page is missing.");
                if (result.TotalCount < 0 || (expectedTotal.HasValue && expectedTotal.Value != result.TotalCount))
                    throw new InvalidOperationException("Stock changed while the pages were being loaded.");
                expectedTotal = result.TotalCount;

                foreach (var item in result.Items)
                {
                    if (item.WarehouseId != warehouseId || !quantities.TryAdd(item.ProductId, item.Quantity))
                        throw new InvalidOperationException("Stock pages do not form a consistent warehouse snapshot.");
                    if (!sellables.TryAdd(item.ProductId, item.SellableQuantity))
                        throw new InvalidOperationException("Stock pages do not form a consistent sellable snapshot.");
                }
                received += result.Items.Count;
                if (received > expectedTotal.Value) throw new InvalidOperationException("Stock page count is inconsistent.");
                if (received == expectedTotal.Value) break;
                if (result.Items.Count == 0) throw new InvalidOperationException("Stock pages are incomplete.");
                page++;
            }
            Quantities = quantities;
            Sellables = sellables;
            IsLoaded = true;
        }
        catch
        {
            // Unknown inventory is not zero. Leave the snapshot unavailable on failure.
            if (requestVersion != version) return false;
        }
        finally
        {
            if (requestVersion == version) IsLoading = false;
        }
        return true;
    }
}

/// <summary>Invoice-only detail for a product option; ordinary lookup DTOs are unchanged.</summary>
public sealed class InvoiceProductOption : LookupItem
{
    public decimal? StockQuantity { get; private init; }
    public string? StockUnit { get; private init; }
    public bool IsStockWarning => StockQuantity is <= 0m;

    /// <summary>مقدار قابل ثبت در تاریخ سند (با احتساب فروش بعد از آن تاریخ).</summary>
    public decimal? SellableQuantity { get; private init; }

    /// <summary>بخشی از موجودی تاریخ سند که بعد از آن تاریخ فروش رفته است.</summary>
    public decimal SoldAfterDate => StockQuantity.HasValue && SellableQuantity.HasValue
        ? Math.Max(0m, StockQuantity.Value - SellableQuantity.Value) : 0m;

    public bool IsSellableShort => SoldAfterDate > 0m;

    public static InvoiceProductOption Create(int id, string name, string? unit, bool isService, InvoiceWarehouseStock stock)
    {
        decimal? quantity = null;
        decimal? sellable = null;
        if (!isService && stock.WarehouseId > 0 && stock.IsLoaded)
        {
            quantity = stock.Quantities.TryGetValue(id, out var value) ? value : 0m;
            sellable = stock.SellableFor(id);
        }

        return new InvoiceProductOption
        {
            Id = id,
            Name = name,
            StockQuantity = quantity,
            SellableQuantity = sellable,
            StockUnit = unit,
            InfoText = isService ? "خدمات"
                : stock.WarehouseId <= 0 ? "ابتدا انبار را انتخاب کنید"
                : stock.IsLoading ? "در حال دریافت موجودی انبار…"
                : !stock.IsLoaded ? "موجودی انبار نامشخص است"
                : null
        };
    }

    // Do not round a small positive stock to a misleading zero.
    public static string FormatStock(decimal quantity)
        => Fa.Digits(quantity.ToString("#,##0.############################", CultureInfo.InvariantCulture));
}
