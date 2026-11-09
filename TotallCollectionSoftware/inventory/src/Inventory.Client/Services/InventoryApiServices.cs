using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

// =====================================================================
// پیاده‌سازی سرویس‌های سمت کلاینت — تنها نقطه‌ای که مسیرهای API ساخته می‌شوند.
// =====================================================================

/// <summary>پیاده‌سازی سرویس پروفایل شرکت (تعریف شرکت) + تنظیمات چاپ فاکتور.</summary>
public class CompanyProfileService : ICompanyProfileService
{
    private readonly IApiClient _api;
    public CompanyProfileService(IApiClient api) => _api = api;

    public Task<CompanyProfileDto> GetAsync()
        => _api.GetAsync<CompanyProfileDto>("api/company-profile");

    public Task<CompanyProfileDto> SaveAsync(CompanyProfileDto profile)
        => _api.PostAsync<CompanyProfileDto>("api/company-profile", profile);
}

/// <summary>پیاده‌سازی سرویس تنظیمات.</summary>
public class SettingsService : ISettingsService
{
    private readonly IApiClient _api;
    public SettingsService(IApiClient api) => _api = api;

    public Task<AppSettings> GetAsync()
        => _api.GetAsync<AppSettings>("api/settings");

    public Task<AppSettings> SaveAsync(AppSettings settings)
        => _api.PostAsync<AppSettings>("api/settings", settings);
}

/// <summary>پیاده‌سازی سرویس معرف‌ها.</summary>
public class ReferrerService : IReferrerService
{
    private readonly IApiClient _api;
    public ReferrerService(IApiClient api) => _api = api;

    public Task<PagedResult<Referrer>> GetPagedAsync(int skip, int take, bool activeOnly = false, string? search = null)
        => ListOrPaged.GetPagedAsync<Referrer>(_api, $"api/referrers?skip={skip}&take={take}&activeOnly={activeOnly}&search={Uri.EscapeDataString(search ?? "")}");

    public Task<List<Referrer>> GetAllAsync(bool activeOnly = false)
        => _api.GetAsync<List<Referrer>>($"api/referrers?activeOnly={activeOnly}");

    public Task<Referrer> SaveAsync(Referrer referrer)
        => _api.PostAsync<Referrer>("api/referrers", referrer);

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/referrers/{id}");

    public Task<List<Referrer>> GetWalletsAsync(string? search = null, string sortBy = "name", bool desc = false)
        => _api.GetAsync<List<Referrer>>(
            $"api/referrers/wallets?search={Uri.EscapeDataString(search ?? "")}&sortBy={sortBy}&desc={desc}");

    public Task<List<ReferrerPayment>> GetPaymentsAsync(int? referrerId = null)
        => _api.GetAsync<List<ReferrerPayment>>(
            referrerId is > 0 ? $"api/referrers/payments?referrerId={referrerId}" : "api/referrers/payments");

    public Task<ReferrerPayment> AddPaymentAsync(ReferrerPayment payment)
        => _api.PostAsync<ReferrerPayment>("api/referrers/payments", payment);

    public Task DeletePaymentAsync(int id)
        => _api.DeleteAsync($"api/referrers/payments/{id}");
}

/// <summary>پیاده‌سازی سرویس گروه‌های کالا.</summary>
public class CategoryService : ICategoryService
{
    private readonly IApiClient _api;
    public CategoryService(IApiClient api) => _api = api;

    public Task<List<ProductCategory>> GetAllAsync(bool activeOnly = false)
        => _api.GetAsync<List<ProductCategory>>($"api/categories?activeOnly={activeOnly}");

    public Task<ProductCategory> SaveAsync(ProductCategory category)
        => _api.PostAsync<ProductCategory>("api/categories", category);

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/categories/{id}");
}

/// <summary>پیاده‌سازی سرویس کالاها.</summary>
public class ProductService : IProductService
{
    private readonly IApiClient _api;
    public ProductService(IApiClient api) => _api = api;

    public Task<PagedResult<Product>> GetProductsAsync(string? search = null, bool belowOnly = false, int page = 1, int pageSize = 20, int? warehouseId = null)
    {
        var q = $"api/products?search={Uri.EscapeDataString(search ?? "")}&below={belowOnly}&page={page}&pageSize={pageSize}";
        if (warehouseId is > 0) q += $"&warehouseId={warehouseId}";
        return _api.GetAsync<PagedResult<Product>>(q);
    }

    public Task<List<LookupItem>> GetLookupsAsync()
        => _api.GetAsync<List<LookupItem>>("api/products/lookups");

    public Task<Product?> GetByIdAsync(int id)
        => _api.GetAsync<Product?>($"api/products/{id}");

    public Task<Product> SaveAsync(Product product)
        => _api.PostAsync<Product>("api/products", product);

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/products/{id}");

    public Task<ExcelImportResult> ImportExcelAsync(Stream fileStream, string fileName, int warehouseId)
        => _api.PostFileAsync<ExcelImportResult>($"api/products/import?warehouseId={warehouseId}", fileStream, fileName);

    public Task<(byte[] Data, string FileName, string ContentType)> DownloadTemplateAsync()
        => _api.GetFileAsync("api/products/import/template");
}

/// <summary>پیاده‌سازی سرویس واحدهای شمارش.</summary>
public class UnitService : IUnitService
{
    private readonly IApiClient _api;
    public UnitService(IApiClient api) => _api = api;

    public Task<PagedResult<MeasureUnit>> GetPagedAsync(int skip, int take, bool activeOnly = false, string? search = null)
        => ListOrPaged.GetPagedAsync<MeasureUnit>(_api, $"api/units?skip={skip}&take={take}&activeOnly={activeOnly}&search={Uri.EscapeDataString(search ?? "")}");

    public Task<List<MeasureUnit>> GetAllAsync(bool activeOnly = false)
        => _api.GetAsync<List<MeasureUnit>>($"api/units?activeOnly={activeOnly}");

    public Task<MeasureUnit> SaveAsync(MeasureUnit unit)
        => _api.PostAsync<MeasureUnit>("api/units", unit);

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/units/{id}");
}

/// <summary>پیاده‌سازی سرویس انبارها.</summary>
public class WarehouseService : IWarehouseService
{
    private readonly IApiClient _api;
    public WarehouseService(IApiClient api) => _api = api;

    public Task<PagedResult<Warehouse>> GetPagedAsync(int skip, int take)
        => ListOrPaged.GetPagedAsync<Warehouse>(_api, $"api/warehouses?skip={skip}&take={take}");

    public Task<List<Warehouse>> GetAllAsync()
        => _api.GetAsync<List<Warehouse>>("api/warehouses");

    public async Task<List<LookupItem>> GetLookupsAsync(bool activeOnly = false)
    {
        var whs = await GetAllAsync();
        if (activeOnly) whs = whs.Where(w => w.IsActive).ToList();
        return whs.Select(w => new LookupItem { Id = w.Id, Name = w.Name }).ToList();
    }

    public Task<Warehouse> SaveAsync(Warehouse warehouse)
        => _api.PostAsync<Warehouse>("api/warehouses", warehouse);

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/warehouses/{id}");
}

/// <summary>پیاده‌سازی سرویس طرف حساب‌ها.</summary>
public class PartyService : IPartyService
{
    private readonly IApiClient _api;
    public PartyService(IApiClient api) => _api = api;

    public Task<PagedResult<Party>> GetPagedAsync(PartyType type, int skip, int take)
        => ListOrPaged.GetPagedAsync<Party>(_api, $"api/parties?skip={skip}&take={take}&type={(int)type}");

    public Task<List<Party>> GetAsync(PartyType type)
        => _api.GetAsync<List<Party>>($"api/parties?type={(int)type}");

    public async Task<List<LookupItem>> GetLookupsAsync(PartyType type, bool activeOnly = false)
    {
        var parties = await GetAsync(type);
        if (activeOnly) parties = parties.Where(p => p.IsActive).ToList();
        return parties.Select(p => ToLookup(p)).ToList();
    }

    /// <summary>
    /// ساخت گزینهٔ انتخابگر طرف حساب. تلفن و موبایل به <c>SearchText</c> میروند تا
    /// «جستجوی مشتری» در <b>تمام</b> فرمهایی که از این سرویس استفاده میکنند روی شماره
    /// هم کار کند، بیآنکه <c>Name</c> (که در اسناد و عناوین فرمها بهکار میرود) عوض شود؛
    /// <c>InfoText</c> همان شمارهها را زیر نام نشان میدهد تا نتیجه قابل تشخیص باشد.
    /// </summary>
    /// <param name="p">طرف حساب کامل (DTO شامل Phone/Mobile).</param>
    /// <param name="displayName">عنوان سفارشی، مثلاً نام مشتری بههمراه معرف در فرم سفارش.</param>
    public static LookupItem ToLookup(Party p, string? displayName = null)
    {
        var phones = new[] { p.Mobile, p.Phone }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new LookupItem
        {
            Id = p.Id,
            Name = displayName ?? p.Name,
            SearchText = phones.Length == 0 ? null : string.Join(" ", phones),
            InfoText = phones.Length == 0 ? null : string.Join(" · ", phones)
        };
    }

    public async Task<List<LookupItem>> GetShopCustomerLookupsAsync(string context)
    {
        var parties = await _api.GetAsync<List<Party>>($"api/parties/shop-customers?context={Uri.EscapeDataString(context)}");
        return parties.Where(p => p.IsActive).Select(p => ToLookup(p)).ToList();
    }

    public Task<Party> SaveAsync(Party party)
        => _api.PostAsync<Party>("api/parties", party);

    public Task<Party> SaveShopQuickAsync(Party party)
        => _api.PostAsync<Party>("api/parties/shop-quick", party);

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/parties/{id}");

    public Task<PartyStatementReportDto> GetPartyStatementsSummaryAsync(PartyType? type = null, DateTime? from = null, DateTime? to = null, string? search = null)
    {
        var q = $"api/parties/statements-summary?search={Uri.EscapeDataString(search ?? "")}";
        if (type.HasValue) q += $"&type={(int)type.Value}";
        if (from.HasValue) q += $"&from={from.Value:yyyy-MM-dd}";
        if (to.HasValue) q += $"&to={to.Value:yyyy-MM-dd}";
        return _api.GetAsync<PartyStatementReportDto>(q);
    }

    public Task<PartyStatementDetailDto> GetPartyStatementDetailAsync(int partyId, DateTime? from = null, DateTime? to = null)
    {
        var q = $"api/parties/{partyId}/statement?";
        if (from.HasValue) q += $"&from={from.Value:yyyy-MM-dd}";
        if (to.HasValue) q += $"&to={to.Value:yyyy-MM-dd}";
        return _api.GetAsync<PartyStatementDetailDto>(q.TrimEnd('?', '&'));
    }

}

/// <summary>پیاده‌سازی سرویس موجودی.</summary>
public class StockService : IStockService
{
    private readonly IApiClient _api;
    public StockService(IApiClient api) => _api = api;

    public Task<PagedResult<StockItem>> GetStockAsync(int? warehouseId = null, string? search = null, bool belowOnly = false, int page = 1, int pageSize = 20)
    {
        var q = $"api/stock?page={page}&pageSize={pageSize}&below={belowOnly}";
        if (warehouseId is > 0) q += $"&warehouseId={warehouseId}";
        if (!string.IsNullOrWhiteSpace(search)) q += $"&search={Uri.EscapeDataString(search)}";
        return _api.GetAsync<PagedResult<StockItem>>(q);
    }

    public Task AdjustAsync(AdjustmentCommand cmd)
        => _api.PostAsync<object>("api/stock/adjust", cmd);
}

/// <summary>پیاده‌سازی سرویس اسناد خرید و فروش.</summary>
public class OrderService : IOrderService
{
    private readonly IApiClient _api;
    public OrderService(IApiClient api) => _api = api;

    public Task<bool> CanViewProfitAsync() => _api.GetAsync<bool>("api/orders/profit-access");
    public Task<bool> CanManagePricingAsync() => _api.GetAsync<bool>("api/orders/pricing-access");
    public Task<DatedSalePriceDto> GetDatedSalePriceAsync(int productId, DateTime date) =>
        _api.GetAsync<DatedSalePriceDto>($"api/orders/dated-sale-price?productId={productId}&date={date:yyyy-MM-dd}");

    public Task<ShopMinimumProfitBasisDto> GetMinimumProfitBasisAsync(int productId, DateTime date, int? saleId)
        => _api.GetAsync<ShopMinimumProfitBasisDto>($"api/orders/minimum-profit-basis?productId={productId}&date={date:yyyy-MM-dd}" +
            (saleId is > 0 ? $"&saleId={saleId.Value}" : ""));

    public Task<PagedResult<Product>> GetInvoiceProductsAsync(TransactionType type, int page = 1, int pageSize = 10000)
        => _api.GetAsync<PagedResult<Product>>($"api/orders/invoice-products?type={(int)type}&page={page}&pageSize={pageSize}");
    public Task<List<LookupItem>> GetInvoiceWarehousesAsync(TransactionType type, bool activeOnly = false)
        => _api.GetAsync<List<LookupItem>>($"api/orders/invoice-warehouses?type={(int)type}&activeOnly={activeOnly}");
    public Task<List<Party>> GetInvoicePartiesAsync(TransactionType type)
        => _api.GetAsync<List<Party>>($"api/orders/invoice-parties?type={(int)type}");

    public Task<PagedResult<InvoiceStockItem>> GetWarehouseStockAsync(int warehouseId, int page = 1, int pageSize = 10000)
        => _api.GetAsync<PagedResult<InvoiceStockItem>>($"api/orders/warehouse-stock?warehouseId={warehouseId}&page={page}&pageSize={pageSize}");

    public Task<PagedResult<Order>> GetOrdersAsync(TransactionType type, DateTime? from = null, DateTime? to = null, int? partyId = null, int? warehouseId = null, int page = 1, int pageSize = 20, bool? hasChequeIncome = null, bool openingOnly = false)
    {
        var q = $"api/orders?type={(int)type}&page={page}&pageSize={pageSize}&openingOnly={openingOnly.ToString().ToLowerInvariant()}";
        if (from.HasValue) q += $"&from={from.Value:yyyy-MM-dd}";
        if (to.HasValue) q += $"&to={to.Value:yyyy-MM-dd}";
        if (partyId is > 0) q += $"&partyId={partyId}";
        if (warehouseId is > 0) q += $"&warehouseId={warehouseId}";
        if (hasChequeIncome.HasValue) q += $"&hasChequeIncome={hasChequeIncome.Value.ToString().ToLowerInvariant()}";
        return _api.GetAsync<PagedResult<Order>>(q);
    }

    public Task<OperationsOrderReportDto> GetReportSummaryAsync(TransactionType type, DateTime? from = null, DateTime? to = null,
        int? partyId = null, int? warehouseId = null, bool? hasChequeIncome = null)
    {
        var q = $"api/operations/reports/orders?type={(int)type}";
        if (from.HasValue) q += $"&from={from.Value:yyyy-MM-dd}";
        if (to.HasValue) q += $"&to={to.Value:yyyy-MM-dd}";
        if (partyId is > 0) q += $"&partyId={partyId}";
        if (warehouseId is > 0) q += $"&warehouseId={warehouseId}";
        if (hasChequeIncome.HasValue) q += $"&hasChequeIncome={hasChequeIncome.Value.ToString().ToLowerInvariant()}";
        return _api.GetAsync<OperationsOrderReportDto>(q);
    }

    public Task<ChequeIncomeReportDto> GetChequeIncomeReportAsync(DateTime? from = null, DateTime? to = null, int? partyId = null)
    {
        var q = "api/operations/reports/cheque-income?";
        if (from.HasValue) q += $"&from={from.Value:yyyy-MM-dd}";
        if (to.HasValue) q += $"&to={to.Value:yyyy-MM-dd}";
        if (partyId is > 0) q += $"&partyId={partyId}";
        return _api.GetAsync<ChequeIncomeReportDto>(q);
    }

    public Task<Order?> GetAsync(int id)
        => _api.GetAsync<Order?>($"api/orders/{id}");

    public Task<Order> CreateAsync(OrderCommand cmd)
        => _api.PostAsync<Order>("api/orders", cmd);

    public Task<Order> UpdateAsync(int id, OrderCommand cmd)
        => _api.PutAsync<Order>($"api/orders/{id}", cmd);

    public async Task ReconcileCashAsync(int id, ShopCashReconciliationCommand cmd)
        => await _api.PostAsync<object>($"api/orders/{id}/cash-reconciliation", cmd);

    public Task<ShopOrderCreditState> GetCreditStateAsync(int id)
        => _api.GetAsync<ShopOrderCreditState>($"api/orders/{id}/credit-receipts");

    public async Task ReceiveCreditAsync(int id, ShopOrderCreditReceiptCommand cmd)
        => await _api.PostAsync<object>($"api/orders/{id}/credit-receipts", cmd);

    public async Task ReverseCreditReceiptAsync(int id, int receiptId, ShopOrderCreditReversalCommand cmd)
        => await _api.PostAsync<object>($"api/orders/{id}/credit-receipts/{receiptId}/reversal", cmd);

    public Task<SaleChequeReschedulePreview> PreviewChequeRescheduleAsync(int chequeId, DateTime expectedDueDate, DateTime newDueDate)
        => _api.GetAsync<SaleChequeReschedulePreview>($"api/orders/cheques/{chequeId}/reschedule-preview?expectedDueDate={expectedDueDate:yyyy-MM-dd}&newDueDate={newDueDate:yyyy-MM-dd}");

    public Task<SaleChequeReschedulePreview> RescheduleChequeAsync(int chequeId, SaleChequeRescheduleCommand cmd)
        => _api.PutAsync<SaleChequeReschedulePreview>($"api/orders/cheques/{chequeId}/reschedule", cmd);

    public async Task ReceiveChequeAsync(int chequeId, SaleChequeReceiptCommand cmd)
    {
        await _api.PutAsync<object>($"api/orders/cheques/{chequeId}/receipt", cmd);
    }

    public async Task ReverseChequeReceiptAsync(int chequeId, SaleChequeReceiptReversalCommand cmd)
    {
        await _api.PutAsync<object>($"api/orders/cheques/{chequeId}/receipt-reversal", cmd);
    }

    public async Task ChangeChequeStatusAsync(int chequeId, SaleChequeStatusCommand cmd)
    {
        await _api.PutAsync<object>($"api/orders/cheques/{chequeId}/status", cmd);
    }

    public Task<List<SaleChequeStatusHistoryItem>> GetChequeStatusHistoryAsync(int chequeId)
        => _api.GetAsync<List<SaleChequeStatusHistoryItem>>($"api/orders/cheques/{chequeId}/status-history");

    public Task<SaleChequeReturnPreviewDto> PreviewChequeReturnAsync(int saleId, SaleChequeReturnPreviewRequest req)
        => _api.PostAsync<SaleChequeReturnPreviewDto>($"api/orders/{saleId}/cheque-return-preview", req);

    public Task<SaleChequeReturnSummaryDto> ReturnChequeOrderAsync(int saleId, SaleChequeReturnCommand cmd)
        => _api.PostAsync<SaleChequeReturnSummaryDto>($"api/orders/{saleId}/cheque-return", cmd);

    public async Task ReplaceChequeAsync(int chequeId, SaleChequeReplaceCommand cmd)
    {
        await _api.PutAsync<object>($"api/orders/cheques/{chequeId}/replace", cmd);
    }

    public async Task RefundChequeCreditAsync(int saleId, SaleChequeCreditRefundCommand cmd)
    {
        await _api.PostAsync<object>($"api/orders/{saleId}/credit-refund", cmd);
    }

    public async Task AllocateChequeCreditAsync(int saleId, SaleChequeCreditAllocationCommand cmd)
    {
        await _api.PostAsync<object>($"api/orders/{saleId}/credit-allocation", cmd);
    }

    public async Task ReceiveChequeInstallmentAsync(int installmentId, SaleChequeInstallmentReceiptCommand cmd)
    {
        await _api.PutAsync<object>($"api/orders/installments/{installmentId}/receipt", cmd);
    }

    public async Task ReverseChequeInstallmentReceiptAsync(int installmentId, SaleChequeInstallmentReversalCommand cmd)
    {
        await _api.PutAsync<object>($"api/orders/installments/{installmentId}/receipt-reversal", cmd);
    }

    public async Task CancelChequeOrderAsync(int saleId, SaleChequeCancelCommand cmd)
    {
        await _api.PostAsync<object>($"api/orders/{saleId}/cheque-cancel", cmd);
    }

    public Task<Order> EditChequeOrderAsync(int saleId, OrderCommand cmd)
        => _api.PutAsync<Order>($"api/orders/{saleId}/cheque-edit", cmd);

    public Task<List<TrsAccount>> GetActiveTreasuryAccountsAsync(TreasuryAccountKind? kind = null)
        => _api.GetAsync<List<TrsAccount>>(kind.HasValue ? $"api/orders/active-treasury-accounts?kind={(int)kind.Value}" : "api/orders/active-treasury-accounts");

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/orders/{id}");

    public Task<decimal> SuggestPriceAsync(int productId, TransactionType type)
        => _api.GetAsync<decimal>($"api/orders/suggest-price?productId={productId}&type={(int)type}");

    public Task<Order?> GetLastPurchaseAsync(int productId)
        => _api.GetAsync<Order?>($"api/orders/last-purchase?productId={productId}");
}

/// <summary>پیاده‌سازی سرویس گزارش‌ها.</summary>
public class ReportService : IReportService
{
    private readonly IApiClient _api;
    public ReportService(IApiClient api) => _api = api;

    public Task<List<KardexRow>> GetKardexAsync(int productId, int? warehouseId = null, DateTime? from = null, DateTime? to = null)
    {
        var q = $"api/kardex?productId={productId}";
        if (warehouseId is > 0) q += $"&warehouseId={warehouseId}";
        if (from.HasValue) q += $"&from={from.Value:yyyy-MM-dd}";
        if (to.HasValue) q += $"&to={to.Value:yyyy-MM-dd}";
        return _api.GetAsync<List<KardexRow>>(q);
    }

    public Task<PagedResult<ReorderItem>> GetReorderPageAsync(int skip, int take, int? warehouseId = null)
        => ListOrPaged.GetPagedAsync<ReorderItem>(_api, $"api/reorder?skip={skip}&take={take}" +
            (warehouseId is > 0 ? $"&warehouseId={warehouseId}" : ""));

    public Task<List<ReorderItem>> GetReorderAsync(int? warehouseId = null)
    {
        var q = "api/reorder";
        if (warehouseId is > 0) q += $"?warehouseId={warehouseId}";
        return _api.GetAsync<List<ReorderItem>>(q);
    }
}

/// <summary>پیاده‌سازی سرویس داشبورد.</summary>
public class DashboardService : IDashboardService
{
    private readonly IApiClient _api;
    public DashboardService(IApiClient api) => _api = api;

    public Task<DashboardSummary> GetSummaryAsync()
        => _api.GetAsync<DashboardSummary>("api/dashboard");

    public Task<OperationsDashboardDto> GetOperationsDashboardAsync(DateTime? from = null, DateTime? to = null)
    {
        var q = "api/operations/reports/dashboard";
        if (from.HasValue) q += $"?from={from.Value:yyyy-MM-dd}";
        if (to.HasValue) q += (q.Contains('?') ? "&" : "?") + $"to={to.Value:yyyy-MM-dd}";
        return _api.GetAsync<OperationsDashboardDto>(q);
    }

    public Task<List<RecentActivity>> GetRecentAsync(int count = 8)
        => _api.GetAsync<List<RecentActivity>>($"api/dashboard/recent?count={count}");

    public Task<AdminDashboard> GetAdminDashboardAsync()
        => _api.GetAsync<AdminDashboard>("api/dashboard/admin");

    public Task ClearChequeAsync(int chequeId)
        => _api.PostAsync<object>($"api/dashboard/cheques/{chequeId}/clear");

    public Task BounceChequeAsync(int chequeId, string? reason = null)
    {
        var url = $"api/dashboard/cheques/{chequeId}/bounce";
        if (!string.IsNullOrWhiteSpace(reason)) url += $"?reason={Uri.EscapeDataString(reason)}";
        return _api.PostAsync<object>(url);
    }

    public Task ResetChequeAsync(int chequeId)
        => _api.PostAsync<object>($"api/dashboard/cheques/{chequeId}/reset");

    public Task PayInstallmentAsync(int installmentId)
        => _api.PostAsync<object>($"api/dashboard/installments/{installmentId}/pay");

    public Task UnpayInstallmentAsync(int installmentId)
        => _api.PostAsync<object>($"api/dashboard/installments/{installmentId}/unpay");

    public Task SettleCreditAsync(int transactionId, decimal amount)
        => _api.PostAsync<object>($"api/dashboard/credits/{transactionId}/settle?amount={amount}");

    public Task<ShopCreditSummary> GetShopCreditSummaryAsync()
        => _api.GetAsync<ShopCreditSummary>("api/dashboard/shop-credit/summary");

    public Task<List<ShopChequeItem>> GetShopChequesAsync(ChequeStatus? status = null, TransactionType? txnType = null, string? search = null)
    {
        var q = "api/dashboard/shop-credit/cheques?";
        if (status.HasValue) q += $"status={(int)status.Value}&";
        if (txnType.HasValue) q += $"txnType={(int)txnType.Value}&";
        if (!string.IsNullOrWhiteSpace(search)) q += $"search={Uri.EscapeDataString(search)}&";
        return _api.GetAsync<List<ShopChequeItem>>(q.TrimEnd('?', '&'));
    }

    public Task<List<ShopInstallmentItem>> GetShopInstallmentsAsync(bool? isPaid = null, string? search = null)
    {
        var q = "api/dashboard/shop-credit/installments?";
        if (isPaid.HasValue) q += $"isPaid={isPaid.Value}&";
        if (!string.IsNullOrWhiteSpace(search)) q += $"search={Uri.EscapeDataString(search)}&";
        return _api.GetAsync<List<ShopInstallmentItem>>(q.TrimEnd('?', '&'));
    }
}

/// <summary>پیاده‌سازی سرویس تعمیرات سمت کلاینت.</summary>
public class RepairService : IRepairService
{
    private readonly IApiClient _api;
    public RepairService(IApiClient api) => _api = api;

    public Task<List<Technician>> GetTechniciansAsync(bool activeOnly = false)
        => _api.GetAsync<List<Technician>>($"api/technicians?activeOnly={activeOnly}");

    public Task<Technician> SaveTechnicianAsync(Technician technician)
        => _api.PostAsync<Technician>("api/technicians", technician);

    public Task DeleteTechnicianAsync(int id)
        => _api.DeleteAsync($"api/technicians/{id}");

    public Task<PagedResult<RepairOrderDto>> GetRepairsAsync(string? search, RepairStatus? status, int? technicianId, int page, int pageSize)
    {
        var url = $"api/repairs?search={Uri.EscapeDataString(search ?? "")}&page={page}&pageSize={pageSize}";
        if (status.HasValue) url += $"&status={status.Value}";
        if (technicianId is > 0) url += $"&technicianId={technicianId}";
        return _api.GetAsync<PagedResult<RepairOrderDto>>(url);
    }

    public Task<RepairOrderDto> GetRepairAsync(int id)
        => _api.GetAsync<RepairOrderDto>($"api/repairs/{id}");

    public Task<RepairOrderDto> SaveRepairAsync(RepairOrderDto repair)
        => _api.PostAsync<RepairOrderDto>("api/repairs", repair);

    public Task DeleteRepairAsync(int id)
        => _api.DeleteAsync($"api/repairs/{id}");

    public Task<RepairOrderDto> SetStatusAsync(int id, RepairStatus status)
        => _api.PostAsync<RepairOrderDto>($"api/repairs/{id}/status/{status}");

    public Task<RepairOrderDto> InvoiceAsync(int id, RepairInvoiceRequest request)
        => _api.PostAsync<RepairOrderDto>($"api/repairs/{id}/invoice", request);

    public Task<RepairOrderDto> InvoiceAsync(int id, int warehouseId)
        => InvoiceAsync(id, new RepairInvoiceRequest { WarehouseId = warehouseId });
}


/// <summary>پیاده‌سازی سرویس هزینه‌ها سمت کلاینت.</summary>
public class ExpenseService : IExpenseService
{
    private readonly IApiClient _api;
    public ExpenseService(IApiClient api) => _api = api;

    public Task<List<ExpenseCategoryDto>> GetCategoriesAsync(bool activeOnly = false)
        => _api.GetAsync<List<ExpenseCategoryDto>>($"api/expense-categories?activeOnly={activeOnly}");

    public Task<ExpenseCategoryDto> SaveCategoryAsync(ExpenseCategoryDto category)
        => _api.PostAsync<ExpenseCategoryDto>("api/expense-categories", category);

    public Task DeleteCategoryAsync(int id)
        => _api.DeleteAsync($"api/expense-categories/{id}");

    public Task<PagedResult<ExpenseDto>> GetExpensesAsync(string? search, int? categoryId, DateTime? from, DateTime? to, int page, int pageSize)
    {
        var url = $"api/expenses?search={Uri.EscapeDataString(search ?? "")}&page={page}&pageSize={pageSize}";
        if (categoryId is > 0) url += $"&categoryId={categoryId}";
        if (from.HasValue) url += $"&from={from:yyyy-MM-dd}";
        if (to.HasValue) url += $"&to={to:yyyy-MM-dd}";
        return _api.GetAsync<PagedResult<ExpenseDto>>(url);
    }

    public Task<ExpenseReportDto> GetReportAsync(int? categoryId, DateTime? from, DateTime? to)
    {
        var url = "api/expenses/report?";
        if (categoryId is > 0) url += $"categoryId={categoryId}&";
        if (from.HasValue) url += $"from={from:yyyy-MM-dd}&";
        if (to.HasValue) url += $"to={to:yyyy-MM-dd}&";
        return _api.GetAsync<ExpenseReportDto>(url.TrimEnd('?', '&'));
    }

    public Task<List<TrsAccount>> GetChequeBanksAsync() => _api.GetAsync<List<TrsAccount>>("api/expenses/cheque-banks");
    public Task<List<ShopChequeItem>> GetAvailableReceivedChequesAsync() => _api.GetAsync<List<ShopChequeItem>>("api/expenses/available-received-cheques");
    public Task<ExpenseChequeCreatedResult> CreateChequeExpenseAsync(ExpenseChequeCreateCommand command)
        => _api.PostAsync<ExpenseChequeCreatedResult>($"api/expenses/{(command.Kind == ExpenseChequeKind.Issued ? "issued-cheque" : "received-cheque")}", command);
    public async Task ChangeChequeStatusAsync(int id, ExpenseChequeKind kind, ExpenseChequeStatusCommand command)
        => await _api.PostAsync<object>($"api/expenses/cheques/{id}/{(kind == ExpenseChequeKind.Issued ? "issued-status" : "received-status")}", command);

    public Task<ExpenseDto> SaveExpenseAsync(ExpenseDto expense)
        => _api.PostAsync<ExpenseDto>("api/expenses", expense);

    public Task DeleteExpenseAsync(int id)
        => _api.DeleteAsync($"api/expenses/{id}");
}

/// <summary>پیاده‌سازی سرویس حواله تحویل کالا (بدون قیمت).</summary>
public class GoodsIssueService : IGoodsIssueService
{
    private readonly IApiClient _api;
    public GoodsIssueService(IApiClient api) => _api = api;

    public Task<List<GoodsIssueDto>> GetAsync(int? partyId = null, DateTime? from = null, DateTime? to = null, string? search = null)
    {
        var url = "api/goods-issues?";
        if (partyId is > 0) url += $"partyId={partyId}&";
        if (from.HasValue) url += $"from={from:yyyy-MM-dd}&";
        if (to.HasValue) url += $"to={to:yyyy-MM-dd}&";
        url += $"search={Uri.EscapeDataString(search ?? "")}";
        return _api.GetAsync<List<GoodsIssueDto>>(url);
    }

    public Task<GoodsIssueDto?> GetAsync(int id)
        => _api.GetAsync<GoodsIssueDto?>($"api/goods-issues/{id}");

    public Task<GoodsIssueDto> SaveAsync(GoodsIssueCommand cmd)
        => _api.PostAsync<GoodsIssueDto>("api/goods-issues", cmd);

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/goods-issues/{id}");
}

public class ShopPricingClient : IShopPricingClient
{
    private readonly IApiClient _api;
    public ShopPricingClient(IApiClient api) => _api = api;
    public Task<ShopPricingPolicyDto> GetPolicyAsync() => _api.GetAsync<ShopPricingPolicyDto>("api/shop-pricing/policy");
    public Task<ShopPricingPolicyDto> SavePolicyAsync(ShopPricingPolicyDto policy) => _api.PutAsync<ShopPricingPolicyDto>("api/shop-pricing/policy", policy);
    public Task<List<LookupItem>> GetCategoriesAsync() => _api.GetAsync<List<LookupItem>>("api/shop-pricing/categories");
    public Task<ShopPriceAdjustmentPreview> PreviewAsync(ShopPriceAdjustmentRequest request) => _api.PostAsync<ShopPriceAdjustmentPreview>("api/shop-pricing/preview", request);
    public Task<ShopPriceAdjustmentPreview> ApplyAsync(ShopPriceAdjustmentCommand command) => _api.PostAsync<ShopPriceAdjustmentPreview>("api/shop-pricing/apply", command);
    public Task<List<ShopPriceHistoryItem>> GetHistoryAsync() => _api.GetAsync<List<ShopPriceHistoryItem>>("api/shop-pricing/history?take=200");
}
