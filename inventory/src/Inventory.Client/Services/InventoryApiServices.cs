using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

// =====================================================================
// پیاده‌سازی سرویس‌های سمت کلاینت — تنها نقطه‌ای که مسیرهای API ساخته می‌شوند.
// =====================================================================

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

    /// <summary>همهٔ معرف‌ها (API صفحه‌بندی‌شده است — همهٔ صفحه‌ها گرفته می‌شود).</summary>
    public Task<List<Referrer>> GetAllAsync(bool activeOnly = false)
        => PagedFetch.AllAsync<Referrer>((page, size) =>
            _api.GetAsync<PagedResult<Referrer>>($"api/referrers?activeOnly={activeOnly}&page={page}&pageSize={size}"));

    public Task<Referrer> SaveAsync(Referrer referrer)
        => _api.PostAsync<Referrer>("api/referrers", referrer);

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/referrers/{id}");

    public Task<List<Referrer>> GetWalletsAsync(string? search = null, string sortBy = "name", bool desc = false)
    {
        var url = $"api/referrers/wallets?search={Uri.EscapeDataString(search ?? "")}&sortBy={sortBy}&desc={desc}";
        return PagedFetch.AllAsync<Referrer>((page, size) =>
            _api.GetAsync<PagedResult<Referrer>>($"{url}&page={page}&pageSize={size}"));
    }

    /// <summary>فهرست صفحه‌بندی‌شدهٔ کیف پول معرف‌ها (برای جدول با صفحه‌بندی واقعی).</summary>
    public Task<PagedResult<Referrer>> GetWalletsPagedAsync(string? search = null, string sortBy = "name", bool desc = false, int page = 1, int pageSize = 20)
        => _api.GetAsync<PagedResult<Referrer>>(
            $"api/referrers/wallets?search={Uri.EscapeDataString(search ?? "")}&sortBy={sortBy}&desc={desc}&page={page}&pageSize={pageSize}");

    /// <summary>پیشوند کوئری اسناد پرداخت (فیلتر اختیاری روی یک معرف).</summary>
    private static string PaymentsUrl(int? referrerId, int page, int pageSize) =>
        referrerId is > 0
            ? $"api/referrers/payments?referrerId={referrerId}&page={page}&pageSize={pageSize}"
            : $"api/referrers/payments?page={page}&pageSize={pageSize}";

    public Task<List<ReferrerPayment>> GetPaymentsAsync(int? referrerId = null)
        => PagedFetch.AllAsync<ReferrerPayment>((page, size) =>
            _api.GetAsync<PagedResult<ReferrerPayment>>(PaymentsUrl(referrerId, page, size)));

    /// <summary>فهرست صفحه‌بندی‌شدهٔ اسناد پرداخت معرف.</summary>
    public Task<PagedResult<ReferrerPayment>> GetPaymentsPagedAsync(int? referrerId = null, int page = 1, int pageSize = 20)
        => _api.GetAsync<PagedResult<ReferrerPayment>>(PaymentsUrl(referrerId, page, pageSize));

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

    /// <summary>همهٔ گروه‌های کالا (برای درخت گروه‌ها) — API صفحه‌بندی‌شده است.</summary>
    public Task<List<ProductCategory>> GetAllAsync(bool activeOnly = false)
        => PagedFetch.AllAsync<ProductCategory>((page, size) =>
            _api.GetAsync<PagedResult<ProductCategory>>($"api/categories?activeOnly={activeOnly}&page={page}&pageSize={size}"));

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

    public Task<Product> SaveAsync(Product product)
        => _api.PostAsync<Product>("api/products", product);

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/products/{id}");

    public Task<ExcelImportResult> ImportExcelAsync(Stream fileStream, string fileName)
        => _api.PostFileAsync<ExcelImportResult>("api/products/import", fileStream, fileName);

    public string TemplateUrl => _api.BuildUrl("api/products/import/template");
}

/// <summary>پیاده‌سازی سرویس واحدهای شمارش.</summary>
public class UnitService : IUnitService
{
    private readonly IApiClient _api;
    public UnitService(IApiClient api) => _api = api;

    /// <summary>همهٔ واحدهای شمارش (برای کمبو) — API صفحه‌بندی‌شده است.</summary>
    public Task<List<MeasureUnit>> GetAllAsync(bool activeOnly = false)
        => PagedFetch.AllAsync<MeasureUnit>((page, size) =>
            _api.GetAsync<PagedResult<MeasureUnit>>($"api/units?activeOnly={activeOnly}&page={page}&pageSize={size}"));

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

    /// <summary>همهٔ انبارها (برای کمبو) — API صفحه‌بندی‌شده است.</summary>
    public Task<List<Warehouse>> GetAllAsync()
        => PagedFetch.AllAsync<Warehouse>((page, size) =>
            _api.GetAsync<PagedResult<Warehouse>>($"api/warehouses?page={page}&pageSize={size}"));

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

    /// <summary>همهٔ طرف حساب‌های یک نوع (برای کمبو) — API صفحه‌بندی‌شده است.</summary>
    public Task<List<Party>> GetAsync(PartyType type)
        => PagedFetch.AllAsync<Party>((page, size) =>
            _api.GetAsync<PagedResult<Party>>($"api/parties?type={(int)type}&page={page}&pageSize={size}"));

    public async Task<List<LookupItem>> GetLookupsAsync(PartyType type, bool activeOnly = false)
    {
        var parties = await GetAsync(type);
        if (activeOnly) parties = parties.Where(p => p.IsActive).ToList();
        return parties.Select(p => new LookupItem { Id = p.Id, Name = p.Name }).ToList();
    }

    public Task<Party> SaveAsync(Party party)
        => _api.PostAsync<Party>("api/parties", party);

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/parties/{id}");
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

    public Task<PagedResult<Order>> GetOrdersAsync(TransactionType type, DateTime? from = null, DateTime? to = null, int? partyId = null, int? warehouseId = null, int page = 1, int pageSize = 20)
    {
        var q = $"api/orders?type={(int)type}&page={page}&pageSize={pageSize}";
        if (from.HasValue) q += $"&from={from.Value:yyyy-MM-dd}";
        if (to.HasValue) q += $"&to={to.Value:yyyy-MM-dd}";
        if (partyId is > 0) q += $"&partyId={partyId}";
        if (warehouseId is > 0) q += $"&warehouseId={warehouseId}";
        return _api.GetAsync<PagedResult<Order>>(q);
    }

    public Task<Order?> GetAsync(int id)
        => _api.GetAsync<Order?>($"api/orders/{id}");

    public Task<Order> CreateAsync(OrderCommand cmd)
        => _api.PostAsync<Order>("api/orders", cmd);

    public Task<Order> UpdateAsync(int id, OrderCommand cmd)
        => _api.PutAsync<Order>($"api/orders/{id}", cmd);

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
        return PagedFetch.AllAsync<KardexRow>((page, size) =>
            _api.GetAsync<PagedResult<KardexRow>>($"{q}&page={page}&pageSize={size}"));
    }

    /// <summary>کاردکس صفحه‌بندی‌شده (برای جدول با صفحه‌بندی واقعی).</summary>
    public Task<PagedResult<KardexRow>> GetKardexPagedAsync(int productId, int? warehouseId = null, DateTime? from = null, DateTime? to = null, int page = 1, int pageSize = 50)
    {
        var q = $"api/kardex?productId={productId}&page={page}&pageSize={pageSize}";
        if (warehouseId is > 0) q += $"&warehouseId={warehouseId}";
        if (from.HasValue) q += $"&from={from.Value:yyyy-MM-dd}";
        if (to.HasValue) q += $"&to={to.Value:yyyy-MM-dd}";
        return _api.GetAsync<PagedResult<KardexRow>>(q);
    }

    public Task<List<ReorderItem>> GetReorderAsync(int? warehouseId = null)
        => PagedFetch.AllAsync<ReorderItem>((page, size) =>
            _api.GetAsync<PagedResult<ReorderItem>>(
                $"api/reorder?page={page}&pageSize={size}{(warehouseId is > 0 ? $"&warehouseId={warehouseId}" : "")}"));

    /// <summary>نقطهٔ سفارش صفحه‌بندی‌شده (برای جدول با صفحه‌بندی واقعی).</summary>
    public Task<PagedResult<ReorderItem>> GetReorderPagedAsync(int? warehouseId = null, int page = 1, int pageSize = 50)
        => _api.GetAsync<PagedResult<ReorderItem>>(
            $"api/reorder?page={page}&pageSize={pageSize}{(warehouseId is > 0 ? $"&warehouseId={warehouseId}" : "")}");
}

/// <summary>پیاده‌سازی سرویس داشبورد.</summary>
public class DashboardService : IDashboardService
{
    private readonly IApiClient _api;
    public DashboardService(IApiClient api) => _api = api;

    public Task<DashboardSummary> GetSummaryAsync()
        => _api.GetAsync<DashboardSummary>("api/dashboard");

    public Task<List<RecentActivity>> GetRecentAsync(int count = 8)
        => _api.GetAsync<List<RecentActivity>>($"api/dashboard/recent?count={count}");

    public Task<AdminDashboard> GetAdminDashboardAsync()
        => _api.GetAsync<AdminDashboard>("api/dashboard/admin");

    public Task ClearChequeAsync(int chequeId)
        => _api.PostAsync<object>($"api/dashboard/cheques/{chequeId}/clear");

    public Task PayInstallmentAsync(int installmentId)
        => _api.PostAsync<object>($"api/dashboard/installments/{installmentId}/pay");

    public Task SettleCreditAsync(int transactionId, decimal amount)
        => _api.PostAsync<object>($"api/dashboard/credits/{transactionId}/settle?amount={amount}");
}

/// <summary>پیاده‌سازی سرویس تعمیرات سمت کلاینت.</summary>
public class RepairService : IRepairService
{
    private readonly IApiClient _api;
    public RepairService(IApiClient api) => _api = api;

    /// <summary>همهٔ تعمیرکارها (برای کمبو) — API صفحه‌بندی‌شده است.</summary>
    public Task<List<Technician>> GetTechniciansAsync(bool activeOnly = false)
        => PagedFetch.AllAsync<Technician>((page, size) =>
            _api.GetAsync<PagedResult<Technician>>($"api/technicians?activeOnly={activeOnly}&page={page}&pageSize={size}"));

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

    public Task<RepairOrderDto> InvoiceAsync(int id, int warehouseId)
        => _api.PostAsync<RepairOrderDto>($"api/repairs/{id}/invoice", new RepairInvoiceRequest { WarehouseId = warehouseId });
}


/// <summary>پیاده‌سازی سرویس هزینه‌ها سمت کلاینت.</summary>
public class ExpenseService : IExpenseService
{
    private readonly IApiClient _api;
    public ExpenseService(IApiClient api) => _api = api;

    /// <summary>همهٔ دسته‌های هزینه (برای کمبو) — API صفحه‌بندی‌شده است.</summary>
    public Task<List<ExpenseCategoryDto>> GetCategoriesAsync(bool activeOnly = false)
        => PagedFetch.AllAsync<ExpenseCategoryDto>((page, size) =>
            _api.GetAsync<PagedResult<ExpenseCategoryDto>>($"api/expense-categories?activeOnly={activeOnly}&page={page}&pageSize={size}"));

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

    public Task<ExpenseDto> SaveExpenseAsync(ExpenseDto expense)
        => _api.PostAsync<ExpenseDto>("api/expenses", expense);

    public Task DeleteExpenseAsync(int id)
        => _api.DeleteAsync($"api/expenses/{id}");
}
