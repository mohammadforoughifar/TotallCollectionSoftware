using System.Text.Json;
using Inventory.Shared.Dtos.Moadian;
using InvoiceVM = Inventory.Shared.Dtos.Moadian.VmInvoiceItems.InvoiceVM;

namespace Inventory.Client.Services;

/// <summary>
/// سرویس کلاینت سامانه مودیان (SDK 0.0.27) — پیوند مستقیم به MoadianController.
/// توجه: کنترلر مودیان فاقد [Route] است؛ با رویت پیش‌فرض ASP.NET همهٔ endpointها زیر پیشوند «Moadian/» قرار می‌گیرند.
/// </summary>
public interface IMoadianService
{
    // ---------- شرکت / سال مالی / مشتری ----------
    Task<List<VMShowCpinfoByCompany>> GetCpInfosAsync();
    Task<ApiModel> SaveCpInfoAsync(VMShowCpinfoByCompany vm);
    Task<ApiModel> DeleteCpInfoAsync(int id);
    Task<List<VMFiscalYear>> GetFiscalYearsAsync(int cpinfoId);
    Task<ApiModel> SaveFiscalYearAsync(VMFiscalYear vm);
    Task<List<VMCustomerList>> GetCustomersAsync(int cpinfoId);
    Task<ApiModel> InsertCustomerAsync(VMCustomerList vm);
    Task<ApiModel> UpdateCustomerAsync(VMCustomerList vm);

    // ---------- اتصال به سامانه ----------
    Task<ApiModel> IsConnectAsync(VMConnection vm);
    Task<VMConnection?> GetConnectionAsync(int cpinfoId);
    Task<ApiModel> SaveConnectionAsync(VMConnection vm);

    // ---------- صورتحساب ----------
    Task<ApiModel> GetAllInvoicesAsync(string operation, int cpinfoId, DateTime? firstDate, DateTime? endDate, string? search, int page, int pageSize);
    Task<ApiModel> AddInvoiceAsync(InvoiceVM vm);
    Task<ApiModel> SendInvoiceAsync(InvoiceVM vm);
    Task<ApiModel> EslahiInvoiceAsync(InvoiceVM vm);
    Task<ApiModel> EbtalInvoiceAsync(InvoiceVM vm);
    Task<ApiModel> EstelamInvoiceAsync(string referenceId, int invoiceId, string dateOrder);
    Task<ApiModel> ErrorInvoiceAsync(string referenceId);
    Task<ApiModel> UpdateStatusInvoiceAsync(int invoiceId);
    Task<InvoiceVM?> GetInvoiceAsync(int id);
    Task<ApiModel> DeleteInvoiceAsync(int id);

    // ---------- کالا / واحد سنجش ----------
    Task<ApiModel> GetGoodsAsync(int cpinfoId, string? search, int page, int pageSize);
    Task<ApiModel> InsertGoodAsync(VMGoodsOrService vm);
    Task<ApiModel> UpdateGoodAsync(VMGoodsOrService vm);
    Task<List<VMGoodsOrServicesList>> GetGoodsComboAsync(int cpinfoId);
    Task<ApiModel> GetUnitsAsync(string? search, int page, int pageSize);
    Task<ApiModel> InsertUnitAsync(VMUnitOfMeasurement vm);
    Task<ApiModel> UpdateUnitAsync(VMUnitOfMeasurement vm);
    Task<ApiModel> DeleteUnitAsync(int id);
    Task<List<VMUnitOfMeasurement>> GetUnitsComboAsync();
}

public class MoadianService : IMoadianService
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };
    private readonly IApiClient _api;
    public MoadianService(IApiClient api) => _api = api;

    /// <summary>مبدل Data خامِ ApiModel به نوع مجرّد (JsonElement → T) — هرگز exception نمی‌دهد؛
    /// پاسخ‌های خطا با Data=false می‌آیند و در آن صورت default برمی‌گردد (پیام خطا از m.Message).</summary>
    public static T? As<T>(ApiModel? m)
    {
        if (m?.Data is null) return default;
        try
        {
            return m.Data switch
            {
                JsonElement el when el.ValueKind == JsonValueKind.Null => default,
                JsonElement el => el.Deserialize<T>(Opts),
                var v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v), Opts)
            };
        }
        catch { return default; }
    }

    /// <summary>برای فراخوانی‌هایی که باید داده برگردانند: خطای کسب‌وکار سرور را با پیام فارسی به‌صورت exception می‌رساند.</summary>
    private static T? Must<T>(ApiModel? m) where T : class
    {
        if (m is null) return null;
        if (!m.Success) throw new Exception(m.Message ?? "خطای سرور در سامانه مودیان.");
        return As<T>(m);
    }

    // ---------- شرکت / سال مالی / مشتری ----------
    public async Task<List<VMShowCpinfoByCompany>> GetCpInfosAsync()
        => Must<List<VMShowCpinfoByCompany>>(await _api.GetAsync<ApiModel>("Moadian/GetCpInfos")) ?? new();

    public Task<ApiModel> SaveCpInfoAsync(VMShowCpinfoByCompany vm)
        => _api.PostAsync<ApiModel>("Moadian/SaveCpInfo", vm);

    public Task<ApiModel> DeleteCpInfoAsync(int id)
        => _api.DeleteAsync<ApiModel>($"Moadian/DeleteCpInfo/{id}");

    public async Task<List<VMFiscalYear>> GetFiscalYearsAsync(int cpinfoId)
        => Must<List<VMFiscalYear>>(await _api.GetAsync<ApiModel>($"Moadian/GetFiscalYears/{cpinfoId}")) ?? new();

    public Task<ApiModel> SaveFiscalYearAsync(VMFiscalYear vm)
        => _api.PostAsync<ApiModel>("Moadian/SaveFiscalYear", vm);

    public async Task<List<VMCustomerList>> GetCustomersAsync(int cpinfoId)
        => Must<List<VMCustomerList>>(await _api.GetAsync<ApiModel>($"Moadian/GetAllCostomer?cpinfoid={cpinfoId}")) ?? new();

    public Task<ApiModel> InsertCustomerAsync(VMCustomerList vm)
        => _api.PostAsync<ApiModel>("Moadian/InsertCustomerAsync", vm);

    public Task<ApiModel> UpdateCustomerAsync(VMCustomerList vm)
        => _api.PutAsync<ApiModel>("Moadian/UpdateCustomer", vm);

    // ---------- اتصال به سامانه ----------
    public Task<ApiModel> IsConnectAsync(VMConnection vm)
        => _api.PostAsync<ApiModel>("Moadian/GetIsConnectToMoadian", vm);

    public async Task<VMConnection?> GetConnectionAsync(int cpinfoId)
        => Must<VMConnection>(await _api.GetAsync<ApiModel>($"Moadian/GetConnectForCpInfo/{cpinfoId}"));

    public Task<ApiModel> SaveConnectionAsync(VMConnection vm)
        => _api.PostAsync<ApiModel>("Moadian/ConnectionApiAsync", vm);

    // ---------- صورتحساب ----------
    public Task<ApiModel> GetAllInvoicesAsync(string operation, int cpinfoId, DateTime? firstDate, DateTime? endDate, string? search, int page, int pageSize)
    {
        var qs = $"operation={Uri.EscapeDataString(operation)}&CpinfoId={cpinfoId}"
             + $"&firstDate={firstDate:yyyy-MM-dd HH:mm:ss}&endDate={endDate:yyyy-MM-dd HH:mm:ss}"
             + $"&searchTerm={Uri.EscapeDataString(search ?? "")}&page={page}&pageSize={pageSize}";
        return _api.GetAsync<ApiModel>($"Moadian/GetAllInvoice?{qs}");
    }

    public Task<ApiModel> AddInvoiceAsync(InvoiceVM vm) => _api.PostAsync<ApiModel>("Moadian/AddInvoice", vm);
    public Task<ApiModel> SendInvoiceAsync(InvoiceVM vm) => _api.PostAsync<ApiModel>("Moadian/SendInvoiceAsync", vm);
    public Task<ApiModel> EslahiInvoiceAsync(InvoiceVM vm) => _api.PostAsync<ApiModel>("Moadian/EslahiInvoiceAsync", vm);
    public Task<ApiModel> EbtalInvoiceAsync(InvoiceVM vm) => _api.PostAsync<ApiModel>("Moadian/EbtalInvoiceAsync", vm);

    public Task<ApiModel> EstelamInvoiceAsync(string referenceId, int invoiceId, string dateOrder)
        => _api.GetAsync<ApiModel>($"Moadian/EstelamInvoiceAsync?ReferenceId={Uri.EscapeDataString(referenceId)}&InvoiceId={invoiceId}&dateorder={Uri.EscapeDataString(dateOrder)}");

    public Task<ApiModel> ErrorInvoiceAsync(string referenceId)
        => _api.GetAsync<ApiModel>($"Moadian/ErrorInvoiceAsync?ReferenceId={Uri.EscapeDataString(referenceId)}");

    public Task<ApiModel> UpdateStatusInvoiceAsync(int invoiceId)
        => _api.GetAsync<ApiModel>($"Moadian/UpdateStatusInvoice/{invoiceId}");

    public async Task<InvoiceVM?> GetInvoiceAsync(int id)
        => Must<InvoiceVM>(await _api.GetAsync<ApiModel>($"Moadian/GetInvoiceByIdAsync/{id}"));

    public Task<ApiModel> DeleteInvoiceAsync(int id) => _api.DeleteAsync<ApiModel>($"Moadian/Invoice/{id}");

    // ---------- کالا / واحد سنجش ----------
    public Task<ApiModel> GetGoodsAsync(int cpinfoId, string? search, int page, int pageSize)
        => _api.GetAsync<ApiModel>($"Moadian/GetGoodOrService?cpinfoId={cpinfoId}&searchTerm={Uri.EscapeDataString(search ?? "")}&page={page}&pageSize={pageSize}");

    public Task<ApiModel> InsertGoodAsync(VMGoodsOrService vm) => _api.PostAsync<ApiModel>("Moadian/InsertGoodOrServiceAsync", vm);
    public Task<ApiModel> UpdateGoodAsync(VMGoodsOrService vm) => _api.PutAsync<ApiModel>("Moadian/UpdateGoodOrService", vm);

    public async Task<List<VMGoodsOrServicesList>> GetGoodsComboAsync(int cpinfoId)
        => Must<List<VMGoodsOrServicesList>>(await _api.GetAsync<ApiModel>($"Moadian/GetGoodOrServiceForComboAsync?cpinfoid={cpinfoId}")) ?? new();

    public Task<ApiModel> GetUnitsAsync(string? search, int page, int pageSize)
        => _api.GetAsync<ApiModel>($"Moadian/GetAllUnitOfMeasurementAsync?searchTerm={Uri.EscapeDataString(search ?? "")}&page={page}&pageSize={pageSize}");

    public Task<ApiModel> InsertUnitAsync(VMUnitOfMeasurement vm) => _api.PostAsync<ApiModel>("Moadian/InsertUnitOfMeasurementAsync", vm);
    public Task<ApiModel> UpdateUnitAsync(VMUnitOfMeasurement vm) => _api.PutAsync<ApiModel>("Moadian/UpdateUnitOfMeasurementAsync", vm);
    public Task<ApiModel> DeleteUnitAsync(int id) => _api.DeleteAsync<ApiModel>($"Moadian/{id}");
    public async Task<List<VMUnitOfMeasurement>> GetUnitsComboAsync()
        => Must<List<VMUnitOfMeasurement>>(await _api.GetAsync<ApiModel>("Moadian/GetUnitOfMeasurementForComboAsync")) ?? new();
}
