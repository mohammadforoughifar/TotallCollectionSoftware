using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Infrastructure;
using Inventory.Api.Services.Core;
using Inventory.Api.Services.Remote;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers.Remote;

[ApiController]
[Route("api/remote")]
[Authorize]
public sealed class RemoteController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IRemoteAuthService _auth;
    private readonly IRemoteSessionService _sessions;
    private readonly IEffectivePermissions _permissions;

    public RemoteController(AppDbContext db, IRemoteAuthService auth, IRemoteSessionService sessions, IEffectivePermissions permissions)
    { _db = db; _auth = auth; _sessions = sessions; _permissions = permissions; }

    private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;
    private async Task<bool> CanManageAsync()
        => await _permissions.IsLegacyAdminAsync(User) || (await _permissions.GetAsync(User)).Contains("RemoteDesktop.Manage");

    private static RemoteDeviceDto ToDto(RemoteDevice d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        CompanyCode = d.CompanyCode,
        Os = d.Os,
        AgentVersion = d.AgentVersion,
        IsOnline = d.IsOnline,
        IsEnabled = d.IsEnabled,
        LastSeenAt = d.LastSeenAt,
        LastIp = d.LastIp,
        KeyHint = d.DeviceKeyHint,
        CreatedAt = d.CreatedAt
    };

    // ================== دستگاه‌ها ==================
    [HttpGet("devices")]
    [RequirePermission("RemoteDesktop", "View")]
    public async Task<IActionResult> Devices()
    {
        var rows = await _db.RemoteDevices.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
        return Ok(rows.Select(ToDto).ToList());
    }

    [HttpGet("devices/{id}")]
    [RequirePermission("RemoteDesktop", "View")]
    public async Task<IActionResult> Device(string id)
    {
        var d = await _db.RemoteDevices.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return d == null ? NotFound() : Ok(ToDto(d));
    }

    [HttpPost("devices")]
    [RequirePermission("RemoteDesktop", "Manage")]
    public async Task<IActionResult> CreateDevice([FromBody] CreateRemoteDeviceRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { message = "نام دستگاه الزامی است." });
        var (device, key) = await _auth.CreateDeviceAsync(req.Name, req.CompanyCode ?? "");
        return Ok(new RemoteDeviceCreatedDto { Device = ToDto(device), DeviceKey = key });
    }

    [HttpPut("devices/{id}")]
    [RequirePermission("RemoteDesktop", "Manage")]
    public async Task<IActionResult> UpdateDevice(string id, [FromBody] UpdateRemoteDeviceRequest req)
    {
        var d = await _db.RemoteDevices.FirstOrDefaultAsync(x => x.Id == id);
        if (d == null) return NotFound();
        if (req.Name != null) d.Name = req.Name.Trim();
        if (req.IsEnabled.HasValue) d.IsEnabled = req.IsEnabled.Value;
        d.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        if (!d.IsEnabled) await _sessions.EndDeviceSessionsAsync(d.Id, "DeviceDisabled");
        return Ok(ToDto(d));
    }

    [HttpDelete("devices/{id}")]
    [RequirePermission("RemoteDesktop", "Manage")]
    public async Task<IActionResult> DeleteDevice(string id)
    {
        var d = await _db.RemoteDevices.FirstOrDefaultAsync(x => x.Id == id);
        if (d == null) return NotFound();
        await _sessions.EndDeviceSessionsAsync(d.Id, "DeviceDeleted");
        _db.RemoteDevices.Remove(d);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPost("devices/{id}/rotate-key")]
    [RequirePermission("RemoteDesktop", "Manage")]
    public async Task<IActionResult> RotateKey(string id)
    {
        var key = await _auth.RotateKeyAsync(id);
        return key == null ? NotFound() : Ok(new { deviceKey = key });
    }

    // ================== جلسه‌ها ==================
    [HttpGet("sessions")]
    [RequirePermission("RemoteDesktop", "View")]
    public async Task<IActionResult> Sessions([FromQuery] string? deviceId, [FromQuery] string? status, [FromQuery] int take = 100)
    {
        take = Math.Clamp(take, 1, 500);
        var query = _db.RemoteSessions.AsNoTracking().AsQueryable();
        if (!await CanManageAsync()) query = query.Where(x => x.TechnicianUserId == UserId);
        if (!string.IsNullOrWhiteSpace(deviceId)) query = query.Where(x => x.DeviceId == deviceId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        var rows = await query.OrderByDescending(x => x.CreatedAt).Take(take).ToListAsync();
        var deviceIds = rows.Select(x => x.DeviceId).Distinct().ToList();
        var names = await _db.RemoteDevices.AsNoTracking().Where(x => deviceIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name);
        return Ok(rows.Select(s => _sessions.ToDto(s, names.GetValueOrDefault(s.DeviceId))).ToList());
    }

    [HttpGet("sessions/{id}")]
    [RequirePermission("RemoteDesktop", "View")]
    public async Task<IActionResult> Session(string id)
    {
        var s = await _sessions.GetSessionForUserAsync(id, UserId);
        if (s == null && await CanManageAsync())
        {
            var entity = await _db.RemoteSessions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (entity != null)
            {
                var name = await _db.RemoteDevices.AsNoTracking().Where(x => x.Id == entity.DeviceId).Select(x => x.Name).FirstOrDefaultAsync();
                s = _sessions.ToDto(entity, name);
            }
        }
        return s == null ? NotFound() : Ok(s);
    }

    [HttpPost("sessions")]
    [RequirePermission("RemoteDesktop", "View")]
    public async Task<IActionResult> CreateSession([FromBody] CreateRemoteSessionRequest req)
    {
        if (!req.ViewOnly)
        {
            var allowed = await _permissions.IsLegacyAdminAsync(User) || (await _permissions.GetAsync(User)).Contains("RemoteDesktop.Control");
            if (!allowed) return StatusCode(403, new { message = "مجوز کنترل از راه دور ندارید." });
        }
        try
        {
            var session = await _sessions.CreateSessionAsync(User, req.DeviceId, req.ViewOnly);
            return Ok(session);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("sessions/{id}/end")]
    [RequirePermission("RemoteDesktop", "View")]
    public async Task<IActionResult> EndSession(string id)
    {
        var s = await _sessions.GetSessionForUserAsync(id, UserId);
        if (s == null && !await CanManageAsync()) return NotFound();
        await _sessions.EndSessionAsync(id, "ApiEnded", UserId);
        return Ok(new { ok = true });
    }

    // ================== تنظیمات و حسابرسی ==================
    [HttpGet("settings")]
    [RequirePermission("RemoteDesktop", "Manage")]
    public async Task<IActionResult> GetSettings() => Ok(await _sessions.GetSettingsAsync());

    [HttpPut("settings")]
    [RequirePermission("RemoteDesktop", "Manage")]
    public async Task<IActionResult> SaveSettings([FromBody] RemoteSettingsDto dto) => Ok(await _sessions.SaveSettingsAsync(dto));

    [HttpGet("audit")]
    [RequirePermission("RemoteDesktop", "Manage")]
    public async Task<IActionResult> Audit([FromQuery] string? sessionId, [FromQuery] int take = 200)
    {
        take = Math.Clamp(take, 1, 1000);
        var q = _db.RemoteAuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(sessionId)) q = q.Where(x => x.SessionId == sessionId);
        var rows = await q.OrderByDescending(x => x.Id).Take(take).ToListAsync();
        return Ok(rows.Select(x => new RemoteAuditDto
        {
            Id = x.Id,
            SessionId = x.SessionId,
            DeviceId = x.DeviceId,
            UserId = x.UserId,
            Action = x.Action,
            Detail = x.Detail,
            CreatedAt = x.CreatedAt
        }).ToList());
    }
}
