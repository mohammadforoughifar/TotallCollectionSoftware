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
    private readonly IPushService _push;
    public NotificationsController(AppDbContext db, IMessengerService messenger, IPushService push) { _db = db; _messenger = messenger; _push = push; }

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

    // ================== نوتیفیکیشن گوشی/تبلت (Web Push) ==================

    /// <summary>کلید عمومی VAPID — برای ثبت اشتراک push در مرورگر/دستگاه.</summary>
    [HttpGet("push-vapid-key")]
    public IActionResult VapidKey() => Ok(new { publicKey = _push.VapidPublicKey });

    /// <summary>ثبت اشتراک push این دستگاه برای کاربر جاری.</summary>
    [HttpPost("push-subscribe")]
    public async Task<IActionResult> PushSubscribe([FromBody] PushSubscribeInput? input)
    {
        if (input == null || string.IsNullOrWhiteSpace(input.Endpoint))
            return BadRequest(new { message = "اندپوینت اشتراک ارسال نشده است." });

        var ua = Request.Headers.UserAgent.ToString();
        await _push.SaveSubscriptionAsync(MyUserId, input.Endpoint, input.P256DH ?? "", input.Auth ?? "", ua);
        return Ok(new { ok = true, message = "اشتراک نوتیفیکیشن دستگاه ثبت شد." });
    }

    /// <summary>لغو اشتراک push یک دستگاه (کلیر از سمت کاربر).</summary>
    [HttpPost("push-unsubscribe")]
    public async Task<IActionResult> PushUnsubscribe([FromBody] PushUnsubscribeInput? input)
    {
        if (input != null && !string.IsNullOrWhiteSpace(input.Endpoint))
            await _push.RemoveSubscriptionAsync(MyUserId, input.Endpoint);
        return Ok(new { ok = true });
    }

    /// <summary>ارسال پیام آزمایشی push به کاربر جاری (برای تست تنظیمات).</summary>
    [HttpPost("test-push")]
    public async Task<IActionResult> TestPush()
    {
        await _push.SendToUserAsync(MyUserId, "✅ آزمون نوتیفیکیشن",
            "اگر این پیام بالای صفحه‌ی گوشی/تبلت شما آمد، Web Push فعال است.",
            null);
        return Ok(new { message = "نوتیفیکیشن آزمایشی ارسال شد." });
    }

    public class PushSubscribeInput
    {
        public string? Endpoint { get; set; }
        public string? P256DH { get; set; }
        public string? Auth { get; set; }
    }
    public class PushUnsubscribeInput { public string? Endpoint { get; set; } }
}
