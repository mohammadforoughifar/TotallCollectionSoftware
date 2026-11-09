using System.Data;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.Core;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>Independent SMS module opt-ins for the authenticated user's active company.</summary>
[ApiController]
[Authorize]
[Route("api/company-sms-settings")]
public sealed class CompanySmsSettingsController(
    AppDbContext db, IEffectivePermissions permissions, IEnumerable<ICompanySmsRevocationHandler> revocations) : ControllerBase
{
    private async Task<bool> AllowedAsync(bool write)
    {
        if (User.Identity?.IsAuthenticated != true) return false;
        if (await permissions.IsLegacyAdminAsync(User)) return true;
        var granted = await permissions.GetAsync(User);
        return granted.Contains("SmsSettings.Update") || (!write && granted.Contains("SmsSettings.Read"));
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!await AllowedAsync(false)) return Forbid();
        if (db.ActiveCompanyId is not > 0) return BadRequest(new { message = "شرکت فعال و مجاز انتخاب نشده است." });
        var result = await ReadAsync(db.ActiveCompanyId.Value, ct);
        return result is null ? BadRequest(new { message = "شرکت فعال یافت نشد یا غیرفعال است." }) : Ok(result);
    }

    [HttpPut]
    public async Task<IActionResult> Save([FromBody] SaveCompanySmsSettingsDto dto, CancellationToken ct)
    {
        if (!await AllowedAsync(true)) return Forbid();
        var companyId = db.ActiveCompanyId;
        // The payload is an expectation, never a way to choose/impersonate another company.
        if (companyId is not > 0 || dto.CompanyId != companyId.Value)
            return Conflict(new { message = "شرکت فعال تغییر کرده یا مجاز نیست؛ صفحه را دوباره باز کنید." });
        if (dto.Modules is null || dto.Modules.Count == 0 || dto.Modules.Count > SmsModuleCatalog.All.Count ||
            dto.Modules.Any(m => m is null || !SmsModuleCatalog.Contains(m.Key)) ||
            dto.Modules.Select(m => m.Key).Distinct(StringComparer.Ordinal).Count() != dto.Modules.Count)
            return BadRequest(new { message = "فهرست ماژول‌های پیامکی معتبر نیست؛ صفحه را دوباره باز کنید." });

        var handlers = revocations.ToList();
        // Fail closed when a newly integrated module has not supplied its revocation hook.
        if (dto.Modules.Any(m => !handlers.Any(h => h.ModuleKey == m.Key)))
            return StatusCode(500, new { message = "امکان لغو امن صف این ماژول در سرور تنظیم نشده است." });

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (!await db.SystemCompanies.AnyAsync(c => c.Id == companyId.Value && c.IsActive, ct))
            return BadRequest(new { message = "شرکت فعال یافت نشد یا غیرفعال است." });
        var keys = dto.Modules.Select(m => m.Key).ToArray();
        var rows = await db.CompanySmsSettings.Where(s => s.CompanyId == companyId.Value && keys.Contains(s.ModuleKey))
            .ToListAsync(ct);
        var now = DateTime.UtcNow;
        var actor = db.CurrentUserName;
        if (actor?.Length > 120) actor = actor[..120];
        foreach (var change in dto.Modules)
        {
            var row = rows.FirstOrDefault(s => s.ModuleKey == change.Key);
            if (row is null)
            {
                row = new CompanySmsSetting { CompanyId = companyId.Value, ModuleKey = change.Key };
                db.CompanySmsSettings.Add(row);
            }
            // A toggle creates a new permission generation; old/in-flight jobs cannot
            // become eligible again if the module is disabled and re-enabled.
            if (row.Enabled != change.Enabled)
            {
                if (row.Revision == long.MaxValue)
                    return StatusCode(500, new { message = "نسخهٔ دسترسی پیامکی قابل افزایش نیست." });
                row.Revision++;
            }
            row.Enabled = change.Enabled;
            row.UpdatedAtUtc = now;
            row.UpdatedBy = actor;
        }
        // Save the permission and terminate its pending events in one serializable transaction.
        await db.SaveChangesAsync(ct);
        foreach (var disabled in dto.Modules.Where(m => !m.Enabled))
            foreach (var handler in handlers.Where(h => h.ModuleKey == disabled.Key))
                await handler.CancelPendingAsync(companyId.Value, now, ct);
        await tx.CommitAsync(ct);
        return Ok(await ReadAsync(companyId.Value, ct));
    }

    private async Task<CompanySmsSettingsDto?> ReadAsync(int companyId, CancellationToken ct)
    {
        var company = await db.SystemCompanies.AsNoTracking()
            .Where(c => c.Id == companyId && c.IsActive).Select(c => new { c.Id, c.Name }).SingleOrDefaultAsync(ct);
        if (company is null) return null;
        var enabled = await db.CompanySmsSettings.AsNoTracking()
            .Where(s => s.CompanyId == companyId && s.Enabled).Select(s => s.ModuleKey).ToListAsync(ct);
        return new CompanySmsSettingsDto
        {
            CompanyId = companyId,
            CompanyName = company.Name,
            Modules = SmsModuleCatalog.All.Select(m => new CompanySmsModuleDto
            {
                Key = m.Key, Group = m.Group, Title = m.Title,
                Enabled = enabled.Contains(m.Key, StringComparer.Ordinal)
            }).ToList()
        };
    }
}
