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
            new[] { "Shop.PrintSettings.View", "Shop.Sale.View", "Shop.Sale.Manage", "Shop.Purchase.View", "Shop.Purchase.Manage" }.Any(permissions.Contains);
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
}
