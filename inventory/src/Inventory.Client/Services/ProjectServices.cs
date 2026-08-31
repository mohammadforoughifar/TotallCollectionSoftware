using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

// =====================================================================
// سرویس‌های ماژول مدیریت پروژه‌ها (سمت کلاینت)
// =====================================================================

/// <summary>سرویس کارفرماها.</summary>
public interface IKarfarmaService
{
    Task<List<KarFarmaDto>> GetAllAsync(string? search = null);
    Task<KarFarmaDto> GetAsync(int id);
    Task<KarFarmaDto> CreateAsync(KarFarmaDto dto);
    Task<KarFarmaDto> UpdateAsync(int id, KarFarmaDto dto);
    Task DeleteAsync(int id);
}

/// <summary>سرویس انواع فاکتور.</summary>
public interface ITypeFactorService
{
    Task<List<TypeFactorDto>> GetAllAsync(string? search = null);
    Task<TypeFactorDto> CreateAsync(TypeFactorDto dto);
    Task<TypeFactorDto> UpdateAsync(int id, TypeFactorDto dto);
    Task DeleteAsync(int id);
}

/// <summary>سرویس ورود و خروج پروژه‌ها.</summary>
public interface IProjectService
{
    Task<ProjectLookups> GetLookupsAsync();
    Task<string> GetNextSerialAsync();
    Task<List<ProjectEntryExitDto>> GetAllAsync(string? search = null, int? karfarmaId = null,
        int? typeFactorId = null, int? userId = null, bool? returned = null);
    Task<ProjectEntryExitDto> GetAsync(int id);
    /// <summary>ایجاد پروژه — خروجی: شناسه + کد پروژه صادرشده در سرور</summary>
    Task<(int Id, string Code)> CreateAsync(ProjectEntryExitDto dto);
    Task<int> UpdateAsync(int id, ProjectEntryExitDto dto);
    Task DeleteAsync(int id);

    /// <summary>ثبت/ویرایش اطلاعات فاکتور (فرم مجزا)</summary>
    Task UpdateFactorAsync(int id, ProjectFactorDto dto);

    /// <summary>ثبت/ویرایش تاریخ‌های چرخهٔ پروژه (خروج، خروج موقت، نیاز مشتری، تحویل پروژه، تحویل پرونده)</summary>
    Task UpdateDatesAsync(int id, ProjectDatesDto dto);
}

/// <summary>سرویس گزارش‌های کار.</summary>
public interface IReportWorkService
{
    Task<List<ReportWorkDto>> GetAllAsync(int? projectId = null, int? userId = null,
        DateTime? from = null, DateTime? to = null);
    Task CreateAsync(ReportWorkDto dto);
    Task UpdateAsync(int id, ReportWorkDto dto);
    Task DeleteAsync(int id);
}

// =====================================================================
// پیاده‌سازی‌ها
// =====================================================================

public class KarfarmaService : IKarfarmaService
{
    private readonly IApiClient _api;
    public KarfarmaService(IApiClient api) => _api = api;

    public Task<List<KarFarmaDto>> GetAllAsync(string? search = null)
        => _api.GetAsync<List<KarFarmaDto>>($"api/karfarmas?search={Uri.EscapeDataString(search ?? "")}");

    public Task<KarFarmaDto> GetAsync(int id)
        => _api.GetAsync<KarFarmaDto>($"api/karfarmas/{id}");

    public Task<KarFarmaDto> CreateAsync(KarFarmaDto dto)
        => _api.PostAsync<KarFarmaDto>("api/karfarmas", dto);

    public Task<KarFarmaDto> UpdateAsync(int id, KarFarmaDto dto)
        => _api.PutAsync<KarFarmaDto>($"api/karfarmas/{id}", dto);

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/karfarmas/{id}");
}

public class TypeFactorService : ITypeFactorService
{
    private readonly IApiClient _api;
    public TypeFactorService(IApiClient api) => _api = api;

    public Task<List<TypeFactorDto>> GetAllAsync(string? search = null)
        => _api.GetAsync<List<TypeFactorDto>>($"api/typefactors?search={Uri.EscapeDataString(search ?? "")}");

    public Task<TypeFactorDto> CreateAsync(TypeFactorDto dto)
        => _api.PostAsync<TypeFactorDto>("api/typefactors", dto);

    public Task<TypeFactorDto> UpdateAsync(int id, TypeFactorDto dto)
        => _api.PutAsync<TypeFactorDto>($"api/typefactors/{id}", dto);

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/typefactors/{id}");
}

public class ProjectService : IProjectService
{
    private readonly IApiClient _api;
    public ProjectService(IApiClient api) => _api = api;

    public Task<ProjectLookups> GetLookupsAsync()
        => _api.GetAsync<ProjectLookups>("api/projects/lookups");

    public async Task<string> GetNextSerialAsync()
    {
        var res = await _api.GetAsync<NextSerialResponse>("api/projects/next-serial");
        return res?.Next ?? "";
    }

    private class NextSerialResponse { public string Next { get; set; } = ""; }

    public Task<List<ProjectEntryExitDto>> GetAllAsync(string? search = null, int? karfarmaId = null,
        int? typeFactorId = null, int? userId = null, bool? returned = null)
    {
        var url = $"api/projects?search={Uri.EscapeDataString(search ?? "")}";
        if (karfarmaId is > 0) url += $"&karfarmaId={karfarmaId}";
        if (typeFactorId is > 0) url += $"&typeFactorId={typeFactorId}";
        if (userId is > 0) url += $"&userId={userId}";
        if (returned is true) url += "&returned=true";
        return _api.GetAsync<List<ProjectEntryExitDto>>(url);
    }

    public Task<ProjectEntryExitDto> GetAsync(int id)
        => _api.GetAsync<ProjectEntryExitDto>($"api/projects/{id}");

    public async Task<(int Id, string Code)> CreateAsync(ProjectEntryExitDto dto)
    {
        var res = await _api.PostAsync<CreateResponse>("api/projects", dto);
        return (res.Id, res.CodeProject ?? res.Id.ToString());
    }

    private class CreateResponse { public int Id { get; set; } public string? CodeProject { get; set; } }

    public async Task<int> UpdateAsync(int id, ProjectEntryExitDto dto)
    {
        var res = await _api.PutAsync<IdResponse>($"api/projects/{id}", dto);
        return res.Id;
    }

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/projects/{id}");

    public Task UpdateFactorAsync(int id, ProjectFactorDto dto)
        => _api.PutAsync<object>($"api/projects/{id}/factor", dto);

    public Task UpdateDatesAsync(int id, ProjectDatesDto dto)
        => _api.PutAsync<object>($"api/projects/{id}/dates", dto);

    private class IdResponse { public int Id { get; set; } }
}

public class ReportWorkService : IReportWorkService
{
    private readonly IApiClient _api;
    public ReportWorkService(IApiClient api) => _api = api;

    public Task<List<ReportWorkDto>> GetAllAsync(int? projectId = null, int? userId = null,
        DateTime? from = null, DateTime? to = null)
    {
        var url = "api/reportworks?";
        if (projectId is > 0) url += $"projectId={projectId}&";
        if (userId is > 0) url += $"userId={userId}&";
        if (from.HasValue) url += $"from={from:yyyy-MM-dd}&";
        if (to.HasValue) url += $"to={to:yyyy-MM-dd}&";
        return _api.GetAsync<List<ReportWorkDto>>(url);
    }

    public Task CreateAsync(ReportWorkDto dto)
        => _api.PostAsync<object>("api/reportworks", dto);

    public Task UpdateAsync(int id, ReportWorkDto dto)
        => _api.PutAsync<object>($"api/reportworks/{id}", dto);

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/reportworks/{id}");
}

// =====================================================================
// کارتابل مدیریت پروژه (صف مدیر + صف کارشناسی)
// =====================================================================

/// <summary>آیتم کارتابل پروژه — متناظر با خروجی GET api/projectcartable/queue</summary>
public class ProjectCartableItem
{
    public int Id { get; set; }
    public string CodeProject { get; set; } = "";
    public int ReturnProjectId { get; set; }
    public string SerialNumber { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string? KarFarmaName { get; set; }
    public DateTime? EntryDate { get; set; }
    public DateTime? ExitDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? RegisterUser { get; set; }
    public int DaysWaiting { get; set; }
    public int AttachCount { get; set; }
}

/// <summary>سرویس کارتابل پروژه (صف مدیر و صف کارشناسی + اقدام‌های تایید/رد/اتمام).</summary>
public interface IProjectCartableService
{
    Task<ProjectCartableCountsDto?> GetCountsAsync();
    Task<List<ProjectCartableItem>> GetQueueAsync(string kind);
    Task ApproveAsync(int id, string? note);
    Task RejectAsync(int id, string note);
    Task ExpertDoneAsync(int id, string? note);

    /// <summary>ارسال مجدد پروژهٔ ردشده به کارتابل مدیر (بعد از اصلاح)</summary>
    Task ResubmitAsync(int id, string? note);
}

public class ProjectCartableService : IProjectCartableService
{
    private readonly IApiClient _api;
    public ProjectCartableService(IApiClient api) => _api = api;

    public Task<ProjectCartableCountsDto?> GetCountsAsync()
        => _api.GetAsync<ProjectCartableCountsDto?>("api/projectcartable/counts");

    public Task<List<ProjectCartableItem>> GetQueueAsync(string kind)
        => _api.GetAsync<List<ProjectCartableItem>>($"api/projectcartable/queue?kind={Uri.EscapeDataString(kind)}");

    public Task ApproveAsync(int id, string? note)
        => _api.PostAsync<object>($"api/projectcartable/{id}/approve", new ProjectFlowActionDto { Note = note });

    public Task RejectAsync(int id, string note)
        => _api.PostAsync<object>($"api/projectcartable/{id}/reject", new ProjectFlowActionDto { Note = note });

    public Task ExpertDoneAsync(int id, string? note)
        => _api.PostAsync<object>($"api/projectcartable/{id}/expert-done", new ProjectFlowActionDto { Note = note });

    public Task ResubmitAsync(int id, string? note)
        => _api.PostAsync<object>($"api/projectcartable/{id}/resubmit", new ProjectFlowActionDto { Note = note });
}
