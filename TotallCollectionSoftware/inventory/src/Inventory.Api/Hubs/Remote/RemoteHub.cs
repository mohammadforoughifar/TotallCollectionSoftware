using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Services.Core;
using Inventory.Api.Services.Remote;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Hubs.Remote;

/// <summary>اتصال نمایش‌دهنده (تکنسین) — فقط با JWT و مجوز ماژول RemoteDesktop.</summary>
[Authorize]
public sealed class RemoteHub : Hub
{
    private readonly AppDbContext _db;
    private readonly IEffectivePermissions _permissions;
    private readonly IRemoteSessionService _sessions;

    public RemoteHub(AppDbContext db, IEffectivePermissions permissions, IRemoteSessionService sessions)
    { _db = db; _permissions = permissions; _sessions = sessions; }

    private int UserId => int.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var v) && v > 0
        ? v : throw new HubException("نشست ورود معتبر نیست.");

    public override async Task OnConnectedAsync()
    {
        var uid = UserId;
        if (!await _db.Users.AsNoTracking().AnyAsync(u => u.Id == uid && u.IsActive))
            throw new HubException("حساب کاربری فعال نیست.");
        if (Context.User is null || !await _permissions.CanSeeModuleAsync(Context.User, "RemoteDesktop"))
            throw new HubException("شما مجوز ماژول ریموت دسکتاپ را ندارید.");
        await base.OnConnectedAsync();
    }

    public async Task JoinSession(string sessionId)
    {
        var session = await _sessions.GetSessionForUserAsync(sessionId, UserId)
            ?? throw new HubException("جلسه پیدا نشد یا متعلق به شما نیست.");
        await Groups.AddToGroupAsync(Context.ConnectionId, RemoteGroups.Session(sessionId));
        Context.Items["sessionId"] = sessionId;
        await Clients.Caller.SendAsync("SessionStateChanged", session);
    }

    public Task LeaveSession(string sessionId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, RemoteGroups.Session(sessionId));

    public async Task SendInput(string sessionId, string inputJson)
    {
        var uid = UserId;
        var session = await _sessions.GetSessionForUserAsync(sessionId, uid)
            ?? throw new HubException("جلسه پیدا نشد یا متعلق به شما نیست.");
        if (session.Status != "Active") throw new HubException("جلسه فعال نیست.");
        if (!session.AllowControl) throw new HubException("این جلسه فقط مشاهده است.");
        if (Context.User is null) throw new HubException("نشست ورود معتبر نیست.");
        var allowed = await _permissions.IsLegacyAdminAsync(Context.User)
                      || (await _permissions.GetAsync(Context.User)).Contains("RemoteDesktop.Control");
        if (!allowed) throw new HubException("مجوز کنترل از راه دور ندارید.");
        if (string.IsNullOrWhiteSpace(inputJson) || inputJson.Length > 8192) return;
        await Clients.Group(RemoteGroups.Device(session.DeviceId)).SendAsync("Input", sessionId, inputJson);
        await _sessions.TouchAsync(sessionId);
    }

    public async Task EndSession(string sessionId)
    {
        var uid = UserId;
        var session = await _sessions.GetSessionForUserAsync(sessionId, uid)
            ?? throw new HubException("جلسه پیدا نشد یا متعلق به شما نیست.");
        await _sessions.EndSessionAsync(session.Id, "ViewerEnded", uid);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue("sessionId", out var v) && v is string sessionId)
        {
            try { await _sessions.EndSessionAsync(sessionId, "ViewerDisconnected", UserId); }
            catch { }
        }
        await base.OnDisconnectedAsync(exception);
    }
}
