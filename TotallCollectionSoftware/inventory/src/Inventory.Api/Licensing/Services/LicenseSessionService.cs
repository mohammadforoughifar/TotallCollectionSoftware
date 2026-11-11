using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Licensing.Services;

/// <summary>
/// مدیریت نشست‌های فعال — پایهٔ سیاست «کاربر هم‌زمان».
///
/// <para>
/// مرحله ۱: جدول، سرویس و پاک‌سازی بیکارها ساخته می‌شود.
/// مرحله ۳: ساخت نشست هنگام ورود و claim <c>sid</c> در توکن به این سرویس وصل می‌شود.
/// </para>
///
/// <para>
/// چرا نشست سمت سرور لازم است؟ توکن JWT این پروژه ۱۲ ساعت اعتبار دارد و خروج فعلاً فقط
/// سمت کلاینت است. بدون ردیف نشست، «کاربر هم‌زمان» قابل اندازه‌گیری نیست و صندلی تا
/// پایان اعتبار توکن اشغال می‌ماند — حتی اگر کاربر مرورگر را ببندد.
/// </para>
/// </summary>
public sealed class LicenseSessionService
{
    private readonly AppDbContext _db;
    private readonly LicensingOptions _options;
    private readonly ILogger<LicenseSessionService> _log;

    public LicenseSessionService(AppDbContext db, LicensingOptions options, ILogger<LicenseSessionService> log)
    {
        _db = db;
        _options = options;
        _log = log;
    }

    /// <summary>مهلت بیکاری؛ بعد از این مدت صندلی آزاد می‌شود.</summary>
    public DateTime IdleCutoff => DateTime.Now.AddMinutes(-Math.Clamp(_options.SessionIdleMinutes, 1, 24 * 60));

    /// <summary>ساخت/فعال‌سازی نشست برای کاربر.</summary>
    public async Task<LicenseSession> StartAsync(int userId, string sessionId, string? machineName, string? ip,
        CancellationToken cancellationToken = default)
    {
        var existing = await _db.LicenseSessions.FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);
        if (existing is not null)
        {
            existing.Status = "Active";
            existing.LastSeen = DateTime.Now;
            existing.EndedAt = null;
            existing.EndReason = null;
            await _db.SaveChangesAsync(cancellationToken);
            return existing;
        }

        var session = new LicenseSession
        {
            SessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N") : sessionId,
            UserId = userId,
            StartedAt = DateTime.Now,
            LastSeen = DateTime.Now,
            MachineName = Truncate(machineName, 100),
            Ip = Truncate(ip, 64),
            Status = "Active"
        };
        _db.LicenseSessions.Add(session);
        await _db.SaveChangesAsync(cancellationToken);
        return session;
    }

    /// <summary>به‌روزرسانی آخرین فعالیت (heartbeat).</summary>
    public async Task TouchAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        var session = await _db.LicenseSessions.FirstOrDefaultAsync(s => s.SessionId == sessionId && s.Status == "Active", cancellationToken);
        if (session is null) return;
        // برای کاهش نوشتن، حداکثر یک‌بار در دقیقه
        if ((DateTime.Now - session.LastSeen).TotalSeconds < 60) return;
        session.LastSeen = DateTime.Now;
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>بستن یک نشست (خروج).</summary>
    public async Task CloseAsync(string? sessionId, string reason = "logout", CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        var session = await _db.LicenseSessions.FirstOrDefaultAsync(s => s.SessionId == sessionId && s.Status == "Active", cancellationToken);
        if (session is null) return;
        session.Status = "Closed";
        session.EndedAt = DateTime.Now;
        session.EndReason = Truncate(reason, 60);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>شمارش نشست‌های فعال.</summary>
    public Task<int> CountActiveAsync(CancellationToken cancellationToken = default)
        => _db.LicenseSessions.CountAsync(s => s.Status == "Active", cancellationToken);

    /// <summary>
    /// آزادسازی نشست‌های بیکار. هنگام راه‌اندازی برنامه و به‌صورت دوره‌ای اجرا می‌شود تا
    /// صندلی‌های رهاشده (بستن مرورگر بدون خروج) برنگردند.
    /// </summary>
    public async Task<int> CleanupIdleAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = IdleCutoff;
        var stale = await _db.LicenseSessions
            .Where(s => s.Status == "Active" && s.LastSeen < cutoff)
            .ToListAsync(cancellationToken);

        if (stale.Count == 0) return 0;

        foreach (var session in stale)
        {
            session.Status = "Evicted";
            session.EndedAt = DateTime.Now;
            session.EndReason = $"بیکاری بیش از {_options.SessionIdleMinutes} دقیقه";
        }
        await _db.SaveChangesAsync(cancellationToken);
        _log.LogInformation("[License] {Count} نشست بیکار آزاد شد.", stale.Count);
        return stale.Count;
    }

    private static string? Truncate(string? value, int max)
        => string.IsNullOrEmpty(value) ? value : (value.Length <= max ? value : value[..max]);
}
