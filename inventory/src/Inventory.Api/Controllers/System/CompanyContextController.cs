using System.Security.Claims;
using Inventory.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/company-context")]
public sealed class CompanyContextController : ControllerBase
{
    private readonly AppDbContext _db;
    public CompanyContextController(AppDbContext db) => _db = db;

    private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    private bool IsAdmin => User.IsInRole("Admin");

    [HttpGet]
    public async Task<IActionResult> Get([FromHeader(Name = "X-Company-Id")] int? requested)
    {
        var access = _db.UserCompanyAccesses.AsNoTracking().Where(x => x.UserId == UserId && x.Company.IsActive);
        if (!await access.AnyAsync())
        {
            var username = User.FindFirstValue(ClaimTypes.Name) ?? "";
            var legacyCompany = await _db.SystemUsers.AsNoTracking().Where(u => u.Username == username && u.CompanyId != null).Select(u => u.CompanyId!.Value).FirstOrDefaultAsync();
            if (legacyCompany > 0)
                access = _db.SystemCompanies.AsNoTracking().Where(c => c.Id == legacyCompany && c.IsActive).Select(c => new UserCompanyAccess { UserId = UserId, CompanyId = c.Id, Company = c });
        }
        if (IsAdmin && !await access.AnyAsync())
            return Ok(new { activeCompanyId = requested, companies = await _db.SystemCompanies.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync() });
        var companies = await access.OrderBy(x => x.Company.Name).Select(x => x.Company).ToListAsync();
        var active = requested is > 0 && companies.Any(c => c.Id == requested) ? requested : companies.FirstOrDefault()?.Id;
        return Ok(new { activeCompanyId = active, companies });
    }

    [HttpPut("users/{userId:int}")]
    public async Task<IActionResult> SetUserCompanies(int userId, [FromBody] CompanyAssignmentDto dto)
    {
        if (!IsAdmin) return Forbid();
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return NotFound(new { message = "کاربر پیدا نشد." });
        var ids = dto.CompanyIds.Distinct().Where(x => x > 0).ToList();
        var valid = await _db.SystemCompanies.Where(c => ids.Contains(c.Id) && c.IsActive).Select(c => c.Id).ToListAsync();
        if (valid.Count != ids.Count) return BadRequest(new { message = "یکی از شرکت‌های انتخاب‌شده معتبر یا فعال نیست." });
        var old = await _db.UserCompanyAccesses.Where(x => x.UserId == userId).ToListAsync();
        _db.UserCompanyAccesses.RemoveRange(old);
        _db.UserCompanyAccesses.AddRange(valid.Select((id, i) => new UserCompanyAccess { UserId = userId, CompanyId = id, IsDefault = i == 0 }));
        await _db.SaveChangesAsync();
        return Ok(new { count = valid.Count });
    }

    [HttpGet("users/{userId:int}")]
    public async Task<IActionResult> GetUserCompanies(int userId)
    {
        if (!IsAdmin && userId != UserId) return Forbid();
        return Ok(await _db.UserCompanyAccesses.AsNoTracking().Where(x => x.UserId == userId).Select(x => x.CompanyId).ToListAsync());
    }

    public sealed class CompanyAssignmentDto { public List<int> CompanyIds { get; set; } = new(); }
}
