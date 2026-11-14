using Inventory.Api.Data;
using Inventory.Licensing.Abstractions;
using Inventory.Licensing.Entitlement;
using Inventory.Licensing.Model;
using Inventory.Licensing.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Inventory.Api.Licensing.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inventory.Api.Licensing.Services;

/// <summary>نتیجهٔ یک تصمیم صندلی.</summary>
public sealed class SeatGrant
{
    /// <summary>آیا درخواست/ورود پذیرفته شد؟</summary>
    public bool Allowed { get; init; }

    /// <summary>کد ماشین‌خوان: <c>OK</c>، <c>LICENSE_SEAT_FULL</c>، …</summary>
    public string Code { get; init; } = "OK";

    /// <summary>پیام فارسی برای نمایش به کاربر.</summary>
    public string Message { get; init; } = "";

    /// <summary>شناسهٔ نشست — همان claim <c>sid</c>.</summary>
    public string? SessionId { get; init; }

    /// <summary>کاربر معاف است (صندلی مصرف نمی‌کند).</summary>
    public bool IsExempt { get; init; }

    /// <summary>پاسخ در حالت تخریب‌شده (دیتابیس در دسترس نبود) صادر شده است.</summary>
    public bool Degraded { get; init; }

    /// <summary>تعداد صندلی مصرف‌شده در لحظهٔ تصمیم (برای پیام و لاگ).</summary>
    public int Used { get; init; }

    /// <summary>سقف مؤثر.</summary>
    public int? Limit { get; init; }

    /// <summary>نگاشت کد صندلی به کد وضعیت HTTP.</summary>
    public static int StatusFor(string code) => code switch
    {
        "LICENSE_SESSION_MISMATCH" => StatusCodes.Status401Unauthorized,
        // پایان پنجرهٔ توکن‌های بدون sid: پاسخ ۴۰۱ تا کلاینت «یک بار دیگر وارد شو» را نشان دهد.
        "LICENSE_SESSION_ENDED" => StatusCodes.Status401Unauthorized,
        "LICENSE_SEAT_BUSY" or "LICENSE_SEAT_UNAVAILABLE" => StatusCodes.Status503ServiceUnavailable,
        "LICENSE_SEAT_FULL" or "LICENSE_SEAT_ENDED" or "LICENSE_SEAT_UNKNOWN" => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status403Forbidden
    };

    public static SeatGrant Ok(string? sessionId = null, int used = 0, int? limit = null,
        bool exempt = false, bool degraded = false) => new()
    {
        Allowed = true,
        Code = degraded ? "LICENSE_SEAT_DEGRADED" : "OK",
        SessionId = sessionId,
        Used = used,
        Limit = limit,
        IsExempt = exempt,
        Degraded = degraded
    };

    public static SeatGrant Denied(string code, string message, int used = 0, int? limit = null) => new()
    {
        Allowed = false,
        Code = code,
        Message = message,
        Used = used,
        Limit = limit
    };
}

/// <summary>
/// منطق «صندلی کاربر» — قلب مرحله ۳.
///
/// <para>
/// سه تصمیم می‌گیرد:
/// <list type="number">
/// <item><b>ورود</b> (<see cref="BeginLoginAsync"/>) — آیا جای این حساب هست؟ چه <c>sid</c> بگیرد؟</item>
/// <item><b>هر درخواست</b> (<see cref="AuthorizeRequestAsync"/>) — آیا نشست این توکن هنوز معتبر است؟</item>
/// <item><b>هاب</b> (<see cref="ValidateHubAsync"/>) — همان بررسی، بدون گرفتن صندلی و بدون LastSeen.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>قفل (بند ۱۰):</b> روی SQL Server <c>sp_getapplock</c> با <c>@LockOwner='Transaction'</c>
/// و <c>LockTimeoutMs</c>؛ گرفته‌نشدن ⇒ <c>LICENSE_SEAT_BUSY</c> و پیام «دوباره تلاش کنید».
/// </para>
/// </summary>
public sealed class SeatService
{
    /// <summary>مدت انتظار برای گرفتن قفل صندلی (میلی‌ثانیه) — بند ۱۰.</summary>
    private const int LockTimeoutMs = 5000;

    /// <summary>نام منبع قفل در <c>sp_getapplock</c>.</summary>
    private const string AppLockResource = "license.seat";

    /// <summary>عمر ردیف نشست قبل از پاک‌سازی (روز).</summary>
    public const int PurgeDays = 30;

    /// <summary>
    /// قفل درون‌فرایندی. روی SQLite (و هر موتوری که <c>sp_getapplock</c> ندارد) تنها
    /// ضامن «شمارش و درج اتمیک» است؛ روی SQL Server قفل واقعی <c>sp_getapplock</c> است و
    /// این یکی فقط جلوی هزینهٔ رفت‌وبرگشت‌های هم‌زمان درون خود فرایند را می‌گیرد.
    /// </summary>
    private static readonly SemaphoreSlim ProcessLock = new(1, 1);

    private readonly AppDbContext _db;
    private readonly ILicenseService _licenses;
    private readonly LicenseSessionService _sessions;
    private readonly SeatCache _cache;
    private readonly SeatDegradedJournal _journal;
    private readonly LicensingOptions _options;
    private readonly LicenseRuntimeState _runtime;
    private readonly ILogger<SeatService> _log;

    public SeatService(AppDbContext db, ILicenseService licenses, LicenseSessionService sessions,
        SeatCache cache, SeatDegradedJournal journal, LicensingOptions options,
        LicenseRuntimeState runtime, ILogger<SeatService> log)
    {
        _runtime = runtime;
        _db = db;
        _licenses = licenses;
        _sessions = sessions;
        _cache = cache;
        _journal = journal;
        _options = options;
        _log = log;
    }

    private DateTime Now => ClockAnchor.Now();

    private DateTime IdleCutoff => Now.AddMinutes(-_options.EffectiveIdleMinutes());

    // ==================================================================
    //  ۱) ورود
    // ==================================================================

    /// <summary>
    /// تصمیم ورود: آیا این کاربر صندلی می‌گیرد؟ اگر آری، <c>sid</c> برمی‌گردد.
    /// </summary>
    public async Task<SeatGrant> BeginLoginAsync(int userId, string username, string? machineName, string? ip,
        CancellationToken cancellationToken = default)
    {
        if (_cache.IsStructural)
            return SeatGrant.Denied("LICENSE_SEAT_UNAVAILABLE",
                "ساختار جدول نشست‌ها در دسترس نیست؛ تا رفع مشکل، ورود کاربر جدید پذیرفته نمی‌شود.");

        var entitlement = await _licenses.GetEffectiveEntitlementAsync(cancellationToken);
        var seats = entitlement.Seats;

        // لایسنس پل / users:None ⇒ هیچ سقفی اعمال نمی‌شود.
        if (!entitlement.HasLicense || !seats.HasUserLimit)
        {
            try
            {
                var sidNone = await StartOrReuseSessionAsync(userId, machineName, ip, cancellationToken);
                _cache.MarkSuccess();
                return SeatGrant.Ok(sidNone);
            }
            catch (Exception ex) { return await HandleDatabaseFailureAsync(ex, "ساخت نشست", false); }
        }

        var cap = seats.Mode == LicenseUserMode.Named
            ? seats.MaxForModules(ModuleCodes.All.Select(m => m.Code))
            : seats.MaxForModules(ModuleCodes.All.Select(m => m.Code)) ?? seats.GlobalMax;

        // ---- معافیت: فقط از فهرست امضاشده ----
        if (seats.IsExempt(username))
        {
            try
            {
                var sid = await StartOrReuseSessionAsync(userId, machineName, ip, cancellationToken);
                _cache.MarkSuccess();
                return SeatGrant.Ok(sid, 0, cap, exempt: true);
            }
            catch (Exception ex) { return await HandleDatabaseFailureAsync(ex, "ساخت نشست معاف", false); }
        }

        if (seats.Mode == LicenseUserMode.Named)
        {
            // شمارش ساده (بدون قفل): Named صندلی را «نگه نمی‌دارد»؛ ملاک رتبهٔ Id است.
            try
            {
                var rank = await CountNamedThroughAsync(userId, seats, cancellationToken);
                if (cap is { } max && rank > max)
                    return SeatGrant.Denied("LICENSE_SEAT_FULL",
                        $"ظرفیت کاربران لایسنس پر است: این حساب نفر {rank} از {max} است و ورودش مجاز نیست. " +
                        "برای ورود، یکی از کاربران فعال را غیرفعال کنید یا لایسنس را افزایش دهید.",
                        rank, max);

                var sid = await StartOrReuseSessionAsync(userId, machineName, ip, cancellationToken);
                _cache.MarkSuccess();
                return SeatGrant.Ok(sid, rank, cap);
            }
            catch (SeatException) { throw; }
            catch (Exception ex)
            {
                return await HandleDatabaseFailureAsync(ex, "شمارش کاربران نام‌دار", false);
            }
        }

        // ---- Concurrent: شمارش نشست‌های فعال داخل قفل ----
        SeatLease? lease = null;
        try
        {
            lease = await EnterCriticalSectionAsync(cancellationToken);
            if (lease is null)
                return SeatGrant.Denied("LICENSE_SEAT_BUSY",
                    "سرور در این لحظه در حال ثبت ورود کاربران دیگر است. لطفاً چند لحظه بعد دوباره تلاش کنید.");

            var used = await CountConcurrentAsync(userId, cancellationToken);

            if (cap is { } limit && used >= limit)
            {
                var evicted = await TryEvictAsync(limit, cancellationToken);
                if (evicted is null)
                {
                    await lease.RollbackAsync(cancellationToken);
                    return SeatGrant.Denied("LICENSE_SEAT_FULL",
                        $"ظرفیت صندلی‌های لایسنس پر است ({used} از {limit}). چند دقیقه بعد دوباره تلاش کنید، " +
                        "از یکی از کاربران فعلی بخواهید خارج شود، یا برای افزایش ظرفیت با پشتیبانی تماس بگیرید.",
                        used, limit);
                }

                used = Math.Max(0, used - evicted.Value);
            }

            var sessionId = await StartOrReuseSessionAsync(userId, machineName, ip, cancellationToken);
            await lease.CommitAsync(cancellationToken);
            _cache.MarkSuccess();
            return SeatGrant.Ok(sessionId, used + 1, cap);
        }
        catch (SeatException) { throw; }
        catch (Exception ex)
        {
            if (lease is not null) await lease.RollbackQuietlyAsync();
            return await HandleDatabaseFailureAsync(ex, "شمارش صندلی هم‌زمان", false);
        }
        finally
        {
            lease?.Dispose();
        }
    }

    // ==================================================================
    //  ۲) هر درخواست
    // ==================================================================

    /// <summary>
    /// بررسی نشست برای یک درخواست API.
    ///
    /// <para>
    /// <paramref name="isActivity"/> = نتیجهٔ <see cref="Infrastructure.SeatActivityRules.IsActivity"/>.
    /// پولینگ پس‌زمینه: صندلی بررسی می‌شود ولی <c>LastSeen</c> تازه نمی‌شود و خطای
    /// «نشست تمام‌شده» هم برگردانده نمی‌شود (فقط اعتبار توکن ملاک است).
    /// </para>
    /// </summary>
    public async Task<SeatGrant> AuthorizeRequestAsync(int userId, string? sessionId, string username,
        bool isActivity, CancellationToken cancellationToken = default)
    {
        if (_cache.IsStructural)
            return SeatGrant.Ok(sessionId, degraded: true);

        EffectiveEntitlement entitlement;
        try { entitlement = await _licenses.GetEffectiveEntitlementAsync(cancellationToken); }
        catch (Exception ex) { return await HandleDatabaseFailureAsync(ex, "خواندن لایسنس", true); }

        var seats = entitlement.Seats;
        if (!entitlement.HasLicense || !seats.HasUserLimit)
            return SeatGrant.Ok(sessionId);

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            // توکن‌های صادرشده توسط نسخهٔ قبلی claim «sid» ندارند. دو خطای بد ممکن است:
            // رد فوری ⇒ همهٔ کاربران بدون توضیح بیرون می‌افتند؛ پذیرش دائمی ⇒ نگه‌داشتن
            // توکن قدیمی، سقف صندلی را دور می‌زند. راه میانه: پنجرهٔ گذار از «اولین
            // استارت همین بیلد» (پیش‌فرض ۱۳ ساعت، بیش از حداکثر عمر ۱۲ ساعتهٔ توکن).
            var legacyOpen = await _runtime.IsLegacyTokenWindowOpenAsync(Now, cancellationToken);
            if (!legacyOpen)
                return SeatGrant.Denied(LicenseRuntimeState.SessionEndedCode,
                    "نشست این توکن پایان یافته است؛ یک بار دیگر وارد شوید.");

            _log.LogInformation(
                "[License] توکن بدون sid در پنجرهٔ گذار پذیرفته شد (کاربر {UserId}). صندلی مصرف نمی‌شود.",
                userId);
            return SeatGrant.Ok(sessionId);
        }

        var cacheSeconds = _options.EffectiveSeatCacheSeconds();
        var now = Now;

        // ---- کش: حداکثر یک کوئری در پنجرهٔ SeatCacheSeconds ----
        LicenseSession? row;
        var cached = _cache.TryGet(sessionId!, now, out var entry);

        if (cached)
        {
            if (entry.UserId != userId)
                return SeatGrant.Denied("LICENSE_SESSION_MISMATCH", "این نشست به کاربر دیگری تعلق دارد.");

            if (!string.Equals(entry.Status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                if (!isActivity) return SeatGrant.Ok(sessionId);
                return entry.Status == "Expired"
                    ? await RetakeAsync(userId, sessionId!, username, seats, isActivity, cancellationToken)
                    : SeatGrant.Denied("LICENSE_SEAT_ENDED",
                        "نشست شما بسته شده است؛ لطفاً دوباره وارد شوید.");
            }

            if (isActivity && (now - entry.TouchedAt).TotalSeconds >= 60)
            {
                entry.TouchedAt = now;
                try { await _sessions.TouchAsync(sessionId!, cancellationToken); }
                catch (Exception ex) { return await HandleDatabaseFailureAsync(ex, "به‌روزرسانی LastSeen", true); }
            }

            return SeatGrant.Ok(sessionId);
        }

        try
        {
            row = await _sessions.FindAsync(sessionId!, cancellationToken);
        }
        catch (Exception ex)
        {
            return await HandleDatabaseFailureAsync(ex, "خواندن نشست", true);
        }

        if (row is null)
        {
            // نشست ناشناخته: اگر ظرفیت آزاد است، همان توکن صندلی را بازپس می‌گیرد
            // (مثلاً جدول نشست‌ها پاک/بازسازی شده). وگرنه «تمام‌شده» یعنی باید دوباره ورود کند.
            if (!isActivity) return SeatGrant.Ok(sessionId);
            return await RetakeAsync(userId, sessionId!, username, seats, isActivity, cancellationToken);
        }

        if (row.UserId != userId)
            return SeatGrant.Denied("LICENSE_SESSION_MISMATCH", "این نشست به کاربر دیگری تعلق دارد.");

        if (!string.Equals(row.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            if (!isActivity) return SeatGrant.Ok(sessionId);

            if (string.Equals(row.Status, "Expired", StringComparison.OrdinalIgnoreCase))
                return await RetakeAsync(userId, sessionId!, username, seats, isActivity, cancellationToken);

            return SeatGrant.Denied("LICENSE_SEAT_ENDED",
                "نشست شما بسته شده است؛ لطفاً دوباره وارد شوید.");
        }

        _cache.Set(sessionId!, userId, "Active", cacheSeconds, now);

        if (isActivity)
        {
            try { await _sessions.TouchAsync(sessionId!, cancellationToken); }
            catch (Exception ex) { return await HandleDatabaseFailureAsync(ex, "به‌روزرسانی LastSeen", true); }
        }

        return SeatGrant.Ok(sessionId);
    }

    /// <summary>
    /// بازپس‌گیری صندلی برای توکنی که ردیف نشستش نیست یا منقضی شده — اگر ظرفیت آزاد
    /// باشد، بدون اینکه کاربر مجبور به ورود دوباره شود.
    /// </summary>
    private async Task<SeatGrant> RetakeAsync(int userId, string sessionId, string username,
        UserSeatPolicy seats, bool isActivity, CancellationToken cancellationToken)
    {
        if (seats.IsExempt(username))
        {
            try
            {
                await StartOrReuseSessionAsync(userId, null, null, cancellationToken);
                return SeatGrant.Ok(sessionId, exempt: true);
            }
            catch (Exception ex) { return await HandleDatabaseFailureAsync(ex, "بازپس‌گیری معاف", true); }
        }

        var cap = seats.MaxForModules(ModuleCodes.All.Select(m => m.Code)) ?? seats.GlobalMax;

        SeatLease? lease = null;
        try
        {
            lease = await EnterCriticalSectionAsync(cancellationToken);
            if (lease is null)
                return SeatGrant.Denied("LICENSE_SEAT_BUSY",
                    "سرور در این لحظه در حال ثبت ورود کاربران دیگر است. لطفاً چند لحظه بعد دوباره تلاش کنید.");

            var used = await CountConcurrentAsync(userId, cancellationToken);
            if (cap is { } limit && used >= limit)
            {
                await lease.RollbackAsync(cancellationToken);
                return SeatGrant.Denied("LICENSE_SEAT_FULL",
                    $"ظرفیت صندلی‌های لایسنس پر است ({used} از {limit}). " +
                    "چند دقیقه بعد دوباره تلاش کنید یا از یکی از کاربران فعلی بخواهید خارج شود.",
                    used, limit);
            }

            await StartOrReuseSessionAsync(userId, null, null, cancellationToken);
            await lease.CommitAsync(cancellationToken);

            var cacheSeconds = _options.EffectiveSeatCacheSeconds();
            _cache.Set(sessionId, userId, "Active", cacheSeconds, Now);
            _cache.MarkSuccess();

            _log.LogInformation("[License] صندلی برای کاربر {UserId} بازپس گرفته شد (نشست تازه).", userId);
            return SeatGrant.Ok(sessionId);
        }
        catch (Exception ex)
        {
            if (lease is not null) await lease.RollbackQuietlyAsync();
            return await HandleDatabaseFailureAsync(ex, "بازپس‌گیری صندلی", true);
        }
        finally
        {
            lease?.Dispose();
        }
    }

    // ==================================================================
    //  ۳) هاب‌های بلادرنگ (بند ۶) — فقط می‌خواند
    // ==================================================================

    /// <summary>
    /// بررسی اتصال هاب: نه صندلی می‌گیرد، نه <c>LastSeen</c> را می‌زند، نه صندلی را
    /// بازپس می‌گیرد. اگر <c>sid</c> ناشناخته/بسته باشد، اتصال رد می‌شود.
    /// </summary>
    public async Task<SeatGrant> ValidateHubAsync(int userId, string? sessionId, string username,
        CancellationToken cancellationToken = default)
    {
        if (_cache.IsStructural) return SeatGrant.Ok(sessionId, degraded: true);

        EffectiveEntitlement entitlement;
        try { entitlement = await _licenses.GetEffectiveEntitlementAsync(cancellationToken); }
        catch (Exception ex) { return await HandleDatabaseFailureAsync(ex, "خواندن لایسنس (هاب)", true); }

        var seats = entitlement.Seats;
        if (!entitlement.HasLicense || !seats.HasUserLimit) return SeatGrant.Ok(sessionId);
        if (string.IsNullOrWhiteSpace(sessionId))
            return SeatGrant.Denied("LICENSE_SEAT_UNKNOWN", "این توکن شناسهٔ نشست ندارد.");

        LicenseSession? row;
        try { row = await _sessions.FindAsync(sessionId!, cancellationToken); }
        catch (Exception ex) { return await HandleDatabaseFailureAsync(ex, "خواندن نشست (هاب)", true); }

        if (row is null)
            return SeatGrant.Denied("LICENSE_SEAT_UNKNOWN",
                "نشست این توکن پیدا نشد؛ لطفاً دوباره وارد شوید.");

        if (row.UserId != userId)
            return SeatGrant.Denied("LICENSE_SESSION_MISMATCH", "این نشست به کاربر دیگری تعلق دارد.");

        if (!string.Equals(row.Status, "Active", StringComparison.OrdinalIgnoreCase))
            return SeatGrant.Denied("LICENSE_SEAT_ENDED",
                "نشست شما بسته شده است؛ لطفاً دوباره وارد شوید.");

        return SeatGrant.Ok(sessionId);
    }

    // ==================================================================
    //  ۴) گارد ساخت/فعال‌سازی کاربر (Named)
    // ==================================================================

    /// <summary>
    /// پیش از ساخت کاربر یا روشن‌کردن <c>IsActive</c>: آیا جا هست؟ (بند ۱.۱)
    /// فقط برای سیاست Named و فقط حساب‌های قابل‌ورودِ غیرمعاف.
    /// </summary>
    public async Task EnsureUserSlotAvailableAsync(int? userId, string username, bool willBeActive,
        CancellationToken cancellationToken = default)
    {
        if (!willBeActive) return;   // غیرفعال‌کردن هرگز مسدود نمی‌شود

        var entitlement = await _licenses.GetEffectiveEntitlementAsync(cancellationToken);
        var seats = entitlement.Seats;
        if (!entitlement.HasLicense || !seats.HasUserLimit) return;
        if (seats.Mode != LicenseUserMode.Named) return;   // هم‌زمان: سقف هنگام ورود اعمال می‌شود
        if (seats.IsExempt(username)) return;

        var cap = seats.MaxForModules(ModuleCodes.All.Select(m => m.Code));
        if (cap is null) return;

        var here = userId ?? 0;
        var countable = await CountNamedAsync(seats, here, cancellationToken);
        var afterCreate = countable + 1;

        if (afterCreate > cap)
            throw new Infrastructure.SeatException("LICENSE_SEAT_FULL",
                $"ظرفیت کاربران لایسنس پر است (سقف {cap.Value}، حساب فعالِ غیرمعاف {countable}). " +
                "یا یکی از حساب‌های فعال را غیرفعال کنید، یا لایسنس را افزایش دهید.");
    }

    // ==================================================================
    //  ۵) شمارش‌ها
    // ==================================================================

    /// <summary>
    /// تعداد صندلی‌های مصرف‌شده در سیاست «هم‌زمان».
    /// ملاک: <c>Status='Active'</c> و <c>LastSeen &gt; now − SessionIdleMinutes</c>؛
    /// کاربران معاف کسر می‌شوند و <paramref name="excludeUserId"/> (خودِ کاربر) شمرده نمی‌شود.
    /// </summary>
    public async Task<int> CountConcurrentAsync(int excludeUserId, CancellationToken cancellationToken = default)
    {
        var entitlement = await _licenses.GetEffectiveEntitlementAsync(cancellationToken);
        var seats = entitlement.Seats;
        if (!entitlement.HasLicense || !seats.HasUserLimit) return 0;

        var cutoff = IdleCutoff;
        var exemptIds = await ExemptUserIdsAsync(seats, cancellationToken);

        var query = _db.LicenseSessions.AsNoTracking()
            .Where(s => s.Status == "Active" && s.LastSeen > cutoff);

        if (excludeUserId > 0) query = query.Where(s => s.UserId != excludeUserId);
        if (exemptIds.Count > 0) query = query.Where(s => !exemptIds.Contains(s.UserId));

        return await query.Select(s => s.UserId).Distinct().CountAsync(cancellationToken);
    }

    /// <summary>رتبهٔ کاربر در سیاست Named = تعداد حساب‌های فعالِ غیرمعاف با <c>Id</c> کوچک‌تر یا مساوی.</summary>
    private async Task<int> CountNamedThroughAsync(int userId, UserSeatPolicy seats, CancellationToken cancellationToken)
    {
        var names = seats.EffectiveExemptUsernames.ToList();

        var query = _db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Id <= userId);

        if (names.Count > 0) query = query.Where(u => !names.Contains(u.Username));

        var rows = await query.Select(u => u.PasswordHash).ToListAsync(cancellationToken);
        return rows.Count(IsLoginCapable);
    }

    /// <summary>تعداد حساب‌های فعالِ غیرمعاف (بدون <paramref name="excludeId"/>).</summary>
    private async Task<int> CountNamedAsync(UserSeatPolicy seats, int excludeId, CancellationToken cancellationToken)
    {
        var names = seats.EffectiveExemptUsernames.ToList();

        var query = _db.Users.AsNoTracking().Where(u => u.IsActive && u.Id != excludeId);
        if (names.Count > 0) query = query.Where(u => !names.Contains(u.Username));

        var rows = await query.Select(u => u.PasswordHash).ToListAsync(cancellationToken);
        return rows.Count(IsLoginCapable);
    }

    /// <summary>شناسهٔ کاربران معافِ ثبت‌شده در دیتابیس (نام‌های امضاشده).</summary>
    private async Task<List<int>> ExemptUserIdsAsync(UserSeatPolicy seats, CancellationToken cancellationToken)
    {
        var names = seats.EffectiveExemptUsernames;
        if (names.Count == 0) return new List<int>();

        var list = names.ToList();
        return await _db.Users.AsNoTracking()
            .Where(u => list.Contains(u.Username))
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>آیا این حساب اصلاً می‌تواند وارد شود؟ (حساب سیستمی نه)</summary>
    public static bool IsLoginCapable(string? passwordHash)
        => !string.IsNullOrEmpty(passwordHash) && !passwordHash.StartsWith('!');

    // ==================================================================
    //  ۶) نشست
    // ==================================================================

    /// <summary>
    /// نشست کاربر: اگر نشست فعال دارد همان <c>sid</c> برمی‌گردد (تب/دستگاه دوم صندلی
    /// تازه نمی‌گیرد)؛ وگرنه شناسهٔ تصادفی تازه ساخته می‌شود.
    /// </summary>
    private async Task<string> StartOrReuseSessionAsync(int userId, string? machineName, string? ip,
        CancellationToken cancellationToken)
    {
        var existing = await _sessions.FindActiveForUserAsync(userId, IdleCutoff, cancellationToken);
        if (existing is not null)
        {
            await _sessions.TouchAsync(existing.SessionId, cancellationToken, force: true);
            return existing.SessionId;
        }

        var sid = Guid.NewGuid().ToString("N");
        await _sessions.StartAsync(userId, sid, machineName, ip, cancellationToken);
        _cache.Remove(sid);
        return sid;
    }

    /// <summary>بستن نشست (خروج) و آزادکردن فوری صندلی.</summary>
    public async Task ReleaseAsync(string? sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        await _sessions.CloseAsync(sessionId, "logout", cancellationToken);
        _cache.Remove(sessionId);
    }

    // ==================================================================
    //  ۷) بازپس‌گیری خودکار (eviction)
    // ==================================================================

    /// <summary>
    /// بند ۱۲: پیش‌فرض <c>deny</c> است. اگر مشتری عمداً <c>evict-oldest-idle</c> بگذارد،
    /// قدیمی‌ترین نشستی که بیش از <c>SeatEvictionMinIdleMinutes</c> بیکار بوده بیرون می‌رود.
    /// خروجی: تعداد صندلی آزادشده یا <c>null</c> اگر کسی واجد شرط نبود.
    /// </summary>
    private async Task<int?> TryEvictAsync(int limit, CancellationToken cancellationToken)
    {
        if (!string.Equals(_options.EffectiveSeatEviction(), LicensingOptions.SeatEvictionOldestIdle,
                StringComparison.OrdinalIgnoreCase))
            return null;

        var minIdle = _options.EffectiveSeatEvictionMinIdleMinutes();
        var cutoff = Now.AddMinutes(-minIdle);

        var victim = await _db.LicenseSessions
            .Where(s => s.Status == "Active" && s.LastSeen < cutoff)
            .OrderBy(s => s.LastSeen)
            .FirstOrDefaultAsync(cancellationToken);

        if (victim is null) return null;

        victim.Status = "Evicted";
        victim.EndedAt = Now;
        victim.EndReason = $"بیرون‌انداخته‌شد برای ورود کاربر تازه (سقف {limit})";
        await _db.SaveChangesAsync(cancellationToken);
        _cache.Remove(victim.SessionId);

        _log.LogWarning("[License] نشست بیکار {SessionId} (کاربر {UserId}) به‌خاطر پر بودن ظرفیت بیرون انداخته شد.",
            victim.SessionId, victim.UserId);
        return 1;
    }

    // ==================================================================
    //  ۸) قفل و تراکنش
    // ==================================================================

    /// <summary>
    /// ورود به بخش بحرانی: قفل درون‌فرایندی + (روی SQL Server) تراکنش با sp_getapplock.
    /// خروجی <c>null</c> یعنی «قفل گرفته نشد» ⇒ ورود باید با <c>LICENSE_SEAT_BUSY</c> رد شود.
    /// </summary>
    private async Task<SeatLease?> EnterCriticalSectionAsync(CancellationToken cancellationToken)
    {
        if (!await ProcessLock.WaitAsync(LockTimeoutMs, cancellationToken))
        {
            _log.LogWarning("[License] قفل درون‌فرایندی صندلی در {Timeout}ms گرفته نشد.", LockTimeoutMs);
            return null;
        }

        IDbContextTransaction? transaction = null;
        try
        {
            if (_db.Database.IsRelational())
            {
                transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

                if (_db.Database.IsSqlServer() && !await TryAppLockAsync(cancellationToken))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    await transaction.DisposeAsync();
                    ProcessLock.Release();
                    return null;
                }
            }

            return new SeatLease(_db, transaction, ProcessLock);
        }
        catch
        {
            if (transaction is not null)
            {
                try { await transaction.RollbackAsync(cancellationToken); } catch { }
                await transaction.DisposeAsync();
            }
            ProcessLock.Release();
            throw;
        }
    }

    /// <summary>
    /// <c>sp_getapplock</c> با <c>@LockOwner='Transaction'</c> — بند ۱۰.
    /// نتیجهٔ منفی یعنی «نگرفت» (۰ = timeout، ‎-۱ = لغو، …) و همان «دوباره تلاش کنید».
    /// </summary>
    private async Task<bool> TryAppLockAsync(CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        var efTransaction = _db.Database.CurrentTransaction;
        if (efTransaction is null) return false;

        await using var command = connection.CreateCommand();
        command.Transaction = efTransaction.GetDbTransaction();
        command.CommandText =
            "DECLARE @rc int; " +
            "EXEC @rc = sp_getapplock @Resource = @res, @LockMode = 'Exclusive', " +
            "     @LockOwner = 'Transaction', @LockTimeout = @timeout; SELECT @rc;";
        command.CommandTimeout = (LockTimeoutMs / 1000) + 5;
        AddParameter(command, "@res", AppLockResource);
        AddParameter(command, "@timeout", LockTimeoutMs);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        var code = result is null or DBNull ? -999 : Convert.ToInt32(result);
        if (code >= 0) return true;

        _log.LogWarning("[License] sp_getapplock با کد {Code} نگرفت (0 = timeout).", code);
        return false;
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    // ==================================================================
    //  ۹) خطای پایگاه داده (بند ۲)
    // ==================================================================

    /// <summary>خطای پایگاه داده در مسیر صندلی: ساختاری ⇒ رد، گذرا ⇒ پنجرهٔ ۱۰ دقیقه.</summary>
    /// <param name="allowExisting">
    /// در مسیر «هر درخواست/هاب» نشست موجود باید ادامه دهد (فقط برچسب degraded می‌گیرد)؛
    /// در مسیر «ورود جدید» نه — چون نمی‌توانیم بشماریم و صندلی بی‌حساب پخش می‌شود.
    /// </param>
    private async Task<SeatGrant> HandleDatabaseFailureAsync(Exception ex, string what, bool allowExisting)
    {
        var failure = SeatErrors.Classify(ex);
        var now = ClockAnchor.Now();

        if (failure == SeatFailureClass.Structural)
        {
            _cache.MarkStructural();
            _log.LogWarning(ex, "[License] خطای ساختاری پایگاه داده در {What}.", what);

            try
            {
                _db.LicenseEvents.Add(new Data.LicenseEvent
                {
                    At = now,
                    Kind = "SEAT_SCHEMA_BROKEN",
                    Severity = "Warning",
                    Message = $"ساختار جدول نشست‌های لایسنس قابل استفاده نیست ({what}). تا رفع مشکل، ورود کاربر جدید رد می‌شود.",
                    DataJson = System.Text.Json.JsonSerializer.Serialize(new { what, error = ex.Message })
                });
                await _db.SaveChangesAsync();
            }
            catch (Exception logEx)
            {
                _log.LogError(logEx, "[License] ثبت رخداد SEAT_SCHEMA_BROKEN هم ممکن نشد.");
            }

            if (allowExisting)
                return SeatGrant.Ok(degraded: true);

            return SeatGrant.Denied("LICENSE_SEAT_UNAVAILABLE",
                "فهرست نشست‌های لایسنس در دسترس نیست؛ تا رفع مشکل، ورود کاربر جدید پذیرفته نمی‌شود. " +
                "کاربران فعلی می‌توانند به کار خود ادامه دهند.");
        }

        var withinWindow = _cache.MarkTransientFailure(now);
        _log.LogWarning(ex, "[License] خطای گذرا در {What} (پنجرهٔ تخریب: {Within}).", what, withinWindow);

        if (!withinWindow)
            return SeatGrant.Denied("LICENSE_SEAT_UNAVAILABLE",
                "ارتباط با پایگاه داده بیش از ۱۰ دقیقه قطع بوده است؛ ورود کاربر جدید موقتاً پذیرفته نمی‌شود. " +
                "لطفاً بعد از رفع مشکل دوباره تلاش کنید.");

        if (allowExisting)
            return SeatGrant.Ok(degraded: true);

        return SeatGrant.Ok(degraded: true);
    }

    // ==================================================================
    //  ۱۰) کمکی‌ها
    // ==================================================================

    private static string? Truncate(string? value, int max)
        => string.IsNullOrEmpty(value) ? value : (value.Length <= max ? value : value[..max]);
}

/// <summary>اجارهٔ بخش بحرانی: تراکنش + قفل درون‌فرایندی، با آزادسازی تضمین‌شده.</summary>
internal sealed class SeatLease : IDisposable
{
    private readonly IDbContextTransaction? _transaction;
    private readonly SemaphoreSlim _processLock;
    private bool _finished;
    private bool _disposed;

    public SeatLease(AppDbContext db, IDbContextTransaction? transaction, SemaphoreSlim processLock)
    {
        _transaction = transaction;
        _processLock = processLock;
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (_transaction is not null) await _transaction.CommitAsync(cancellationToken);
        _finished = true;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        if (_transaction is not null) await _transaction.RollbackAsync(cancellationToken);
        _finished = true;
    }

    public async Task RollbackQuietlyAsync()
    {
        try { if (_transaction is not null && !_finished) await _transaction.RollbackAsync(); }
        catch { /* بی‌صدا */ }
        _finished = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_transaction is not null)
            {
                if (!_finished) { try { _transaction.Rollback(); } catch { } }
                _transaction.Dispose();
            }
        }
        catch { /* بی‌صدا */ }

        _processLock.Release();
    }
}
