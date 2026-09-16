using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

/// <summary>پاسخ فهرست دیتاست‌ها — همراه با اینکه آیا کاربر اجازهٔ طراحی گزارش دارد.</summary>
public class ReportDatasetsResult
{
    public bool CanDesign { get; set; }
    public List<ReportDatasetDto> Datasets { get; set; } = new();
}

/// <summary>پاسخ سادهٔ «شناسه» برای ساخت/ویرایش گزارش.</summary>
public class ReportIdResult
{
    public int Id { get; set; }
}

public interface IReportClient
{
    /// <summary>دیتاست‌هایی که کاربر مجوز دیدن داده‌شان را دارد.</summary>
    Task<ReportDatasetsResult> GetDatasetsAsync();

    /// <summary>نقش‌های فعال برای اشتراک‌گذاری گزارش.</summary>
    Task<List<ReportRoleDto>> GetRolesAsync();

    /// <summary>گزارش‌های من + گزارش‌های اشتراک‌شده با نقش‌هایم.</summary>
    Task<List<UserReportDto>> GetReportsAsync();

    /// <summary>اجرای کوئری بدون ذخیره (پیش‌نمایش).</summary>
    Task<ReportRunResultDto> PreviewAsync(ReportQueryDto query);

    /// <summary>اجرای گزارش ذخیره‌شده با شناسه.</summary>
    Task<ReportRunResultDto> RunAsync(int id);

    Task<int> CreateAsync(ReportSaveDto dto);
    Task UpdateAsync(int id, ReportSaveDto dto);
    Task DeleteAsync(int id);
}

/// <summary>کلاینت «گزارش‌ساز» — همهٔ مسیرها زیر api/reports.</summary>
public class ReportClient : IReportClient
{
    private readonly IApiClient _api;
    public ReportClient(IApiClient api) => _api = api;

    public Task<ReportDatasetsResult> GetDatasetsAsync() =>
        _api.GetAsync<ReportDatasetsResult>("api/reports/datasets");

    public Task<List<ReportRoleDto>> GetRolesAsync() =>
        _api.GetAsync<List<ReportRoleDto>>("api/reports/roles");

    public Task<List<UserReportDto>> GetReportsAsync() =>
        _api.GetAsync<List<UserReportDto>>("api/reports");

    public Task<ReportRunResultDto> PreviewAsync(ReportQueryDto query) =>
        _api.PostAsync<ReportRunResultDto>("api/reports/preview", query);

    public Task<ReportRunResultDto> RunAsync(int id) =>
        _api.GetAsync<ReportRunResultDto>($"api/reports/{id}/data");

    public async Task<int> CreateAsync(ReportSaveDto dto)
    {
        var res = await _api.PostAsync<ReportIdResult>("api/reports", dto);
        return res?.Id ?? 0;
    }

    public async Task UpdateAsync(int id, ReportSaveDto dto) =>
        await _api.PutAsync<ReportIdResult>($"api/reports/{id}", dto);

    public Task DeleteAsync(int id) => _api.DeleteAsync($"api/reports/{id}");
}
