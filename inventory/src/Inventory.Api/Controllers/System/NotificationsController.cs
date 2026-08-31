using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>اعلان‌های کاربر جاری — لیست، شمارش نخوانده و علامت‌گذاری.</summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IMessengerService _messenger;
    public NotificationsController(AppDbContext db, IMessengerService messenger) { _db = db; _messenger = messenger; }

    private int MyUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;

    [HttpGet]
    public async Task<IActionResult> My([FromQuery] int take = 50) =>
        Ok(await _db.AppNotifications.Where(n => n.UserId == MyUserId)
            .OrderByDescending(n => n.Id).Take(take)
            .Select(n => new { n.Id, n.Title, n.Body, n.FromName, n.FormName, n.Link, n.IsRead, n.CreatedAt })
            .ToListAsync());

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount() =>
        Ok(new { count = await _db.AppNotifications.CountAsync(n => n.UserId == MyUserId && !n.IsRead) });

    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        var n = await _db.AppNotifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == MyUserId);
        if (n == null) return NotFound();
        n.IsRead = true;
        await _db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>همگام‌سازی بله — تطبیق مخاطبین ربات با موبایل کاربران.</summary>
    [HttpPost("sync-bale")]
    public async Task<IActionResult> SyncBale()
    {
        var (matched, message) = await _messenger.SyncBaleAsync();
        return Ok(new { matched, message });
    }

    /// <summary>ارسال پیام آزمایشی بله/ایتا به کاربر جاری.</summary>
    [HttpPost("test-messenger")]
    public async Task<IActionResult> TestMessenger()
    {
        await _messenger.SendToUserAsync(MyUserId, "پیام آزمایشی", "اتصال بله/ایتا برقرار است ✅");
        return Ok(new { message = "در صورت تنظیم توکن و شناسه چت، پیام ارسال شد." });
    }

    /// <summary>پاک کردن همه اعلان‌های کاربر (کلیر کامل زنگ).</summary>
    [HttpDelete("clear-all")]
    public async Task<IActionResult> ClearAll()
    {
        var list = await _db.AppNotifications.Where(n => n.UserId == MyUserId).ToListAsync();
        _db.AppNotifications.RemoveRange(list);
        await _db.SaveChangesAsync();
        return Ok(new { removed = list.Count });
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        var list = await _db.AppNotifications.Where(n => n.UserId == MyUserId && !n.IsRead).ToListAsync();
        foreach (var n in list) n.IsRead = true;
        await _db.SaveChangesAsync();
        return Ok();
    }
}
