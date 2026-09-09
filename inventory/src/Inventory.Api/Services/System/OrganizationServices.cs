using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

/// <summary>
/// سرویس سازمان‌ها — تأمین جزء «واحد» در شماره اندیکاتور نامه‌ها.
/// (پورت OrganizationService کارفرما با متد GetOrganizationNameUniqAsync)
/// </summary>
public interface IOrganizationServices
{
    /// <summary>
    /// نام اختصاصی سازمان (NameUniq) برای یک سمت مشخص.
    /// اگر سمت وجود نداشته باشد (یا چارت سازمانی هنوز فعال نشده)،
    /// از سازمانِ پیش‌فرض (IsDefault) و سپس اولین سازمانِ فعال خوانده می‌شود.
    /// </summary>
    Task<string> GetOrganizationNameUniqAsync(int? sematId = null);

    /// <summary>فهرست سازمان‌های فعال (برای انتخاب در تنظیمات)</summary>
    Task<List<OrganizationDto>> GetOrganizationsAsync();

    /// <summary>شناسه سازمان پیش‌فرض — یا null اگر تعریف نشده</summary>
    Task<int?> GetDefaultOrganizationIdAsync();
}

public class OrganizationServices : IOrganizationServices
{
    private readonly AppDbContext _db;

    public OrganizationServices(AppDbContext db) => _db = db;

    public async Task<string> GetOrganizationNameUniqAsync(int? sematId = null)
    {
        // ۱) اگر سمت (Semat) مشخص است → سازمانِ همان سمت
        if (sematId is > 0)
        {
            var name = await _db.Semats.AsNoTracking()
                .Where(s => s.SematId == sematId && !s.IsDelete && s.IsActive
                            && s.Organization != null && !s.Organization.IsDelete && s.Organization.IsActive)
                .Select(s => s.Organization!.NameUniq)
                .FirstOrDefaultAsync();
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }

        // ۲) سازمانِ پیش‌فرض (وقتی صادرکننده سمت ندارد — وضعیت فعلی پروژه)
        var def = await _db.Organizations.AsNoTracking()
            .Where(o => !o.IsDelete && o.IsActive && o.IsDefault)
            .Select(o => o.NameUniq)
            .FirstOrDefaultAsync();
        if (!string.IsNullOrWhiteSpace(def)) return def;

        // ۳) اولین سازمان فعال به‌عنوان آخرین fallback
        return await _db.Organizations.AsNoTracking()
                   .Where(o => !o.IsDelete && o.IsActive)
                   .Select(o => o.NameUniq)
                   .FirstOrDefaultAsync() ?? "";
    }

    public async Task<List<OrganizationDto>> GetOrganizationsAsync() =>
        await _db.Organizations.AsNoTracking()
            .Where(o => !o.IsDelete && o.IsActive)
            .OrderBy(o => o.NameUnit)
            .Select(o => new OrganizationDto
            {
                OrganizationId = o.OrganizationId,
                NameUnit = o.NameUnit,
                NameUniq = o.NameUniq,
                IsDefault = o.IsDefault
            })
            .ToListAsync();

    public async Task<int?> GetDefaultOrganizationIdAsync()
    {
        var id = await _db.Organizations.AsNoTracking()
            .Where(o => !o.IsDelete && o.IsActive && o.IsDefault)
            .Select(o => (int?)o.OrganizationId)
            .FirstOrDefaultAsync();
        if (id is > 0) return id;

        // اگر هنوز پیش‌فرض تعیین نشده، اولین سازمان فعال مبناست
        return await _db.Organizations.AsNoTracking()
            .Where(o => !o.IsDelete && o.IsActive)
            .Select(o => (int?)o.OrganizationId)
            .FirstOrDefaultAsync();
    }
}
