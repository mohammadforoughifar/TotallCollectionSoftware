using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Api.Services;

/// <summary>قرارداد سرویس تعمیرات (پذیرش دستگاه، تعمیرکار، صدور فاکتور).</summary>
public interface IRepairService
{
    // ---------------- تعمیرکارها ----------------
    Task<List<Technician>> GetTechniciansAsync(bool activeOnly = false, Paging.Request? pagination = null);
    Task<Technician> SaveTechnicianAsync(Technician dto);
    Task DeleteTechnicianAsync(int id);

    Task<PagedResult<Product>> RepairProductsAsync(string? search, int page, int pageSize);
    Task<Product?> RepairProductAsync(int id);
    Task<List<Warehouse>> RepairWarehousesAsync();
    Task<List<TrsAccount>> RepairAccountsAsync();

    // ---------------- پذیرش تعمیر ----------------
    Task<PagedResult<RepairOrderDto>> GetRepairsAsync(string? search, RepairStatus? status, int? technicianId, int page, int pageSize);
    Task<RepairOrderDto?> GetRepairAsync(int id);
    Task<RepairOrderDto> SaveRepairAsync(RepairOrderDto dto);
    Task DeleteRepairAsync(int id);

    /// <summary>تغییر وضعیت پذیرش (در حال تعمیر / آماده تحویل / انصراف).</summary>
    Task<RepairOrderDto> SetStatusAsync(int id, RepairStatus status);

    /// <summary>صدور فاکتور فروش از روی پذیرش + تحویل دستگاه (خروج از مجموعه).</summary>
    Task<RepairOrderDto> InvoiceAsync(int id, RepairInvoiceRequest request, string? user = null);
    Task<ShopOrderCreditState> WarrantyCreditStateAsync(int id);
    Task ReceiveWarrantyCreditAsync(int id, ShopOrderCreditReceiptCommand command, string? user);
    Task ReverseWarrantyCreditAsync(int id, int receiptId, ShopOrderCreditReversalCommand command, string? user);
    Task<Order> LinkedInvoiceAsync(int id);
}
