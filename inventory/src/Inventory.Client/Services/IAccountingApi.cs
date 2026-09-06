using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

// =====================================================================
// قراردادهای سرویس‌های ماژول حسابداری (سمت کلاینت)
// صفحات فقط با این اینترفیس‌ها کار می‌کنند و هیچ آدرس API در صفحه نیست.
// =====================================================================

/// <summary>سال‌های (دوره‌های) مالی.</summary>
public interface IAccFiscalYearService
{
    Task<List<AccFiscalYear>> GetAllAsync();
    Task<AccFiscalYear> SaveAsync(AccFiscalYear year);
    Task SetCurrentAsync(int id);
    Task DeleteAsync(int id);
}

/// <summary>کدینگ حساب‌ها — درختی (گروه ← کل ← معین ← تفصیلی).</summary>
public interface IAccAccountService
{
    Task<List<AccAccount>> GetTreeAsync(bool activeOnly = false, bool withBalances = false);
    Task<List<AccAccount>> GetFlatAsync(bool activeOnly = false, bool withBalances = false);
    Task<List<LookupItem>> GetLookupsAsync(string? search = null);
    Task<AccAccount> SaveAsync(AccAccount account);
    Task MoveAsync(AccAccountMove cmd);
    Task DeleteAsync(int id);
}

/// <summary>اسناد حسابداری.</summary>
public interface IAccVoucherService
{
    Task<PagedResult<AccVoucher>> GetAllAsync(int? fiscalYearId = null, VoucherStatus? status = null,
        VoucherSource? source = null, string? search = null, DateTime? from = null, DateTime? to = null,
        int page = 1, int pageSize = 15);
    Task<AccVoucher> GetAsync(int id);
    Task<AccVoucher> NewAsync();
    Task<AccVoucher> SaveAsync(AccVoucher voucher);
    Task<AccVoucher> ConfirmAsync(int id);
    Task<AccVoucher> UnconfirmAsync(int id);
    Task<AccVoucher> CancelAsync(int id);
    Task DeleteAsync(int id);
}

/// <summary>گزارش‌های حسابداری: دفتر حساب، روزنامه، تراز آزمایشی و داشبورد.</summary>
public interface IAccReportService
{
    Task<AccLedgerResult> GetLedgerAsync(int accountId, DateTime? from = null, DateTime? to = null, bool includeChildren = true);
    Task<PagedResult<AccLedgerRow>> GetJournalAsync(DateTime? from = null, DateTime? to = null, int page = 1, int pageSize = 30);
    Task<AccTrialBalanceResult> GetTrialBalanceAsync(AccountLevel level = AccountLevel.General,
        DateTime? from = null, DateTime? to = null, bool hideZero = true);
    Task<AccDashboard> GetDashboardAsync();
}

/// <summary>قواعد صدور خودکار سند حسابداری از روی اسناد انبار.</summary>
public interface IAccInvRuleService
{
    Task<List<AccInvRule>> GetAllAsync();
    Task<AccInvRule> SaveAsync(AccInvRule rule);
    Task<AccVoucher> PostDocAsync(int invDocId);
}
