using Inventory.Api.Data;
using Inventory.Licensing.Abstractions;
using Inventory.Licensing.Entitlement;
using Inventory.Licensing.Model;
using Inventory.Licensing.Modules;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Licensing.Services;

/// <summary>
/// سرویس اصلی لایسنس — تنها مرجع تصمیم‌گیری.
///
/// <para>
/// همهٔ چیزهایی که «قفل/باز» می‌کنند از همین‌جا رد می‌شوند: وضعیت ماژول، سهمیه، و اجازهٔ
/// ورود. هیچ controller یا صفحهٔ کلاینتی نباید خودش لایسنس را تفسیر کند.
/// </para>
/// </summary>
public sealed class LicenseService : ILicenseService
{
    private readonly EntitlementCache _cache;
    private readonly QuotaService _quota;
    private readonly AppDbContext _db;
    private readonly ILogger<LicenseService> _log;

    public LicenseService(EntitlementCache cache, QuotaService quota, AppDbContext db, ILogger<LicenseService> log)
    {
        _cache = cache;
        _quota = quota;
        _db = db;
        _log = log;
    }

    // ==================== وضعیت ماژول ====================

    public ModuleState GetModuleState(string module)
    {
        var entitlement = _cache.GetBlocking();

        // تا وقتی هیچ سند معتبری نیست، همه‌چیز جز پایه‌های همیشه‌فعال غیرفعال است.
        if (entitlement.Status != LicenseOverallStatus.Valid && !ModuleCodes.IsAlwaysOn(module))
            return ModuleState.Disabled;

        return entitlement.GetState(module);
    }

    public ModuleEntitlement? GetModule(string module)
    {
        var entitlement = _cache.GetBlocking();
        var found = entitlement.FindModule(module);
        if (found is not null) return found;

        return ModuleCodes.IsKnown(module)
            ? new ModuleEntitlement
            {
                Module = ModuleCodes.Normalize(module),
                State = ModuleState.Disabled,
                IsAlwaysOn = ModuleCodes.IsAlwaysOn(module)
            }
            : null;
    }

    // ==================== Entitlement ====================

    public EffectiveEntitlement GetEffectiveEntitlement() => _cache.GetBlocking();

    public Task<EffectiveEntitlement> GetEffectiveEntitlementAsync(CancellationToken cancellationToken = default)
        => _cache.GetAsync(cancellationToken);

    public LicenseOverallStatus GetOverallStatus() => _cache.GetBlocking().Status;

    // ==================== سهمیه ====================

    public async Task<QuotaConsumeResult> TryConsumeAsync(string module, string metric, int amount = 1,
        CancellationToken cancellationToken = default)
    {
        var entitlement = await _cache.GetAsync(cancellationToken);

        // بررسی دوم (اولی در فیلتر است): اگر کسی فیلتر را دور زد، این‌جا گرفته می‌شود.
        var state = entitlement.GetState(module);
        if (state is ModuleState.Disabled or ModuleState.ReadOnly)
            return QuotaConsumeResult.Denied(metric, 0, 0, "-",
                $"ماژول «{ModuleCodes.TitleOf(module)}» در وضعیت «{StateTitle(state)}» است و ثبت رکورد در آن مجاز نیست.");

        return await _quota.TryConsumeAsync(entitlement, module, metric, amount, cancellationToken);
    }

    // ==================== ورود کاربر ====================

    public async Task<SeatCheckResult> CanLoginUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        var entitlement = await _cache.GetAsync(cancellationToken);
        var seats = entitlement.Seats;

        if (entitlement.Status != LicenseOverallStatus.Valid)
            return SeatCheckResult.Denied("LICENSE_INVALID",
                "لایسنس این نصب معتبر نیست؛ فقط مدیر سیستم می‌تواند وارد شود.", 0, null);

        if (!seats.HasUserLimit) return SeatCheckResult.Ok();

        // معافیت صندلی: از فیلد امضاشده می‌آید و فقط به حساب‌های مشخص داده می‌شود
        // (به نقش Admin داده نمی‌شود).
        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.IsActive, u.LicenseSeatExempt })
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null) return SeatCheckResult.Denied("USER_NOT_FOUND", "کاربر یافت نشد.", 0, null);
        if (user.LicenseSeatExempt) return SeatCheckResult.Ok(0, null);

        if (seats.Mode == LicenseUserMode.Named)
        {
            // «کاربر نام‌دار» = تعداد حساب‌های فعال. ملاک فقط Users.IsActive است
            // (نه نقش، نه وجود ردیف RBAC).
            var activeCount = await _db.Users.AsNoTracking().CountAsync(u => u.IsActive, cancellationToken);
            var exemptActive = await _db.Users.AsNoTracking()
                .CountAsync(u => u.IsActive && u.LicenseSeatExempt, cancellationToken);

            // سقف معافیت از سند امضاشده می‌آید (users.exempt). اگر مدیر بیشتر از این تعداد
            // حساب معاف کرده باشد، حساب‌های اضافه «صندلی» حساب می‌شوند — وگرنه می‌شد با
            // علامت‌زدن همهٔ کاربران، سقف لایسنس را دور زد.
            var exemptAllowed = Math.Min(exemptActive, Math.Max(0, seats.MaxExempt));
            if (exemptActive > exemptAllowed)
                _log.LogWarning("[License] تعداد حساب‌های معاف ({Exempt}) از سقف مجاز ({Max}) بیشتر است؛ مازاد به‌عنوان صندلی شمرده می‌شود.",
                    exemptActive, seats.MaxExempt);

            var counted = Math.Max(0, activeCount - exemptAllowed);
            var max = seats.MaxForModules(ModuleCodes.All.Select(m => m.Code));

            if (max is { } cap && counted > cap)
                return SeatCheckResult.Denied("SEAT_LIMIT",
                    $"تعداد کاربران فعال ({counted}) از سقف لایسنس ({cap}) بیشتر است. برای افزایش سقف با پشتیبانی تماس بگیرید.",
                    counted, cap);

            return SeatCheckResult.Ok(counted, max);
        }

        if (seats.Mode == LicenseUserMode.Concurrent)
        {
            var max = seats.GlobalMax;
            if (max is null) return SeatCheckResult.Ok();

            // اگر همین کاربر از قبل نشست فعال دارد، ورود دوباره صندلی تازه نمی‌خواهد.
            var hasOwn = await _db.LicenseSessions.AsNoTracking()
                .AnyAsync(s => s.UserId == userId && s.Status == "Active", cancellationToken);
            if (hasOwn) return SeatCheckResult.Ok(0, max);

            var active = await _db.LicenseSessions.AsNoTracking()
                .CountAsync(s => s.Status == "Active", cancellationToken);

            if (active >= max.Value)
                return SeatCheckResult.Denied("SEAT_LIMIT",
                    $"همهٔ {max} صندلی هم‌زمان لایسنس اشغال است. چند دقیقه بعد یا پس از خروج یکی از کاربران تلاش کنید.",
                    active, max);

            return SeatCheckResult.Ok(active, max);
        }

        return SeatCheckResult.Ok();
    }

    public void Invalidate()
    {
        _cache.Invalidate();
        _log.LogInformation("[License] کش لایسنس باطل شد؛ بازخوانی بدون ری‌استارت.");
    }

    private static string StateTitle(ModuleState state) => state switch
    {
        ModuleState.Valid => "معتبر",
        ModuleState.ExpiringSoon => "در آستانهٔ انقضا",
        ModuleState.Grace => "مهلت تمدید",
        ModuleState.ReadOnly => "فقط خواندنی",
        _ => "غیرفعال"
    };
}
