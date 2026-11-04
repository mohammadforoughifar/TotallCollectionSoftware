using Inventory.Api.Data;
using Inventory.Api.Services.Remote;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Hubs.Remote;

/// <summary>اتصال ایجنت ریموت — بدون JWT کاربر؛ احراز هویت با کلید اختصاصی دستگاه.</summary>
[AllowAnonymous]
public sealed class RemoteAgentHub : Hub
{
    private readonly AppDbContext _db;
    private readonly IRemoteAuthService _auth;
    private readonly IRemoteSessionService _sessions;

    public RemoteAgentHub(AppDbContext db, IRemoteAuthService auth, IRemoteSessionService sessions)
    { _db = db; _auth = auth; _sessions = sessions; }

    private string? DeviceId => Context.Items.TryGetValue("deviceId", out var v) ? v as string : null;

    public override async Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext();
        var key = http?.Request.Headers["X-Remote-Device-Key"].FirstOrDefault();
        var device = await _auth.AuthenticateDeviceAsync(key);
        if (device == null)
        {
            Context.Abort();
            return;
        }
        Context.Items["deviceId"] = device.Id;
        var tracked = await _db.RemoteDevices.FirstOrDefaultAsync(x => x.Id == device.Id);
        if (tracked != null)
        {
            tracked.IsOnline = true;
            tracked.LastSeenAt = DateTime.Now;
            tracked.LastIp = http?.Connection.RemoteIpAddress?.ToString();
            tracked.Os = http?.Request.Headers["X-Remote-Agent-Os"].FirstOrDefault() ?? tracked.Os;
            tracked.AgentVersion = http?.Request.Headers["X-Remote-Agent-Version"].FirstOrDefault() ?? tracked.AgentVersion;
            tracked.UpdatedAt = DateTime.Now;
            await _db.SaveChangesAsync();
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, RemoteGroups.Device(device.Id));
        await Clients.Caller.SendAsync("Registered", device.Id, device.Name);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var deviceId = DeviceId;
        if (!string.IsNullOrEmpty(deviceId))
        {
            var tracked = await _db.RemoteDevices.FirstOrDefaultAsync(x => x.Id == deviceId);
            if (tracked != null)
            {
                tracked.IsOnline = false;
                tracked.LastSeenAt = DateTime.Now;
                tracked.UpdatedAt = DateTime.Now;
                await _db.SaveChangesAsync();
            }
            await _sessions.EndDeviceSessionsAsync(deviceId, "AgentDisconnected");
        }
        await base.OnDisconnectedAsync(exception);
    }

    public async Task ConsentResult(string sessionId, bool accepted)
    {
        var deviceId = DeviceId ?? throw new HubException("دستگاه احراز هویت نشده است.");
        await _sessions.ConsentAsync(deviceId, sessionId, accepted);
    }

    public async Task Frame(string sessionId, byte[] jpeg, int width, int height)
    {
        var deviceId = DeviceId;
        if (string.IsNullOrEmpty(deviceId) || jpeg.Length == 0 || jpeg.Length > 4 * 1024 * 1024) return;
        var session = await _db.RemoteSessions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == sessionId && x.DeviceId == deviceId && x.Status == "Active");
        if (session == null) return;
        await _sessions.TouchAsync(sessionId);
        await Clients.Group(RemoteGroups.Session(sessionId)).SendAsync("Frame", jpeg, width, height);
    }
}
