using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Api.Services.FaCom;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Hosting;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ClosedXML.Excel;
using Inventory.Api.Services.Pdf;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.HrCore;

/// <summary>نام‌های فارسی انواع ماژول کارگزینی (تک‌منبع سمت سرور)</summary>
public static class HrCoreTexts
{
    public static string EmploymentType(int t) => t switch
    {
        0 => "رسمی", 1 => "قراردادی", 2 => "پیمانی", 3 => "ساعتی", 4 => "مشاوره‌ای",
        5 => "تمام‌وقت", 6 => "پاره‌وقت", 7 => "پروژه‌ای", 8 => "آزمایشی", _ => "نامشخص"
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
    public static string SkillLevel(int l) => l switch
    {
        0 => "مبتدی", 1 => "متوسط", 2 => "پیشرفته", 3 => "خبره", _ => "—"
    };
}

public interface IHrCoreService
{
    // پرسنل
    Task<(List<HrEmployeeDto> Items, int Total)> SearchEmployeesAsync(string? q, int? orgUnitId, int? status, int skip, int take, int? hrMainNodeId = null);
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
    Task<HrContractDto> SaveContractAsync(int? id, HrContractSaveDto dto, int byUserId, string byName);
    Task DeleteContractAsync(int id);
    Task<HrContractDto> RenewContractAsync(int id, int months);
    Task<int> RemindExpiringDocumentsAsync(int days);
    Task<byte[]> DossierPdfAsync(int employeeId);
    Task<(byte[] Data, string FileName)> ExportEmployeesExcelAsync(string? q);
    Task<(byte[] Data, string FileName)> ExportContractsExcelAsync();
    Task<(byte[] Data, string FileName)> ExportDecreesExcelAsync();
    Task<(byte[] Data, string FileName)> ExportOrgExcelAsync();
    Task<HrAuditListResult> SearchHrAuditAsync(string? module, string? action, string? q,
        DateTime? from, DateTime? to, int skip, int take);
    Task<HrAuditLogDto?> GetHrAuditAsync(long id);
    Task<HrManagerDashboardDto> GetManagerDashboardAsync(int year);

    // قالب‌های قرارداد (§۹)
    Task<List<HrContractTemplateDto>> ListTemplatesAsync();
    Task<HrContractTemplateDto> SaveTemplateAsync(int? id, HrContractTemplateSaveDto dto);
    Task DeleteTemplateAsync(int id);

    // امضا و نسخه‌های قرارداد (§۹)
    Task<HrContractDto> SubmitForSignAsync(int id);
    Task<HrContractDto> SignEmployeeAsync(int id, string name);
    Task<HrContractDto> SignEmployerAsync(int id, int byUserId, string byName);
    Task<List<HrContractVersionDto>> ListVersionsAsync(int contractId);

    // هشدارهای انقضا (§۹)
    Task<int> CheckAlertsAsync(int days);
    Task<List<HrContractAlertDto>> ListAlertsAsync();
    Task DismissAlertAsync(int id);

    // احکام
    Task<List<HrDecreeDto>> EmployeeDecreesAsync(int employeeId);
    Task<List<HrDecreeDto>> ListDecreesAsync(int? employeeId, bool? onlyPending);
    Task<HrDecreeDto> SaveDecreeAsync(int? id, HrDecreeSaveDto dto, int byUserId, string byName);
    Task<HrDecreeDto> ApplyDecreeAsync(int id);
    Task<byte[]> DecreePdfAsync(int id);
    Task<byte[]> ContractPdfAsync(int id);
    Task DeleteDecreeAsync(int id);

    // پرونده کارمندان — تحت‌تکفل، دوره‌ها، مهارت‌ها، زبان‌ها، اسناد، عکس
    Task<HrEmployeeDossierDto> GetDossierAsync(int employeeId, int expiringDays = 30);
    Task<List<HrEmployeeDependentDto>> ListDependentsAsync(int employeeId);
    Task<HrEmployeeDependentDto> SaveDependentAsync(int employeeId, int? id, HrEmployeeDependentSaveDto dto);
    Task DeleteDependentAsync(int id);
    Task<List<HrEmployeeCourseDto>> ListCoursesAsync(int employeeId);
    Task<HrEmployeeCourseDto> SaveCourseAsync(int employeeId, int? id, HrEmployeeCourseSaveDto dto);
    Task DeleteCourseAsync(int id);
    Task<List<HrEmployeeSkillDto>> ListSkillsAsync(int employeeId);
    Task<HrEmployeeSkillDto> SaveSkillAsync(int employeeId, int? id, HrEmployeeSkillSaveDto dto);
    Task DeleteSkillAsync(int id);
    Task<List<HrEmployeeLanguageDto>> ListLanguagesAsync(int employeeId);
    Task<HrEmployeeLanguageDto> SaveLanguageAsync(int employeeId, int? id, HrEmployeeLanguageSaveDto dto);
    Task DeleteLanguageAsync(int id);
    Task<List<HrEmployeeDocumentDto>> ListDocumentsAsync(int employeeId, int expiringDays = 30);
    Task<HrEmployeeDocumentDto?> GetDocumentAsync(int id);
    Task<HrEmployeeDocumentDto> SaveDocumentAsync(int employeeId, int? id, HrEmployeeDocumentSaveDto dto);
    Task<HrEmployeeDocumentDto> AttachDocumentFileAsync(int id, string filePath, string fileName, string contentType, long fileSize);
    Task DeleteDocumentAsync(int id);
    Task<List<HrEmployeeDocumentDto>> ExpiringDocumentsAsync(int days);
    Task<HrEmployeeDto> SetEmployeePhotoAsync(int employeeId, string? photoPath);

    // داشبورد
    Task<HrDashboardDto> DashboardAsync(int expiringDays = 30);
}

public class HrCoreService : IHrCoreService
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly INotifyService _notify;
    private readonly ISmsSender _sms;
    public HrCoreService(AppDbContext db, IWebHostEnvironment env, INotifyService notify, ISmsSender sms) => (_db, _env, _notify, _sms) = (db, env, notify, sms);

    // ==================== پرسنل ====================

    public async Task<(List<HrEmployeeDto>, int)> SearchEmployeesAsync(string? q, int? orgUnitId, int? status, int skip, int take, int? hrMainNodeId = null)
    {
        take = Math.Clamp(take, 1, 200);
        var query = _db.HrEmployees.AsNoTracking().AsQueryable();
        if (orgUnitId is > 0) query = query.Where(e => e.OrgUnitId == orgUnitId.Value);
        if (hrMainNodeId is > 0) query = query.Where(e => e.HrMainNodeId == hrMainNodeId.Value);
        if (status is >= 0) query = query.Where(e => (int)e.Status == status.Value);
        if (!string.IsNullOrWhiteSpace(q))
        {
            q = q.Trim();
            query = query.Where(e => e.FirstName.Contains(q) || e.LastName.Contains(q)
                || e.Code.Contains(q) || e.NationalCode.Contains(q) || (e.PostTitle ?? "").Contains(q));
        }
        var total = await query.CountAsync();
        var rows = await query.OrderBy(e => e.Code).ThenBy(e => e.Id).Skip(Math.Max(0, skip)).Take(take).ToListAsync();
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

        await ValidateRefsAsync(dto.OrgUnitId, dto.ManagerId, null, dto.SystemUserId, dto.HrMainNodeId, dto.HrMainPositionId);

        var e = new HrEmployee
        {
            Code = code, FirstName = dto.FirstName.Trim(), LastName = dto.LastName.Trim(),
            NationalCode = dto.NationalCode.Trim(), BirthDate = dto.BirthDate,
            Gender = dto.Gender, MaritalStatus = dto.MaritalStatus,
            Mobile = dto.Mobile?.Trim(), Email = dto.Email?.Trim(), Address = dto.Address?.Trim(),
            Landline = dto.Landline?.Trim(),
            EmergencyContactName = dto.EmergencyContactName?.Trim(),
            EmergencyContactRelation = dto.EmergencyContactRelation?.Trim(),
            EmergencyContactPhone = dto.EmergencyContactPhone?.Trim(),
            Workplace = dto.Workplace?.Trim(), Degree = dto.Degree?.Trim(), FieldOfStudy = dto.FieldOfStudy?.Trim(),
            HireDate = dto.HireDate == default ? DateTime.Today : dto.HireDate,
            OrgUnitId = dto.OrgUnitId, PostTitle = dto.PostTitle?.Trim(), ManagerId = dto.ManagerId,
            HrMainNodeId = dto.HrMainNodeId, HrMainPositionId = dto.HrMainPositionId,
            EmploymentType = (HrEmploymentType)dto.EmploymentType, Status = (HrEmployeeStatus)dto.Status,
            SystemUserId = dto.SystemUserId, BaseSalary = dto.BaseSalary, IsActive = dto.IsActive, Sheba = HrSheba.Norm(dto.Sheba), BankName = dto.BankName?.Trim(), InsuranceNo = dto.InsuranceNo?.Trim()
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
        await ValidateRefsAsync(dto.OrgUnitId, dto.ManagerId, id, dto.SystemUserId, dto.HrMainNodeId, dto.HrMainPositionId);

        e.Code = code; e.FirstName = dto.FirstName.Trim(); e.LastName = dto.LastName.Trim();
        e.NationalCode = dto.NationalCode.Trim(); e.BirthDate = dto.BirthDate;
        e.Gender = dto.Gender; e.MaritalStatus = dto.MaritalStatus;
        e.Mobile = dto.Mobile?.Trim(); e.Email = dto.Email?.Trim(); e.Address = dto.Address?.Trim();
        e.Landline = dto.Landline?.Trim();
        e.EmergencyContactName = dto.EmergencyContactName?.Trim();
        e.EmergencyContactRelation = dto.EmergencyContactRelation?.Trim();
        e.EmergencyContactPhone = dto.EmergencyContactPhone?.Trim();
        e.Workplace = dto.Workplace?.Trim(); e.Degree = dto.Degree?.Trim(); e.FieldOfStudy = dto.FieldOfStudy?.Trim();
        e.HireDate = dto.HireDate; e.OrgUnitId = dto.OrgUnitId; e.PostTitle = dto.PostTitle?.Trim();
        e.HrMainNodeId = dto.HrMainNodeId; e.HrMainPositionId = dto.HrMainPositionId;
        e.ManagerId = dto.ManagerId; e.EmploymentType = (HrEmploymentType)dto.EmploymentType;
        e.Status = (HrEmployeeStatus)dto.Status; e.SystemUserId = dto.SystemUserId;
        e.BaseSalary = dto.BaseSalary; e.IsActive = dto.IsActive; e.Sheba = HrSheba.Norm(dto.Sheba); e.BankName = dto.BankName?.Trim(); e.InsuranceNo = dto.InsuranceNo?.Trim(); e.UpdatedAt = DateTime.Now;
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

    private async Task ValidateRefsAsync(int? orgUnitId, int? managerId, int? selfId, int? systemUserId,
        int? hrMainNodeId = null, int? hrMainPositionId = null)
    {
        if (orgUnitId is > 0 && !await _db.HrOrgUnits.AnyAsync(u => u.Id == orgUnitId.Value))
            throw new InvalidOperationException("واحد سازمانی نامعتبر است.");
        if (hrMainNodeId is > 0 && !await _db.HrMainOrgNodes.AnyAsync(n => n.Id == hrMainNodeId.Value))
            throw new InvalidOperationException("گره ساختار سازمانی (منابع انسانی اصلی) نامعتبر است.");
        if (hrMainPositionId is > 0 && !await _db.HrMainPositions.AnyAsync(p => p.Id == hrMainPositionId.Value))
            throw new InvalidOperationException("پست سازمانی (منابع انسانی اصلی) نامعتبر است.");
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
        var hrMainNodeName = e.HrMainNodeId is > 0
            ? await _db.HrMainOrgNodes.Where(n => n.Id == e.HrMainNodeId!.Value).Select(n => n.Name).FirstOrDefaultAsync()
            : null;
        var hrMainPositionTitle = e.HrMainPositionId is > 0
            ? await _db.HrMainPositions.Where(p => p.Id == e.HrMainPositionId!.Value).Select(p => p.Title).FirstOrDefaultAsync()
            : null;
        var activeContract = await _db.HrContracts.AsNoTracking()
            .Where(c => c.EmployeeId == e.Id && c.IsActive)
            .OrderByDescending(c => c.StartDate).FirstOrDefaultAsync();
        return new HrEmployeeDto
        {
            Id = e.Id, Code = e.Code, FirstName = e.FirstName, LastName = e.LastName,
            NationalCode = e.NationalCode, BirthDate = e.BirthDate, Gender = e.Gender,
            MaritalStatus = e.MaritalStatus, Mobile = e.Mobile, Email = e.Email, Address = e.Address,
            Landline = e.Landline, PhotoPath = e.PhotoPath,
            EmergencyContactName = e.EmergencyContactName, EmergencyContactRelation = e.EmergencyContactRelation,
            EmergencyContactPhone = e.EmergencyContactPhone, Workplace = e.Workplace,
            Degree = e.Degree, FieldOfStudy = e.FieldOfStudy,
            HireDate = e.HireDate, OrgUnitId = e.OrgUnitId, OrgUnitName = orgName,
            PostTitle = e.PostTitle, ManagerId = e.ManagerId, ManagerName = mgrName,
            HrMainNodeId = e.HrMainNodeId, HrMainNodeName = hrMainNodeName,
            HrMainPositionId = e.HrMainPositionId, HrMainPositionTitle = hrMainPositionTitle,
            EmploymentType = (int)e.EmploymentType, Status = (int)e.Status,
            SystemUserId = e.SystemUserId, SystemUserName = sysName,
            BaseSalary = e.BaseSalary, IsActive = e.IsActive, Sheba = e.Sheba, BankName = e.BankName, InsuranceNo = e.InsuranceNo,
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
        var tmplIds = rows.Where(c => c.TemplateId != null).Select(c => c.TemplateId!.Value).Distinct().ToList();
        var tmplNames = tmplIds.Count == 0 ? new Dictionary<int, string>()
            : await _db.HrContractTemplates.Where(x => tmplIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name);
        var cids = rows.Select(c => c.Id).ToList();
        var vcounts = cids.Count == 0 ? new Dictionary<int, int>()
            : await _db.HrContractVersions.Where(v => cids.Contains(v.ContractId))
                .GroupBy(v => v.ContractId).ToDictionaryAsync(g => g.Key, g => g.Count());
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
                Description = c.Description, IsActive = c.IsActive,
                TemplateId = c.TemplateId,
                TemplateName = c.TemplateId != null && tmplNames.TryGetValue(c.TemplateId.Value, out var tn) ? tn : null,
                SignStatus = (int)c.SignStatus,
                EmployeeSignedBy = c.EmployeeSignedBy, EmployeeSignedAt = c.EmployeeSignedAt,
                EmployerSignedByName = c.EmployerSignedByName, EmployerSignedAt = c.EmployerSignedAt,
                VersionsCount = vcounts.TryGetValue(c.Id, out var vc) ? vc : 0
            });
        }
        return list;
    }

    public async Task<HrContractDto> SaveContractAsync(int? id, HrContractSaveDto dto, int byUserId, string byName)
    {
        if (!await _db.HrEmployees.AnyAsync(e => e.Id == dto.EmployeeId))
            throw new InvalidOperationException("پرسنل نامعتبر است.");
        if (dto.EndDate != null && dto.EndDate < dto.StartDate)
            throw new InvalidOperationException("تاریخ پایان نمی‌تواند قبل از شروع باشد.");
        if (dto.OrgUnitId is > 0 && !await _db.HrOrgUnits.AnyAsync(u => u.Id == dto.OrgUnitId.Value))
            throw new InvalidOperationException("واحد سازمانی نامعتبر است.");

        if (dto.TemplateId is > 0 && !await _db.HrContractTemplates.AnyAsync(x => x.Id == dto.TemplateId.Value))
            throw new InvalidOperationException("قالب قرارداد نامعتبر است.");

        HrContract c;
        if (id is > 0)
        {
            c = await _db.HrContracts.FirstOrDefaultAsync(x => x.Id == id.Value)
                ?? throw new InvalidOperationException("قرارداد یافت نشد.");
            // اسنپ‌شات نسخه قبل از تغییر (§۹-۳) + برگشت امضا به پیش‌نویس
            var maxV = await _db.HrContractVersions.Where(v => v.ContractId == c.Id).MaxAsync(v => (int?)v.VersionNo) ?? 0;
            _db.HrContractVersions.Add(new HrContractVersion
            {
                ContractId = c.Id, VersionNo = maxV + 1, ContractNo = c.ContractNo, Type = c.Type,
                StartDate = c.StartDate, EndDate = c.EndDate, BaseSalary = (double)c.BaseSalary,
                JobTitle = c.JobTitle, OrgUnitId = c.OrgUnitId, Description = c.Description, IsActive = c.IsActive,
                ChangedByUserId = byUserId, ChangedByName = byName, ChangedAt = DateTime.Now, ChangeNote = dto.ChangeNote?.Trim()
            });
            c.SignStatus = HrContractSignStatus.Draft;
            c.EmployeeSignedBy = null; c.EmployeeSignedAt = null;
            c.EmployerSignedByUserId = null; c.EmployerSignedByName = null; c.EmployerSignedAt = null;
        }
        else { c = new HrContract(); _db.HrContracts.Add(c); }

        c.EmployeeId = dto.EmployeeId; c.ContractNo = (dto.ContractNo ?? "").Trim();
        c.Type = (HrEmploymentType)dto.Type; c.StartDate = dto.StartDate; c.EndDate = dto.EndDate;
        c.BaseSalary = dto.BaseSalary; c.JobTitle = dto.JobTitle?.Trim(); c.OrgUnitId = dto.OrgUnitId;
        c.Description = dto.Description?.Trim(); c.IsActive = dto.IsActive; c.TemplateId = dto.TemplateId;
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

    // ==================== قالب‌های قرارداد (§۹) ====================

    public async Task<List<HrContractTemplateDto>> ListTemplatesAsync()
        => await _db.HrContractTemplates.AsNoTracking().OrderBy(t => t.SortOrder).ThenBy(t => t.Id)
            .Select(t => new HrContractTemplateDto
            {
                Id = t.Id, Name = t.Name, Type = (int)t.Type, DurationMonths = t.DurationMonths,
                JobTitle = t.JobTitle, Terms = t.Terms, SortOrder = t.SortOrder, IsActive = t.IsActive
            }).ToListAsync();

    public async Task<HrContractTemplateDto> SaveTemplateAsync(int? id, HrContractTemplateSaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) throw new InvalidOperationException("نام قالب الزامی است.");
        HrContractTemplate t;
        if (id is > 0)
            t = await _db.HrContractTemplates.FirstOrDefaultAsync(x => x.Id == id.Value)
                ?? throw new InvalidOperationException("قالب یافت نشد.");
        else { t = new HrContractTemplate(); _db.HrContractTemplates.Add(t); }
        t.Name = dto.Name.Trim(); t.Type = (HrEmploymentType)dto.Type;
        t.DurationMonths = dto.DurationMonths; t.JobTitle = dto.JobTitle?.Trim();
        t.Terms = dto.Terms?.Trim(); t.SortOrder = dto.SortOrder; t.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();
        return (await ListTemplatesAsync()).First(x => x.Id == t.Id);
    }

    public async Task DeleteTemplateAsync(int id)
    {
        var t = await _db.HrContractTemplates.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("قالب یافت نشد.");
        if (await _db.HrContracts.AnyAsync(c => c.TemplateId == id))
            throw new InvalidOperationException("این قالب در قراردادها استفاده شده؛ ابتدا غیرفعالش کنید.");
        _db.HrContractTemplates.Remove(t);
        await _db.SaveChangesAsync();
    }

    // ==================== امضا و نسخه‌های قرارداد (§۹) ====================

    public async Task<HrContractDto> SubmitForSignAsync(int id)
    {
        var c = await _db.HrContracts.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("قرارداد یافت نشد.");
        if (c.SignStatus != HrContractSignStatus.Draft)
            throw new InvalidOperationException("فقط قرارداد پیش‌نویس قابل ارسال برای امضا است.");
        c.SignStatus = HrContractSignStatus.PendingSign;
        await _db.SaveChangesAsync();
        return (await EmployeeContractsAsync(c.EmployeeId)).First(x => x.Id == c.Id);
    }

    public async Task<HrContractDto> SignEmployeeAsync(int id, string name)
    {
        var c = await _db.HrContracts.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("قرارداد یافت نشد.");
        if (c.SignStatus != HrContractSignStatus.PendingSign)
            throw new InvalidOperationException("قرارداد در مرحله امضا نیست.");
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("نام امضاکننده الزامی است.");
        c.EmployeeSignedBy = name.Trim(); c.EmployeeSignedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return (await EmployeeContractsAsync(c.EmployeeId)).First(x => x.Id == c.Id);
    }

    public async Task<HrContractDto> SignEmployerAsync(int id, int byUserId, string byName)
    {
        var c = await _db.HrContracts.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("قرارداد یافت نشد.");
        if (c.SignStatus != HrContractSignStatus.PendingSign)
            throw new InvalidOperationException("قرارداد در مرحله امضا نیست.");
        if (c.EmployeeSignedAt == null)
            throw new InvalidOperationException("ابتدا امضای کارمند ثبت شود.");
        c.EmployerSignedByUserId = byUserId; c.EmployerSignedByName = byName; c.EmployerSignedAt = DateTime.Now;
        c.SignStatus = HrContractSignStatus.Signed;
        await _db.SaveChangesAsync();
        return (await EmployeeContractsAsync(c.EmployeeId)).First(x => x.Id == c.Id);
    }

    public async Task<List<HrContractVersionDto>> ListVersionsAsync(int contractId)
        => await _db.HrContractVersions.AsNoTracking().Where(v => v.ContractId == contractId)
            .OrderByDescending(v => v.VersionNo).Select(v => new HrContractVersionDto
            {
                Id = v.Id, ContractId = v.ContractId, VersionNo = v.VersionNo, ContractNo = v.ContractNo,
                Type = (int)v.Type, StartDate = v.StartDate, EndDate = v.EndDate, BaseSalary = v.BaseSalary,
                JobTitle = v.JobTitle, OrgUnitId = v.OrgUnitId, Description = v.Description, IsActive = v.IsActive,
                ChangedByName = v.ChangedByName, ChangedAt = v.ChangedAt, ChangeNote = v.ChangeNote
            }).ToListAsync();

    // ==================== هشدارهای انقضای قرارداد (§۹) ====================

    public async Task<int> CheckAlertsAsync(int days)
    {
        if (days <= 0) days = 30;
        var horizon = DateTime.Today.AddDays(days);
        var contracts = await _db.HrContracts.AsNoTracking()
            .Where(c => c.IsActive && c.EndDate != null && c.EndDate.Value.Date <= horizon).ToListAsync();
        int n = 0;
        var fresh = new List<(int EmployeeId, DateTime Expire)>();
        foreach (var c in contracts)
        {
            var a = await _db.HrContractExpiryAlerts
                .FirstOrDefaultAsync(x => x.ContractId == c.Id && x.ThresholdDays == days);
            if (a == null)
            {
                _db.HrContractExpiryAlerts.Add(new HrContractExpiryAlert
                { ContractId = c.Id, ThresholdDays = days, ExpireDate = c.EndDate!.Value, NotifiedCount = 1 });
                n++;
                fresh.Add((c.EmployeeId, c.EndDate!.Value));
            }
            else { a.ExpireDate = c.EndDate!.Value; a.NotifiedCount++; a.CreatedAt = DateTime.Now; }
        }
        await _db.SaveChangesAsync();
        if (fresh.Count > 0)
        {
            try
            {
                var eids = fresh.Select(x => x.EmployeeId).Distinct().ToList();
                var emps = await _db.HrEmployees.AsNoTracking().Where(e => eids.Contains(e.Id)).ToListAsync();
                foreach (var f in fresh)
                {
                    var e = emps.FirstOrDefault(x => x.Id == f.EmployeeId);
                    if (e == null) continue;
                    var left = (int)(f.Expire.Date - DateTime.Today).TotalDays;
                    var msg = left < 0 ? $"قرارداد شما {-left} روز است منقضی شده."
                        : left == 0 ? "قرارداد شما امروز به پایان می‌رسد."
                        : $"فقط {left} روز تا پایان قرارداد شما مانده.";
                    if (e.SystemUserId is > 0)
                        await _notify.SendAsync(e.SystemUserId.Value, "هشدار پایان قرارداد", msg,
                            "منابع انسانی", "HrCore", "hr-core/contracts");
                    if (_sms.IsConfigured && !string.IsNullOrWhiteSpace(e.Mobile))
                        try { await _sms.SendAsync(e.Mobile!.Trim(), msg); } catch { }
                }
                await _notify.BroadcastChangedAsync("hr-core");
            }
            catch { }
        }
        return n;
    }

    public async Task<List<HrContractAlertDto>> ListAlertsAsync()
    {
        var alerts = await _db.HrContractExpiryAlerts.AsNoTracking().OrderBy(a => a.ExpireDate).Take(200).ToListAsync();
        if (alerts.Count == 0) return new();
        var cids = alerts.Select(a => a.ContractId).Distinct().ToList();
        var contracts = await _db.HrContracts.AsNoTracking().Where(c => cids.Contains(c.Id)).ToDictionaryAsync(c => c.Id);
        var eids = contracts.Values.Select(c => c.EmployeeId).Distinct().ToList();
        var names = await _db.HrEmployees.Where(e => eids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.FirstName + " " + e.LastName);
        return alerts.Select(a =>
        {
            contracts.TryGetValue(a.ContractId, out var c);
            return new HrContractAlertDto
            {
                Id = a.Id, ContractId = a.ContractId, ContractNo = c?.ContractNo ?? "",
                EmployeeName = c != null && names.TryGetValue(c.EmployeeId, out var nm) ? nm : "",
                ThresholdDays = a.ThresholdDays, ExpireDate = a.ExpireDate,
                DaysToEnd = (int)(a.ExpireDate.Date - DateTime.Today).TotalDays,
                NotifiedCount = a.NotifiedCount, CreatedAt = a.CreatedAt
            };
        }).ToList();
    }

    public async Task DismissAlertAsync(int id)
    {
        var a = await _db.HrContractExpiryAlerts.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("هشدار یافت نشد.");
        _db.HrContractExpiryAlerts.Remove(a);
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
    public async Task<byte[]> ContractPdfAsync(int id)
    {
        var list = await ListContractsQuery(_db.HrContracts.Where(c => c.Id == id));
        var d = list.FirstOrDefault() ?? throw new InvalidOperationException("قرارداد یافت نشد.");
        var emp = await _db.HrEmployees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == d.EmployeeId);
        var co = await _db.HrMainCompanies.AsNoTracking().FirstOrDefaultAsync();
        HrPdf.EnsureFonts();
        var logo = await HrPdf.TryLoadLogoAsync(_env.WebRootPath, co?.LogoPath);
        var vers = await ListVersionsAsync(id);
        var verNo = vers.Count == 0 ? 1 : vers.Max(v => v.VersionNo);
        string? terms = null;
        var tmplName = d.TemplateName;
        if (d.TemplateId is > 0)
        {
            var tmpl = (await ListTemplatesAsync()).FirstOrDefault(x => x.Id == d.TemplateId!.Value);
            terms = tmpl?.Terms;
            tmplName ??= tmpl?.Name;
        }
        var paras = (terms ?? "").Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        var coLine = string.Join(" • ", new[] { co?.Address, co?.Phone }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var typeName = HrCoreTexts.EmploymentType(d.Type);
        var endText = d.EndDate == null ? "دائمی" : PersianDate.ToShortFa(d.EndDate.Value);
        var sign = d.SignStatus switch { 0 => "پیش‌نویس", 1 => "در انتظار امضا", 2 => "امضاشده", _ => "—" };
        var empSign = d.EmployeeSignedBy == null ? null
            : (d.EmployeeSignedAt == null ? d.EmployeeSignedBy : $"{d.EmployeeSignedBy} — {PersianDate.ToShortFa(d.EmployeeSignedAt.Value)}");
        var sigs = new List<(string Role, string? Name)>
        {
            ("امضای کارمند", empSign ?? d.EmployeeName),
            ("امضای کارفرما", d.EmployerSignedByName ?? co?.ManagerName),
        };
        var doc = Document.Create(c =>
        {
            c.Page(pg =>
            {
                pg.Size(PageSizes.A4);
                pg.Margin(28);
                pg.ContentFromRightToLeft();
                pg.DefaultTextStyle(x => x.FontFamily(HrPdf.Font).FontSize(10));
                pg.Header().Column(col =>
                {
                    if (logo != null)
                    {
                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Column(c2 =>
                            {
                                c2.Item().Text(co?.Name ?? "").FontFamily(HrPdf.FontBold).FontSize(16).AlignCenter();
                                if (!string.IsNullOrWhiteSpace(coLine))
                                    c2.Item().Text(coLine).FontSize(8).FontColor(QuestPDF.Helpers.Colors.Grey.Darken1).AlignCenter();
                            });
                            r.ConstantItem(60).AlignMiddle().Image(logo).FitWidth();
                        });
                    }
                    else
                    {
                        col.Item().Text(co?.Name ?? "").FontFamily(HrPdf.FontBold).FontSize(16).AlignCenter();
                        if (!string.IsNullOrWhiteSpace(coLine))
                            col.Item().Text(coLine).FontSize(8).FontColor(QuestPDF.Helpers.Colors.Grey.Darken1).AlignCenter();
                    }
                    col.Item().PaddingTop(4).LineHorizontal(1);
                    col.Item().PaddingTop(6).Text("قرارداد همکاری").FontFamily(HrPdf.FontBold).FontSize(18).AlignCenter();
                    col.Item().Text($"{typeName} • نسخه {Fa.Digits(verNo.ToString())}").FontFamily(HrPdf.FontBold).FontSize(12).AlignCenter();
                    col.Item().PaddingTop(2).Text($"شماره قرارداد: {Fa.Digits(d.ContractNo)} • شروع: {PersianDate.ToShortFa(d.StartDate)} • پایان: {endText} • وضعیت: {sign}").FontSize(9).AlignCenter();
                    col.Item().PaddingTop(4).LineHorizontal(1);
                });
                pg.Content().Column(col =>
                {
                    col.Item().PaddingTop(8).Text("مشخصات پرسنل").FontFamily(HrPdf.FontBold).FontSize(12);
                    col.Item().PaddingTop(4).Table(tb =>
                    {
                        tb.ColumnsDefinition(cd => { cd.RelativeColumn(2); cd.RelativeColumn(3); });
                        HrPdf.KvRow(tb, "نام و نام خانوادگی", d.EmployeeName);
                        HrPdf.KvRow(tb, "کد پرسنلی", Fa.Digits(emp?.Code ?? "—"));
                        HrPdf.KvRow(tb, "کد ملی", Fa.Digits(emp?.NationalCode ?? "—"));
                    });
                    col.Item().PaddingTop(10).Text("مشخصات قرارداد").FontFamily(HrPdf.FontBold).FontSize(12);
                    col.Item().PaddingTop(4).Table(tb =>
                    {
                        tb.ColumnsDefinition(cd => { cd.RelativeColumn(2); cd.RelativeColumn(3); });
                        HrPdf.KvRow(tb, "نوع همکاری", typeName);
                        HrPdf.KvRow(tb, "عنوان شغلی", d.JobTitle ?? "—");
                        HrPdf.KvRow(tb, "واحد سازمانی", d.OrgUnitName ?? "—");
                        HrPdf.KvRow(tb, "حقوق پایه (ریال)", Fa.Digits(d.BaseSalary.ToString("#,0")));
                        HrPdf.KvRow(tb, "قالب قرارداد", tmplName ?? "—");
                    });
                    col.Item().PaddingTop(10).Text($"متن قرارداد{(tmplName == null ? "" : $" (قالب: {tmplName})")}").FontFamily(HrPdf.FontBold).FontSize(12);
                    if (paras.Count == 0)
                        col.Item().PaddingTop(4).Text("(متن قالب ثبت نشده است.)").FontSize(10).FontColor(QuestPDF.Helpers.Colors.Grey.Darken1);
                    foreach (var pa in paras)
                        col.Item().PaddingTop(3).Text(pa).FontSize(10);
                    if (!string.IsNullOrWhiteSpace(d.Description))
                    {
                        col.Item().PaddingTop(8).Text("توضیحات").FontFamily(HrPdf.FontBold).FontSize(12);
                        col.Item().PaddingTop(3).Text(d.Description).FontSize(10);
                    }
                    col.Item().PaddingTop(10).Row(r =>
                    {
                        r.Spacing(16);
                        foreach (var (role, name) in sigs)
                        {
                            r.RelativeItem().Column(c2 =>
                            {
                                c2.Item().PaddingTop(40).LineHorizontal(0.5f);
                                c2.Item().Text(role).FontSize(9).AlignCenter();
                                if (!string.IsNullOrWhiteSpace(name))
                                    c2.Item().Text(name).FontSize(8).FontColor(QuestPDF.Helpers.Colors.Grey.Darken1).AlignCenter();
                            });
                        }
                    });
                });
                pg.Footer().Column(col =>
                {
                    col.Item().LineHorizontal(1);
                    col.Item().Text($"تاریخ صدور: {PersianDate.ToShortFa(DateTime.Now)} • نسخه {Fa.Digits(verNo.ToString())} — این قرارداد به‌صورت سیستمی صادر شده است.")
                        .FontSize(8).FontColor(QuestPDF.Helpers.Colors.Grey.Darken1).AlignCenter();
                });
            });
        });
        return doc.GeneratePdf();
    }

    public async Task<byte[]> DecreePdfAsync(int id)
    {
        var list = await ListDecreesQuery(_db.HrDecrees.Where(d => d.Id == id));
        var d = list.FirstOrDefault() ?? throw new InvalidOperationException("حکم یافت نشد.");
        var emp = await _db.HrEmployees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == d.EmployeeId);
        var co = await _db.HrMainCompanies.AsNoTracking().FirstOrDefaultAsync();
        HrPdf.EnsureFonts();
        var logo = await HrPdf.TryLoadLogoAsync(_env.WebRootPath, co?.LogoPath);
        var typeName = HrCoreTexts.DecreeType(d.Type);
        var coLine = string.Join(" • ", new[] { co?.Address, co?.Phone }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var changes = new List<string>();
        if (!string.IsNullOrWhiteSpace(d.NewPostTitle)) changes.Add($"پست جدید: {d.NewPostTitle}");
        if (!string.IsNullOrWhiteSpace(d.NewOrgUnitName)) changes.Add($"واحد سازمانی جدید: {d.NewOrgUnitName}");
        if (d.NewBaseSalary is > 0) changes.Add($"حقوق پایه جدید: {Fa.Digits(d.NewBaseSalary.Value.ToString("#,0"))} ریال");
        if (d.NewStatus is >= 0) changes.Add($"وضعیت جدید: {HrCoreTexts.EmployeeStatus(d.NewStatus.Value)}");
        var status = d.IsApplied
            ? (d.AppliedAt == null ? "اجرا شده" : $"اجرا شده در {PersianDate.ToShortFa(d.AppliedAt.Value)}")
            : "در انتظار اجرا";
        var sigs = new List<(string Role, string? Name)>
        {
            ("امضای کارمند", d.EmployeeName),
            ("مدیر منابع انسانی", null),
            ("مدیرعامل", co?.ManagerName),
        };
        var doc = Document.Create(c =>
        {
            c.Page(pg =>
            {
                pg.Size(PageSizes.A4);
                pg.Margin(28);
                pg.ContentFromRightToLeft();
                pg.DefaultTextStyle(x => x.FontFamily(HrPdf.Font).FontSize(10));
                pg.Header().Column(col =>
                {
                    if (logo != null)
                    {
                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Column(c2 =>
                            {
                                c2.Item().Text(co?.Name ?? "").FontFamily(HrPdf.FontBold).FontSize(16).AlignCenter();
                                if (!string.IsNullOrWhiteSpace(coLine))
                                    c2.Item().Text(coLine).FontSize(8).FontColor(QuestPDF.Helpers.Colors.Grey.Darken1).AlignCenter();
                            });
                            r.ConstantItem(60).AlignMiddle().Image(logo).FitWidth();
                        });
                    }
                    else
                    {
                        col.Item().Text(co?.Name ?? "").FontFamily(HrPdf.FontBold).FontSize(16).AlignCenter();
                        if (!string.IsNullOrWhiteSpace(coLine))
                            col.Item().Text(coLine).FontSize(8).FontColor(QuestPDF.Helpers.Colors.Grey.Darken1).AlignCenter();
                    }
                    col.Item().PaddingTop(4).LineHorizontal(1);
                    col.Item().PaddingTop(6).Text("حکم کارگزینی").FontFamily(HrPdf.FontBold).FontSize(18).AlignCenter();
                    col.Item().Text($"نوع حکم: {typeName}").FontFamily(HrPdf.FontBold).FontSize(12).AlignCenter();
                    col.Item().PaddingTop(2).Text($"شماره حکم: {Fa.Digits(d.DecreeNo)} • اجرا از: {PersianDate.ToShortFa(d.EffectiveDate)} • وضعیت: {status}").FontSize(9).AlignCenter();
                    col.Item().PaddingTop(4).LineHorizontal(1);
                });
                pg.Content().Column(col =>
                {
                    col.Item().PaddingTop(8).Text("مشخصات پرسنل").FontFamily(HrPdf.FontBold).FontSize(12);
                    col.Item().PaddingTop(4).Table(tb =>
                    {
                        tb.ColumnsDefinition(cd => { cd.RelativeColumn(2); cd.RelativeColumn(3); });
                        HrPdf.KvRow(tb, "نام و نام خانوادگی", d.EmployeeName);
                        HrPdf.KvRow(tb, "کد پرسنلی", Fa.Digits(emp?.Code ?? "—"));
                        HrPdf.KvRow(tb, "کد ملی", Fa.Digits(emp?.NationalCode ?? "—"));
                    });
                    col.Item().PaddingTop(10).Text("متن حکم").FontFamily(HrPdf.FontBold).FontSize(12);
                    col.Item().PaddingTop(4).Text($"بدین‌وسیله {typeName} نامبرده از تاریخ {PersianDate.ToShortFa(d.EffectiveDate)} به شرح زیر اعلام می‌گردد:").FontSize(10);
                    foreach (var ch in changes)
                        col.Item().PaddingTop(2).Text($"• {ch}").FontSize(10);
                    if (!string.IsNullOrWhiteSpace(d.Description))
                        col.Item().PaddingTop(6).Text(d.Description).FontSize(10);
                    col.Item().PaddingTop(10).Row(r =>
                    {
                        r.Spacing(16);
                        foreach (var (role, name) in sigs)
                        {
                            r.RelativeItem().Column(c2 =>
                            {
                                c2.Item().PaddingTop(40).LineHorizontal(0.5f);
                                c2.Item().Text(role).FontSize(9).AlignCenter();
                                if (!string.IsNullOrWhiteSpace(name))
                                    c2.Item().Text(name).FontSize(8).FontColor(QuestPDF.Helpers.Colors.Grey.Darken1).AlignCenter();
                            });
                        }
                    });
                });
                pg.Footer().Column(col =>
                {
                    col.Item().LineHorizontal(1);
                    col.Item().Text($"تاریخ صدور: {PersianDate.ToShortFa(d.CreatedAt)} • صادرکننده: {d.CreatedByName ?? "—"} — این حکم به‌صورت سیستمی صادر شده است.")
                        .FontSize(8).FontColor(QuestPDF.Helpers.Colors.Grey.Darken1).AlignCenter();
                });
            });
        });
        return doc.GeneratePdf();
    }

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

    // ==================== پرونده کارمندان ====================

    private async Task EnsureEmployeeAsync(int employeeId)
    {
        if (!await _db.HrEmployees.AnyAsync(e => e.Id == employeeId))
            throw new InvalidOperationException("پرسنل یافت نشد.");
    }

    public async Task<HrEmployeeDossierDto> GetDossierAsync(int employeeId, int expiringDays = 30)
    {
        var emp = await GetEmployeeAsync(employeeId)
            ?? throw new InvalidOperationException("پرسنل یافت نشد.");
        var docs = await ListDocumentsQuery(
            _db.HrEmployeeDocuments.Where(d => d.EmployeeId == employeeId), expiringDays);
        return new HrEmployeeDossierDto
        {
            Employee = emp,
            Dependents = await ListDependentsAsync(employeeId),
            Courses = await ListCoursesAsync(employeeId),
            Skills = await ListSkillsAsync(employeeId),
            Languages = await ListLanguagesAsync(employeeId),
            Documents = docs,
            ExpiringDocuments = docs.Where(d => d.IsExpired || d.IsExpiringSoon).ToList()
        };
    }

    // ---------- تحت‌تکفل ----------

    public async Task<List<HrEmployeeDependentDto>> ListDependentsAsync(int employeeId)
    {
        await EnsureEmployeeAsync(employeeId);
        return await _db.HrEmployeeDependents.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId).OrderBy(x => x.Id)
            .Select(x => new HrEmployeeDependentDto
            {
                Id = x.Id, EmployeeId = x.EmployeeId, FullName = x.FullName, Relation = x.Relation,
                BirthDate = x.BirthDate, NationalCode = x.NationalCode, IsActive = x.IsActive
            }).ToListAsync();
    }

    public async Task<HrEmployeeDependentDto> SaveDependentAsync(int employeeId, int? id, HrEmployeeDependentSaveDto dto)
    {
        await EnsureEmployeeAsync(employeeId);
        if (string.IsNullOrWhiteSpace(dto.FullName)) throw new InvalidOperationException("نام و نام خانوادگی الزامی است.");
        if (string.IsNullOrWhiteSpace(dto.Relation)) throw new InvalidOperationException("نسبت الزامی است.");
        HrEmployeeDependent x;
        if (id is > 0)
        {
            x = await _db.HrEmployeeDependents.FirstOrDefaultAsync(v => v.Id == id.Value && v.EmployeeId == employeeId)
                ?? throw new InvalidOperationException("فرد تحت‌تکفل یافت نشد.");
        }
        else { x = new HrEmployeeDependent { EmployeeId = employeeId }; _db.HrEmployeeDependents.Add(x); }
        x.FullName = dto.FullName.Trim(); x.Relation = dto.Relation.Trim();
        x.BirthDate = dto.BirthDate; x.NationalCode = dto.NationalCode?.Trim(); x.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();
        return (await ListDependentsAsync(employeeId)).First(v => v.Id == x.Id);
    }

    public async Task DeleteDependentAsync(int id)
    {
        var x = await _db.HrEmployeeDependents.FirstOrDefaultAsync(v => v.Id == id)
            ?? throw new InvalidOperationException("فرد تحت‌تکفل یافت نشد.");
        _db.HrEmployeeDependents.Remove(x);
        await _db.SaveChangesAsync();
    }

    // ---------- دوره‌های آموزشی ----------

    public async Task<List<HrEmployeeCourseDto>> ListCoursesAsync(int employeeId)
    {
        await EnsureEmployeeAsync(employeeId);
        return await _db.HrEmployeeCourses.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId).OrderByDescending(x => x.Year).ThenBy(x => x.Id)
            .Select(x => new HrEmployeeCourseDto
            {
                Id = x.Id, EmployeeId = x.EmployeeId, Title = x.Title, Institute = x.Institute,
                Year = x.Year, DurationHours = x.DurationHours, HasCertificate = x.HasCertificate
            }).ToListAsync();
    }

    public async Task<HrEmployeeCourseDto> SaveCourseAsync(int employeeId, int? id, HrEmployeeCourseSaveDto dto)
    {
        await EnsureEmployeeAsync(employeeId);
        if (string.IsNullOrWhiteSpace(dto.Title)) throw new InvalidOperationException("عنوان دوره الزامی است.");
        HrEmployeeCourse x;
        if (id is > 0)
        {
            x = await _db.HrEmployeeCourses.FirstOrDefaultAsync(v => v.Id == id.Value && v.EmployeeId == employeeId)
                ?? throw new InvalidOperationException("دوره یافت نشد.");
        }
        else { x = new HrEmployeeCourse { EmployeeId = employeeId }; _db.HrEmployeeCourses.Add(x); }
        x.Title = dto.Title.Trim(); x.Institute = dto.Institute?.Trim();
        x.Year = dto.Year; x.DurationHours = dto.DurationHours; x.HasCertificate = dto.HasCertificate;
        await _db.SaveChangesAsync();
        return (await ListCoursesAsync(employeeId)).First(v => v.Id == x.Id);
    }

    public async Task DeleteCourseAsync(int id)
    {
        var x = await _db.HrEmployeeCourses.FirstOrDefaultAsync(v => v.Id == id)
            ?? throw new InvalidOperationException("دوره یافت نشد.");
        _db.HrEmployeeCourses.Remove(x);
        await _db.SaveChangesAsync();
    }

    // ---------- مهارت‌ها ----------

    public async Task<List<HrEmployeeSkillDto>> ListSkillsAsync(int employeeId)
    {
        await EnsureEmployeeAsync(employeeId);
        return await _db.HrEmployeeSkills.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId).OrderBy(x => x.Id)
            .Select(x => new HrEmployeeSkillDto
            {
                Id = x.Id, EmployeeId = x.EmployeeId, Title = x.Title, Level = (int)x.Level
            }).ToListAsync();
    }

    public async Task<HrEmployeeSkillDto> SaveSkillAsync(int employeeId, int? id, HrEmployeeSkillSaveDto dto)
    {
        await EnsureEmployeeAsync(employeeId);
        if (string.IsNullOrWhiteSpace(dto.Title)) throw new InvalidOperationException("عنوان مهارت الزامی است.");
        HrEmployeeSkill x;
        if (id is > 0)
        {
            x = await _db.HrEmployeeSkills.FirstOrDefaultAsync(v => v.Id == id.Value && v.EmployeeId == employeeId)
                ?? throw new InvalidOperationException("مهارت یافت نشد.");
        }
        else { x = new HrEmployeeSkill { EmployeeId = employeeId }; _db.HrEmployeeSkills.Add(x); }
        x.Title = dto.Title.Trim(); x.Level = (HrSkillLevel)Math.Clamp(dto.Level, 0, 3);
        await _db.SaveChangesAsync();
        return (await ListSkillsAsync(employeeId)).First(v => v.Id == x.Id);
    }

    public async Task DeleteSkillAsync(int id)
    {
        var x = await _db.HrEmployeeSkills.FirstOrDefaultAsync(v => v.Id == id)
            ?? throw new InvalidOperationException("مهارت یافت نشد.");
        _db.HrEmployeeSkills.Remove(x);
        await _db.SaveChangesAsync();
    }

    // ---------- زبان‌های خارجی ----------

    public async Task<List<HrEmployeeLanguageDto>> ListLanguagesAsync(int employeeId)
    {
        await EnsureEmployeeAsync(employeeId);
        return await _db.HrEmployeeLanguages.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId).OrderBy(x => x.Id)
            .Select(x => new HrEmployeeLanguageDto
            {
                Id = x.Id, EmployeeId = x.EmployeeId, Language = x.Language, Level = (int)x.Level
            }).ToListAsync();
    }

    public async Task<HrEmployeeLanguageDto> SaveLanguageAsync(int employeeId, int? id, HrEmployeeLanguageSaveDto dto)
    {
        await EnsureEmployeeAsync(employeeId);
        if (string.IsNullOrWhiteSpace(dto.Language)) throw new InvalidOperationException("نام زبان الزامی است.");
        HrEmployeeLanguage x;
        if (id is > 0)
        {
            x = await _db.HrEmployeeLanguages.FirstOrDefaultAsync(v => v.Id == id.Value && v.EmployeeId == employeeId)
                ?? throw new InvalidOperationException("زبان یافت نشد.");
        }
        else { x = new HrEmployeeLanguage { EmployeeId = employeeId }; _db.HrEmployeeLanguages.Add(x); }
        x.Language = dto.Language.Trim(); x.Level = (HrSkillLevel)Math.Clamp(dto.Level, 0, 3);
        await _db.SaveChangesAsync();
        return (await ListLanguagesAsync(employeeId)).First(v => v.Id == x.Id);
    }

    public async Task DeleteLanguageAsync(int id)
    {
        var x = await _db.HrEmployeeLanguages.FirstOrDefaultAsync(v => v.Id == id)
            ?? throw new InvalidOperationException("زبان یافت نشد.");
        _db.HrEmployeeLanguages.Remove(x);
        await _db.SaveChangesAsync();
    }

    // ---------- اسناد ----------

    public async Task<List<HrEmployeeDocumentDto>> ListDocumentsAsync(int employeeId, int expiringDays = 30)
    {
        await EnsureEmployeeAsync(employeeId);
        return await ListDocumentsQuery(_db.HrEmployeeDocuments.Where(d => d.EmployeeId == employeeId), expiringDays);
    }

    public async Task<HrEmployeeDocumentDto?> GetDocumentAsync(int id)
    {
        var list = await ListDocumentsQuery(_db.HrEmployeeDocuments.Where(d => d.Id == id), 30);
        return list.FirstOrDefault();
    }

    public async Task<HrEmployeeDocumentDto> SaveDocumentAsync(int employeeId, int? id, HrEmployeeDocumentSaveDto dto)
    {
        await EnsureEmployeeAsync(employeeId);
        if (string.IsNullOrWhiteSpace(dto.Title)) throw new InvalidOperationException("عنوان سند الزامی است.");
        if (dto.ExpiryDate != null && dto.IssueDate != null && dto.ExpiryDate < dto.IssueDate)
            throw new InvalidOperationException("تاریخ انقضا نمی‌تواند قبل از تاریخ صدور باشد.");
        HrEmployeeDocument x;
        if (id is > 0)
        {
            x = await _db.HrEmployeeDocuments.FirstOrDefaultAsync(v => v.Id == id.Value && v.EmployeeId == employeeId)
                ?? throw new InvalidOperationException("سند یافت نشد.");
        }
        else { x = new HrEmployeeDocument { EmployeeId = employeeId }; _db.HrEmployeeDocuments.Add(x); }
        x.Title = dto.Title.Trim(); x.DocType = (HrDocType)dto.DocType;
        x.IssueDate = dto.IssueDate; x.ExpiryDate = dto.ExpiryDate; x.Notes = dto.Notes?.Trim();
        await _db.SaveChangesAsync();
        return (await GetDocumentAsync(x.Id))!;
    }

    public async Task<HrEmployeeDocumentDto> AttachDocumentFileAsync(int id, string filePath, string fileName, string contentType, long fileSize)
    {
        var x = await _db.HrEmployeeDocuments.FirstOrDefaultAsync(v => v.Id == id)
            ?? throw new InvalidOperationException("سند یافت نشد.");
        x.FilePath = filePath; x.FileName = fileName; x.ContentType = contentType; x.FileSize = fileSize;
        await _db.SaveChangesAsync();
        return (await GetDocumentAsync(id))!;
    }

    public async Task DeleteDocumentAsync(int id)
    {
        var x = await _db.HrEmployeeDocuments.FirstOrDefaultAsync(v => v.Id == id)
            ?? throw new InvalidOperationException("سند یافت نشد.");
        _db.HrEmployeeDocuments.Remove(x);
        await _db.SaveChangesAsync();
    }

    public async Task<List<HrEmployeeDocumentDto>> ExpiringDocumentsAsync(int days)
    {
        days = Math.Clamp(days, 1, 365);
        var horizon = DateTime.Today.AddDays(days);
        var list = await ListDocumentsQuery(_db.HrEmployeeDocuments
            .Where(d => d.ExpiryDate != null && d.ExpiryDate.Value.Date <= horizon)
            .Join(_db.HrEmployees.Where(e => e.IsActive), d => d.EmployeeId, e => e.Id, (d, e) => d), days);
        return list;
    }

    private async Task<List<HrEmployeeDocumentDto>> ListDocumentsQuery(IQueryable<HrEmployeeDocument> q, int expiringDays)
    {
        expiringDays = Math.Clamp(expiringDays, 1, 365);
        var rows = await q.AsNoTracking().OrderByDescending(x => x.Id).Take(500).ToListAsync();
        var today = DateTime.Today;
        var empIds = rows.Select(r => r.EmployeeId).Distinct().ToList();
        var names = await _db.HrEmployees.Where(e => empIds.Contains(e.Id))
            .Select(e => new { e.Id, N = e.FirstName + " " + e.LastName })
            .ToDictionaryAsync(x => x.Id, x => x.N);
        var list = new List<HrEmployeeDocumentDto>();
        foreach (var x in rows)
        {
            int? diff = x.ExpiryDate == null ? null : (int)(x.ExpiryDate.Value.Date - today).TotalDays;
            list.Add(new HrEmployeeDocumentDto
            {
                Id = x.Id, EmployeeId = x.EmployeeId,
                EmployeeName = names.TryGetValue(x.EmployeeId, out var n) ? n : null,
                Title = x.Title, DocType = (int)x.DocType,
                FilePath = x.FilePath, FileName = x.FileName, ContentType = x.ContentType, FileSize = x.FileSize,
                IssueDate = x.IssueDate, ExpiryDate = x.ExpiryDate, Notes = x.Notes,
                DaysToExpiry = diff,
                IsExpired = diff != null && diff < 0,
                IsExpiringSoon = diff != null && diff >= 0 && diff <= expiringDays
            });
        }
        return list;
    }

    // ---------- عکس پروفایل ----------

    public async Task<HrEmployeeDto> SetEmployeePhotoAsync(int employeeId, string? photoPath)
    {
        var e = await _db.HrEmployees.FirstOrDefaultAsync(x => x.Id == employeeId)
            ?? throw new InvalidOperationException("پرسنل یافت نشد.");
        e.PhotoPath = string.IsNullOrWhiteSpace(photoPath) ? null : photoPath.Trim();
        e.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return (await GetEmployeeAsync(employeeId))!;
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

    // ==================== تمدید یک‌کلیکه / یادآور مدارک / چاپ پرونده / اکسل ====================

    /// <summary>تمدید یک‌کلیکه: قرارداد قبلی غیرفعال و نسخه جدید از فردای پایان ساخته می‌شود.</summary>
    public async Task<HrContractDto> RenewContractAsync(int id, int months)
    {
        months = Math.Clamp(months, 1, 60);
        var old = await _db.HrContracts.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("قرارداد یافت نشد.");
        if (old.EndDate == null) throw new InvalidOperationException("تمدید فقط برای قراردادهای مدت‌دار ممکن است.");
        var prefix = old.ContractNo.Trim() + "/R";
        var k = await _db.HrContracts.AsNoTracking()
            .Where(x => x.EmployeeId == old.EmployeeId && x.ContractNo.StartsWith(prefix)).CountAsync() + 1;
        var start = old.EndDate.Value.Date.AddDays(1);
        var c = new HrContract
        {
            EmployeeId = old.EmployeeId, ContractNo = prefix + k,
            Type = old.Type, StartDate = start, EndDate = start.AddMonths(months).AddDays(-1),
            BaseSalary = old.BaseSalary, JobTitle = old.JobTitle, OrgUnitId = old.OrgUnitId,
            Description = $"تمدید قرارداد {old.ContractNo}",
            IsActive = true, TemplateId = old.TemplateId, SignStatus = HrContractSignStatus.Draft
        };
        old.IsActive = false;
        _db.HrContracts.Add(c);
        await _db.SaveChangesAsync();
        return (await EmployeeContractsAsync(c.EmployeeId)).First(x => x.Id == c.Id);
    }

    /// <summary>یادآوری انقضای مدارک: برای هر پرسنلِ دارای مدرک نزدیک‌به‌انقضا یک اعلان درون‌سیستمی.</summary>
    public async Task<int> RemindExpiringDocumentsAsync(int days)
    {
        var docs = await ExpiringDocumentsAsync(days);
        if (docs.Count == 0) return 0;
        var empIds = docs.Select(d => d.EmployeeId).Distinct().ToList();
        var emps = await _db.HrEmployees.AsNoTracking().Where(e => empIds.Contains(e.Id)).ToListAsync();
        var n = 0;
        foreach (var g in docs.GroupBy(d => d.EmployeeId))
        {
            var e = emps.FirstOrDefault(x => x.Id == g.Key);
            if (e?.SystemUserId is not > 0) continue;
            var titles = string.Join("، ", g.Select(x => x.Title).Distinct().Take(3));
            var msg = $"مدارک زیر نزدیک به انقضاست: {titles}. لطفاً برای تمدید اقدام کنید.";
            try { await _notify.SendAsync(e.SystemUserId.Value, "یادآور انقضای مدارک", msg, "منابع انسانی", "HrCore", "hr-core/profile"); n++; }
            catch { }
        }
        try { await _notify.BroadcastChangedAsync("hr-core"); } catch { }
        return n;
    }

    /// <summary>چاپ پرونده پرسنل (PDF یک‌جا: مشخصات + خانواده + دوره‌ها + مهارت‌ها + زبان‌ها + مدارک).</summary>
    public async Task<byte[]> DossierPdfAsync(int employeeId)
    {
        var d = await GetDossierAsync(employeeId, 365);
        var e = d.Employee;
        var co = await _db.HrMainCompanies.AsNoTracking().FirstOrDefaultAsync();
        HrPdf.EnsureFonts();
        var logo = await HrPdf.TryLoadLogoAsync(_env.WebRootPath, co?.LogoPath);
        var hire = PersianDate.ToShortFa(e.HireDate);
        var contracts = await EmployeeContractsAsync(employeeId);
        var decrees = await EmployeeDecreesAsync(employeeId);
        var doc = Document.Create(c =>
        {
            c.Page(pg =>
            {
                pg.Size(PageSizes.A4);
                pg.Margin(28);
                pg.ContentFromRightToLeft();
                pg.DefaultTextStyle(x => x.FontFamily(HrPdf.Font).FontSize(10));
                pg.Header().Column(col =>
                {
                    col.Item().Text(co?.Name ?? "").FontFamily(HrPdf.FontBold).FontSize(15).AlignCenter();
                    col.Item().PaddingTop(2).Text($"پرونده پرسنلی — {e.FirstName} {e.LastName} ({Fa.Digits(e.Code)})")
                        .FontFamily(HrPdf.FontBold).FontSize(14).AlignCenter();
                    col.Item().PaddingTop(2).Text($"کدملی: {Fa.Digits(e.NationalCode ?? "—")} • استخدام: {hire} • وضعیت: {(e.IsActive ? "فعال" : "غیرفعال")}")
                        .FontSize(9).AlignCenter();
                    col.Item().PaddingTop(4).LineHorizontal(1);
                });
                pg.Content().Column(col =>
                {
                    col.Item().PaddingTop(6).Text("مشخصات فردی").FontFamily(HrPdf.FontBold).FontSize(12);
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(cd => { cd.RelativeColumn(); cd.RelativeColumn(); cd.RelativeColumn(); cd.RelativeColumn(); });
                        HrPdf.KvRow(t, "سمت", e.PostTitle ?? e.HrMainNodeName ?? "—");
                        HrPdf.KvRow(t, "تاریخ تولد", e.BirthDate == null ? "—" : PersianDate.ToShortFa(e.BirthDate.Value));
                        HrPdf.KvRow(t, "موبایل", Fa.Digits(e.Mobile ?? "—"));
                        HrPdf.KvRow(t, "مدرک", string.IsNullOrWhiteSpace(e.Degree) ? "—" : e.Degree + (string.IsNullOrWhiteSpace(e.FieldOfStudy) ? "" : " — " + e.FieldOfStudy));
                    });
                    if (d.Dependents.Count > 0)
                    {
                        col.Item().PaddingTop(8).Text("افراد تحت تکفل").FontFamily(HrPdf.FontBold).FontSize(12);
                        col.Item().Table(t =>
                        {
                            t.ColumnsDefinition(cd => { cd.RelativeColumn(2); cd.RelativeColumn(); cd.RelativeColumn(); });
                            t.Cell().Text("نام").FontFamily(HrPdf.FontBold).FontSize(9);
                            t.Cell().Text("نسبت").FontFamily(HrPdf.FontBold).FontSize(9);
                            t.Cell().Text("کدملی").FontFamily(HrPdf.FontBold).FontSize(9);
                            foreach (var x in d.Dependents)
                            {
                                t.Cell().Text(x.FullName).FontSize(9);
                                t.Cell().Text(x.Relation ?? "—").FontSize(9);
                                t.Cell().Text(Fa.Digits(x.NationalCode ?? "—")).FontSize(9);
                            }
                        });
                    }
                    if (d.Courses.Count > 0)
                    {
                        col.Item().PaddingTop(8).Text("دوره‌های آموزشی").FontFamily(HrPdf.FontBold).FontSize(12);
                        foreach (var x in d.Courses)
                            col.Item().Text($"• {x.Title}" + (x.Institute == null ? "" : $" — {x.Institute}") + (x.Year == null ? "" : $" ({Fa.Digits(x.Year.Value.ToString())})")).FontSize(9);
                    }
                    if (d.Skills.Count > 0)
                        col.Item().PaddingTop(6).Text("مهارت‌ها: " + string.Join("، ", d.Skills.Select(x => x.Title))).FontSize(9);
                    if (d.Languages.Count > 0)
                        col.Item().PaddingTop(2).Text("زبان‌ها: " + string.Join("، ", d.Languages.Select(x => x.Language + $" ({HrCoreTexts.SkillLevel(x.Level)})"))).FontSize(9);
                    if (d.Documents.Count > 0)
                    {
                        col.Item().PaddingTop(8).Text("مدارک").FontFamily(HrPdf.FontBold).FontSize(12);
                        col.Item().Table(t =>
                        {
                            t.ColumnsDefinition(cd => { cd.RelativeColumn(2); cd.RelativeColumn(); cd.RelativeColumn(); });
                            t.Cell().Text("عنوان").FontFamily(HrPdf.FontBold).FontSize(9);
                            t.Cell().Text("انقضا").FontFamily(HrPdf.FontBold).FontSize(9);
                            t.Cell().Text("وضعیت").FontFamily(HrPdf.FontBold).FontSize(9);
                            foreach (var x in d.Documents)
                            {
                                t.Cell().Text(x.Title).FontSize(9);
                                t.Cell().Text(x.ExpiryDate == null ? "—" : PersianDate.ToShortFa(x.ExpiryDate.Value)).FontSize(9);
                                t.Cell().Text(x.IsExpired ? "منقضی" : x.IsExpiringSoon ? "نزدیک انقضا" : "معتبر").FontSize(9);
                            }
                        });
                    }
                    if (contracts.Count > 0)
                    {
                        col.Item().PaddingTop(8).Text("قراردادها").FontFamily(HrPdf.FontBold).FontSize(12);
                        foreach (var x in contracts.Take(10))
                        {
                            var end = x.EndDate == null ? "دائمی" : PersianDate.ToShortFa(x.EndDate.Value);
                            col.Item().Text($"• {Fa.Digits(x.ContractNo)} — {HrCoreTexts.EmploymentType(x.Type)} — {PersianDate.ToShortFa(x.StartDate)} تا {end}{(x.IsActive ? " (فعال)" : "")}").FontSize(9);
                        }
                    }
                    if (decrees.Count > 0)
                    {
                        col.Item().PaddingTop(8).Text("احکام").FontFamily(HrPdf.FontBold).FontSize(12);
                        foreach (var x in decrees.Take(10))
                            col.Item().Text($"• {Fa.Digits(x.DecreeNo)} — اجرا: {PersianDate.ToShortFa(x.EffectiveDate)}{(x.IsApplied ? "" : " (اعمال‌نشده)")}").FontSize(9);
                    }
                });
                pg.Footer().AlignCenter().Text(x =>
                {
                    x.Span("صفحه ").FontSize(8);
                    x.CurrentPageNumber().FontSize(8);
                });
            });
        });
        return doc.GeneratePdf();
    }

    private static XLWorkbook NewWorkbook(string sheet)
    {
        var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sheet);
        ws.RightToLeft = true;
        return wb;
    }

    private static byte[] WorkbookBytes(XLWorkbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<(byte[] Data, string FileName)> ExportEmployeesExcelAsync(string? q)
    {
        var (items, _) = await SearchEmployeesAsync(q, null, null, 0, 10000);
        using var wb = NewWorkbook("پرسنل");
        var ws = wb.Worksheet(1);
        string[] heads = { "کد", "نام", "نام خانوادگی", "کدملی", "موبایل", "استخدام", "وضعیت" };
        for (var i = 0; i < heads.Length; i++) ws.Cell(1, i + 1).Value = heads[i];
        var r = 2;
        foreach (var e in items)
        {
            ws.Cell(r, 1).Value = e.Code;
            ws.Cell(r, 2).Value = e.FirstName;
            ws.Cell(r, 3).Value = e.LastName;
            ws.Cell(r, 4).Value = e.NationalCode ?? "";
            ws.Cell(r, 5).Value = e.Mobile ?? "";
            ws.Cell(r, 6).Value = PersianDate.ToShortFa(e.HireDate);
            ws.Cell(r, 7).Value = e.IsActive ? "فعال" : "غیرفعال";
            r++;
        }
        ws.Columns().AdjustToContents();
        return (WorkbookBytes(wb), "employees.xlsx");
    }

    public async Task<(byte[] Data, string FileName)> ExportContractsExcelAsync()
    {
        var items = await ListContractsAsync(null);
        using var wb = NewWorkbook("قراردادها");
        var ws = wb.Worksheet(1);
        string[] heads = { "پرسنل", "شماره", "نوع", "شروع", "پایان", "حقوق پایه", "سمت", "فعال" };
        for (var i = 0; i < heads.Length; i++) ws.Cell(1, i + 1).Value = heads[i];
        var r = 2;
        foreach (var x in items)
        {
            ws.Cell(r, 1).Value = x.EmployeeName;
            ws.Cell(r, 2).Value = x.ContractNo;
            ws.Cell(r, 3).Value = HrCoreTexts.EmploymentType(x.Type);
            ws.Cell(r, 4).Value = PersianDate.ToShortFa(x.StartDate);
            ws.Cell(r, 5).Value = x.EndDate == null ? "دائمی" : PersianDate.ToShortFa(x.EndDate.Value);
            ws.Cell(r, 6).Value = (double)x.BaseSalary;
            ws.Cell(r, 7).Value = x.JobTitle ?? "";
            ws.Cell(r, 8).Value = x.IsActive ? "بله" : "خیر";
            r++;
        }
        ws.Columns().AdjustToContents();
        return (WorkbookBytes(wb), "contracts.xlsx");
    }

    public async Task<(byte[] Data, string FileName)> ExportDecreesExcelAsync()
    {
        var items = await ListDecreesAsync(null, null);
        using var wb = NewWorkbook("احکام");
        var ws = wb.Worksheet(1);
        string[] heads = { "پرسنل", "شماره حکم", "اجرا", "سمت جدید", "واحد جدید", "حقوق جدید", "اعمال‌شده" };
        for (var i = 0; i < heads.Length; i++) ws.Cell(1, i + 1).Value = heads[i];
        var r = 2;
        foreach (var x in items)
        {
            ws.Cell(r, 1).Value = x.EmployeeName;
            ws.Cell(r, 2).Value = x.DecreeNo;
            ws.Cell(r, 3).Value = PersianDate.ToShortFa(x.EffectiveDate);
            ws.Cell(r, 4).Value = x.NewPostTitle ?? "";
            ws.Cell(r, 5).Value = x.NewOrgUnitName ?? "";
            ws.Cell(r, 6).Value = x.NewBaseSalary == null ? "" : ((double)x.NewBaseSalary.Value).ToString();
            ws.Cell(r, 7).Value = x.IsApplied ? "بله" : "خیر";
            r++;
        }
        ws.Columns().AdjustToContents();
        return (WorkbookBytes(wb), "decrees.xlsx");
    }

    public async Task<(byte[] Data, string FileName)> ExportOrgExcelAsync()
    {
        var tree = await GetTreeAsync();
        using var wb = NewWorkbook("چارت سازمانی");
        var ws = wb.Worksheet(1);
        string[] heads = { "سطح", "کد", "نام واحد", "نوع", "بالادستی", "مدیر", "تعداد پرسنل" };
        for (var i = 0; i < heads.Length; i++) ws.Cell(1, i + 1).Value = heads[i];
        var r = 2;
        void Walk(List<HrOrgUnitDto> list, int level)
        {
            foreach (var x in list)
            {
                ws.Cell(r, 1).Value = level;
                ws.Cell(r, 2).Value = x.Code;
                ws.Cell(r, 3).Value = x.Name;
                ws.Cell(r, 4).Value = HrCoreTexts.OrgUnitType(x.Type);
                ws.Cell(r, 5).Value = x.ParentName ?? "";
                ws.Cell(r, 6).Value = x.ManagerName ?? "";
                ws.Cell(r, 7).Value = x.EmployeeCount;
                r++;
                if (x.Children.Count > 0) Walk(x.Children, level + 1);
            }
        }
        Walk(tree, 1);
        ws.Columns().AdjustToContents();
        return (WorkbookBytes(wb), "org-chart.xlsx");
    }

    // ==================== تاریخچه عملیات HR (خواندن از لاگ سراسری) ====================

    private static readonly string[] HrModules = { "HrCore", "HrTalent", "FaAtt", "FaPay", "FaLms", "FaCom" };

    public async Task<HrAuditListResult> SearchHrAuditAsync(string? module, string? action, string? q,
        DateTime? from, DateTime? to, int skip, int take)
    {
        take = Math.Clamp(take, 1, 100);
        var query = _db.AuditLogs.AsNoTracking().Where(l => HrModules.Contains(l.Module));
        if (!string.IsNullOrWhiteSpace(module)) query = query.Where(l => l.Module == module);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(l => l.Action == action);
        if (from != null) query = query.Where(l => l.At >= from.Value.Date);
        if (to != null) query = query.Where(l => l.At < to.Value.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(l =>
                (l.Username != null && l.Username.Contains(term)) ||
                (l.Summary != null && l.Summary.Contains(term)) ||
                (l.Path != null && l.Path.Contains(term)));
        }
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(l => l.At).ThenByDescending(l => l.Id)
            .Skip(skip).Take(take)
            .Select(l => new HrAuditLogDto
            {
                Id = l.Id, At = l.At, UserId = l.UserId, Username = l.Username,
                Module = l.Module, Action = l.Action, HttpMethod = l.HttpMethod,
                Path = l.Path, Summary = l.Summary, StatusCode = l.StatusCode, DurationMs = l.DurationMs
            }).ToListAsync();
        return new HrAuditListResult { Total = total, Items = items };
    }

    public async Task<HrAuditLogDto?> GetHrAuditAsync(long id)
    {
        var l = await _db.AuditLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && HrModules.Contains(x.Module));
        if (l == null) return null;
        return new HrAuditLogDto
        {
            Id = l.Id, At = l.At, UserId = l.UserId, Username = l.Username,
            Module = l.Module, Action = l.Action, HttpMethod = l.HttpMethod,
            Path = l.Path, Summary = l.Summary, Payload = l.Payload, Ip = l.Ip,
            StatusCode = l.StatusCode, DurationMs = l.DurationMs
        };
    }

    // ==================== داشبورد یکپارچه مدیر ====================

    public async Task<HrManagerDashboardDto> GetManagerDashboardAsync(int year)
    {
        var pc = new System.Globalization.PersianCalendar();
        if (year is < 1300 or > 1500) year = pc.GetYear(DateTime.Today);
        var from = pc.ToDateTime(year, 1, 1, 0, 0, 0, 0);
        var to = pc.ToDateTime(year + 1, 1, 1, 0, 0, 0, 0);
        int Jm(DateTime d) { try { return pc.GetMonth(d); } catch { return 1; } }

        var active = await _db.HrEmployees.AsNoTracking().CountAsync(e => e.IsActive);
        var hires = await _db.HrEmployees.AsNoTracking()
            .CountAsync(e => e.HireDate >= from && e.HireDate < to);
        var exits = await _db.HrExitCases.AsNoTracking()
            .Where(x => x.Status == HrTalentCaseStatus.Completed
                && (x.CompletedAt ?? x.RequestDate) >= from && (x.CompletedAt ?? x.RequestDate) < to)
            .ToListAsync();
        var avgHead = Math.Max(1, active + exits.Count / 2.0);

        var leaves = await _db.FaAttLeaves.AsNoTracking()
            .Where(l => l.Status == FaAttRequestStatus.Approved && l.FromDate >= from && l.FromDate < to)
            .Take(20000).ToListAsync();
        double LeaveDays(FaAttLeave l) => l.HoursPerDay != null
            ? l.HoursPerDay.Value / 8.0 * Math.Max(1, (l.ToDate.Date - l.FromDate.Date).Days + 1)
            : Math.Max(1, (l.ToDate.Date - l.FromDate.Date).Days + 1);
        var totalLeaveDays = leaves.Sum(LeaveDays);
        var typeIds = leaves.Select(l => l.LeaveTypeId).Distinct().ToList();
        var typeNames = typeIds.Count == 0 ? new Dictionary<int, string>()
            : await _db.FaAttLeaveTypes.AsNoTracking().Where(x => typeIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name);

        var runs = await _db.FaPayRuns.AsNoTracking()
            .Where(r => r.Year == year && r.Kind == FaPayRunKind.Monthly)
            .OrderBy(r => r.Month).ToListAsync();
        var runIds = runs.Select(r => r.Id).ToList();
        var slips = runIds.Count == 0 ? new List<FaPaySlip>()
            : await _db.FaPaySlips.AsNoTracking().Where(s => runIds.Contains(s.RunId)).Take(60000).ToListAsync();
        var lastRun = runs.OrderByDescending(r => r.Month).FirstOrDefault();
        var lastSlips = lastRun == null ? new List<FaPaySlip>() : slips.Where(s => s.RunId == lastRun.Id).ToList();

        var units = await _db.HrEmployees.AsNoTracking().Where(e => e.IsActive && e.OrgUnitId != null)
            .GroupBy(e => e.OrgUnitId!.Value).Select(g => new { Id = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).Take(8).ToListAsync();
        var unitIds = units.Select(u => u.Id).ToList();
        var unitNames = unitIds.Count == 0 ? new Dictionary<int, string>()
            : await _db.HrOrgUnits.AsNoTracking().Where(u => unitIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Name);

        var dto = new HrManagerDashboardDto
        {
            Year = year,
            ActiveEmployees = active,
            HiresThisYear = hires,
            ExitsThisYear = exits.Count,
            TurnoverRate = Math.Round(exits.Count / avgHead * 100, 1),
            AbsenceRate = active == 0 ? 0 : Math.Round(totalLeaveDays / (active * 260.0) * 100, 1),
            AvgLeaveDaysPerEmp = active == 0 ? 0 : Math.Round(totalLeaveDays / active, 1),
            AvgCostPerHead = lastSlips.Count == 0 ? null : Math.Round(lastSlips.Average(s => s.GrossEarnings)),
            TotalPayrollLastRun = lastSlips.Count == 0 ? null : Math.Round(lastSlips.Sum(s => s.GrossEarnings)),
            LastRunLabel = lastRun == null ? null : $"{year}/{lastRun.Month:00}",
            LeaveByType = leaves.GroupBy(l => l.LeaveTypeId)
                .Select(g => new HrNameValueDto
                {
                    Name = typeNames.TryGetValue(g.Key, out var n) ? n : "—",
                    Value = Math.Round(g.Sum(LeaveDays), 1)
                }).OrderByDescending(x => x.Value).Take(8).ToList(),
            HeadcountByUnit = units.Select(u => new HrNameValueDto
            {
                Name = unitNames.TryGetValue(u.Id, out var n) ? n : "—",
                Value = u.Count
            }).ToList()
        };
        for (var m = 1; m <= 12; m++)
        {
            dto.LeaveTrend.Add(new HrMonthPointDto
                { Month = m, Value = Math.Round(leaves.Where(l => Jm(l.FromDate) == m).Sum(LeaveDays), 1) });
            var monthRunIds = runs.Where(r => r.Month == m).Select(r => r.Id).ToHashSet();
            var ms = slips.Where(s => monthRunIds.Contains(s.RunId)).ToList();
            dto.OvertimeTrend.Add(new HrMonthPointDto
                { Month = m, Value = Math.Round(ms.Sum(s => s.OvertimeMinutes) / 60.0, 1) });
            dto.PayrollTrend.Add(new HrMonthPointDto
                { Month = m, Value = Math.Round(ms.Sum(s => s.GrossEarnings)) });
        }
        return dto;
    }
}
