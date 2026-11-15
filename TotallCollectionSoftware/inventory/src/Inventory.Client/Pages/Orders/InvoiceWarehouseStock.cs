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
    public bool IsLoading { get; private set; }
    public bool IsLoaded { get; private set; }
    public IReadOnlyDictionary<int, decimal> Quantities { get; private set; } = new Dictionary<int, decimal>();

    /// <returns>False if a newer warehouse request superseded this one.</returns>
    public async Task<bool> LoadAsync(int warehouseId, Func<int, int, int, Task<PagedResult<InvoiceStockItem>>> getPage)
    {
        var requestVersion = ++version;
        WarehouseId = warehouseId;
        IsLoaded = false;
        IsLoading = warehouseId > 0;
        Quantities = new Dictionary<int, decimal>();
        if (warehouseId <= 0) return true;

        try
        {
            var quantities = new Dictionary<int, decimal>();
            var page = 1;
            int? expectedTotal = null;
            long received = 0;
            while (true)
            {
                var result = await getPage(warehouseId, page, PageSize);
                if (requestVersion != version) return false;
                if (result?.Items is null) throw new InvalidOperationException("Stock page is missing.");
                if (result.TotalCount < 0 || (expectedTotal.HasValue && expectedTotal.Value != result.TotalCount))
                    throw new InvalidOperationException("Stock changed while the pages were being loaded.");
                expectedTotal = result.TotalCount;

                foreach (var item in result.Items)
                {
                    if (item.WarehouseId != warehouseId || !quantities.TryAdd(item.ProductId, item.Quantity))
                        throw new InvalidOperationException("Stock pages do not form a consistent warehouse snapshot.");
                }
                received += result.Items.Count;
                if (received > expectedTotal.Value) throw new InvalidOperationException("Stock page count is inconsistent.");
                if (received == expectedTotal.Value) break;
                if (result.Items.Count == 0) throw new InvalidOperationException("Stock pages are incomplete.");
                page++;
            }
            Quantities = quantities;
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

    public static InvoiceProductOption Create(int id, string name, string? unit, bool isService, InvoiceWarehouseStock stock)
    {
        decimal? quantity = null;
        if (!isService && stock.WarehouseId > 0 && stock.IsLoaded)
            quantity = stock.Quantities.TryGetValue(id, out var value) ? value : 0m;

        return new InvoiceProductOption
        {
            Id = id,
            Name = name,
            StockQuantity = quantity,
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
