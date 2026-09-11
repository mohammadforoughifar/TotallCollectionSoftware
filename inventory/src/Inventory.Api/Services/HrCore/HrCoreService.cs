using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.HrCore;

/// <summary>نام‌های فارسی انواع ماژول کارگزینی (تک‌منبع سمت سرور)</summary>
public static class HrCoreTexts
{
    public static string EmploymentType(int t) => t switch
    {
        0 => "رسمی", 1 => "قراردادی", 2 => "پیمانی", 3 => "ساعتی", 4 => "مشاوره‌ای", _ => "نامشخص"
    };
    public static string EmployeeStatus(int s) => s switch
    {
        0 => "فعال", 1 => "مرخصی بلندمدت", 2 => "معلق", 3 => "قطع همکاری", 4 => "بازنشسته", _ => "نامشخص"
    };
    public static string DecreeType(int t) => t switch
    {
        0 => "استخدام", 1 => "ارتقا", 2 => "انتقال", 3 => "تغییر حقوق",
        4 => "تشویق", 5 => "تنبیه", 6 => "قطع همکاری", 7 => "بازنشستگی", _ => "نامشخص"
    };
    public static string OrgUnitType(int t) => t switch
    {
        0 => "شرکت", 1 => "شعبه", 2 => "دپارتمان", 3 => "مرکز هزینه", _ => "واحد"
    };
}

public interface IHrCoreService
{
    // پرسنل
    Task<(List<HrEmployeeDto> Items, int Total)> SearchEmployeesAsync(string? q, int? orgUnitId, int? status, int skip, int take);
    Task<HrEmployeeDto?> GetEmployeeAsync(int id);
    Task<HrEmployeeDto> CreateEmployeeAsync(HrEmployeeSaveDto dto);
    Task<HrEmployeeDto> UpdateEmployeeAsync(int id, HrEmployeeSaveDto dto);
    Task SetEmployeeActiveAsync(int id, bool active);
    Task<string> NextEmployeeCodeAsync();

    // ساختار سازمانی
    Task<List<HrOrgUnitDto>> GetTreeAsync();
    Task<List<HrOrgUnitDto>> ListUnitsAsync();
    Task<HrOrgUnitDto> SaveUnitAsync(int? id, HrOrgUnitSaveDto dto);
    Task DeleteUnitAsync(int id);

    // قراردادها
    Task<List<HrContractDto>> EmployeeContractsAsync(int employeeId);
    Task<List<HrContractDto>> ListContractsAsync(bool? onlyActive);
    Task<List<HrContractDto>> ExpiringContractsAsync(int days);
    Task<HrContractDto> SaveContractAsync(int? id, HrContractSaveDto dto);
    Task DeleteContractAsync(int id);

    // احکام
    Task<List<HrDecreeDto>> EmployeeDecreesAsync(int employeeId);
    Task<List<HrDecreeDto>> ListDecreesAsync(int? employeeId, bool? onlyPending);
    Task<HrDecreeDto> SaveDecreeAsync(int? id, HrDecreeSaveDto dto, int byUserId, string byName);
    Task<HrDecreeDto> ApplyDecreeAsync(int id);
    Task DeleteDecreeAsync(int id);

    // داشبورد
    Task<HrDashboardDto> DashboardAsync(int expiringDays = 30);
}

public class HrCoreService : IHrCoreService
{
    private readonly AppDbContext _db;
    public HrCoreService(AppDbContext db) => _db = db;

    // ==================== پرسنل ====================

    public async Task<(List<HrEmployeeDto>, int)> SearchEmployeesAsync(string? q, int? orgUnitId, int? status, int skip, int take)
    {
        take = Math.Clamp(take, 1, 200);
        var query = _db.HrEmployees.AsNoTracking().AsQueryable();
        if (orgUnitId is > 0) query = query.Where(e => e.OrgUnitId == orgUnitId.Value);
        if (status is >= 0) query = query.Where(e => (int)e.Status == status.Value);
        if (!string.IsNullOrWhiteSpace(q))
        {
            q = q.Trim();
            query = query.Where(e => e.FirstName.Contains(q) || e.LastName.Contains(q)
                || e.Code.Contains(q) || e.NationalCode.Contains(q) || (e.PostTitle ?? "").Contains(q));
        }
        var total = await query.CountAsync();
        var rows = await query.OrderBy(e => e.Code).Skip(skip).Take(take).ToListAsync();
        var items = new List<HrEmployeeDto>();
        foreach (var e in rows) items.Add(await MapEmployeeAsync(e));
        return (items, total);
    }

    public async Task<HrEmployeeDto?> GetEmployeeAsync(int id)
    {
        var e = await _db.HrEmployees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return e is null ? null : await MapEmployeeAsync(e);
    }

    public async Task<HrEmployeeDto> CreateEmployeeAsync(HrEmployeeSaveDto dto)
    {
        ValidateNationalCode(dto.NationalCode);
        if (string.IsNullOrWhiteSpace(dto.FirstName) || string.IsNullOrWhiteSpace(dto.LastName))
            throw new InvalidOperationException("نام و نام خانوادگی الزامی است.");
        if (await _db.HrEmployees.AnyAsync(e => e.NationalCode == dto.NationalCode))
            throw new InvalidOperationException("این کد ملی قبلاً ثبت شده است.");

        var code = string.IsNullOrWhiteSpace(dto.Code) ? await NextEmployeeCodeAsync() : dto.Code.Trim();
        if (await _db.HrEmployees.AnyAsync(e => e.Code == code))
            throw new InvalidOperationException($"کد پرسنلی {code} تکراری است.");

        await ValidateRefsAsync(dto.OrgUnitId, dto.ManagerId, null, dto.SystemUserId);

        var e = new HrEmployee
        {
            Code = code, FirstName = dto.FirstName.Trim(), LastName = dto.LastName.Trim(),
            NationalCode = dto.NationalCode.Trim(), BirthDate = dto.BirthDate,
            Gender = dto.Gender, MaritalStatus = dto.MaritalStatus,
            Mobile = dto.Mobile?.Trim(), Email = dto.Email?.Trim(), Address = dto.Address?.Trim(),
            HireDate = dto.HireDate == default ? DateTime.Today : dto.HireDate,
            OrgUnitId = dto.OrgUnitId, PostTitle = dto.PostTitle?.Trim(), ManagerId = dto.ManagerId,
            EmploymentType = (HrEmploymentType)dto.EmploymentType, Status = (HrEmployeeStatus)dto.Status,
            SystemUserId = dto.SystemUserId, BaseSalary = dto.BaseSalary, IsActive = dto.IsActive
        };
        _db.HrEmployees.Add(e);
        await _db.SaveChangesAsync();
        return (await GetEmployeeAsync(e.Id))!;
    }

    public async Task<HrEmployeeDto> UpdateEmployeeAsync(int id, HrEmployeeSaveDto dto)
    {
        var e = await _db.HrEmployees.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("پرسنل یافت نشد.");
        ValidateNationalCode(dto.NationalCode);
        if (await _db.HrEmployees.AnyAsync(x => x.Id != id && x.NationalCode == dto.NationalCode))
            throw new InvalidOperationException("این کد ملی قبلاً ثبت شده است.");
        var code = string.IsNullOrWhiteSpace(dto.Code) ? e.Code : dto.Code.Trim();
        if (await _db.HrEmployees.AnyAsync(x => x.Id != id && x.Code == code))
            throw new InvalidOperationException($"کد پرسنلی {code} تکراری است.");
        await ValidateRefsAsync(dto.OrgUnitId, dto.ManagerId, id, dto.SystemUserId);

        e.Code = code; e.FirstName = dto.FirstName.Trim(); e.LastName = dto.LastName.Trim();
        e.NationalCode = dto.NationalCode.Trim(); e.BirthDate = dto.BirthDate;
        e.Gender = dto.Gender; e.MaritalStatus = dto.MaritalStatus;
        e.Mobile = dto.Mobile?.Trim(); e.Email = dto.Email?.Trim(); e.Address = dto.Address?.Trim();
        e.HireDate = dto.HireDate; e.OrgUnitId = dto.OrgUnitId; e.PostTitle = dto.PostTitle?.Trim();
        e.ManagerId = dto.ManagerId; e.EmploymentType = (HrEmploymentType)dto.EmploymentType;
        e.Status = (HrEmployeeStatus)dto.Status; e.SystemUserId = dto.SystemUserId;
        e.BaseSalary = dto.BaseSalary; e.IsActive = dto.IsActive; e.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return (await GetEmployeeAsync(id))!;
    }

    public async Task SetEmployeeActiveAsync(int id, bool active)
    {
        var e = await _db.HrEmployees.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("پرسنل یافت نشد.");
        e.IsActive = active;
        e.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task<string> NextEmployeeCodeAsync()
    {
        var codes = await _db.HrEmployees.Select(e => e.Code).ToListAsync();
        var max = 1000;
        foreach (var c in codes)
            if (int.TryParse(c, out var n) && n > max) max = n;
        return (max + 1).ToString();
    }

    private async Task ValidateRefsAsync(int? orgUnitId, int? managerId, int? selfId, int? systemUserId)
    {
        if (orgUnitId is > 0 && !await _db.HrOrgUnits.AnyAsync(u => u.Id == orgUnitId.Value))
            throw new InvalidOperationException("واحد سازمانی نامعتبر است.");
        if (managerId is > 0)
        {
            if (selfId is > 0 && managerId.Value == selfId.Value)
                throw new InvalidOperationException("مدیر مستقیم نمی‌تواند خود شخص باشد.");
            if (!await _db.HrEmployees.AnyAsync(e => e.Id == managerId.Value))
                throw new InvalidOperationException("مدیر مستقیم نامعتبر است.");
        }
        if (systemUserId is > 0 && !await _db.SystemUsers.AnyAsync(u => u.Id == systemUserId.Value))
            throw new InvalidOperationException("کاربر سیستمی نامعتبر است.");
    }

    private static void ValidateNationalCode(string nc)
    {
        nc = (nc ?? "").Trim();
        if (nc.Length != 10 || !nc.All(char.IsDigit))
            throw new InvalidOperationException("کد ملی باید ۱۰ رقم باشد.");
    }

    private async Task<HrEmployeeDto> MapEmployeeAsync(HrEmployee e)
    {
        var orgName = e.OrgUnitId is > 0
            ? await _db.HrOrgUnits.Where(u => u.Id == e.OrgUnitId!.Value).Select(u => u.Name).FirstOrDefaultAsync()
            : null;
        var mgrName = e.ManagerId is > 0
            ? await _db.HrEmployees.Where(x => x.Id == e.ManagerId!.Value)
                .Select(x => x.FirstName + " " + x.LastName).FirstOrDefaultAsync()
            : null;
        var sysName = e.SystemUserId is > 0
            ? await _db.SystemUsers.Where(u => u.Id == e.SystemUserId!.Value).Select(u => u.Username).FirstOrDefaultAsync()
            : null;
        var activeContract = await _db.HrContracts.AsNoTracking()
            .Where(c => c.EmployeeId == e.Id && c.IsActive)
            .OrderByDescending(c => c.StartDate).FirstOrDefaultAsync();
        return new HrEmployeeDto
        {
            Id = e.Id, Code = e.Code, FirstName = e.FirstName, LastName = e.LastName,
            NationalCode = e.NationalCode, BirthDate = e.BirthDate, Gender = e.Gender,
            MaritalStatus = e.MaritalStatus, Mobile = e.Mobile, Email = e.Email, Address = e.Address,
            HireDate = e.HireDate, OrgUnitId = e.OrgUnitId, OrgUnitName = orgName,
            PostTitle = e.PostTitle, ManagerId = e.ManagerId, ManagerName = mgrName,
            EmploymentType = (int)e.EmploymentType, Status = (int)e.Status,
            SystemUserId = e.SystemUserId, SystemUserName = sysName,
            BaseSalary = e.BaseSalary, IsActive = e.IsActive,
            ActiveContractNo = activeContract?.ContractNo, ActiveContractEnd = activeContract?.EndDate,
            ContractsCount = await _db.HrContracts.CountAsync(c => c.EmployeeId == e.Id),
            DecreesCount = await _db.HrDecrees.CountAsync(d => d.EmployeeId == e.Id)
        };
    }

    // ==================== ساختار سازمانی ====================

    public async Task<List<HrOrgUnitDto>> ListUnitsAsync()
    {
        var units = await _db.HrOrgUnits.AsNoTracking().OrderBy(u => u.SortOrder).ThenBy(u => u.Name).ToListAsync();
        var list = new List<HrOrgUnitDto>();
        foreach (var u in units) list.Add(await MapUnitAsync(u, false));
        return list;
    }

    public async Task<List<HrOrgUnitDto>> GetTreeAsync()
    {
        var flat = await ListUnitsAsync();
        var byId = flat.ToDictionary(u => u.Id);
        var roots = new List<HrOrgUnitDto>();
        foreach (var u in flat)
        {
            if (u.ParentId is > 0 && byId.TryGetValue(u.ParentId.Value, out var p)) p.Children.Add(u);
            else roots.Add(u);
        }
        return roots;
    }

    public async Task<HrOrgUnitDto> SaveUnitAsync(int? id, HrOrgUnitSaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) throw new InvalidOperationException("نام واحد الزامی است.");
        if (string.IsNullOrWhiteSpace(dto.Code)) throw new InvalidOperationException("کد واحد الزامی است.");
        if (await _db.HrOrgUnits.AnyAsync(u => u.Id != (id ?? 0) && u.Code == dto.Code.Trim()))
            throw new InvalidOperationException("کد واحد تکراری است.");
        if (dto.ParentId is > 0)
        {
            if (id is > 0 && dto.ParentId.Value == id.Value)
                throw new InvalidOperationException("والد نمی‌تواند خود واحد باشد.");
            if (!await _db.HrOrgUnits.AnyAsync(u => u.Id == dto.ParentId.Value))
                throw new InvalidOperationException("واحد والد نامعتبر است.");
            // جلوگیری از حلقه در درخت
            if (id is > 0)
            {
                var walker = dto.ParentId;
                for (var i = 0; i < 50 && walker is > 0; i++)
                {
                    if (walker.Value == id.Value) throw new InvalidOperationException("انتخاب این والد باعث حلقه در چارت می‌شود.");
                    walker = await _db.HrOrgUnits.Where(u => u.Id == walker.Value).Select(u => u.ParentId).FirstOrDefaultAsync();
                }
            }
        }
        if (dto.ManagerEmployeeId is > 0 && !await _db.HrEmployees.AnyAsync(e => e.Id == dto.ManagerEmployeeId.Value))
            throw new InvalidOperationException("مدیر واحد نامعتبر است.");

        HrOrgUnit u;
        if (id is > 0)
        {
            u = await _db.HrOrgUnits.FirstOrDefaultAsync(x => x.Id == id.Value)
                ?? throw new InvalidOperationException("واحد یافت نشد.");
        }
        else { u = new HrOrgUnit(); _db.HrOrgUnits.Add(u); }

        u.ParentId = dto.ParentId; u.Type = (HrOrgUnitType)dto.Type;
        u.Code = dto.Code.Trim(); u.Name = dto.Name.Trim();
        u.ManagerEmployeeId = dto.ManagerEmployeeId; u.Phone = dto.Phone?.Trim(); u.Address = dto.Address?.Trim();
        u.IsActive = dto.IsActive; u.SortOrder = dto.SortOrder;
        await _db.SaveChangesAsync();
        return await MapUnitAsync(u, false);
    }

    public async Task DeleteUnitAsync(int id)
    {
        var u = await _db.HrOrgUnits.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("واحد یافت نشد.");
        if (await _db.HrOrgUnits.AnyAsync(x => x.ParentId == id))
            throw new InvalidOperationException("این واحد زیرمجموعه دارد؛ ابتدا آن‌ها را منتقل کنید.");
        if (await _db.HrEmployees.AnyAsync(e => e.OrgUnitId == id))
            throw new InvalidOperationException("پرسنلی در این واحد هست؛ ابتدا آن‌ها را منتقل کنید.");
        _db.HrOrgUnits.Remove(u);
        await _db.SaveChangesAsync();
    }

    private async Task<HrOrgUnitDto> MapUnitAsync(HrOrgUnit u, bool withChildren)
    {
        var dto = new HrOrgUnitDto
        {
            Id = u.Id, ParentId = u.ParentId, Type = (int)u.Type, Code = u.Code, Name = u.Name,
            ManagerEmployeeId = u.ManagerEmployeeId, Phone = u.Phone,
            IsActive = u.IsActive, SortOrder = u.SortOrder,
            EmployeeCount = await _db.HrEmployees.CountAsync(e => e.OrgUnitId == u.Id && e.IsActive)
        };
        if (u.ParentId is > 0)
            dto.ParentName = await _db.HrOrgUnits.Where(x => x.Id == u.ParentId!.Value).Select(x => x.Name).FirstOrDefaultAsync();
        if (u.ManagerEmployeeId is > 0)
            dto.ManagerName = await _db.HrEmployees.Where(e => e.Id == u.ManagerEmployeeId!.Value)
                .Select(e => e.FirstName + " " + e.LastName).FirstOrDefaultAsync();
        return dto;
    }

    // ==================== قراردادها ====================

    public async Task<List<HrContractDto>> EmployeeContractsAsync(int employeeId) =>
        await ListContractsQuery(_db.HrContracts.Where(c => c.EmployeeId == employeeId));

    public async Task<List<HrContractDto>> ListContractsAsync(bool? onlyActive)
    {
        var q = _db.HrContracts.AsQueryable();
        if (onlyActive == true) q = q.Where(c => c.IsActive);
        return await ListContractsQuery(q);
    }

    public async Task<List<HrContractDto>> ExpiringContractsAsync(int days)
    {
        days = Math.Clamp(days, 1, 365);
        var horizon = DateTime.Today.AddDays(days);
        return await ListContractsQuery(_db.HrContracts
            .Where(c => c.IsActive && c.EndDate != null && c.EndDate.Value.Date <= horizon)
            .Join(_db.HrEmployees.Where(e => e.IsActive), c => c.EmployeeId, e => e.Id, (c, e) => c));
    }

    private async Task<List<HrContractDto>> ListContractsQuery(IQueryable<HrContract> q)
    {
        var rows = await q.AsNoTracking().OrderByDescending(c => c.StartDate).Take(500).ToListAsync();
        var list = new List<HrContractDto>();
        foreach (var c in rows)
        {
            var empName = await _db.HrEmployees.Where(e => e.Id == c.EmployeeId)
                .Select(e => e.FirstName + " " + e.LastName).FirstOrDefaultAsync() ?? "";
            var orgName = c.OrgUnitId is > 0
                ? await _db.HrOrgUnits.Where(u => u.Id == c.OrgUnitId!.Value).Select(u => u.Name).FirstOrDefaultAsync()
                : null;
            list.Add(new HrContractDto
            {
                Id = c.Id, EmployeeId = c.EmployeeId, EmployeeName = empName,
                ContractNo = c.ContractNo, Type = (int)c.Type,
                StartDate = c.StartDate, EndDate = c.EndDate,
                DaysToEnd = c.EndDate == null ? null : (int)(c.EndDate.Value.Date - DateTime.Today).TotalDays,
                BaseSalary = c.BaseSalary, JobTitle = c.JobTitle,
                OrgUnitId = c.OrgUnitId, OrgUnitName = orgName,
                Description = c.Description, IsActive = c.IsActive
            });
        }
        return list;
    }

    public async Task<HrContractDto> SaveContractAsync(int? id, HrContractSaveDto dto)
    {
        if (!await _db.HrEmployees.AnyAsync(e => e.Id == dto.EmployeeId))
            throw new InvalidOperationException("پرسنل نامعتبر است.");
        if (dto.EndDate != null && dto.EndDate < dto.StartDate)
            throw new InvalidOperationException("تاریخ پایان نمی‌تواند قبل از شروع باشد.");
        if (dto.OrgUnitId is > 0 && !await _db.HrOrgUnits.AnyAsync(u => u.Id == dto.OrgUnitId.Value))
            throw new InvalidOperationException("واحد سازمانی نامعتبر است.");

        HrContract c;
        if (id is > 0)
        {
            c = await _db.HrContracts.FirstOrDefaultAsync(x => x.Id == id.Value)
                ?? throw new InvalidOperationException("قرارداد یافت نشد.");
        }
        else { c = new HrContract(); _db.HrContracts.Add(c); }

        c.EmployeeId = dto.EmployeeId; c.ContractNo = (dto.ContractNo ?? "").Trim();
        c.Type = (HrEmploymentType)dto.Type; c.StartDate = dto.StartDate; c.EndDate = dto.EndDate;
        c.BaseSalary = dto.BaseSalary; c.JobTitle = dto.JobTitle?.Trim(); c.OrgUnitId = dto.OrgUnitId;
        c.Description = dto.Description?.Trim(); c.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();
        return (await EmployeeContractsAsync(c.EmployeeId)).First(x => x.Id == c.Id);
    }

    public async Task DeleteContractAsync(int id)
    {
        var c = await _db.HrContracts.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("قرارداد یافت نشد.");
        _db.HrContracts.Remove(c);
        await _db.SaveChangesAsync();
    }

    // ==================== احکام ====================

    public async Task<List<HrDecreeDto>> EmployeeDecreesAsync(int employeeId) =>
        await ListDecreesQuery(_db.HrDecrees.Where(d => d.EmployeeId == employeeId));

    public async Task<List<HrDecreeDto>> ListDecreesAsync(int? employeeId, bool? onlyPending)
    {
        var q = _db.HrDecrees.AsQueryable();
        if (employeeId is > 0) q = q.Where(d => d.EmployeeId == employeeId.Value);
        if (onlyPending == true) q = q.Where(d => !d.IsApplied);
        return await ListDecreesQuery(q);
    }

    private async Task<List<HrDecreeDto>> ListDecreesQuery(IQueryable<HrDecree> q)
    {
        var rows = await q.AsNoTracking().OrderByDescending(d => d.EffectiveDate).ThenByDescending(d => d.Id).Take(500).ToListAsync();
        var list = new List<HrDecreeDto>();
        foreach (var d in rows)
        {
            var empName = await _db.HrEmployees.Where(e => e.Id == d.EmployeeId)
                .Select(e => e.FirstName + " " + e.LastName).FirstOrDefaultAsync() ?? "";
            var orgName = d.NewOrgUnitId is > 0
                ? await _db.HrOrgUnits.Where(u => u.Id == d.NewOrgUnitId!.Value).Select(u => u.Name).FirstOrDefaultAsync()
                : null;
            list.Add(new HrDecreeDto
            {
                Id = d.Id, EmployeeId = d.EmployeeId, EmployeeName = empName,
                DecreeNo = d.DecreeNo, Type = (int)d.Type, EffectiveDate = d.EffectiveDate,
                NewPostTitle = d.NewPostTitle, NewOrgUnitId = d.NewOrgUnitId, NewOrgUnitName = orgName,
                NewBaseSalary = d.NewBaseSalary, NewStatus = d.NewStatus == null ? null : (int)d.NewStatus.Value,
                Description = d.Description, IsApplied = d.IsApplied, AppliedAt = d.AppliedAt,
                CreatedByName = d.CreatedByName, CreatedAt = d.CreatedAt
            });
        }
        return list;
    }

    public async Task<HrDecreeDto> SaveDecreeAsync(int? id, HrDecreeSaveDto dto, int byUserId, string byName)
    {
        if (!await _db.HrEmployees.AnyAsync(e => e.Id == dto.EmployeeId))
            throw new InvalidOperationException("پرسنل نامعتبر است.");
        if (dto.NewOrgUnitId is > 0 && !await _db.HrOrgUnits.AnyAsync(u => u.Id == dto.NewOrgUnitId.Value))
            throw new InvalidOperationException("واحد سازمانی نامعتبر است.");

        HrDecree d;
        if (id is > 0)
        {
            d = await _db.HrDecrees.FirstOrDefaultAsync(x => x.Id == id.Value)
                ?? throw new InvalidOperationException("حکم یافت نشد.");
            if (d.IsApplied) throw new InvalidOperationException("حکم اجراشده قابل ویرایش نیست.");
        }
        else
        {
            d = new HrDecree { CreatedByUserId = byUserId, CreatedByName = byName };
            _db.HrDecrees.Add(d);
        }

        d.EmployeeId = dto.EmployeeId; d.DecreeNo = (dto.DecreeNo ?? "").Trim();
        d.Type = (HrDecreeType)dto.Type; d.EffectiveDate = dto.EffectiveDate;
        d.NewPostTitle = dto.NewPostTitle?.Trim(); d.NewOrgUnitId = dto.NewOrgUnitId;
        d.NewBaseSalary = dto.NewBaseSalary;
        d.NewStatus = dto.NewStatus == null ? null : (HrEmployeeStatus)dto.NewStatus.Value;
        d.Description = dto.Description?.Trim();
        await _db.SaveChangesAsync();
        return (await EmployeeDecreesAsync(d.EmployeeId)).First(x => x.Id == d.Id);
    }

    /// <summary>اجرای حکم روی پرونده پرسنل — یک‌بار و برگشت‌ناپذیر (حکم جدید لازم است)</summary>
    public async Task<HrDecreeDto> ApplyDecreeAsync(int id)
    {
        var d = await _db.HrDecrees.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("حکم یافت نشد.");
        if (d.IsApplied) throw new InvalidOperationException("این حکم قبلاً اجرا شده است.");
        var e = await _db.HrEmployees.FirstOrDefaultAsync(x => x.Id == d.EmployeeId)
            ?? throw new InvalidOperationException("پرسنل حکم یافت نشد.");

        if (!string.IsNullOrWhiteSpace(d.NewPostTitle)) e.PostTitle = d.NewPostTitle;
        if (d.NewOrgUnitId is > 0) e.OrgUnitId = d.NewOrgUnitId;
        if (d.NewBaseSalary is > 0) e.BaseSalary = d.NewBaseSalary.Value;
        if (d.NewStatus != null)
        {
            e.Status = d.NewStatus.Value;
            if (d.NewStatus is HrEmployeeStatus.Terminated or HrEmployeeStatus.Retired) e.IsActive = false;
            if (d.NewStatus is HrEmployeeStatus.Active) e.IsActive = true;
        }
        // احکام نوع قطع همکاری/بازنشستگی حتی بدون NewStatus اثر می‌کنند
        if (d.Type == HrDecreeType.GhatHamkari) { e.Status = HrEmployeeStatus.Terminated; e.IsActive = false; }
        if (d.Type == HrDecreeType.Bazneshastegi) { e.Status = HrEmployeeStatus.Retired; e.IsActive = false; }
        e.UpdatedAt = DateTime.Now;

        d.IsApplied = true;
        d.AppliedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return (await EmployeeDecreesAsync(d.EmployeeId)).First(x => x.Id == d.Id);
    }

    public async Task DeleteDecreeAsync(int id)
    {
        var d = await _db.HrDecrees.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("حکم یافت نشد.");
        if (d.IsApplied) throw new InvalidOperationException("حکم اجراشده قابل حذف نیست.");
        _db.HrDecrees.Remove(d);
        await _db.SaveChangesAsync();
    }

    // ==================== داشبورد ====================

    public async Task<HrDashboardDto> DashboardAsync(int expiringDays = 30)
    {
        var expiring = await ExpiringContractsAsync(expiringDays);
        var recent = await _db.HrDecrees.AsNoTracking().OrderByDescending(d => d.Id).Take(5).ToListAsync();
        var recentDtos = new List<HrDecreeDto>();
        foreach (var d in recent)
            recentDtos.Add((await ListDecreesQuery(_db.HrDecrees.Where(x => x.Id == d.Id))).First());

        var byType = await _db.HrEmployees.Where(e => e.IsActive)
            .GroupBy(e => e.EmploymentType)
            .Select(g => new { g.Key, C = g.Count() })
            .ToDictionaryAsync(x => HrCoreTexts.EmploymentType((int)x.Key), x => x.C);

        return new HrDashboardDto
        {
            ActiveEmployees = await _db.HrEmployees.CountAsync(e => e.IsActive),
            OrgUnits = await _db.HrOrgUnits.CountAsync(u => u.IsActive),
            ExpiringContracts = expiring.Count,
            PendingDecrees = await _db.HrDecrees.CountAsync(d => !d.IsApplied),
            ExpiringList = expiring.Take(10).ToList(),
            RecentDecrees = recentDtos,
            ByEmploymentType = byType
        };
    }
}
