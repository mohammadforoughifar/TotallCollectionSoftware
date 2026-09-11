using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

public interface IHrCoreService
{
    Task<HrEmployeeListResult> SearchEmployeesAsync(string? q, int? orgUnitId, int? status, int skip, int take);
    Task<HrEmployeeDto> GetEmployeeAsync(int id);
    Task<HrEmployeeDto> SaveEmployeeAsync(int? id, HrEmployeeSaveDto dto);
    Task SetEmployeeActiveAsync(int id, bool active);
    Task<string> NextEmployeeCodeAsync();

    Task<List<HrOrgUnitDto>> GetOrgTreeAsync();
    Task<List<HrOrgUnitDto>> GetOrgUnitsAsync();
    Task<HrOrgUnitDto> SaveOrgUnitAsync(int? id, HrOrgUnitSaveDto dto);
    Task DeleteOrgUnitAsync(int id);

    Task<List<HrContractDto>> GetContractsAsync(bool? onlyActive = null);
    Task<List<HrContractDto>> GetExpiringContractsAsync(int days = 30);
    Task<List<HrContractDto>> GetEmployeeContractsAsync(int employeeId);
    Task<HrContractDto> SaveContractAsync(int? id, HrContractSaveDto dto);
    Task DeleteContractAsync(int id);

    Task<List<HrDecreeDto>> GetDecreesAsync(int? employeeId = null, bool? onlyPending = null);
    Task<List<HrDecreeDto>> GetEmployeeDecreesAsync(int employeeId);
    Task<HrDecreeDto> SaveDecreeAsync(int? id, HrDecreeSaveDto dto);
    Task<HrDecreeDto> ApplyDecreeAsync(int id);
    Task DeleteDecreeAsync(int id);

    Task<HrDashboardDto> GetDashboardAsync();
}

public class HrEmployeeListResult
{
    public int Total { get; set; }
    public List<HrEmployeeDto> Items { get; set; } = new();
}

public class HrCoreService : IHrCoreService
{
    private readonly IApiClient _api;
    public HrCoreService(IApiClient api) => _api = api;

    private const string Root = "api/hr-core";

    public Task<HrEmployeeListResult> SearchEmployeesAsync(string? q, int? orgUnitId, int? status, int skip, int take)
    {
        var qs = $"?skip={skip}&take={take}";
        if (!string.IsNullOrWhiteSpace(q)) qs += $"&q={Uri.EscapeDataString(q)}";
        if (orgUnitId is > 0) qs += $"&orgUnitId={orgUnitId}";
        if (status is >= 0) qs += $"&status={status}";
        return _api.GetAsync<HrEmployeeListResult>($"{Root}/employees{qs}");
    }

    public Task<HrEmployeeDto> GetEmployeeAsync(int id)
        => _api.GetAsync<HrEmployeeDto>($"{Root}/employees/{id}");

    public Task<HrEmployeeDto> SaveEmployeeAsync(int? id, HrEmployeeSaveDto dto)
        => id is > 0
            ? _api.PutAsync<HrEmployeeDto>($"{Root}/employees/{id}", dto)
            : _api.PostAsync<HrEmployeeDto>($"{Root}/employees", dto);

    public Task SetEmployeeActiveAsync(int id, bool active)
        => _api.PostAsync<object>($"{Root}/employees/{id}/active?active={active}", null);

    public async Task<string> NextEmployeeCodeAsync()
    {
        var r = await _api.GetAsync<Dictionary<string, string>>($"{Root}/employees/next-code");
        return r.TryGetValue("code", out var c) ? c : "";
    }

    public Task<List<HrOrgUnitDto>> GetOrgTreeAsync()
        => _api.GetAsync<List<HrOrgUnitDto>>($"{Root}/org/tree");

    public Task<List<HrOrgUnitDto>> GetOrgUnitsAsync()
        => _api.GetAsync<List<HrOrgUnitDto>>($"{Root}/org/units");

    public Task<HrOrgUnitDto> SaveOrgUnitAsync(int? id, HrOrgUnitSaveDto dto)
        => id is > 0
            ? _api.PutAsync<HrOrgUnitDto>($"{Root}/org/units/{id}", dto)
            : _api.PostAsync<HrOrgUnitDto>($"{Root}/org/units", dto);

    public Task DeleteOrgUnitAsync(int id)
        => _api.DeleteAsync($"{Root}/org/units/{id}");

    public Task<List<HrContractDto>> GetContractsAsync(bool? onlyActive = null)
        => _api.GetAsync<List<HrContractDto>>($"{Root}/contracts{(onlyActive == true ? "?onlyActive=true" : "")}");

    public Task<List<HrContractDto>> GetExpiringContractsAsync(int days = 30)
        => _api.GetAsync<List<HrContractDto>>($"{Root}/contracts/expiring?days={days}");

    public Task<List<HrContractDto>> GetEmployeeContractsAsync(int employeeId)
        => _api.GetAsync<List<HrContractDto>>($"{Root}/employees/{employeeId}/contracts");

    public Task<HrContractDto> SaveContractAsync(int? id, HrContractSaveDto dto)
        => id is > 0
            ? _api.PutAsync<HrContractDto>($"{Root}/contracts/{id}", dto)
            : _api.PostAsync<HrContractDto>($"{Root}/contracts", dto);

    public Task DeleteContractAsync(int id)
        => _api.DeleteAsync($"{Root}/contracts/{id}");

    public Task<List<HrDecreeDto>> GetDecreesAsync(int? employeeId = null, bool? onlyPending = null)
    {
        var qs = new List<string>();
        if (employeeId is > 0) qs.Add($"employeeId={employeeId}");
        if (onlyPending == true) qs.Add("onlyPending=true");
        var s = qs.Count > 0 ? "?" + string.Join("&", qs) : "";
        return _api.GetAsync<List<HrDecreeDto>>($"{Root}/decrees{s}");
    }

    public Task<List<HrDecreeDto>> GetEmployeeDecreesAsync(int employeeId)
        => _api.GetAsync<List<HrDecreeDto>>($"{Root}/employees/{employeeId}/decrees");

    public Task<HrDecreeDto> SaveDecreeAsync(int? id, HrDecreeSaveDto dto)
        => id is > 0
            ? _api.PutAsync<HrDecreeDto>($"{Root}/decrees/{id}", dto)
            : _api.PostAsync<HrDecreeDto>($"{Root}/decrees", dto);

    public Task<HrDecreeDto> ApplyDecreeAsync(int id)
        => _api.PostAsync<HrDecreeDto>($"{Root}/decrees/{id}/apply", null);

    public Task DeleteDecreeAsync(int id)
        => _api.DeleteAsync($"{Root}/decrees/{id}");

    public Task<HrDashboardDto> GetDashboardAsync()
        => _api.GetAsync<HrDashboardDto>($"{Root}/dashboard");
}
