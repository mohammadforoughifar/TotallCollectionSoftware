using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Hubs.Remote;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Remote;

public interface IRemoteSessionService
{
    Task<RemoteSettingsDto> GetSettingsAsync();
    Task<RemoteSettingsDto> SaveSettingsAsync(RemoteSettingsDto dto);
    Task<RemoteSessionDto> CreateSessionAsync(ClaimsPrincipal user, string deviceId, bool viewOnly);
    Task<RemoteSessionDto?> GetSessionForUserAsync(string sessionId, int userId);
    Task<bool> ConsentAsync(string deviceId, string sessionId, bool accepted);
    Task EndSessionAsync(string sessionId, string reason, int? userId = null);
    Task TouchAsync(string sessionId);
    Task ExpireAndCleanupAsync();
    Task EndDeviceSessionsAsync(string deviceId, string reason);
    RemoteSessionDto ToDto(RemoteSession s, string? deviceName = null);
}

public sealed class RemoteSessionService : IRemoteSessionService
{
    private readonly AppDbContext _db;
    private readonly IHubContext<RemoteAgentHub> _agentHub;
    private readonly IHubContext<RemoteHub> _viewerHub;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> LastTouch = new();

    public RemoteSessionService(AppDbContext db, IHubContext<RemoteAgentHub> agentHub, IHubContext<RemoteHub> viewerHub)
    {
        _db = db; _agentHub = agentHub; _viewerHub = viewerHub;
    }

    private async Task<RemoteSetting> SettingsEntityAsync()
        => await _db.RemoteSettings.FirstOrDefaultAsync(x => x.Id == 1) ?? new RemoteSetting();

    public async Task<RemoteSettingsDto> GetSettingsAsync()
    {
        var s = await SettingsEntityAsync();
        return new RemoteSettingsDto
        {
            Enabled = s.Enabled,
            ConsentTimeoutSeconds = s.ConsentTimeoutSeconds,
            SessionIdleTimeoutMinutes = s.SessionIdleTimeoutMinutes,
            FrameIntervalMs = s.FrameIntervalMs,
            JpegQuality = s.JpegQuality,
            MaxWidth = s.MaxWidth,
            UpdatedAt = s.UpdatedAt
        };
    }

    public async Task<RemoteSettingsDto> SaveSettingsAsync(RemoteSettingsDto dto)
    {
        var s = await _db.RemoteSettings.FirstOrDefaultAsync(x => x.Id == 1);
        if (s == null) { s = new RemoteSetting(); _db.RemoteSettings.Add(s); }
        s.Enabled = dto.Enabled;
        s.ConsentTimeoutSeconds = Math.Clamp(dto.ConsentTimeoutSeconds, 10, 600);
        s.SessionIdleTimeoutMinutes = Math.Clamp(dto.SessionIdleTimeoutMinutes, 1, 240);
        s.FrameIntervalMs = Math.Clamp(dto.FrameIntervalMs, 100, 2000);
        s.JpegQuality = Math.Clamp(dto.JpegQuality, 20, 90);
        s.MaxWidth = Math.Clamp(dto.MaxWidth, 640, 2560);
        s.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return await GetSettingsAsync();
    }

    private async Task AuditAsync(string? sessionId, string deviceId, int? userId, string action, string? detail = null)
    {
        _db.RemoteAuditLogs.Add(new RemoteAuditLog
        {
            SessionId = sessionId,
            DeviceId = deviceId,
            UserId = userId,
            Action = action,
            Detail = detail == null ? null : detail[..Math.Min(1000, detail.Length)]
        });
        await _db.SaveChangesAsync();
    }

    public async Task<RemoteSessionDto> CreateSessionAsync(ClaimsPrincipal user, string deviceId, bool viewOnly)
    {
        var settings = await SettingsEntityAsync();
        if (!settings.Enabled) throw new InvalidOperationException("ماژول ریموت دسکتاپ غیرفعال است.");
        var device = await _db.RemoteDevices.FirstOrDefaultAsync(x => x.Id == deviceId && x.IsEnabled)
            ?? throw new InvalidOperationException("دستگاه پیدا نشد یا غیرفعال است.");
        if (!device.IsOnline) throw new InvalidOperationException("دستگاه در حال حاضر آنلاین نیست.");
        var userId = int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;
        var name = user.FindFirstValue(ClaimTypes.Name) ?? "";
        if (userId <= 0) throw new InvalidOperationException("نشست کاربر معتبر نیست.");

        var session = new RemoteSession
        {
            DeviceId = device.Id,
            TechnicianUserId = userId,
            TechnicianName = name,
            Status = "Pending",
            AllowControl = !viewOnly,
            ExpiresAt = DateTime.Now.AddSeconds(settings.ConsentTimeoutSeconds),
            LastActivityAt = DateTime.Now
        };
        _db.RemoteSessions.Add(session);
        await _db.SaveChangesAsync();
        await AuditAsync(session.Id, device.Id, userId, "SessionCreated", viewOnly ? "ViewOnly" : "Control");
        await _agentHub.Clients.Group(RemoteGroups.Device(device.Id)).SendAsync("RequestConsent", session.Id, name, settings.ConsentTimeoutSeconds);
        return ToDto(session, device.Name);
    }

    public async Task<RemoteSessionDto?> GetSessionForUserAsync(string sessionId, int userId)
    {
        var s = await _db.RemoteSessions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sessionId && x.TechnicianUserId == userId);
        if (s == null) return null;
        var deviceName = await _db.RemoteDevices.AsNoTracking().Where(x => x.Id == s.DeviceId).Select(x => x.Name).FirstOrDefaultAsync();
        return ToDto(s, deviceName);
    }

    public async Task<bool> ConsentAsync(string deviceId, string sessionId, bool accepted)
    {
        var s = await _db.RemoteSessions.FirstOrDefaultAsync(x => x.Id == sessionId && x.DeviceId == deviceId);
        if (s == null || s.Status != "Pending") return false;
        if (DateTime.Now > s.ExpiresAt)
        {
            s.Status = "Expired"; s.EndedAt = DateTime.Now; s.EndReason = "ConsentTimeout";
            await _db.SaveChangesAsync();
            await AuditAsync(s.Id, deviceId, s.TechnicianUserId, "SessionExpired");
            await _viewerHub.Clients.Group(RemoteGroups.Session(s.Id)).SendAsync("SessionStateChanged", ToDto(s));
            return false;
        }
        var settings = await SettingsEntityAsync();
        if (accepted)
        {
            s.Status = "Active"; s.StartedAt = DateTime.Now; s.LastActivityAt = DateTime.Now;
            await _db.SaveChangesAsync();
            await AuditAsync(s.Id, deviceId, s.TechnicianUserId, "ConsentAccepted");
            await _viewerHub.Clients.Group(RemoteGroups.Session(s.Id)).SendAsync("SessionStateChanged", ToDto(s));
            await _agentHub.Clients.Group(RemoteGroups.Device(deviceId)).SendCoreAsync("StartCapture", new object?[]
            {
                s.Id, settings.FrameIntervalMs, settings.JpegQuality, settings.MaxWidth, s.AllowControl
            });
        }
        else
        {
            s.Status = "Rejected"; s.EndedAt = DateTime.Now; s.EndReason = "UserRejected";
            await _db.SaveChangesAsync();
            await AuditAsync(s.Id, deviceId, s.TechnicianUserId, "ConsentRejected");
            await _viewerHub.Clients.Group(RemoteGroups.Session(s.Id)).SendAsync("SessionStateChanged", ToDto(s));
        }
        return true;
    }

    public async Task EndSessionAsync(string sessionId, string reason, int? userId = null)
    {
        var s = await _db.RemoteSessions.FirstOrDefaultAsync(x => x.Id == sessionId);
        if (s == null || s.Status is "Ended" or "Rejected" or "Expired") return;
        s.Status = "Ended"; s.EndedAt = DateTime.Now; s.EndReason = reason;
        await _db.SaveChangesAsync();
        LastTouch.TryRemove(sessionId, out _);
        await AuditAsync(s.Id, s.DeviceId, userId ?? s.TechnicianUserId, "SessionEnded", reason);
        await _agentHub.Clients.Group(RemoteGroups.Device(s.DeviceId)).SendAsync("StopCapture", s.Id);
        await _viewerHub.Clients.Group(RemoteGroups.Session(s.Id)).SendAsync("SessionStateChanged", ToDto(s));
    }

    public async Task TouchAsync(string sessionId)
    {
        var now = DateTime.Now;
        if (LastTouch.TryGetValue(sessionId, out var last) && (now - last).TotalSeconds < 5) return;
        LastTouch[sessionId] = now;
        await _db.RemoteSessions.Where(x => x.Id == sessionId && x.Status == "Active")
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.LastActivityAt, now));
    }

    public async Task ExpireAndCleanupAsync()
    {
        var now = DateTime.Now;
        var pending = await _db.RemoteSessions.Where(x => x.Status == "Pending" && x.ExpiresAt < now).ToListAsync();
        foreach (var s in pending)
        {
            s.Status = "Expired"; s.EndedAt = now; s.EndReason = "ConsentTimeout";
            await AuditAsync(s.Id, s.DeviceId, s.TechnicianUserId, "SessionExpired");
            await _viewerHub.Clients.Group(RemoteGroups.Session(s.Id)).SendAsync("SessionStateChanged", ToDto(s));
        }
        var settings = await SettingsEntityAsync();
        var idleBefore = now.AddMinutes(-settings.SessionIdleTimeoutMinutes);
        var idle = await _db.RemoteSessions.Where(x => x.Status == "Active" && x.LastActivityAt < idleBefore).ToListAsync();
        foreach (var s in idle)
        {
            s.Status = "Ended"; s.EndedAt = now; s.EndReason = "IdleTimeout";
            await AuditAsync(s.Id, s.DeviceId, s.TechnicianUserId, "SessionEnded", "IdleTimeout");
            await _agentHub.Clients.Group(RemoteGroups.Device(s.DeviceId)).SendAsync("StopCapture", s.Id);
            await _viewerHub.Clients.Group(RemoteGroups.Session(s.Id)).SendAsync("SessionStateChanged", ToDto(s));
        }
        if (pending.Count > 0 || idle.Count > 0) await _db.SaveChangesAsync();
    }

    public async Task EndDeviceSessionsAsync(string deviceId, string reason)
    {
        var active = await _db.RemoteSessions.Where(x => x.DeviceId == deviceId && (x.Status == "Active" || x.Status == "Pending")).ToListAsync();
        foreach (var s in active) await EndSessionAsync(s.Id, reason, s.TechnicianUserId);
    }

    public RemoteSessionDto ToDto(RemoteSession s, string? deviceName = null) => new()
    {
        Id = s.Id,
        DeviceId = s.DeviceId,
        DeviceName = deviceName ?? "",
        TechnicianUserId = s.TechnicianUserId,
        TechnicianName = s.TechnicianName,
        Status = s.Status,
        AllowControl = s.AllowControl,
        CreatedAt = s.CreatedAt,
        StartedAt = s.StartedAt,
        EndedAt = s.EndedAt,
        ExpiresAt = s.ExpiresAt,
        EndReason = s.EndReason
    };
}
