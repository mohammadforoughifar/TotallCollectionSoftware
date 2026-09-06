using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

// =====================================================================
// قراردادها و پیاده‌سازی سرویس‌های سامانه مودیان + چاپگر مالی (سمت کلاینت)
// صفحات فقط با این اینترفیس‌ها کار می‌کنند؛ هیچ آدرس API در صفحه نیست.
// =====================================================================

/// <summary>سامانه مودیان — فاکتور الکترونیکی.</summary>
public interface IMoadianClientService
{
    Task<MoadianSetting> GetSettingAsync();
    Task<MoadianSetting> SaveSettingAsync(MoadianSetting setting);

    Task<List<MoadianFiscalPeriod>> GetPeriodsAsync();
    Task<MoadianFiscalPeriod> SavePeriodAsync(MoadianFiscalPeriod period);
    Task DeletePeriodAsync(int id);

    Task<List<MoadianInvoice>> GetInvoicesAsync(int? periodId = null, MoadianInvoiceStatus? status = null, string? search = null);
    Task<MoadianInvoice> GetInvoiceAsync(int id);
    Task<MoadianInvoice> CreateManualAsync(MoadianInvoiceRequest req);
    Task<MoadianInvoice> CreateFromFacInvoiceAsync(int facInvoiceId);
    Task DeleteInvoiceAsync(int id);

    Task<MoadianInvoice> EnqueueAsync(int id);
    Task<MoadianInvoice> CancelAsync(int id);
    Task<MoadianInvoice> RetryAsync(int id);
    Task<(int sent, MoadianInvoice invoice)> SendNowAsync(int id);
    Task<int> SendPendingAsync();

    Task<MoadianPayloadResult> GetPayloadAsync(int id);

    Task<List<MoadianLog>> GetLogsAsync(int? invoiceId = null);
    Task<List<MoadianCpc>> GetCpcAsync(string? search = null);
    Task<MoadianCpc> SaveCpcAsync(MoadianCpc cpc);
    Task DeleteCpcAsync(int id);

    Task<MoadianDashboard> GetDashboardAsync();
}

/// <summary>پیاده‌سازی سرویس مودیان.</summary>
public class MoadianClientService : IMoadianClientService
{
    private readonly IApiClient _api;
    public MoadianClientService(IApiClient api) => _api = api;

    public Task<MoadianSetting> GetSettingAsync() => _api.GetAsync<MoadianSetting>("api/moadian/setting");
    public Task<MoadianSetting> SaveSettingAsync(MoadianSetting setting)
        => _api.PostAsync<MoadianSetting>("api/moadian/setting", setting);

    public Task<List<MoadianFiscalPeriod>> GetPeriodsAsync() => _api.GetAsync<List<MoadianFiscalPeriod>>("api/moadian/periods");
    public Task<MoadianFiscalPeriod> SavePeriodAsync(MoadianFiscalPeriod period)
        => _api.PostAsync<MoadianFiscalPeriod>("api/moadian/periods", period);
    public Task DeletePeriodAsync(int id) => _api.DeleteAsync($"api/moadian/periods/{id}");

    public Task<List<MoadianInvoice>> GetInvoicesAsync(int? periodId = null, MoadianInvoiceStatus? status = null, string? search = null)
    {
        var q = new List<string>();
        if (periodId is not null) q.Add($"periodId={periodId}");
        if (status is not null) q.Add($"status={(int)status}");
        if (!string.IsNullOrWhiteSpace(search)) q.Add($"search={Uri.EscapeDataString(search)}");
        var suffix = q.Count > 0 ? "?" + string.Join("&", q) : "";
        return _api.GetAsync<List<MoadianInvoice>>($"api/moadian/invoices{suffix}");
    }

    public Task<MoadianInvoice> GetInvoiceAsync(int id) => _api.GetAsync<MoadianInvoice>($"api/moadian/invoices/{id}");
    public Task<MoadianInvoice> CreateManualAsync(MoadianInvoiceRequest req)
        => _api.PostAsync<MoadianInvoice>("api/moadian/invoices", req);
    public Task<MoadianInvoice> CreateFromFacInvoiceAsync(int facInvoiceId)
        => _api.PostAsync<MoadianInvoice>($"api/moadian/invoices/from-fac/{facInvoiceId}", new { });
    public Task DeleteInvoiceAsync(int id) => _api.DeleteAsync($"api/moadian/invoices/{id}");

    public Task<MoadianInvoice> EnqueueAsync(int id)
        => _api.PostAsync<MoadianInvoice>($"api/moadian/invoices/{id}/enqueue", new { });
    public Task<MoadianInvoice> CancelAsync(int id)
        => _api.PostAsync<MoadianInvoice>($"api/moadian/invoices/{id}/cancel", new { });
    public Task<MoadianInvoice> RetryAsync(int id)
        => _api.PostAsync<MoadianInvoice>($"api/moadian/invoices/{id}/retry", new { });
    public Task<(int sent, MoadianInvoice invoice)> SendNowAsync(int id)
        => _api.PostAsync<SendNowResult>($"api/moadian/invoices/{id}/send", new { })
            .ContinueWith(t => (t.Result.sent, t.Result.invoice));
    public Task<int> SendPendingAsync() => _api.PostAsync<SendPendingResult>("api/moadian/send-pending", new { })
        .ContinueWith(t => t.Result.sent);

    public Task<MoadianPayloadResult> GetPayloadAsync(int id)
        => _api.GetAsync<MoadianPayloadResult>($"api/moadian/invoices/{id}/payload");

    public Task<List<MoadianLog>> GetLogsAsync(int? invoiceId = null)
        => _api.GetAsync<List<MoadianLog>>($"api/moadian/logs{(invoiceId is not null ? $"?invoiceId={invoiceId}" : "")}");
    public Task<List<MoadianCpc>> GetCpcAsync(string? search = null)
        => _api.GetAsync<List<MoadianCpc>>($"api/moadian/cpc?search={Uri.EscapeDataString(search ?? "")}");
    public Task<MoadianCpc> SaveCpcAsync(MoadianCpc cpc) => _api.PostAsync<MoadianCpc>("api/moadian/cpc", cpc);
    public Task DeleteCpcAsync(int id) => _api.DeleteAsync($"api/moadian/cpc/{id}");

    public Task<MoadianDashboard> GetDashboardAsync() => _api.GetAsync<MoadianDashboard>("api/moadian/dashboard");

    private class SendNowResult { public int sent { get; set; } public MoadianInvoice invoice { get; set; } = new(); }
    private class SendPendingResult { public int sent { get; set; } }
}

/// <summary>چاپگر مالی/حرارتی.</summary>
public interface IFiscalPrinterClientService
{
    Task<FiscalPrinterSetting> GetSettingAsync();
    Task<FiscalPrinterSetting> SaveSettingAsync(FiscalPrinterSetting setting);
    Task<FiscalPrintResult> RenderAsync(int invoiceId);
    Task<FiscalPrintResult> PrintAsync(int invoiceId);
}

/// <summary>پیاده‌سازی سرویس چاپگر مالی.</summary>
public class FiscalPrinterClientService : IFiscalPrinterClientService
{
    private readonly IApiClient _api;
    public FiscalPrinterClientService(IApiClient api) => _api = api;

    public Task<FiscalPrinterSetting> GetSettingAsync() => _api.GetAsync<FiscalPrinterSetting>("api/fiscal-printer/setting");
    public Task<FiscalPrinterSetting> SaveSettingAsync(FiscalPrinterSetting setting)
        => _api.PostAsync<FiscalPrinterSetting>("api/fiscal-printer/setting", setting);
    public Task<FiscalPrintResult> RenderAsync(int invoiceId) => _api.GetAsync<FiscalPrintResult>($"api/fiscal-printer/render/{invoiceId}");
    public Task<FiscalPrintResult> PrintAsync(int invoiceId)
        => _api.PostAsync<FiscalPrintResult>($"api/fiscal-printer/print/{invoiceId}", new { });
}
