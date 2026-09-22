using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

// =====================================================================
// اینترفیس‌های سرویس‌های سمت کلاینت — صفحات فقط با این قراردادها کار می‌کنند
// و هیچ صفحه‌ای مستقیماً URL یا HttpClient صدا نمی‌زند.
// =====================================================================

/// <summary>سرویس تنظیمات برنامه.</summary>
public interface ISettingsService
{
    Task<AppSettings> GetAsync();
    Task<AppSettings> SaveAsync(AppSettings settings);
}

/// <summary>سرویس معرف‌ها (بازاریاب‌ها) + کیف پول و پرداخت‌ها.</summary>
public interface IReferrerService
{
    Task<List<Referrer>> GetAllAsync(bool activeOnly = false);
    Task<Referrer> SaveAsync(Referrer referrer);
    Task DeleteAsync(int id);

    /// <summary>کیف پول معرف‌ها با جستجو و مرتب‌سازی (sortBy: name|commission|paid|balance).</summary>
    Task<List<Referrer>> GetWalletsAsync(string? search = null, string sortBy = "name", bool desc = false);
    Task<List<ReferrerPayment>> GetPaymentsAsync(int? referrerId = null);
    Task<ReferrerPayment> AddPaymentAsync(ReferrerPayment payment);
    Task DeletePaymentAsync(int id);
}

/// <summary>سرویس گروه‌های کالا (درختی).</summary>
public interface ICategoryService
{
    Task<List<ProductCategory>> GetAllAsync(bool activeOnly = false);
    Task<ProductCategory> SaveAsync(ProductCategory category);
    Task DeleteAsync(int id);
}

/// <summary>سرویس واحدهای شمارش.</summary>
public interface IUnitService
{
    Task<List<MeasureUnit>> GetAllAsync(bool activeOnly = false);
    Task<MeasureUnit> SaveAsync(MeasureUnit unit);
    Task DeleteAsync(int id);
}

/// <summary>سرویس کالاها.</summary>
public interface IProductService
{
    Task<PagedResult<Product>> GetProductsAsync(string? search = null, bool belowOnly = false, int page = 1, int pageSize = 20, int? warehouseId = null);
    Task<List<LookupItem>> GetLookupsAsync();
    Task<Product> SaveAsync(Product product);
    Task DeleteAsync(int id);

    /// <summary>ورود گروهی کالا از فایل اکسل.</summary>
    Task<ExcelImportResult> ImportExcelAsync(Stream fileStream, string fileName);

    /// <summary>دانلود فایل اکسل نمونه (با توکن احراز هویت؛ مرورگر را از صفحه خارج نمی‌کند).</summary>
    Task<(byte[] Data, string FileName, string ContentType)> DownloadTemplateAsync();
}

/// <summary>سرویس انبارها.</summary>
public interface IWarehouseService
{
    Task<List<Warehouse>> GetAllAsync();
    Task<List<LookupItem>> GetLookupsAsync(bool activeOnly = false);
    Task<Warehouse> SaveAsync(Warehouse warehouse);
    Task DeleteAsync(int id);
}

/// <summary>سرویس طرف حساب‌ها.</summary>
public interface IPartyService
{
    Task<List<Party>> GetAsync(PartyType type);
    Task<List<LookupItem>> GetLookupsAsync(PartyType type, bool activeOnly = false);
    Task<Party> SaveAsync(Party party);
    Task DeleteAsync(int id);
}

/// <summary>سرویس موجودی انبار.</summary>
public interface IStockService
{
    Task<PagedResult<StockItem>> GetStockAsync(int? warehouseId = null, string? search = null, bool belowOnly = false, int page = 1, int pageSize = 20);
    Task AdjustAsync(AdjustmentCommand cmd);
}

/// <summary>سرویس اسناد خرید و فروش.</summary>
public interface IOrderService
{
    Task<PagedResult<Order>> GetOrdersAsync(TransactionType type, DateTime? from = null, DateTime? to = null, int? partyId = null, int? warehouseId = null, int page = 1, int pageSize = 20);
    Task<Order?> GetAsync(int id);
    Task<Order> CreateAsync(OrderCommand cmd);
    Task<Order> UpdateAsync(int id, OrderCommand cmd);
    Task DeleteAsync(int id);
    Task<decimal> SuggestPriceAsync(int productId, TransactionType type);

    /// <summary>آخرین فاکتور خریدی که شامل این کالاست.</summary>
    Task<Order?> GetLastPurchaseAsync(int productId);
}

/// <summary>سرویس گزارش‌ها (کاردکس و نقطه سفارش).</summary>
public interface IReportService
{
    Task<List<KardexRow>> GetKardexAsync(int productId, int? warehouseId = null, DateTime? from = null, DateTime? to = null);
    Task<List<ReorderItem>> GetReorderAsync(int? warehouseId = null);
}

/// <summary>سرویس داشبورد.</summary>
public interface IDashboardService
{
    Task<DashboardSummary> GetSummaryAsync();
    Task<List<RecentActivity>> GetRecentAsync(int count = 8);
    Task<AdminDashboard> GetAdminDashboardAsync();
    Task ClearChequeAsync(int chequeId);
    Task PayInstallmentAsync(int installmentId);
    Task SettleCreditAsync(int transactionId, decimal amount);
}

/// <summary>سرویس اعلان‌ها (Toast).</summary>
public interface IToastService
{
    IReadOnlyList<ToastMessage> Toasts { get; }
    event Action? OnChange;
    void Success(string message);
    void Error(string message);
    void Info(string message);
    void Remove(Guid id);
}

/// <summary>سرویس تعمیرات سمت کلاینت.</summary>
public interface IRepairService
{
    // تعمیرکارها
    Task<List<Technician>> GetTechniciansAsync(bool activeOnly = false);
    Task<Technician> SaveTechnicianAsync(Technician technician);
    Task DeleteTechnicianAsync(int id);

    // پذیرش‌ها
    Task<PagedResult<RepairOrderDto>> GetRepairsAsync(string? search, RepairStatus? status, int? technicianId, int page, int pageSize);
    Task<RepairOrderDto> GetRepairAsync(int id);
    Task<RepairOrderDto> SaveRepairAsync(RepairOrderDto repair);
    Task DeleteRepairAsync(int id);
    Task<RepairOrderDto> SetStatusAsync(int id, RepairStatus status);
    Task<RepairOrderDto> InvoiceAsync(int id, int warehouseId);
}


/// <summary>سرویس هزینه‌ها سمت کلاینت.</summary>
public interface IExpenseService
{
    Task<List<ExpenseCategoryDto>> GetCategoriesAsync(bool activeOnly = false);
    Task<ExpenseCategoryDto> SaveCategoryAsync(ExpenseCategoryDto category);
    Task DeleteCategoryAsync(int id);

    Task<PagedResult<ExpenseDto>> GetExpensesAsync(string? search, int? categoryId, DateTime? from, DateTime? to, int page, int pageSize);
    Task<ExpenseDto> SaveExpenseAsync(ExpenseDto expense);
    Task DeleteExpenseAsync(int id);
}

// =====================================================================
//  ماژول مدیریت برنامه‌نویسان (DevTeam)
//  سه پرسشی که پاسخ می‌دهد: چه کسی روی چه ماژولی کار کرده، کدام آیتم در
//  جریان است، و مالک هر ماژول کیست.
// =====================================================================

/// <summary>سرویس مدیریت برنامه‌نویسان سمت کلاینت.</summary>
public interface IDevTeamService
{
    // بورد و تاریخچه
    Task<DevBoardDto> GetBoardAsync(int? moduleId = null, int? assigneeId = null);
    Task<List<DevTaskLogDto>> GetActivityAsync(int days = 14, int take = 200);

    // اعضای تیم
    Task<List<DevMemberDto>> GetMembersAsync(bool activeOnly = false);
    Task<DevMemberDto> SaveMemberAsync(DevMemberDto dto);
    Task DeleteMemberAsync(int id);

    // ماژول‌ها و مالکیت
    Task<List<DevModuleDto>> GetModulesAsync(bool activeOnly = false);
    Task<DevModuleDto> SaveModuleAsync(DevModuleDto dto);
    Task DeleteModuleAsync(int id);
    Task<DevModuleDto> SetModuleOwnerAsync(int moduleId, int? ownerId);
    Task<int> SyncModulesFromRepoAsync();

    // آیتم‌های کاری
    Task<PagedResult<DevTaskDto>> GetTasksAsync(string? search, DevTaskStatus? status, int? moduleId, int? assigneeId, int page, int pageSize);
    Task<DevTaskDto?> GetTaskAsync(int id);
    Task<DevTaskDto> SaveTaskAsync(DevTaskDto dto);
    Task DeleteTaskAsync(int id);
    Task<DevTaskDto> SetStatusAsync(int id, DevTaskStatusRequest request);
    Task<DevTaskDto> AssignAsync(int id, int? memberId, string? note = null);
    Task<List<DevTaskLogDto>> GetTaskLogsAsync(int taskId);
    Task<DevTaskLogDto> AddLogAsync(int taskId, DevTaskLogRequest request);
}
