using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>اعلان‌های کاربر جاری — لیست، شمارش نخوانده و علامت‌گذاری با تفکیک و ایزولاسیون کامل کاربران.</summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IMessengerService _messenger;
    private readonly IPushService _push;
    public NotificationsController(AppDbContext db, IMessengerService messenger, IPushService push)
    {
        _db = db;
        _messenger = messenger;
        _push = push;
    }

    private int MyUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) && v > 0 ? v : 0;

    [HttpGet]
    public async Task<IActionResult> My([FromQuery] int take = 50)
    {
        if (MyUserId <= 0) return Unauthorized();

        var list = await _db.AppNotifications.AsNoTracking()
            .Where(n => n.UserId == MyUserId)
            .OrderByDescending(n => n.Id)
            .Take(Math.Min(take, 100))
            .Select(n => new { n.Id, n.Title, n.Body, n.FromName, n.FormName, n.Link, n.IsRead, n.CreatedAt })
            .ToListAsync();

        return Ok(list);
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount()
    {
        if (MyUserId <= 0) return Unauthorized();

        var count = await _db.AppNotifications.AsNoTracking()
            .CountAsync(n => n.UserId == MyUserId && !n.IsRead);

        return Ok(new { count });
    }

    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        if (MyUserId <= 0) return Unauthorized();

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
        if (MyUserId <= 0) return Unauthorized();
        await _messenger.SendToUserAsync(MyUserId, "پیام آزمایشی", "اتصال بله/ایتا برقرار است ✅");
        return Ok(new { message = "در صورت تنظیم توکن و شناسه چت، پیام ارسال شد." });
    }

    /// <summary>پاک کردن همه اعلان‌های کاربر جاری.</summary>
    [HttpDelete("clear-all")]
    public async Task<IActionResult> ClearAll()
    {
        if (MyUserId <= 0) return Unauthorized();

        var list = await _db.AppNotifications.Where(n => n.UserId == MyUserId).ToListAsync();
        _db.AppNotifications.RemoveRange(list);
        await _db.SaveChangesAsync();
        return Ok(new { removed = list.Count });
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        if (MyUserId <= 0) return Unauthorized();

        var list = await _db.AppNotifications.Where(n => n.UserId == MyUserId && !n.IsRead).ToListAsync();
        foreach (var n in list) n.IsRead = true;
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ================== نوتیفیکیشن گوشی/تبلت (Web Push) ==================

    /// <summary>کلید عمومی VAPID — برای ثبت اشتراک push در مرورگر/دستگاه.</summary>
    [HttpGet("push-vapid-key")]
    public IActionResult VapidKey() => Ok(new { publicKey = _push.VapidPublicKey, configured = _push.IsConfigured });

    /// <summary>ثبت اشتراک push این دستگاه برای کاربر جاری.</summary>
    [HttpPost("push-subscribe")]
    public async Task<IActionResult> PushSubscribe([FromBody] PushSubscribeInput? input)
    {
        if (MyUserId <= 0) return Unauthorized();
        if (input == null || string.IsNullOrWhiteSpace(input.Endpoint))
            return BadRequest(new { message = "اندپوینت اشتراک ارسال نشده است." });

        if (!_push.IsConfigured) return StatusCode(503, new { message = "کلیدهای اعلان روی سرور تنظیم نشده یا معتبر نیستند." });
        if (!PushEndpointPolicy.ValidEndpoint(input.Endpoint) || !PushEndpointPolicy.ValidKeys(input.P256DH ?? "", input.Auth ?? ""))
            return BadRequest(new { message = "اشتراک دستگاه معتبر نیست؛ مرورگر را به‌روز کنید." });
        if (!await _db.Users.AnyAsync(u => u.Id == MyUserId && u.IsActive)) return Forbid();
        var ua = Request.Headers.UserAgent.ToString();
        await _push.SaveSubscriptionAsync(MyUserId, input.Endpoint, input.P256DH ?? "", input.Auth ?? "", ua);
        return Ok(new { ok = true, message = "اشتراک نوتیفیکیشن دستگاه ثبت شد." });
    }

    /// <summary>لغو اشتراک push یک دستگاه (کلیر از سمت کاربر).</summary>
    [HttpPost("push-unsubscribe")]
    public async Task<IActionResult> PushUnsubscribe([FromBody] PushUnsubscribeInput? input)
    {
        if (MyUserId <= 0) return Unauthorized();
        if (input != null && !string.IsNullOrWhiteSpace(input.Endpoint))
            await _push.RemoveSubscriptionAsync(MyUserId, input.Endpoint);
        return Ok(new { ok = true });
    }

    /// <summary>ارسال پیام آزمایشی push به کاربر جاری (برای تست تنظیمات).</summary>
    [HttpPost("test-push")]
    public async Task<IActionResult> TestPush([FromBody] PushUnsubscribeInput? input)
    {
        if (MyUserId <= 0) return Unauthorized();
        if (!_push.IsConfigured) return StatusCode(503, new { message = "کلیدهای اعلان روی سرور تنظیم نشده یا معتبر نیستند." });
        var endpoint = input?.Endpoint ?? "";
        var sub = await _db.PushSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == MyUserId && s.Endpoint == endpoint);
        if (sub == null) return BadRequest(new { message = "ابتدا اعلان این دستگاه را فعال کنید." });
        var recent = DateTime.UtcNow.AddSeconds(-30);
        if (await _db.PushDeliveries.AnyAsync(j => j.UserId == MyUserId && j.Tag.StartsWith("test-") && j.CreatedAtUtc > recent))
            return StatusCode(429, new { message = "برای آزمایش بعدی ۳۰ ثانیه صبر کنید." });
        var now = DateTime.UtcNow;
        _db.PushDeliveries.Add(new PushDelivery { UserId = MyUserId, SubscriptionId = sub.Id, Tag = "test-" + Guid.NewGuid().ToString("N"),
            Title = "آزمایش اعلان گوشی", Body = "اعلان آزمایشی سامانه دریافت شد.", Link = "/", CreatedAtUtc = now, NextAttemptAtUtc = now, ExpiresAtUtc = now.AddMinutes(10) });
        await _db.SaveChangesAsync();
        return Accepted(new { message = "اعلان در صف ارسال قرار گرفت؛ دریافت آن را در نوار اعلان گوشی بررسی کنید." });
    }

    [HttpPost("push-status")]
    public async Task<IActionResult> PushStatus([FromBody] PushUnsubscribeInput? input)
    {
        if (MyUserId <= 0) return Unauthorized();
        var sub = await _db.PushSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == MyUserId && s.Endpoint == (input == null ? "" : input.Endpoint));
        var last = sub == null ? null : await _db.PushDeliveries.AsNoTracking().Where(j => j.UserId == MyUserId && j.SubscriptionId == sub.Id).OrderByDescending(j => j.Id)
            .Select(j => new { status = j.Status, errorCode = j.ErrorCode, attempts = j.Attempts }).FirstOrDefaultAsync();
        return Ok(new { configured = _push.IsConfigured, registered = sub != null, last });
    }

    public class PushSubscribeInput
    {
        public string? Endpoint { get; set; }
        public string? P256DH { get; set; }
        public string? Auth { get; set; }
    }
    public class PushUnsubscribeInput { public string? Endpoint { get; set; } }
}
