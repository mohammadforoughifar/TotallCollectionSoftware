using Inventory.Api.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Hubs;

/// <summary>هاب اعلان‌های بلادرنگ — هر کاربر به گروه اختصاصی خودش می‌پیوندد.</summary>
public class NotifyHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.GetHttpContext()?.Request.Query["userId"].ToString();
        if (int.TryParse(userId, out var uid) && uid > 0)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"u{uid}");
        await base.OnConnectedAsync();
    }
}

/// <summary>سرویس ارسال اعلان: ذخیره در دیتابیس + ارسال بلادرنگ با SignalR.</summary>
public interface INotifyService
{
    Task SendAsync(int userId, string title, string? body, string fromName, string formName, string? link);
    Task SendManyAsync(IEnumerable<int> userIds, string title, string? body, string fromName, string formName, string? link);

    /// <summary>اعلام تغییر داده‌ها به همه کلاینت‌ها — برای رفرش خودکار کارتابل‌ها.</summary>
    Task BroadcastChangedAsync(string scope);
}

public class NotifyService : INotifyService
{
    private readonly AppDbContext _db;
    private readonly IHubContext<NotifyHub> _hub;
    private readonly Inventory.Api.Services.IMessengerService _messenger;

    public NotifyService(AppDbContext db, IHubContext<NotifyHub> hub, Inventory.Api.Services.IMessengerService messenger)
    {
        _db = db;
        _hub = hub;
        _messenger = messenger;
    }

    public async Task SendAsync(int userId, string title, string? body, string fromName, string formName, string? link)
    {
        if (userId <= 0) return;
        var n = new AppNotification
        {
            UserId = userId,
            Title = title,
            Body = body,
            FromName = fromName,
            FormName = formName,
            Link = link
        };
        _db.AppNotifications.Add(n);
        await _db.SaveChangesAsync();

        try
        {
            await _hub.Clients.Group($"u{userId}").SendAsync("notify", new
            {
                n.Id, n.Title, n.Body, n.FromName, n.FormName, n.Link, n.CreatedAt
            });
        }
        catch { /* عدم اتصال کاربر مانع ذخیره اعلان نمی‌شود */ }

        // ================== ارسال موازی به بله و ایتا (در صورت داشتن) ==================
        try { await _messenger.SendToUserAsync(userId, title, body); } catch { }
    }

    public async Task SendManyAsync(IEnumerable<int> userIds, string title, string? body, string fromName, string formName, string? link)
    {
        foreach (var id in userIds.Distinct())
            await SendAsync(id, title, body, fromName, formName, link);
    }

    public async Task BroadcastChangedAsync(string scope)
    {
        try { await _hub.Clients.All.SendAsync("datachanged", scope); } catch { }
    }
}
