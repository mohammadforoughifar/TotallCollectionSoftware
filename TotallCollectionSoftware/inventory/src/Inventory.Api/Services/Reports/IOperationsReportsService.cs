using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Api.Services.Reports;

/// <summary>سرویس گزارش‌های بخش عملیات: خرید/فروش و داشبورد یکپارچه.</summary>
public interface IOperationsReportsService
{
    Task<OperationsOrderReportDto> GetOrderReportAsync(TransactionType type, DateTime? from, DateTime? to,
        int? partyId, int? warehouseId, bool? hasChequeIncome = null);

    Task<ChequeIncomeReportDto> GetChequeIncomeReportAsync(DateTime? from, DateTime? to, int? partyId);

    Task<OperationsDashboardDto> GetDashboardAsync(DateTime? from, DateTime? to,
        bool includeOrders, bool includeRepairs, bool includeReferrers);
}
