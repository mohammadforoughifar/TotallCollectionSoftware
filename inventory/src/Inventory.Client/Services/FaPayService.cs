using Inventory.Shared.Dtos;
using Microsoft.JSInterop;

namespace Inventory.Client.Services;

/// <summary>سرویس کلاینت حقوق و دستمزد فروغ آریا — FaPay §۸ (ماژول مستقل).</summary>
public interface IFaPayClientService
{
    Task<FaPaySettingsDto> GetSettingsAsync();
    Task<FaPaySettingsDto> SaveSettingsAsync(FaPaySettingsSaveDto dto);

    Task<List<FaPayTaxBracketDto>> ListBracketsAsync();
    Task<FaPayTaxBracketDto> SaveBracketAsync(int? id, FaPayTaxBracketSaveDto dto);
    Task DeleteBracketAsync(int id);

    Task<List<FaPayItemTypeDto>> ListItemTypesAsync();
    Task<FaPayItemTypeDto> SaveItemTypeAsync(int? id, FaPayItemTypeSaveDto dto);
    Task DeleteItemTypeAsync(int id);

    Task<List<FaPayAdjustmentDto>> ListAdjustmentsAsync(int? year = null, int? month = null, int? employeeId = null);
    Task<FaPayAdjustmentDto> SaveAdjustmentAsync(int? id, FaPayAdjustmentSaveDto dto);
    Task DeleteAdjustmentAsync(int id);

    Task<List<FaPayRunDto>> ListRunsAsync();
    Task<FaPayRunDto> GetRunAsync(int id);
    Task<FaPayRunDto> CreateRunAsync(FaPayRunSaveDto dto);
    Task<int> CalculateRunAsync(int id);
    Task<FaPayRunDto> FinalizeRunAsync(int id);
    Task<FaPayRunDto> ReopenRunAsync(int id);
    Task DeleteRunAsync(int id);
    Task<FaPayMailResultDto> SendRunEmailsAsync(int id);

    Task<List<FaPaySlipDto>> RunSlipsAsync(int runId);
    Task<FaPayBankCheckDto> BankCheckAsync(int runId);
    Task DownloadBankFileAsync(int runId, string format);
    Task<FaPaySlipDto> GetSlipAsync(int id);
    Task<List<FaPaySlipDto>> MySlipsAsync();
    Task<FaPaySlipDto> GetMySlipAsync(int id);
    Task SetPaidAsync(int id, bool paid);
    Task SendSlipAsync(int id);
    Task DownloadSlipPdfAsync(int id, bool mine);

    Task<FaPayInsuranceReportDto> InsuranceReportAsync(int year, int month);
    Task<FaPayTaxReportDto> TaxReportAsync(int year, int month);
    Task<FaPayUnitCostReportDto> UnitCostReportAsync(int year, int month);
}

public class FaPayService : IFaPayClientService
{
    private readonly IApiClient _api;
    private readonly IJSRuntime _js;
    public FaPayService(IApiClient api, IJSRuntime js) { _api = api; _js = js; }

    private const string Root = "api/fa-pay";

    public Task<FaPaySettingsDto> GetSettingsAsync()
        => _api.GetAsync<FaPaySettingsDto>($"{Root}/settings");

    public Task<FaPaySettingsDto> SaveSettingsAsync(FaPaySettingsSaveDto dto)
        => _api.PutAsync<FaPaySettingsDto>($"{Root}/settings", dto);

    public Task<List<FaPayTaxBracketDto>> ListBracketsAsync()
        => _api.GetAsync<List<FaPayTaxBracketDto>>($"{Root}/brackets");

    public Task<FaPayTaxBracketDto> SaveBracketAsync(int? id, FaPayTaxBracketSaveDto dto)
        => id == null ? _api.PostAsync<FaPayTaxBracketDto>($"{Root}/brackets", dto)
                      : _api.PutAsync<FaPayTaxBracketDto>($"{Root}/brackets/{id}", dto);

    public Task DeleteBracketAsync(int id)
        => _api.DeleteAsync($"{Root}/brackets/{id}");

    public Task<List<FaPayItemTypeDto>> ListItemTypesAsync()
        => _api.GetAsync<List<FaPayItemTypeDto>>($"{Root}/itemtypes");

    public Task<FaPayItemTypeDto> SaveItemTypeAsync(int? id, FaPayItemTypeSaveDto dto)
        => id == null ? _api.PostAsync<FaPayItemTypeDto>($"{Root}/itemtypes", dto)
                      : _api.PutAsync<FaPayItemTypeDto>($"{Root}/itemtypes/{id}", dto);

    public Task DeleteItemTypeAsync(int id)
        => _api.DeleteAsync($"{Root}/itemtypes/{id}");

    public Task<List<FaPayAdjustmentDto>> ListAdjustmentsAsync(int? year = null, int? month = null, int? employeeId = null)
        => _api.GetAsync<List<FaPayAdjustmentDto>>($"{Root}/adjustments?year={year}&month={month}&employeeId={employeeId}");

    public Task<FaPayAdjustmentDto> SaveAdjustmentAsync(int? id, FaPayAdjustmentSaveDto dto)
        => id == null ? _api.PostAsync<FaPayAdjustmentDto>($"{Root}/adjustments", dto)
                      : _api.PutAsync<FaPayAdjustmentDto>($"{Root}/adjustments/{id}", dto);

    public Task DeleteAdjustmentAsync(int id)
        => _api.DeleteAsync($"{Root}/adjustments/{id}");

    public Task<List<FaPayRunDto>> ListRunsAsync()
        => _api.GetAsync<List<FaPayRunDto>>($"{Root}/runs");

    public Task<FaPayRunDto> GetRunAsync(int id)
        => _api.GetAsync<FaPayRunDto>($"{Root}/runs/{id}");

    public Task<FaPayRunDto> CreateRunAsync(FaPayRunSaveDto dto)
        => _api.PostAsync<FaPayRunDto>($"{Root}/runs", dto);

    public async Task<int> CalculateRunAsync(int id)
    {
        var r = await _api.PostAsync<CalcResult>($"{Root}/runs/{id}/calculate");
        return r?.Count ?? 0;
    }

    public Task<FaPayRunDto> FinalizeRunAsync(int id)
        => _api.PostAsync<FaPayRunDto>($"{Root}/runs/{id}/finalize");

    public Task<FaPayRunDto> ReopenRunAsync(int id)
        => _api.PostAsync<FaPayRunDto>($"{Root}/runs/{id}/reopen");

    public Task DeleteRunAsync(int id)
        => _api.DeleteAsync($"{Root}/runs/{id}");

    public Task<FaPayMailResultDto> SendRunEmailsAsync(int id)
        => _api.PostAsync<FaPayMailResultDto>($"{Root}/runs/{id}/send-all");

    public Task<List<FaPaySlipDto>> RunSlipsAsync(int runId)
        => _api.GetAsync<List<FaPaySlipDto>>($"{Root}/runs/{runId}/slips");

    public Task<FaPayBankCheckDto> BankCheckAsync(int runId)
        => _api.GetAsync<FaPayBankCheckDto>($"{Root}/runs/{runId}/bank-check");

    public async Task DownloadBankFileAsync(int runId, string format)
    {
        var f = await _api.GetFileAsync($"{Root}/runs/{runId}/bank-file?format={format}");
        await _js.InvokeVoidAsync("saveAsFile", f.FileName, Convert.ToBase64String(f.Data));
    }

    public Task<FaPaySlipDto> GetSlipAsync(int id)
        => _api.GetAsync<FaPaySlipDto>($"{Root}/slips/{id}");

    public Task<List<FaPaySlipDto>> MySlipsAsync()
        => _api.GetAsync<List<FaPaySlipDto>>($"{Root}/slips/my");

    public Task<FaPaySlipDto> GetMySlipAsync(int id)
        => _api.GetAsync<FaPaySlipDto>($"{Root}/slips/my/{id}");

    public Task SetPaidAsync(int id, bool paid)
        => _api.PostAsync<bool>($"{Root}/slips/{id}/paid?paid={paid}");

    public Task SendSlipAsync(int id)
        => _api.PostAsync<MailResult>($"{Root}/slips/{id}/send");

    public async Task DownloadSlipPdfAsync(int id, bool mine)
    {
        var path = mine ? $"{Root}/slips/my/{id}/pdf" : $"{Root}/slips/{id}/pdf";
        var f = await _api.GetFileAsync(path);
        await _js.InvokeVoidAsync("saveAsFile", f.FileName, Convert.ToBase64String(f.Data));
    }

    public Task<FaPayInsuranceReportDto> InsuranceReportAsync(int year, int month)
        => _api.GetAsync<FaPayInsuranceReportDto>($"{Root}/reports/insurance?year={year}&month={month}");

    public Task<FaPayTaxReportDto> TaxReportAsync(int year, int month)
        => _api.GetAsync<FaPayTaxReportDto>($"{Root}/reports/tax?year={year}&month={month}");

    public Task<FaPayUnitCostReportDto> UnitCostReportAsync(int year, int month)
        => _api.GetAsync<FaPayUnitCostReportDto>($"{Root}/reports/unit-cost?year={year}&month={month}");

    private sealed class CalcResult { public int Count { get; set; } }
    private sealed class MailResult { public int MailId { get; set; } }
}
