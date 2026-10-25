using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Shared.Dtos;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers;

/// <summary>مدیریت سازمان‌ها / واحدها — برای تخصیص به کاربران و جزء «واحد» در شماره نامه</summary>
[ApiController]
[Route("api/[controller]")]
public class SystemOrganizationsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IOrganizationServices _org;

    public SystemOrganizationsController(AppDbContext db, IOrganizationServices org)
    {
        _db = db;
        _org = org;
    }

    /// <summary>فهرست واحدها — می‌تواند بر اساس شرکت فیلتر شود (companyId)</summary>
    [HttpGet]
    public async Task<ActionResult> Get([FromQuery] int? companyId = null, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        IQueryable<Organization> query = _db.Organizations.Where(o => !o.IsDelete);
        if (companyId is > 0)
            query = query.Where(o => o.CompanyId == companyId);
        return Ok(await Paging.ResultAsync(query.OrderBy(o => o.NameUnit), skip, take));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult> Get(int id)
    {
        var org = await _db.Organizations.FirstOrDefaultAsync(o => o.OrganizationId == id);
        if (org == null) return NotFound(new { message = "سازمان پیدا نشد." });
        return Ok(org);
    }

    [HttpPost]
    public async Task<ActionResult> Post([FromBody] Organization o)
    {
        o.OrganizationId = 0;
        if (o.CreatedAt == default) o.CreatedAt = DateTime.Now;
        _db.Organizations.Add(o);
        await _db.SaveChangesAsync();
        return Ok(new { id = o.OrganizationId });
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult> Put(int id, [FromBody] Organization o)
    {
        var item = await _db.Organizations.FirstOrDefaultAsync(x => x.OrganizationId == id);
        if (item == null) return NotFound(new { message = "سازمان پیدا نشد." });

        item.NameUnit = o.NameUnit;
        item.NameUniq = o.NameUniq;
        item.CompanyId = o.CompanyId;
        item.IsDefault = o.IsDefault;
        item.IsActive = o.IsActive;
        item.IsDelete = o.IsDelete;

        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id)
    {
        var item = await _db.Organizations.FirstOrDefaultAsync(x => x.OrganizationId == id);
        if (item == null) return NotFound(new { message = "سازمان پیدا نشد." });

        _db.Organizations.Remove(item);
        await _db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>فهرست سازمان‌های فعال برای د virgehenden انتخاب (combeobox) — می‌تواند بر اساس شرکت فیلتر شود</summary>
    [HttpGet("active")]
    public async Task<ActionResult<List<OrganizationDto>>> GetActive([FromQuery] int? companyId = null)
    {
        var orgs = await _db.Organizations.AsNoTracking()
            .Where(o => !o.IsDelete && o.IsActive)
            .OrderBy(o => o.NameUnit)
            .ToListAsync();

        if (companyId is > 0)
            orgs = orgs.Where(o => o.CompanyId == companyId).ToList();

        return Ok(orgs.Select(o => new OrganizationDto
        {
            OrganizationId = o.OrganizationId,
            CompanyId = o.CompanyId,
            NameUnit = o.NameUnit,
            NameUniq = o.NameUniq,
            IsDefault = o.IsDefault
        }).ToList());
    }
}
