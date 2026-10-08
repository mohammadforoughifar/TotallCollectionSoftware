using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

/// <summary>سال مالی مودیان و ثبت صورتحساب مستقل از ERP.</summary>
public interface IMoadianStandaloneClientService
{
    Task<List<MoadianFiscalYearDto>> GetFiscalYearsAsync(int? providerId = null);
    Task<MoadianFiscalYearDetailDto> GetFiscalYearAsync(int id);
    Task<MoadianFiscalYearDetailDto> SaveFiscalYearAsync(MoadianFiscalYearRequest request);
    Task<MoadianFiscalYearDetailDto> CloseFiscalYearAsync(int id);
    Task<MoadianFiscalYearDetailDto> ReopenFiscalYearAsync(int id);
    Task DeleteFiscalYearAsync(int id);
    Task<MoadianFiscalPeriodDto> ClosePeriodAsync(int id);
    Task<MoadianFiscalPeriodDto> ReopenPeriodAsync(int id);

    Task<MoadianNextNumberDto> GetNextNumberAsync(int fiscalYearId, DateTime? date, int? providerId = null);
    Task<MoadianInvoice> CreateInvoiceAsync(MoadianStandaloneInvoiceRequest request);
}

public sealed class MoadianStandaloneClientService : IMoadianStandaloneClientService
{
    private readonly IApiClient _api;
    public MoadianStandaloneClientService(IApiClient api) => _api = api;

    public Task<List<MoadianFiscalYearDto>> GetFiscalYearsAsync(int? providerId = null)
    {
        var query = "api/moadian/standalone/fiscal-years";
        if (providerId is > 0) query += $"?providerId={providerId}";
        return _api.GetAsync<List<MoadianFiscalYearDto>>(query);
    }

    public Task<MoadianFiscalYearDetailDto> GetFiscalYearAsync(int id)
        => _api.GetAsync<MoadianFiscalYearDetailDto>($"api/moadian/standalone/fiscal-years/{id}");

    public Task<MoadianFiscalYearDetailDto> SaveFiscalYearAsync(MoadianFiscalYearRequest request)
        => _api.PostAsync<MoadianFiscalYearDetailDto>("api/moadian/standalone/fiscal-years", request);

    public Task<MoadianFiscalYearDetailDto> CloseFiscalYearAsync(int id)
        => _api.PostAsync<MoadianFiscalYearDetailDto>($"api/moadian/standalone/fiscal-years/{id}/close", new { });

    public Task<MoadianFiscalYearDetailDto> ReopenFiscalYearAsync(int id)
        => _api.PostAsync<MoadianFiscalYearDetailDto>($"api/moadian/standalone/fiscal-years/{id}/reopen", new { });

    public Task DeleteFiscalYearAsync(int id)
        => _api.DeleteAsync($"api/moadian/standalone/fiscal-years/{id}");

    public Task<MoadianFiscalPeriodDto> ClosePeriodAsync(int id)
        => _api.PostAsync<MoadianFiscalPeriodDto>($"api/moadian/standalone/fiscal-periods/{id}/close", new { });

    public Task<MoadianFiscalPeriodDto> ReopenPeriodAsync(int id)
        => _api.PostAsync<MoadianFiscalPeriodDto>($"api/moadian/standalone/fiscal-periods/{id}/reopen", new { });

    public Task<MoadianNextNumberDto> GetNextNumberAsync(int fiscalYearId, DateTime? date, int? providerId = null)
    {
        var query = $"api/moadian/standalone/next-number?fiscalYearId={fiscalYearId}";
        if (date is not null) query += $"&date={Uri.EscapeDataString(date.Value.ToString("s"))}";
        if (providerId is > 0) query += $"&providerId={providerId}";
        return _api.GetAsync<MoadianNextNumberDto>(query);
    }

    public Task<MoadianInvoice> CreateInvoiceAsync(MoadianStandaloneInvoiceRequest request)
        => _api.PostAsync<MoadianInvoice>("api/moadian/standalone/invoices", request);
}
