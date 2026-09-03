using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;
using WebPush;

namespace Inventory.Api.Services;

/// <summary>
/// ================== نوتیفیکیشن گوشی/تبلت (Web Push) ==================
/// شبیه اعلان‌های گوشی: وقتی کاربر با گوشی یا تبلت کار می‌کند، اعلان جدید
/// مثل نوتیف گوشی از بالای صفحه می‌آید و در نوار اعلان می‌ماند —
/// حتی اگر کاربر داخل برنامه نباشد یا صفحه بسته باشد.
///
/// چرخه:
///   کلاینت (Blazor) → درخواست اجازه + ثبت اشتراک (subscribe) → ذخیره در PushSubscriptions
///   سرور (NotifyService) → SendAsync → PushService.SendToUserAsync → ارسال به همه اشتراک‌های کاربر
/// </summary>
public interface IPushService
{
    /// <summary>ثبت/به‌روزرسانی اشتراک push برای کاربر (یکی از دستگاه‌های او).</summary>
    Task SaveSubscriptionAsync(int userId, string endpoint, string p256dh, string auth, string? userAgent);

    /// <summary>حذف یک اشتراک (فعال‌نشدن/لغو اجازه از سمت دستگاه).</summary>
    Task RemoveSubscriptionAsync(int userId, string endpoint);

    /// <summary>ارسال اعلان push به همه دستگاه‌های یک کاربر.</summary>
    Task SendToUserAsync(int userId, string title, string? body, string? link);

    /// <summary>ارسال اعلان push به همه دستگاه‌های چند کاربر.</summary>
    Task SendToUsersAsync(IEnumerable<int> userIds, string title, string? body, string? link);

    /// <summary>کلید عمومی VAPID برای کلاینت (ثبت اشتراک در مرورگر).</summary>
    string VapidPublicKey { get; }
}

/// <summary>پیاده‌سازی Web Push با کتابخانه WebPush (پروتکل استاندارد RFC 8030).</summary>
public class PushService : IPushService
{
    private readonly AppDbContext _db;
    private readonly ILogger<PushService> _log;
    private readonly string _vapidSubject;
    private readonly string _vapidPublic;
    private readonly string _vapidPrivate;
    private readonly WebPushClient _client = new();

    public PushService(AppDbContext db, IConfiguration cfg, ILogger<PushService> log)
    {
        _db = db;
        _log = log;
        var sec = cfg.GetSection("PushNotifications");
        _vapidSubject = sec["VapidSubject"] ?? "mailto:admin@example.com";
        // متغیرهای محیطی اولویت دارند (امنیت در محیط عملیاتی)
        _vapidPublic = Environment.GetEnvironmentVariable("VAPID_PUBLIC_KEY") ?? sec["VapidPublicKey"] ?? "";
        _vapidPrivate = Environment.GetEnvironmentVariable("VAPID_PRIVATE_KEY") ?? sec["VapidPrivateKey"] ?? "";
    }

    public string VapidPublicKey => _vapidPublic;

    public async Task SaveSubscriptionAsync(int userId, string endpoint, string p256dh, string auth, string? userAgent)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(endpoint)) return;
        endpoint = endpoint.Trim();
        if (endpoint.Length > 500) endpoint = endpoint[..500];

        var existing = await _db.PushSubscriptions.FirstOrDefaultAsync(s => s.UserId == userId && s.Endpoint == endpoint);
        if (existing != null)
        {
            existing.LastSeenAt = DateTime.Now;
            if (!string.IsNullOrWhiteSpace(userAgent)) existing.UserAgent = userAgent;
        }
        else
        {
            _db.PushSubscriptions.Add(new Inventory.Api.Data.PushSubscription
            {
                UserId = userId,
                Endpoint = endpoint,
                P256DH = (p256dh ?? "").Trim(),
                Auth = (auth ?? "").Trim(),
                UserAgent = userAgent,
                CreatedAt = DateTime.Now,
                LastSeenAt = DateTime.Now
            });
        }
        await _db.SaveChangesAsync();
    }

    public async Task RemoveSubscriptionAsync(int userId, string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return;
        var rows = await _db.PushSubscriptions
            .Where(s => s.UserId == userId && s.Endpoint == endpoint.Trim())
            .ExecuteDeleteAsync();
        _ = rows;
    }

    public async Task SendToUserAsync(int userId, string title, string? body, string? link)
        => await SendToUsersAsync(new[] { userId }, title, body, link);

    public async Task SendToUsersAsync(IEnumerable<int> userIds, string title, string? body, string? link)
    {
        if (string.IsNullOrWhiteSpace(_vapidPublic) || string.IsNullOrWhiteSpace(_vapidPrivate))
            return; // VAPID پیکربندی نشده — push غیرفعال است

        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return;

        try
        {
            var subs = await _db.PushSubscriptions
                .Where(s => ids.Contains(s.UserId))
                .AsNoTracking()
                .ToListAsync();

            var vapid = new VapidDetails(_vapidSubject, _vapidPublic, _vapidPrivate);
            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                title,
                body,
                link,
                icon = "/icon-192.png",
                badge = "/icon-192.png"
            });

            foreach (var s in subs)
            {
                try
                {
                    var sub = new WebPush.PushSubscription(s.Endpoint, s.P256DH, s.Auth);
                    await _client.SendNotificationAsync(sub, payload, vapid);
                }
                catch (WebPushException ex)
                {
                    // 404/410 یعنی اشتراک منقضی/حذف شده — پاکش می‌کنیم
                    var code = ex.StatusCode;
                    if (code == System.Net.HttpStatusCode.NotFound || code == System.Net.HttpStatusCode.Gone)
                        await _db.PushSubscriptions.Where(x => x.Id == s.Id).ExecuteDeleteAsync();
                    else
                        _log.LogWarning("WebPush failed for sub {Id}: {Code} {Msg}", s.Id, code, ex.Message);
                }
                catch (Exception ex)
                {
                    _log.LogWarning("WebPush error for sub {Id}: {Msg}", s.Id, ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "PushService.SendToUsersAsync failed");
        }
    }
}