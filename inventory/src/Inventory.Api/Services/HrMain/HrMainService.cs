using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.HrMain;

public interface IHrMainService
{
    // پروفایل شرکت
    Task<HrMainCompanyDto> GetCompanyAsync();
    Task<HrMainCompanyDto> SaveCompanyAsync(HrMainCompanySaveDto dto);
    Task<HrMainCompanyDto> SaveLogoAsync(Stream stream, string fileName);
    Task DeleteLogoAsync();

    // شعب و دفاتر
    Task<List<HrMainBranchDto>> ListBranchesAsync();
    Task<HrMainBranchDto> SaveBranchAsync(int? id, HrMainBranchSaveDto dto);
    Task DeleteBranchAsync(int id);

    // ساختار سازمانی
    Task<List<HrMainOrgNodeDto>> GetTreeAsync();
    Task<List<HrMainOrgNodeDto>> ListNodesAsync();
    Task<HrMainOrgNodeDto> SaveNodeAsync(int? id, HrMainOrgNodeSaveDto dto);
    Task DeleteNodeAsync(int id);

    // پست‌های سازمانی
    Task<List<HrMainPositionDto>> ListPositionsAsync(int? orgNodeId);
    Task<HrMainPositionDto> SavePositionAsync(int? id, HrMainPositionSaveDto dto);
    Task DeletePositionAsync(int id);

    // زبان و تقویم
    Task<HrMainLocaleDto> GetLocaleAsync();
    Task<HrMainLocaleDto> SaveLocaleAsync(HrMainLocaleDto dto);

    // قوانین پیش‌فرض
    Task<HrMainRulesDto> GetRulesAsync();
    Task<HrMainRulesDto> SaveRulesAsync(HrMainRulesDto dto);

    // تعطیلات رسمی/شرکتی (روی جدول موجود CompanyHoliday)
    Task<List<HrMainHolidayDto>> ListHolidaysAsync(int? jalaliYear);
    Task<HrMainHolidayDto> SaveHolidayAsync(HrMainHolidaySaveDto dto, string createdBy);
    Task DeleteHolidayAsync(int id);

    // نمای کلی
    Task<HrMainOverviewDto> OverviewAsync();
}

public class HrMainService : IHrMainService
{
    private readonly AppDbContext _db;
    private readonly FileStore _files;

    public HrMainService(AppDbContext db, FileStore files)
    {
        _db = db;
        _files = files;
    }

    // ==================== پروفایل شرکت ====================

    public async Task<HrMainCompanyDto> GetCompanyAsync()
        => MapCompany(await GetOrCreateCompanyAsync());

    public async Task<HrMainCompanyDto> SaveCompanyAsync(HrMainCompanySaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("نام شرکت الزامی است.");
        var c = await GetOrCreateCompanyAsync();
        c.Name = dto.Name.Trim();
        c.Address = dto.Address?.Trim();
        c.EconomicCode = dto.EconomicCode?.Trim();
        c.RegistrationNo = dto.RegistrationNo?.Trim();
        c.NationalId = dto.NationalId?.Trim();
        c.Phone = dto.Phone?.Trim();
        c.Email = dto.Email?.Trim();
        c.Website = dto.Website?.Trim();
        c.ManagerName = dto.ManagerName?.Trim();
        c.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return MapCompany(c);
    }

    public async Task<HrMainCompanyDto> SaveLogoAsync(Stream stream, string fileName)
    {
        var ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        var allowed = new[] { ".png", ".jpg", ".jpeg", ".webp", ".svg" };
        if (!allowed.Contains(ext))
            throw new InvalidOperationException("فرمت لوگو معتبر نیست (png، jpg، webp یا svg).");
        if (stream.CanSeek && stream.Length > 2 * 1024 * 1024)
            throw new InvalidOperationException("حجم لوگو نباید بیشتر از ۲ مگابایت باشد.");

        var c = await GetOrCreateCompanyAsync();
        var rel = await _files.SaveAsync("hr/logo", 1, stream, fileName);
        // حذف لوگوی قبلی (در صورت خطا، ذخیره جدید حفظ می‌شود)
        if (!string.IsNullOrWhiteSpace(c.LogoPath) && c.LogoPath != rel)
        {
            try { _files.Delete(c.LogoPath); } catch { /* نادیده */ }
        }
        c.LogoPath = rel;
        c.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return MapCompany(c);
    }

    public async Task DeleteLogoAsync()
    {
        var c = await GetOrCreateCompanyAsync();
        if (!string.IsNullOrWhiteSpace(c.LogoPath))
        {
            try { _files.Delete(c.LogoPath); } catch { /* نادیده */ }
            c.LogoPath = null;
            c.UpdatedAt = DateTime.Now;
            await _db.SaveChangesAsync();
        }
    }

    private async Task<HrMainCompany> GetOrCreateCompanyAsync()
    {
        var c = await _db.HrMainCompanies.FirstOrDefaultAsync();
        if (c is not null) return c;
        c = new HrMainCompany { Name = "" };
        _db.HrMainCompanies.Add(c);
        await _db.SaveChangesAsync();
        return c;
    }

    private static HrMainCompanyDto MapCompany(HrMainCompany c) => new()
    {
        Id = c.Id, Name = c.Name, LogoPath = c.LogoPath, Address = c.Address,
        EconomicCode = c.EconomicCode, RegistrationNo = c.RegistrationNo, NationalId = c.NationalId,
        Phone = c.Phone, Email = c.Email, Website = c.Website, ManagerName = c.ManagerName
    };

    // ==================== شعب و دفاتر ====================

    public async Task<List<HrMainBranchDto>> ListBranchesAsync()
    {
        var rows = await _db.HrMainBranches.AsNoTracking()
            .OrderBy(b => b.SortOrder).ThenBy(b => b.Name).ToListAsync();
        var counts = await _db.HrMainOrgNodes.AsNoTracking()
            .Where(n => n.BranchId != null)
            .GroupBy(n => n.BranchId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
        return rows.Select(b => new HrMainBranchDto
        {
            Id = b.Id, Code = b.Code, Name = b.Name, Type = (int)b.Type,
            City = b.City, Address = b.Address, Phone = b.Phone, ManagerName = b.ManagerName,
            IsActive = b.IsActive, SortOrder = b.SortOrder,
            NodeCount = counts.TryGetValue(b.Id, out var n) ? n : 0
        }).ToList();
    }

    public async Task<HrMainBranchDto> SaveBranchAsync(int? id, HrMainBranchSaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("نام شعبه/دفتر الزامی است.");
        if (string.IsNullOrWhiteSpace(dto.Code))
            throw new InvalidOperationException("کد شعبه/دفتر الزامی است.");
        if (dto.Type is < 0 or > 2)
            throw new InvalidOperationException("نوع شعبه/دفتر نامعتبر است.");
        if (await _db.HrMainBranches.AnyAsync(b => b.Id != (id ?? 0) && b.Code == dto.Code.Trim()))
            throw new InvalidOperationException("کد شعبه/دفتر تکراری است.");

        HrMainBranch b;
        if (id is > 0)
        {
            b = await _db.HrMainBranches.FirstOrDefaultAsync(x => x.Id == id.Value)
                ?? throw new InvalidOperationException("شعبه/دفتر یافت نشد.");
        }
        else
        {
            b = new HrMainBranch();
            _db.HrMainBranches.Add(b);
        }
        b.Code = dto.Code.Trim(); b.Name = dto.Name.Trim(); b.Type = (HrMainBranchType)dto.Type;
        b.City = dto.City?.Trim(); b.Address = dto.Address?.Trim(); b.Phone = dto.Phone?.Trim();
        b.ManagerName = dto.ManagerName?.Trim(); b.IsActive = dto.IsActive; b.SortOrder = dto.SortOrder;
        await _db.SaveChangesAsync();
        return (await ListBranchesAsync()).First(x => x.Id == b.Id);
    }

    public async Task DeleteBranchAsync(int id)
    {
        var b = await _db.HrMainBranches.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("شعبه/دفتر یافت نشد.");
        if (await _db.HrMainOrgNodes.AnyAsync(n => n.BranchId == id))
            throw new InvalidOperationException("این شعبه/دفتر در ساختار سازمانی استفاده شده و قابل حذف نیست.");
        _db.HrMainBranches.Remove(b);
        await _db.SaveChangesAsync();
    }

    // ==================== ساختار سازمانی ====================

    public async Task<List<HrMainOrgNodeDto>> GetTreeAsync()
    {
        var flat = await ListNodesAsync();
        var byId = flat.ToDictionary(n => n.Id);
        var roots = new List<HrMainOrgNodeDto>();
        foreach (var n in flat)
        {
            if (n.ParentId is > 0 && byId.TryGetValue(n.ParentId.Value, out var p)) p.Children.Add(n);
            else roots.Add(n);
        }
        return roots;
    }

    public async Task<List<HrMainOrgNodeDto>> ListNodesAsync()
    {
        var rows = await _db.HrMainOrgNodes.AsNoTracking()
            .OrderBy(n => n.Level).ThenBy(n => n.SortOrder).ThenBy(n => n.Name).ToListAsync();
        var names = rows.ToDictionary(n => n.Id, n => n.Name);
        var branches = await _db.HrMainBranches.AsNoTracking()
            .ToDictionaryAsync(b => b.Id, b => b.Name);
        var posCounts = await _db.HrMainPositions.AsNoTracking()
            .Where(p => p.OrgNodeId != null)
            .GroupBy(p => p.OrgNodeId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
        return rows.Select(n => new HrMainOrgNodeDto
        {
            Id = n.Id, ParentId = n.ParentId, Level = (int)n.Level,
            Code = n.Code, Name = n.Name,
            ParentName = n.ParentId is > 0 && names.TryGetValue(n.ParentId.Value, out var pn) ? pn : null,
            BranchId = n.BranchId,
            BranchName = n.BranchId is > 0 && branches.TryGetValue(n.BranchId.Value, out var bn) ? bn : null,
            ManagerTitle = n.ManagerTitle, Phone = n.Phone, Description = n.Description,
            SortOrder = n.SortOrder, IsActive = n.IsActive,
            PositionCount = posCounts.TryGetValue(n.Id, out var c) ? c : 0
        }).ToList();
    }

    public async Task<HrMainOrgNodeDto> SaveNodeAsync(int? id, HrMainOrgNodeSaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("نام گره سازمانی الزامی است.");
        if (string.IsNullOrWhiteSpace(dto.Code))
            throw new InvalidOperationException("کد گره سازمانی الزامی است.");
        if (dto.Level is < 0 or > 3)
            throw new InvalidOperationException("سطح سازمانی نامعتبر است.");
        if (await _db.HrMainOrgNodes.AnyAsync(n => n.Id != (id ?? 0) && n.Code == dto.Code.Trim()))
            throw new InvalidOperationException("کد گره سازمانی تکراری است.");
        if (dto.BranchId is > 0 && !await _db.HrMainBranches.AnyAsync(b => b.Id == dto.BranchId.Value))
            throw new InvalidOperationException("شعبه/دفتر نامعتبر است.");

        // قانون سلسله‌مراتب: ریشه فقط «شرکت»؛ زیرمجموعه همیشه عمیق‌تر از والد
        HrMainOrgNode? parent = null;
        if (dto.ParentId is > 0)
        {
            if (id is > 0 && dto.ParentId.Value == id.Value)
                throw new InvalidOperationException("والد نمی‌تواند خود گره باشد.");
            parent = await _db.HrMainOrgNodes.FirstOrDefaultAsync(n => n.Id == dto.ParentId.Value)
                ?? throw new InvalidOperationException("گره والد یافت نشد.");
            if (dto.Level <= (int)parent.Level)
                throw new InvalidOperationException($"سطح «{LevelName(dto.Level)}» باید زیرمجموعه سطح «{LevelName((int)parent.Level)}» باشد (شرکت ← واحد ← دپارتمان ← تیم).");
            // جلوگیری از حلقه: والد نباید از نوادگان خود گره باشد
            if (id is > 0 && await IsDescendantAsync(id.Value, dto.ParentId.Value))
                throw new InvalidOperationException("والد انتخاب‌شده از زیرمجموعه‌های همین گره است (حلقه در درخت).");
        }
        else if (dto.Level != (int)HrMainOrgLevel.Company)
        {
            throw new InvalidOperationException("گره بدون والد (ریشه) باید در سطح «شرکت» باشد.");
        }

        HrMainOrgNode n;
        if (id is > 0)
        {
            n = await _db.HrMainOrgNodes.FirstOrDefaultAsync(x => x.Id == id.Value)
                ?? throw new InvalidOperationException("گره سازمانی یافت نشد.");
        }
        else
        {
            n = new HrMainOrgNode();
            _db.HrMainOrgNodes.Add(n);
        }
        n.ParentId = dto.ParentId; n.Level = (HrMainOrgLevel)dto.Level;
        n.Code = dto.Code.Trim(); n.Name = dto.Name.Trim();
        n.BranchId = dto.BranchId; n.ManagerTitle = dto.ManagerTitle?.Trim();
        n.Phone = dto.Phone?.Trim(); n.Description = dto.Description?.Trim();
        n.SortOrder = dto.SortOrder; n.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();
        return (await ListNodesAsync()).First(x => x.Id == n.Id);
    }

    public async Task DeleteNodeAsync(int id)
    {
        var n = await _db.HrMainOrgNodes.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("گره سازمانی یافت نشد.");
        if (await _db.HrMainOrgNodes.AnyAsync(x => x.ParentId == id))
            throw new InvalidOperationException("این گره زیرمجموعه دارد و قابل حذف نیست؛ ابتدا زیرمجموعه‌ها را حذف یا جابه‌جا کنید.");
        if (await _db.HrMainPositions.AnyAsync(p => p.OrgNodeId == id))
            throw new InvalidOperationException("روی این گره پست سازمانی تعریف شده و قابل حذف نیست.");
        if (await _db.HrEmployees.AnyAsync(e => e.HrMainNodeId == id))
            throw new InvalidOperationException("پرسنلی به این گره منتسب است و قابل حذف نیست.");
        _db.HrMainOrgNodes.Remove(n);
        await _db.SaveChangesAsync();
    }

    /// <summary>آیا candidateId از نوادگان nodeId است؟ (پیمایش والدها به سمت بالا)</summary>
    private async Task<bool> IsDescendantAsync(int nodeId, int candidateId)
    {
        var cur = await _db.HrMainOrgNodes.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == candidateId);
        var guard = 0;
        while (cur?.ParentId is > 0 && guard++ < 100)
        {
            if (cur.ParentId.Value == nodeId) return true;
            cur = await _db.HrMainOrgNodes.AsNoTracking()
                .FirstOrDefaultAsync(n => n.Id == cur.ParentId.Value);
        }
        return false;
    }

    private static string LevelName(int l) => l switch
    {
        0 => "شرکت", 1 => "واحد", 2 => "دپارتمان", 3 => "تیم", _ => "نامشخص"
    };

    // ==================== پست‌های سازمانی ====================

    public async Task<List<HrMainPositionDto>> ListPositionsAsync(int? orgNodeId)
    {
        var q = _db.HrMainPositions.AsNoTracking().AsQueryable();
        if (orgNodeId is > 0) q = q.Where(p => p.OrgNodeId == orgNodeId.Value);
        var rows = await q.OrderBy(p => p.Title).ToListAsync();
        var nodeNames = await _db.HrMainOrgNodes.AsNoTracking()
            .ToDictionaryAsync(n => n.Id, n => n.Name);
        return rows.Select(p => new HrMainPositionDto
        {
            Id = p.Id, Code = p.Code, Title = p.Title, OrgNodeId = p.OrgNodeId,
            OrgNodeName = p.OrgNodeId is > 0 && nodeNames.TryGetValue(p.OrgNodeId.Value, out var nm) ? nm : null,
            Grade = p.Grade, JobDescription = p.JobDescription, Requirements = p.Requirements,
            HeadCount = p.HeadCount, IsActive = p.IsActive
        }).ToList();
    }

    public async Task<HrMainPositionDto> SavePositionAsync(int? id, HrMainPositionSaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new InvalidOperationException("عنوان پست سازمانی الزامی است.");
        if (dto.OrgNodeId is > 0 && !await _db.HrMainOrgNodes.AnyAsync(n => n.Id == dto.OrgNodeId.Value))
            throw new InvalidOperationException("گره سازمانی نامعتبر است.");

        HrMainPosition p;
        if (id is > 0)
        {
            p = await _db.HrMainPositions.FirstOrDefaultAsync(x => x.Id == id.Value)
                ?? throw new InvalidOperationException("پست سازمانی یافت نشد.");
        }
        else
        {
            p = new HrMainPosition();
            _db.HrMainPositions.Add(p);
        }
        p.Code = string.IsNullOrWhiteSpace(dto.Code) ? null : dto.Code.Trim();
        p.Title = dto.Title.Trim(); p.OrgNodeId = dto.OrgNodeId;
        p.Grade = dto.Grade?.Trim(); p.JobDescription = dto.JobDescription?.Trim();
        p.Requirements = dto.Requirements?.Trim();
        p.HeadCount = Math.Max(1, dto.HeadCount); p.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();
        return (await ListPositionsAsync(null)).First(x => x.Id == p.Id);
    }

    public async Task DeletePositionAsync(int id)
    {
        var p = await _db.HrMainPositions.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("پست سازمانی یافت نشد.");
        if (await _db.HrEmployees.AnyAsync(e => e.HrMainPositionId == id))
            throw new InvalidOperationException("پرسنلی به این پست منتسب است و قابل حذف نیست.");
        _db.HrMainPositions.Remove(p);
        await _db.SaveChangesAsync();
    }

    // ==================== زبان و تقویم ====================

    public async Task<HrMainLocaleDto> GetLocaleAsync()
    {
        var s = await _db.HrMainLocales.FirstOrDefaultAsync();
        return s is null ? new HrMainLocaleDto() : new HrMainLocaleDto { Language = s.Language, Calendar = s.Calendar };
    }

    public async Task<HrMainLocaleDto> SaveLocaleAsync(HrMainLocaleDto dto)
    {
        var lang = (dto.Language ?? "").Trim().ToLowerInvariant();
        var cal = (dto.Calendar ?? "").Trim().ToLowerInvariant();
        if (lang is not ("fa" or "en"))
            throw new InvalidOperationException("زبان نامعتبر است (فارسی یا انگلیسی).");
        if (cal is not ("jalali" or "gregorian"))
            throw new InvalidOperationException("تقویم نامعتبر است (شمسی یا میلادی).");
        var s = await _db.HrMainLocales.FirstOrDefaultAsync();
        if (s is null) { s = new HrMainLocale(); _db.HrMainLocales.Add(s); }
        s.Language = lang; s.Calendar = cal; s.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return new HrMainLocaleDto { Language = s.Language, Calendar = s.Calendar };
    }

    // ==================== قوانین پیش‌فرض ====================

    public async Task<HrMainRulesDto> GetRulesAsync()
    {
        var r = await _db.HrMainRules.FirstOrDefaultAsync();
        return r is null ? new HrMainRulesDto() : MapRules(r);
    }

    public async Task<HrMainRulesDto> SaveRulesAsync(HrMainRulesDto dto)
    {
        if (dto.AnnualLeaveDays is < 0 or > 60)
            throw new InvalidOperationException("مرخصی استحقاقی سالانه باید بین ۰ تا ۶۰ روز باشد.");
        if (dto.MaxConsecutiveLeaveDays is < 1 or > 60)
            throw new InvalidOperationException("حداکثر مرخصی پیوسته باید بین ۱ تا ۶۰ روز باشد.");
        if (dto.MaxCarryOverDays is < 0 or > 60)
            throw new InvalidOperationException("سقف انتقال مرخصی باید بین ۰ تا ۶۰ روز باشد.");
        if (dto.LateGraceMinutes is < 0 or > 180)
            throw new InvalidOperationException("ارفاق تأخیر باید بین ۰ تا ۱۸۰ دقیقه باشد.");
        if (dto.MonthlyAllowedLateMinutes is < 0 or > 3000)
            throw new InvalidOperationException("سقف تأخیر ماهانه نامعتبر است.");
        if (dto.MaxLateWithoutLeaveMinutes is < 0 or > 1440)
            throw new InvalidOperationException("حداکثر تأخیر بدون برگه نامعتبر است.");
        if (dto.LateDeductionFactor is < 0 or > 10)
            throw new InvalidOperationException("ضریب کسرکار تأخیر باید بین ۰ تا ۱۰ باشد.");
        if (dto.AbsenceDailyDeductionFactor is < 0 or > 10)
            throw new InvalidOperationException("ضریب کسر غیبت باید بین ۰ تا ۱۰ باشد.");
        if (dto.UnexcusedAbsenceWarningAfter is < 1 or > 30)
            throw new InvalidOperationException("آستانه اخطار غیبت باید بین ۱ تا ۳۰ باشد.");
        if (dto.OvertimeFactor is < 1 or > 5)
            throw new InvalidOperationException("ضریب اضافه‌کاری باید بین ۱ تا ۵ باشد (قانون کار: ۱.۴).");
        if (dto.MaxMonthlyOvertimeHours is < 0 or > 300)
            throw new InvalidOperationException("سقف اضافه‌کاری ماهانه نامعتبر است.");
        if (dto.ContractAlertDays is < 1 or > 365)
            throw new InvalidOperationException("آستانه هشدار انقضای قرارداد باید بین ۱ تا ۳۶۵ روز باشد.");

        var r = await _db.HrMainRules.FirstOrDefaultAsync();
        if (r is null) { r = new HrMainRules(); _db.HrMainRules.Add(r); }
        r.AnnualLeaveDays = dto.AnnualLeaveDays;
        r.MaxConsecutiveLeaveDays = dto.MaxConsecutiveLeaveDays;
        r.MaxCarryOverDays = dto.MaxCarryOverDays;
        r.UnpaidLeaveAllowed = dto.UnpaidLeaveAllowed;
        r.LateGraceMinutes = dto.LateGraceMinutes;
        r.MonthlyAllowedLateMinutes = dto.MonthlyAllowedLateMinutes;
        r.MaxLateWithoutLeaveMinutes = dto.MaxLateWithoutLeaveMinutes;
        r.LateDeductionFactor = dto.LateDeductionFactor;
        r.AbsenceDailyDeductionFactor = dto.AbsenceDailyDeductionFactor;
        r.UnexcusedAbsenceWarningAfter = dto.UnexcusedAbsenceWarningAfter;
        r.OvertimeFactor = dto.OvertimeFactor;
        r.MaxMonthlyOvertimeHours = dto.MaxMonthlyOvertimeHours;
        r.OvertimeNeedsApproval = dto.OvertimeNeedsApproval;
        r.ContractAlertDays = dto.ContractAlertDays;
        r.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return MapRules(r);
    }

    private static HrMainRulesDto MapRules(HrMainRules r) => new()
    {
        AnnualLeaveDays = r.AnnualLeaveDays, MaxConsecutiveLeaveDays = r.MaxConsecutiveLeaveDays,
        MaxCarryOverDays = r.MaxCarryOverDays, UnpaidLeaveAllowed = r.UnpaidLeaveAllowed,
        LateGraceMinutes = r.LateGraceMinutes, MonthlyAllowedLateMinutes = r.MonthlyAllowedLateMinutes,
        MaxLateWithoutLeaveMinutes = r.MaxLateWithoutLeaveMinutes, LateDeductionFactor = r.LateDeductionFactor,
        AbsenceDailyDeductionFactor = r.AbsenceDailyDeductionFactor,
        UnexcusedAbsenceWarningAfter = r.UnexcusedAbsenceWarningAfter,
        OvertimeFactor = r.OvertimeFactor, MaxMonthlyOvertimeHours = r.MaxMonthlyOvertimeHours,
        OvertimeNeedsApproval = r.OvertimeNeedsApproval,
        ContractAlertDays = r.ContractAlertDays
    };

    // ==================== تعطیلات (جدول موجود CompanyHoliday) ====================

    public async Task<List<HrMainHolidayDto>> ListHolidaysAsync(int? jalaliYear)
    {
        var jy = jalaliYear is >= 1300 and <= 1500
            ? jalaliYear.Value
            : PersianDate.FromGregorian(DateTime.Today).Year;
        var start = PersianDate.ToGregorian(jy, 1, 1);
        var end = PersianDate.ToGregorian(jy, 12, PersianDate.DaysInMonth(jy, 12)).Date.AddDays(1);
        if (start == DateTime.MinValue) start = new DateTime(DateTime.Today.Year, 1, 1);
        if (end == DateTime.MinValue) end = start.AddYears(1);
        return await _db.CompanyHolidays.AsNoTracking()
            .Where(h => h.HolidayDate >= start && h.HolidayDate < end)
            .OrderBy(h => h.HolidayDate)
            .Select(h => new HrMainHolidayDto
            {
                Id = h.Id, Date = h.HolidayDate, Name = h.Name,
                IsOfficial = h.IsOfficial, CreatedByName = h.CreatedByName
            })
            .ToListAsync();
    }

    public async Task<HrMainHolidayDto> SaveHolidayAsync(HrMainHolidaySaveDto dto, string createdBy)
    {
        var date = dto.Date == default ? DateTime.Today : dto.Date.Date;
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("عنوان تعطیلی الزامی است.");
        if (await _db.CompanyHolidays.AnyAsync(h => h.HolidayDate == date))
            throw new InvalidOperationException("برای این تاریخ قبلاً تعطیلی ثبت شده است.");
        var h = new CompanyHoliday
        {
            HolidayDate = date, Name = dto.Name.Trim(),
            IsOfficial = dto.IsOfficial, CreatedByName = createdBy
        };
        _db.CompanyHolidays.Add(h);
        await _db.SaveChangesAsync();
        return new HrMainHolidayDto
        {
            Id = h.Id, Date = h.HolidayDate, Name = h.Name,
            IsOfficial = h.IsOfficial, CreatedByName = h.CreatedByName
        };
    }

    public async Task DeleteHolidayAsync(int id)
    {
        var h = await _db.CompanyHolidays.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("تعطیلی یافت نشد.");
        _db.CompanyHolidays.Remove(h);
        await _db.SaveChangesAsync();
    }

    // ==================== نمای کلی ====================

    public async Task<HrMainOverviewDto> OverviewAsync()
    {
        var jy = PersianDate.FromGregorian(DateTime.Today).Year;
        var holidays = await ListHolidaysAsync(jy);
        var c = await _db.HrMainCompanies.AsNoTracking().FirstOrDefaultAsync();
        return new HrMainOverviewDto
        {
            CompanyName = string.IsNullOrWhiteSpace(c?.Name) ? null : c!.Name,
            LogoPath = c?.LogoPath,
            ActiveBranches = await _db.HrMainBranches.CountAsync(b => b.IsActive),
            ActiveNodes = await _db.HrMainOrgNodes.CountAsync(n => n.IsActive),
            ActivePositions = await _db.HrMainPositions.CountAsync(p => p.IsActive),
            ActiveEmployees = await _db.HrEmployees.CountAsync(e => e.IsActive),
            HolidaysThisYear = holidays.Count,
            CurrentJalaliYear = jy
        };
    }
}
