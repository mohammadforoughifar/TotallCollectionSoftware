using System.Text.Json;
using Inventory.Api.Data;
using Inventory.Api.Services.Core;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;
[ApiController, Authorize, Route("api/shop-print-settings")]
public class ShopPrintSettingsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IEffectivePermissions _permissions;
    public ShopPrintSettingsController(AppDbContext db, IEffectivePermissions permissions) { _db = db; _permissions = permissions; }
    private async Task<bool> Allowed(bool write)
    {
        if (await _permissions.IsLegacyAdminAsync(User)) return true;
        var permissions = await _permissions.GetAsync(User);
        return permissions.Contains("Shop.PrintSettings.Manage") || !write &&
            new[] { "Shop.PrintSettings.View", "Shop.Sale.View", "Shop.Sale.Manage", "Shop.Purchase.View", "Shop.Purchase.Manage", "Shop.Repairs.View", "Shop.Repairs.Manage" }.Any(permissions.Contains);
    }
    [HttpGet]
    public async Task<ActionResult<ShopPrintSettingsDto>> Get()
    {
        if (!await Allowed(false)) return Forbid();
        if (_db.ActiveCompanyId is not > 0) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var row = await _db.ShopPrintSettings.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == _db.ActiveCompanyId);
        return Ok(new ShopPrintSettingsDto { Banks = row is null ? new() : JsonSerializer.Deserialize<List<ShopPrintBankDto>>(row.BanksJson) ?? new() });
    }
    [HttpPut]
    public async Task<ActionResult<ShopPrintSettingsDto>> Save(ShopPrintSettingsDto dto)
    {
        if (!await Allowed(true)) return Forbid();
        if (_db.ActiveCompanyId is not > 0) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        ShopPrintSettingsDto normalized;
        try { normalized = ShopPrintBankRules.Validate(dto); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        var row = await _db.ShopPrintSettings.SingleOrDefaultAsync(x => x.CompanyId == _db.ActiveCompanyId);
        if (row is null) { row = new() { CompanyId = _db.ActiveCompanyId }; _db.ShopPrintSettings.Add(row); }
        row.BanksJson = JsonSerializer.Serialize(normalized.Banks);
        await _db.SaveChangesAsync();
        return Ok(normalized);
    }

    // Unlike print data, visitor limits are visible only to shop settings administrators/viewers.
    private async Task<bool> CanAccessVisitorSettings(bool write)
    {
        if (await _permissions.IsLegacyAdminAsync(User)) return true;
        var p = await _permissions.GetAsync(User);
        return p.Contains("Shop.PrintSettings.Manage") || (!write && p.Contains("Shop.PrintSettings.View"));
    }

    private IQueryable<User> ActiveVisitors(int companyId)
    {
        // Company assignments are authoritative; legacy SystemUsers is used only when no assignments exist.
        return _db.Users.AsNoTracking().Where(u => u.IsActive &&
            (_db.UserCompanyAccesses.Any(a => a.UserId == u.Id && a.CompanyId == companyId) ||
             (!_db.UserCompanyAccesses.Any(a => a.UserId == u.Id) &&
              _db.SystemUsers.Any(s => s.Username == u.Username && s.IsActive && s.CompanyId == companyId))) &&
            _db.UserRoles.Any(ur => ur.UserId == u.Id &&
                _db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive) &&
                _db.RolePermissions.Any(rp => rp.RoleId == ur.RoleId &&
                    _db.Permissions.Any(p => p.Id == rp.PermissionId && p.Module == "SalesVisit" && p.Action == "Create"))));
    }

    [HttpGet("visitors")]
    public async Task<ActionResult<List<ShopVisitorDiscountDto>>> GetVisitors()
    {
        if (!await CanAccessVisitorSettings(false)) return Forbid();
        if (_db.ActiveCompanyId is not > 0) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var companyId = _db.ActiveCompanyId.Value;
        var users = await ActiveVisitors(companyId).OrderBy(u => u.Username).ToListAsync();
        var caps = await _db.ShopVisitorDiscounts.AsNoTracking().Where(x => x.CompanyId == companyId).ToListAsync();
        return Ok(users.Select(u => new ShopVisitorDiscountDto {
            CompanyId = companyId, UserId = u.Id,
            UserName = string.IsNullOrWhiteSpace($"{u.FirstName} {u.LastName}".Trim()) ? u.Username : $"{u.FirstName} {u.LastName}".Trim(),
            MaxDiscountPercent = caps.FirstOrDefault(x => x.UserId == u.Id)?.MaxDiscountPercent ?? 0m
        }).ToList());
    }

    [HttpPut("visitors/{userId:int}")]
    public async Task<ActionResult<ShopVisitorDiscountDto>> SaveVisitor(int userId, ShopVisitorDiscountDto input)
    {
        if (!await CanAccessVisitorSettings(true)) return Forbid();
        if (_db.ActiveCompanyId is not > 0) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        if (input is null || input.CompanyId != _db.ActiveCompanyId || input.UserId != userId || input.MaxDiscountPercent < 0 || input.MaxDiscountPercent > 100 || decimal.Round(input.MaxDiscountPercent, 2) != input.MaxDiscountPercent)
            return BadRequest(new { message = "سقف تخفیف باید بین صفر و صد و حداکثر با دو رقم اعشار باشد." });
        var companyId = _db.ActiveCompanyId.Value;
        if (!await ActiveVisitors(companyId).AnyAsync(u => u.Id == userId)) return NotFound();
        var row = await _db.ShopVisitorDiscounts.FindAsync(companyId, userId);
        if (row is null) { row = new ShopVisitorDiscount { CompanyId = companyId, UserId = userId }; _db.ShopVisitorDiscounts.Add(row); }
        row.MaxDiscountPercent = input.MaxDiscountPercent;
        await _db.SaveChangesAsync();
        return Ok(input);
    }

}
