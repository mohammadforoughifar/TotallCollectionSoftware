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
    private readonly LicenseEventLogger _events;
    private readonly ILogger<LicenseService> _log;

    /// <summary>آخرین روزی که برایش رخداد مهلت ثبت شده — جلوگیری از پر شدن جدول رخداد.</summary>
    private static DateTime _graceLoggedFor = DateTime.MinValue;

    public LicenseService(EntitlementCache cache, QuotaService quota, AppDbContext db,
        LicenseEventLogger events, ILogger<LicenseService> log)
    {
        _cache = cache;
        _quota = quota;
        _db = db;
        _events = events;
        _log = log;
    }

    // ==================== مهلت نصب موجود ====================

    /// <summary>
    /// ❌ <b>این متد بازنشسته شد</b> — همیشه <c>false</c>.
    ///
    /// <para>
    /// <b>تصمیم بازبینی چهارم (کارفرما):</b> «هیچ نصبی نباید به خودش مهلت بدهد.»
    /// نسخهٔ قبلی در اولین اجرا روی دیتابیسی که داده داشت، یک بازهٔ بی‌درنگ-قفل‌نشدن
    /// در دیتابیس خودِ مشتری ثبت می‌کرد. سه ایراد داشت:
    /// </para>
    /// <list type="number">
    ///   <item>مشتری می‌توانست آن را پاک/ریست کند (سه راه، دو تای‌شان به نفع او).</item>
    ///   <item>در آن بازه «همه‌چیز» باز بود — از جمله نوشتن؛ یعنی عملاً لایسنس لازم نبود.</item>
    ///   <item>تصمیمِ «چقدر مهلت» را نرم‌افزارِ روی ماشین مشتری می‌گرفت، نه ما.</item>
    /// </list>
    ///
    /// <para>
    /// <b>جایگزین: «لایسنس پل».</b> برای مشتری موجود، یک فایل لایسنس امضاشدهٔ کوتاه
    /// (Subscription، همهٔ ماژول‌ها) صادر می‌کنیم با
    /// <c>tools/license-sign.py --bridge --days 30</c>. تفاوت بنیادی: آن مهلت،
    /// <b>امضای ما</b> را دارد و در همان چارچوب سند لایسنس می‌گنجد؛ نه یک قاعدهٔ داخلی
    /// که خودِ نصب برای خودش تعیین کند.
    /// </para>
    /// </summary>
    /// <remarks>
    /// متد عمداً نگه داشته شده (بدون حذف از رابط) تا کدها/تست‌های موجود نشکنند؛ ولی
    /// هیچ‌جا دیگر «باز» نمی‌کند.
    /// </remarks>
    public Task<bool> IsInExistingInstallGraceAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(false);

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
        string? entityType = null, string? entityId = null, int? userId = null,
        bool recordLedger = true, CancellationToken cancellationToken = default)
    {
        var entitlement = await _cache.GetAsync(cancellationToken);

        // بررسی دوم (اولی در فیلتر است): اگر کسی فیلتر را دور زد، این‌جا گرفته می‌شود.
        var state = entitlement.GetState(module);
        if (state is ModuleState.Disabled or ModuleState.ReadOnly)
            return QuotaConsumeResult.Denied(metric, 0, 0, "-",
                $"ماژول «{ModuleCodes.TitleOf(module)}» در وضعیت «{StateTitle(state)}» است و ثبت رکورد در آن مجاز نیست.");

        return await _quota.TryConsumeAsync(entitlement, module, metric, amount,
            entityType, entityId, userId, recordLedger, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RecordUsageAsync(string module, string metric, int amount = 1, string? entityType = null,
        string? entityId = null, int? userId = null, CancellationToken cancellationToken = default)
    {
        var entitlement = await _cache.GetAsync(cancellationToken);
        await _quota.RecordUsageAsync(entitlement, module, metric, amount, entityType, entityId, userId,
            cancellationToken);
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
