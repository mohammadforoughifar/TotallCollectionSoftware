using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.HrTalent;

public interface IHrTalentService
{
    Task<List<HrOnboardingDto>> ListOnboardingAsync(bool? onlyOpen);
    Task<HrOnboardingDto> CreateOnboardingAsync(HrTalentCaseSaveDto dto);
    Task<HrOnboardingDto> SetOnboardingItemAsync(int itemId, int status, string? doneBy);
    Task<HrOnboardingDto> AddOnboardingItemAsync(int caseId, HrTalentItemSaveDto dto);
    Task DeleteOnboardingItemAsync(int itemId);
    Task<HrOnboardingDto> CompleteOnboardingAsync(int id, string? by);

    Task<List<HrExitCaseDto>> ListExitCasesAsync(bool? onlyOpen);
    Task<HrExitCaseDto> CreateExitCaseAsync(HrTalentCaseSaveDto dto);
    Task<HrExitCaseDto> SetExitItemAsync(int itemId, int status, string? doneBy);
    Task<HrExitCaseDto> AddExitItemAsync(int caseId, HrTalentItemSaveDto dto);
    Task DeleteExitItemAsync(int itemId);
    Task<HrExitCaseDto> CompleteExitCaseAsync(int id, string? by);

    Task<List<HrJobHistoryDto>> EmployeeHistoryAsync(int employeeId);
    Task<HrJobHistoryDto> SaveHistoryAsync(HrJobHistorySaveDto dto);
    Task DeleteHistoryAsync(int id);

    Task<List<HrTrialPeriodDto>> ListTrialsAsync(bool? onlyActive);
    Task<HrTrialPeriodDto> SaveTrialAsync(HrTrialSaveDto dto);
    Task<HrTrialPeriodDto> DecideTrialAsync(int id, HrTrialDecideDto dto);
    Task DeleteTrialAsync(int id);
    Task<int> AutoCreateTrialsAsync();

    Task<List<HrAppraisalDto>> ListAppraisalsAsync();
    Task<HrAppraisalDto> SaveAppraisalAsync(int? id, HrAppraisalSaveDto dto);
    Task DeleteAppraisalAsync(int id);
    Task<List<HrAppraisalKpiDto>> ListKpisAsync(int appraisalId);
    Task<HrAppraisalKpiDto> SaveKpiAsync(int appraisalId, int? id, HrAppraisalKpiSaveDto dto);
    Task DeleteKpiAsync(int id);
    Task SaveScoreAsync(HrAppraisalScoreSaveDto dto);
    Task<List<HrAppraisalScoreDto>> ListScoresAsync(int appraisalId, int? employeeId);
    Task<List<HrAppraisalResultDto>> AppraisalResultsAsync(int appraisalId);
    Task<HrAppraisalDto> SetAppraisalStatusAsync(int id, int status, string? by);
}

/// <summary>استعداد و ارزیابی (HrTalent): آنبوردینگ، ترک‌کار، سوابق، دوره آزمایشی، ارزیابی عملکرد.</summary>
public class HrTalentService : IHrTalentService
{
    private readonly AppDbContext _db;

    private static readonly (string Title, HrTalentOwner Owner)[] OnboardingTemplate =
    {
        ("معرفی به تیم و مدیر مستقیم", HrTalentOwner.Manager),
        ("تحویل تجهیزات (لپ‌تاپ/موبایل/کارت)", HrTalentOwner.It),
        ("ساخت اکانت‌ها و ایمیل سازمانی", HrTalentOwner.It),
        ("آموزش ایمنی و بهداشت کار", HrTalentOwner.Hr),
        ("امضای آیین‌نامه انضباطی و محرمانگی", HrTalentOwner.Self),
        ("آموزش فرآیند مرخصی و تردد", HrTalentOwner.Hr),
        ("تعیین منتور و برنامه هفته اول", HrTalentOwner.Manager),
    };

    private static readonly (string Title, HrTalentOwner Owner)[] ExitTemplate =
    {
        ("تحویل اموال، کلید و کارت", HrTalentOwner.Self),
        ("تسویه مالی و حقوق معوقه", HrTalentOwner.Hr),
        ("غیرفعال‌سازی اکانت‌ها", HrTalentOwner.It),
        ("مصاحبه خروج", HrTalentOwner.Hr),
        ("صدور نامه سابقه‌کار", HrTalentOwner.Hr),
        ("بایگانی پرونده پرسنل", HrTalentOwner.Hr),
    };

    public HrTalentService(AppDbContext db) => _db = db;

    private async Task<Dictionary<int, string>> EmpNamesAsync(IEnumerable<int> ids)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new();
        return await _db.HrEmployees.AsNoTracking().Where(e => list.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.FirstName + " " + e.LastName);
    }

    private static string NameOf(Dictionary<int, string> names, int id)
        => names.TryGetValue(id, out var n) ? n : "—";

    // ==================== آنبوردینگ ====================

    public async Task<List<HrOnboardingDto>> ListOnboardingAsync(bool? onlyOpen)
    {
        var q = _db.HrOnboardings.AsNoTracking().AsQueryable();
        if (onlyOpen == true) q = q.Where(x => x.Status == HrTalentCaseStatus.Open);
        var rows = await q.OrderByDescending(x => x.Id).Take(300).ToListAsync();
        var names = await EmpNamesAsync(rows.Select(x => x.EmployeeId));
        var caseIds = rows.Select(x => x.Id).ToList();
        var items = caseIds.Count == 0 ? new List<HrOnboardingItem>()
            : await _db.HrOnboardingItems.AsNoTracking()
                .Where(i => caseIds.Contains(i.OnboardingId)).OrderBy(i => i.SortOrder).ToListAsync();
        return rows.Select(x => MapOnboarding(x,
            items.Where(i => i.OnboardingId == x.Id).ToList(), NameOf(names, x.EmployeeId))).ToList();
    }

    private static HrOnboardingDto MapOnboarding(HrOnboarding x, List<HrOnboardingItem> items, string name)
    {
        var done = items.Count(i => i.Status != HrTalentItemStatus.Pending);
        return new HrOnboardingDto
        {
            Id = x.Id, EmployeeId = x.EmployeeId, EmployeeName = name,
            StartDate = x.StartDate, Status = (int)x.Status, Note = x.Note,
            Progress = items.Count == 0 ? 0 : done * 100 / items.Count,
            Items = items.Select(i => new HrTalentItemDto
            {
                Id = i.Id, Title = i.Title, Owner = (int)i.Owner, Status = (int)i.Status,
                DoneBy = i.DoneBy, DoneAt = i.DoneAt
            }).ToList()
        };
    }

    private async Task<HrOnboardingDto> GetOnboardingAsync(int id)
    {
        var x = await _db.HrOnboardings.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id)
            ?? throw new InvalidOperationException("پرونده آنبوردینگ یافت نشد.");
        var items = await _db.HrOnboardingItems.AsNoTracking()
            .Where(i => i.OnboardingId == id).OrderBy(i => i.SortOrder).ToListAsync();
        var names = await EmpNamesAsync(new[] { x.EmployeeId });
        return MapOnboarding(x, items, NameOf(names, x.EmployeeId));
    }

    public async Task<HrOnboardingDto> CreateOnboardingAsync(HrTalentCaseSaveDto dto)
    {
        if (dto.EmployeeId <= 0) throw new InvalidOperationException("پرسنل مشخص نیست.");
        var open = await _db.HrOnboardings.AnyAsync(x => x.EmployeeId == dto.EmployeeId && x.Status == HrTalentCaseStatus.Open);
        if (open) throw new InvalidOperationException("این پرسنل یک آنبوردینگ باز دارد.");
        var c = new HrOnboarding { EmployeeId = dto.EmployeeId, Note = dto.Note?.Trim() };
        _db.HrOnboardings.Add(c);
        await _db.SaveChangesAsync();
        var sort = 0;
        foreach (var (title, owner) in OnboardingTemplate)
            _db.HrOnboardingItems.Add(new HrOnboardingItem
                { OnboardingId = c.Id, Title = title, Owner = owner, SortOrder = sort++ });
        await _db.SaveChangesAsync();
        return await GetOnboardingAsync(c.Id);
    }

    public async Task<HrOnboardingDto> SetOnboardingItemAsync(int itemId, int status, string? doneBy)
    {
        var i = await _db.HrOnboardingItems.FirstOrDefaultAsync(x => x.Id == itemId)
            ?? throw new InvalidOperationException("آیتم یافت نشد.");
        i.Status = (HrTalentItemStatus)Math.Clamp(status, 0, 2);
        i.DoneBy = i.Status == HrTalentItemStatus.Pending ? null : doneBy?.Trim();
        i.DoneAt = i.Status == HrTalentItemStatus.Pending ? null : DateTime.Now;
        await _db.SaveChangesAsync();
        return await GetOnboardingAsync(i.OnboardingId);
    }

    public async Task<HrOnboardingDto> AddOnboardingItemAsync(int caseId, HrTalentItemSaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Title)) throw new InvalidOperationException("عنوان آیتم خالی است.");
        var max = await _db.HrOnboardingItems.Where(i => i.OnboardingId == caseId)
            .Select(i => (int?)i.SortOrder).MaxAsync() ?? -1;
        _db.HrOnboardingItems.Add(new HrOnboardingItem
        {
            OnboardingId = caseId, Title = dto.Title.Trim(),
            Owner = (HrTalentOwner)Math.Clamp(dto.Owner, 0, 3), SortOrder = max + 1
        });
        await _db.SaveChangesAsync();
        return await GetOnboardingAsync(caseId);
    }

    public async Task DeleteOnboardingItemAsync(int itemId)
    {
        var i = await _db.HrOnboardingItems.FirstOrDefaultAsync(x => x.Id == itemId)
            ?? throw new InvalidOperationException("آیتم یافت نشد.");
        _db.HrOnboardingItems.Remove(i);
        await _db.SaveChangesAsync();
    }

    public async Task<HrOnboardingDto> CompleteOnboardingAsync(int id, string? by)
    {
        var c = await _db.HrOnboardings.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("پرونده یافت نشد.");
        c.Status = HrTalentCaseStatus.Completed;
        c.CompletedBy = by?.Trim();
        c.CompletedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return await GetOnboardingAsync(id);
    }

    // ==================== ترک‌کار ====================

    public async Task<List<HrExitCaseDto>> ListExitCasesAsync(bool? onlyOpen)
    {
        var q = _db.HrExitCases.AsNoTracking().AsQueryable();
        if (onlyOpen == true) q = q.Where(x => x.Status == HrTalentCaseStatus.Open);
        var rows = await q.OrderByDescending(x => x.Id).Take(300).ToListAsync();
        var names = await EmpNamesAsync(rows.Select(x => x.EmployeeId));
        var caseIds = rows.Select(x => x.Id).ToList();
        var items = caseIds.Count == 0 ? new List<HrExitItem>()
            : await _db.HrExitItems.AsNoTracking()
                .Where(i => caseIds.Contains(i.ExitCaseId)).OrderBy(i => i.SortOrder).ToListAsync();
        return rows.Select(x =>
        {
            var its = items.Where(i => i.ExitCaseId == x.Id).ToList();
            var done = its.Count(i => i.Status != HrTalentItemStatus.Pending);
            return new HrExitCaseDto
            {
                Id = x.Id, EmployeeId = x.EmployeeId, EmployeeName = NameOf(names, x.EmployeeId),
                RequestDate = x.RequestDate, LastWorkDate = x.LastWorkDate,
                Type = (int)x.Type, Reason = x.Reason, Status = (int)x.Status,
                Progress = its.Count == 0 ? 0 : done * 100 / its.Count,
                Items = its.Select(i => new HrTalentItemDto
                {
                    Id = i.Id, Title = i.Title, Owner = (int)i.Owner, Status = (int)i.Status,
                    DoneBy = i.DoneBy, DoneAt = i.DoneAt
                }).ToList()
            };
        }).ToList();
    }

    private async Task<HrExitCaseDto> GetExitCaseAsync(int id)
    {
        var all = await ListExitCasesAsync(null);
        return all.FirstOrDefault(x => x.Id == id)
            ?? throw new InvalidOperationException("پرونده ترک‌کار یافت نشد.");
    }

    public async Task<HrExitCaseDto> CreateExitCaseAsync(HrTalentCaseSaveDto dto)
    {
        if (dto.EmployeeId <= 0) throw new InvalidOperationException("پرسنل مشخص نیست.");
        var open = await _db.HrExitCases.AnyAsync(x => x.EmployeeId == dto.EmployeeId && x.Status == HrTalentCaseStatus.Open);
        if (open) throw new InvalidOperationException("این پرسنل یک پرونده ترک‌کار باز دارد.");
        var c = new HrExitCase
        {
            EmployeeId = dto.EmployeeId, Type = (HrExitType)Math.Clamp(dto.Type, 0, 4),
            LastWorkDate = dto.LastWorkDate, Reason = dto.Note?.Trim()
        };
        _db.HrExitCases.Add(c);
        await _db.SaveChangesAsync();
        var sort = 0;
        foreach (var (title, owner) in ExitTemplate)
            _db.HrExitItems.Add(new HrExitItem
                { ExitCaseId = c.Id, Title = title, Owner = owner, SortOrder = sort++ });
        await _db.SaveChangesAsync();
        return await GetExitCaseAsync(c.Id);
    }

    public async Task<HrExitCaseDto> SetExitItemAsync(int itemId, int status, string? doneBy)
    {
        var i = await _db.HrExitItems.FirstOrDefaultAsync(x => x.Id == itemId)
            ?? throw new InvalidOperationException("آیتم یافت نشد.");
        i.Status = (HrTalentItemStatus)Math.Clamp(status, 0, 2);
        i.DoneBy = i.Status == HrTalentItemStatus.Pending ? null : doneBy?.Trim();
        i.DoneAt = i.Status == HrTalentItemStatus.Pending ? null : DateTime.Now;
        await _db.SaveChangesAsync();
        return await GetExitCaseAsync(i.ExitCaseId);
    }

    public async Task<HrExitCaseDto> AddExitItemAsync(int caseId, HrTalentItemSaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Title)) throw new InvalidOperationException("عنوان آیتم خالی است.");
        var max = await _db.HrExitItems.Where(i => i.ExitCaseId == caseId)
            .Select(i => (int?)i.SortOrder).MaxAsync() ?? -1;
        _db.HrExitItems.Add(new HrExitItem
        {
            ExitCaseId = caseId, Title = dto.Title.Trim(),
            Owner = (HrTalentOwner)Math.Clamp(dto.Owner, 0, 3), SortOrder = max + 1
        });
        await _db.SaveChangesAsync();
        return await GetExitCaseAsync(caseId);
    }

    public async Task DeleteExitItemAsync(int itemId)
    {
        var i = await _db.HrExitItems.FirstOrDefaultAsync(x => x.Id == itemId)
            ?? throw new InvalidOperationException("آیتم یافت نشد.");
        _db.HrExitItems.Remove(i);
        await _db.SaveChangesAsync();
    }

    public async Task<HrExitCaseDto> CompleteExitCaseAsync(int id, string? by)
    {
        var c = await _db.HrExitCases.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("پرونده یافت نشد.");
        c.Status = HrTalentCaseStatus.Completed;
        c.CompletedBy = by?.Trim();
        c.CompletedAt = DateTime.Now;
        var e = await _db.HrEmployees.FirstOrDefaultAsync(x => x.Id == c.EmployeeId);
        if (e != null)
        {
            e.IsActive = false;
            e.Status = c.Type == HrExitType.Retire ? HrEmployeeStatus.Retired : HrEmployeeStatus.Terminated;
        }
        await _db.SaveChangesAsync();
        return await GetExitCaseAsync(id);
    }

    // ==================== سوابق شغلی ====================

    public async Task<List<HrJobHistoryDto>> EmployeeHistoryAsync(int employeeId)
    {
        var list = new List<HrJobHistoryDto>();
        var manual = await _db.HrJobHistories.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId).OrderByDescending(x => x.FromDate).ToListAsync();
        foreach (var x in manual)
            list.Add(new HrJobHistoryDto
            {
                Id = x.Id, EmployeeId = x.EmployeeId, FromDate = x.FromDate, ToDate = x.ToDate,
                Title = x.PostTitle,
                Subtitle = string.Join(" • ", new[] { x.OrgUnitName, x.Note }
                    .Where(s => !string.IsNullOrWhiteSpace(s))),
                Source = (int)HrHistorySource.Manual
            });
        var decrees = await _db.HrDecrees.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId).OrderByDescending(x => x.EffectiveDate).Take(100).ToListAsync();
        foreach (var x in decrees)
            list.Add(new HrJobHistoryDto
            {
                Id = -x.Id, EmployeeId = x.EmployeeId, FromDate = x.EffectiveDate, ToDate = null,
                Title = $"حکم {x.DecreeNo}" + (x.NewPostTitle == null ? "" : $" — {x.NewPostTitle}"),
                Subtitle = x.Description, Source = (int)HrHistorySource.Decree
            });
        var contracts = await _db.HrContracts.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId).OrderByDescending(x => x.StartDate).Take(100).ToListAsync();
        foreach (var x in contracts)
            list.Add(new HrJobHistoryDto
            {
                Id = -100000 - x.Id, EmployeeId = x.EmployeeId,
                FromDate = x.StartDate, ToDate = x.EndDate,
                Title = $"قرارداد {x.ContractNo}" + (x.JobTitle == null ? "" : $" — {x.JobTitle}"),
                Subtitle = x.Description, Source = (int)HrHistorySource.Contract
            });
        return list.OrderByDescending(x => x.FromDate).ToList();
    }

    public async Task<HrJobHistoryDto> SaveHistoryAsync(HrJobHistorySaveDto dto)
    {
        if (dto.EmployeeId <= 0) throw new InvalidOperationException("پرسنل مشخص نیست.");
        if (string.IsNullOrWhiteSpace(dto.PostTitle)) throw new InvalidOperationException("عنوان سمت خالی است.");
        var x = new HrJobHistory
        {
            EmployeeId = dto.EmployeeId, FromDate = dto.FromDate.Date, ToDate = dto.ToDate?.Date,
            PostTitle = dto.PostTitle.Trim(), OrgUnitName = dto.OrgUnitName?.Trim(),
            EmploymentType = dto.EmploymentType, Note = dto.Note?.Trim()
        };
        _db.HrJobHistories.Add(x);
        await _db.SaveChangesAsync();
        return (await EmployeeHistoryAsync(x.EmployeeId)).First(h => h.Id == x.Id);
    }

    public async Task DeleteHistoryAsync(int id)
    {
        var x = await _db.HrJobHistories.FirstOrDefaultAsync(h => h.Id == id)
            ?? throw new InvalidOperationException("رکورد یافت نشد.");
        _db.HrJobHistories.Remove(x);
        await _db.SaveChangesAsync();
    }

    // ==================== دوره آزمایشی ====================

    public async Task<List<HrTrialPeriodDto>> ListTrialsAsync(bool? onlyActive)
    {
        var q = _db.HrTrialPeriods.AsNoTracking().AsQueryable();
        if (onlyActive == true) q = q.Where(x => x.Result == HrTrialResult.Active);
        var rows = await q.OrderByDescending(x => x.Id).Take(500).ToListAsync();
        var names = await EmpNamesAsync(rows.Select(x => x.EmployeeId));
        var today = DateTime.Today;
        return rows.Select(x => new HrTrialPeriodDto
        {
            Id = x.Id, EmployeeId = x.EmployeeId, EmployeeName = NameOf(names, x.EmployeeId),
            StartDate = x.StartDate, Months = x.Months, EndDate = x.EndDate,
            Result = (int)x.Result,
            DaysLeft = x.Result == HrTrialResult.Active ? (int)(x.EndDate.Date - today).TotalDays : null,
            ResultNote = x.ResultNote, DecidedBy = x.DecidedBy, DecidedAt = x.DecidedAt
        }).ToList();
    }

    public async Task<HrTrialPeriodDto> SaveTrialAsync(HrTrialSaveDto dto)
    {
        if (dto.EmployeeId <= 0) throw new InvalidOperationException("پرسنل مشخص نیست.");
        var months = dto.Months is 1 or 3 ? dto.Months : 3;
        var dup = await _db.HrTrialPeriods.AnyAsync(x => x.EmployeeId == dto.EmployeeId && x.Result == HrTrialResult.Active);
        if (dup) throw new InvalidOperationException("این پرسنل یک دوره آزمایشی فعال دارد.");
        var start = dto.StartDate.Date;
        _db.HrTrialPeriods.Add(new HrTrialPeriod
            { EmployeeId = dto.EmployeeId, StartDate = start, Months = months, EndDate = start.AddMonths(months).AddDays(-1) });
        await _db.SaveChangesAsync();
        return (await ListTrialsAsync(null)).OrderByDescending(x => x.Id).First(x => x.EmployeeId == dto.EmployeeId);
    }

    public async Task<HrTrialPeriodDto> DecideTrialAsync(int id, HrTrialDecideDto dto)
    {
        var x = await _db.HrTrialPeriods.FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new InvalidOperationException("دوره آزمایشی یافت نشد.");
        x.Result = dto.Pass ? HrTrialResult.Passed : HrTrialResult.Failed;
        x.ResultNote = dto.Note?.Trim();
        x.DecidedBy = dto.DecidedBy?.Trim();
        x.DecidedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return (await ListTrialsAsync(null)).First(t => t.Id == id);
    }

    public async Task DeleteTrialAsync(int id)
    {
        var x = await _db.HrTrialPeriods.FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new InvalidOperationException("دوره آزمایشی یافت نشد.");
        _db.HrTrialPeriods.Remove(x);
        await _db.SaveChangesAsync();
    }

    /// <summary>ایجاد خودکار دوره آزمایشی ۳ماهه برای استخدام‌های ۱۲۰ روز اخیرِ بدون دوره.</summary>
    public async Task<int> AutoCreateTrialsAsync()
    {
        var since = DateTime.Today.AddDays(-120);
        var hires = await _db.HrEmployees.AsNoTracking()
            .Where(e => e.IsActive && e.HireDate.Date >= since)
            .Select(e => new { e.Id, e.HireDate }).ToListAsync();
        if (hires.Count == 0) return 0;
        var has = await _db.HrTrialPeriods.AsNoTracking()
            .Where(t => hires.Select(h => h.Id).Contains(t.EmployeeId))
            .Select(t => t.EmployeeId).Distinct().ToListAsync();
        var n = 0;
        foreach (var h in hires.Where(h => !has.Contains(h.Id)))
        {
            var start = h.HireDate.Date;
            _db.HrTrialPeriods.Add(new HrTrialPeriod
                { EmployeeId = h.Id, StartDate = start, Months = 3, EndDate = start.AddMonths(3).AddDays(-1) });
            n++;
        }
        await _db.SaveChangesAsync();
        return n;
    }

    // ==================== ارزیابی عملکرد ====================

    public async Task<List<HrAppraisalDto>> ListAppraisalsAsync()
    {
        var rows = await _db.HrAppraisals.AsNoTracking().OrderByDescending(x => x.Year).ThenByDescending(x => x.Id).Take(100).ToListAsync();
        var ids = rows.Select(x => x.Id).ToList();
        var kpis = ids.Count == 0 ? new List<HrAppraisalKpi>()
            : await _db.HrAppraisalKpis.AsNoTracking().Where(k => ids.Contains(k.AppraisalId)).ToListAsync();
        var scores = ids.Count == 0 ? new List<HrAppraisalScore>()
            : await _db.HrAppraisalScores.AsNoTracking().Where(s => ids.Contains(s.AppraisalId)).ToListAsync();
        return rows.Select(x => new HrAppraisalDto
        {
            Id = x.Id, Title = x.Title, Year = x.Year, Period = (int)x.Period, Status = (int)x.Status,
            KpiCount = kpis.Count(k => k.AppraisalId == x.Id),
            ScoredCount = scores.Where(s => s.AppraisalId == x.Id).Select(s => s.EmployeeId).Distinct().Count(),
            WeightSum = kpis.Where(k => k.AppraisalId == x.Id).Sum(k => k.Weight)
        }).ToList();
    }

    private async Task<HrAppraisalDto> GetAppraisalAsync(int id)
        => (await ListAppraisalsAsync()).FirstOrDefault(x => x.Id == id)
            ?? throw new InvalidOperationException("دوره ارزیابی یافت نشد.");

    public async Task<HrAppraisalDto> SaveAppraisalAsync(int? id, HrAppraisalSaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Title)) throw new InvalidOperationException("عنوان دوره خالی است.");
        HrAppraisal x;
        if (id is > 0)
        {
            x = await _db.HrAppraisals.FirstOrDefaultAsync(a => a.Id == id.Value)
                ?? throw new InvalidOperationException("دوره ارزیابی یافت نشد.");
            if (x.Status == HrAppraisalStatus.Final) throw new InvalidOperationException("دوره نهایی‌شده قابل ویرایش نیست.");
        }
        else { x = new HrAppraisal(); _db.HrAppraisals.Add(x); }
        x.Title = dto.Title.Trim();
        x.Year = dto.Year is >= 1300 and <= 1500 ? dto.Year : new System.Globalization.PersianCalendar().GetYear(DateTime.Today);
        x.Period = (HrAppraisalPeriod)Math.Clamp(dto.Period, 0, 3);
        await _db.SaveChangesAsync();
        return await GetAppraisalAsync(x.Id);
    }

    public async Task DeleteAppraisalAsync(int id)
    {
        var x = await _db.HrAppraisals.FirstOrDefaultAsync(a => a.Id == id)
            ?? throw new InvalidOperationException("دوره ارزیابی یافت نشد.");
        if (x.Status == HrAppraisalStatus.Final) throw new InvalidOperationException("دوره نهایی‌شده قابل حذف نیست.");
        _db.HrAppraisalScores.RemoveRange(_db.HrAppraisalScores.Where(s => s.AppraisalId == id));
        _db.HrAppraisalKpis.RemoveRange(_db.HrAppraisalKpis.Where(k => k.AppraisalId == id));
        _db.HrAppraisals.Remove(x);
        await _db.SaveChangesAsync();
    }

    public async Task<List<HrAppraisalKpiDto>> ListKpisAsync(int appraisalId)
        => await _db.HrAppraisalKpis.AsNoTracking().Where(k => k.AppraisalId == appraisalId)
            .OrderBy(k => k.SortOrder).ThenBy(k => k.Id)
            .Select(k => new HrAppraisalKpiDto { Id = k.Id, Title = k.Title, Weight = k.Weight, MaxScore = k.MaxScore })
            .ToListAsync();

    public async Task<HrAppraisalKpiDto> SaveKpiAsync(int appraisalId, int? id, HrAppraisalKpiSaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Title)) throw new InvalidOperationException("عنوان شاخص خالی است.");
        HrAppraisalKpi k;
        if (id is > 0)
        {
            k = await _db.HrAppraisalKpis.FirstOrDefaultAsync(x => x.Id == id.Value && x.AppraisalId == appraisalId)
                ?? throw new InvalidOperationException("شاخص یافت نشد.");
        }
        else
        {
            var max = await _db.HrAppraisalKpis.Where(x => x.AppraisalId == appraisalId)
                .Select(x => (int?)x.SortOrder).MaxAsync() ?? -1;
            k = new HrAppraisalKpi { AppraisalId = appraisalId, SortOrder = max + 1 };
            _db.HrAppraisalKpis.Add(k);
        }
        k.Title = dto.Title.Trim();
        k.Weight = Math.Clamp(dto.Weight, 0, 100);
        k.MaxScore = dto.MaxScore <= 0 ? 100 : dto.MaxScore;
        await _db.SaveChangesAsync();
        return (await ListKpisAsync(appraisalId)).First(x => x.Id == k.Id);
    }

    public async Task DeleteKpiAsync(int id)
    {
        var k = await _db.HrAppraisalKpis.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("شاخص یافت نشد.");
        _db.HrAppraisalScores.RemoveRange(_db.HrAppraisalScores.Where(s => s.KpiId == id));
        _db.HrAppraisalKpis.Remove(k);
        await _db.SaveChangesAsync();
    }

    public async Task SaveScoreAsync(HrAppraisalScoreSaveDto dto)
    {
        if (dto.EmployeeId <= 0 || dto.KpiId <= 0) throw new InvalidOperationException("پرسنل/شاخص مشخص نیست.");
        // توجه: AppraisalId از روی شاخص خوانده می‌شود تا ناسازگاری رخ ندهد
        var kpi = await _db.HrAppraisalKpis.AsNoTracking().FirstOrDefaultAsync(k => k.Id == dto.KpiId)
            ?? throw new InvalidOperationException("شاخص یافت نشد.");
        // نمره مدیر و خودارزیابی: بین صفر تا سقف نمره شاخص
        if ((dto.ManagerScore ?? 0) < 0 || (dto.ManagerScore ?? 0) > kpi.MaxScore
            || (dto.SelfScore ?? 0) < 0 || (dto.SelfScore ?? 0) > kpi.MaxScore)
            throw new InvalidOperationException($"نمره شاخص «{kpi.Title}» باید بین ۰ تا {kpi.MaxScore} باشد.");
        var s = await _db.HrAppraisalScores.FirstOrDefaultAsync(x =>
            x.AppraisalId == kpi.AppraisalId && x.KpiId == dto.KpiId && x.EmployeeId == dto.EmployeeId);
        if (s == null)
        {
            s = new HrAppraisalScore
                { AppraisalId = kpi.AppraisalId, KpiId = dto.KpiId, EmployeeId = dto.EmployeeId };
            _db.HrAppraisalScores.Add(s);
        }
        s.ManagerScore = dto.ManagerScore;
        s.SelfScore = dto.SelfScore;
        s.Note = dto.Note?.Trim();
        s.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task<List<HrAppraisalScoreDto>> ListScoresAsync(int appraisalId, int? employeeId)
    {
        var q = _db.HrAppraisalScores.AsNoTracking().Where(s => s.AppraisalId == appraisalId);
        if (employeeId is > 0) q = q.Where(s => s.EmployeeId == employeeId.Value);
        var rows = await q.Take(5000).ToListAsync();
        var names = await EmpNamesAsync(rows.Select(x => x.EmployeeId));
        return rows.Select(s => new HrAppraisalScoreDto
        {
            EmployeeId = s.EmployeeId, EmployeeName = NameOf(names, s.EmployeeId),
            KpiId = s.KpiId, ManagerScore = s.ManagerScore, SelfScore = s.SelfScore, Note = s.Note
        }).ToList();
    }

    public async Task<List<HrAppraisalResultDto>> AppraisalResultsAsync(int appraisalId)
    {
        var kpis = await _db.HrAppraisalKpis.AsNoTracking()
            .Where(k => k.AppraisalId == appraisalId).ToListAsync();
        if (kpis.Count == 0) return new();
        var wSum = kpis.Sum(k => k.Weight);
        if (wSum <= 0) wSum = kpis.Count;
        var scores = await _db.HrAppraisalScores.AsNoTracking()
            .Where(s => s.AppraisalId == appraisalId).ToListAsync();
        var names = await EmpNamesAsync(scores.Select(s => s.EmployeeId));
        var list = new List<HrAppraisalResultDto>();
        foreach (var g in scores.GroupBy(s => s.EmployeeId))
        {
            var total = 0.0;
            var scored = 0;
            foreach (var k in kpis)
            {
                var s = g.FirstOrDefault(x => x.KpiId == k.Id);
                var v = s?.ManagerScore ?? s?.SelfScore;
                if (v == null) continue;
                var pct = k.MaxScore > 0 ? Math.Clamp(v.Value / k.MaxScore * 100.0, 0, 100) : 0;
                total += k.Weight / wSum * pct;
                scored++;
            }
            list.Add(new HrAppraisalResultDto
            {
                EmployeeId = g.Key, EmployeeName = NameOf(names, g.Key),
                Total = Math.Round(total, 1),
                Grade = total >= 90 ? "A" : total >= 75 ? "B" : total >= 60 ? "C" : "D",
                ScoredKpis = scored, KpiCount = kpis.Count
            });
        }
        return list.OrderByDescending(x => x.Total).ToList();
    }

    public async Task<HrAppraisalDto> SetAppraisalStatusAsync(int id, int status, string? by)
    {
        var x = await _db.HrAppraisals.FirstOrDefaultAsync(a => a.Id == id)
            ?? throw new InvalidOperationException("دوره ارزیابی یافت نشد.");
        x.Status = (HrAppraisalStatus)Math.Clamp(status, 0, 2);
        if (x.Status == HrAppraisalStatus.Final)
        {
            x.FinalizedBy = by?.Trim();
            x.FinalizedAt = DateTime.Now;
        }
        else { x.FinalizedBy = null; x.FinalizedAt = null; }
        await _db.SaveChangesAsync();
        return await GetAppraisalAsync(id);
    }
}
