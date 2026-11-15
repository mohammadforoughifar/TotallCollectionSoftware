using Inventory.Licensing.Abstractions;
using Inventory.Licensing.Entitlement;
using Inventory.Licensing.Model;

namespace Inventory.Api.Licensing.Services;

/// <summary>
/// کشِ Entitlement مؤثر — <b>Singleton</b>.
///
/// <para>
/// چرا کش؟ هر درخواست API نباید چند کوئری دیتابیس و چند بررسی امضا بزند؛ و
/// <c>ActiveCompanyMiddleware</c> از قبل روی هر درخواست یک کوئری دارد. این کش با
/// <b>ابطال فوری</b> پس از هر تغییر لایسنس همراه است، پس «آپلود بدون ری‌استارت»
/// همان چیزی است که انتظار می‌رود.
/// </para>
///
/// <para>
/// ناسازگاری ساعت: TTL کوتاه (پیش‌فرض ۶۰ ثانیه) فقط برای حالت «تغییر از بیرون»
/// است؛ مسیر معمول، ابطال صریح است.
/// </para>
/// </summary>
public sealed class EntitlementCache
{
    private readonly IServiceScopeFactory _scopes;
    private readonly LicensingOptions _options;
    private readonly ILogger<EntitlementCache> _log;

    /// <summary>دور زدن لایسنس در محیط توسعه (در Production همیشه غیرفعال).</summary>
    private readonly DevelopmentLicenseBypass? _bypass;

    /// <summary>هشدار حالت توسعه فقط یک‌بار در هر فرایند چاپ می‌شود.</summary>
    private bool _bypassLogged;

    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private EffectiveEntitlement? _current;
    private DateTime _loadedAtUtc = DateTime.MinValue;

    /// <summary>نسخهٔ تغییر — برای اینکه مصرف‌کننده بفهمد شکل‌گیری دوباره رخ داده است.</summary>
    private long _revision;

    public EntitlementCache(IServiceScopeFactory scopes, LicensingOptions options, ILogger<EntitlementCache> log,
        DevelopmentLicenseBypass? bypass = null)
    {
        _scopes = scopes;
        _options = options;
        _log = log;
        _bypass = bypass;
    }

    /// <summary>نسخهٔ فعلی کش (برای تست و صفحهٔ وضعیت).</summary>
    public long Revision => Interlocked.Read(ref _revision);

    /// <summary>آخرین وضعیت بارگذاری‌شده، بدون تضمین تازگی (برای مسیرهای همگام).</summary>
    public EffectiveEntitlement? Peek => _current;

    /// <summary>مدت اعتبار کش.</summary>
    private TimeSpan Ttl => TimeSpan.FromSeconds(Math.Clamp(_options.EntitlementCacheSeconds, 5, 3600));

    /// <summary>باطل‌کردن کش — با تغییر لایسنس (اعمال/ابطال/تمدید).</summary>
    public void Invalidate()
    {
        _loadedAtUtc = DateTime.MinValue;
        Interlocked.Increment(ref _revision);
        _log.LogInformation("[License] کش Entitlement باطل شد.");
    }

    /// <summary>دریافت وضعیت مؤثر (با بارگذاری در صورت کهنگی).</summary>
    public async Task<EffectiveEntitlement> GetAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = _current;
        if (snapshot is not null && DateTime.UtcNow - _loadedAtUtc < Ttl) return snapshot;

        await _reloadGate.WaitAsync(cancellationToken);
        try
        {
            if (_current is not null && DateTime.UtcNow - _loadedAtUtc < Ttl) return _current;
            _current = await LoadAsync(cancellationToken);
            _loadedAtUtc = DateTime.UtcNow;
            return _current;
        }
        finally { _reloadGate.Release(); }
    }

    /// <summary>
    /// مسیر همگام — فقط برای امضاهایی که async نیستند (مثل <c>GetModuleState</c>).
    ///
    /// <para>
    /// در عمل هیچ‌وقت منتظر نمی‌ماند: <c>LicenseStartup</c> قبل از سرو شدن اولین درخواست
    /// کش را پر می‌کند. این تابع فقط «تور ایمنی» است برای حالتی که کش به هر دلیلی خالی
    /// مانده باشد.
    /// </para>
    /// </summary>
    public EffectiveEntitlement GetBlocking()
    {
        var snapshot = _current;
        if (snapshot is not null && DateTime.UtcNow - _loadedAtUtc < Ttl) return snapshot;
        return Task.Run(() => GetAsync()).GetAwaiter().GetResult();
    }

    private async Task<EffectiveEntitlement> LoadAsync(CancellationToken cancellationToken)
    {
        // ---- محیط توسعه: هیچ سندی لازم نیست ----
        // چرا این‌جا (و نه در Provider)؟ چون Providerها «واقعیتِ روی دیسک» را می‌خوانند؛
        // تصمیم «در توسعه لایسنس را جدی نگیر» مال میزبان است و باید پیش از هر کوئری/امضا
        // گرفته شود. نتیجه: در Development نه فایل برنامه‌ریزی‌شده لازم است، نه کلید عمومی،
        // نه دیتابیس — و هیچ خطای لایسنسی در لاگ نمی‌نشیند.
        if (_bypass?.IsActive == true)
        {
            if (!_bypassLogged)
            {
                _bypassLogged = true;
                _log.LogWarning("{Message}", _bypass.WarningMessage);
            }

            return _bypass.Build();
        }

        try
        {
            using var scope = _scopes.CreateScope();
            var provider = scope.ServiceProvider.GetRequiredService<ILicenseProvider>();
            var documents = await provider.LoadDocumentsAsync(cancellationToken);
            var entitlement = EntitlementMerger.Merge(documents);

            if (entitlement.Status != LicenseOverallStatus.Valid)
                _log.LogWarning("[License] وضعیت لایسنس: {Status} — Provider={Provider}", entitlement.Status, provider.Name);

            foreach (var issue in entitlement.Issues.Where(i => i.Severity == LicenseIssueSeverity.Error))
                _log.LogError("[License] {Code}: {Message}", issue.Code, issue.Message);

            return entitlement;
        }
        catch (Exception ex)
        {
            // ⚠️ هرگز برنامه را نمی‌اندازیم: نبودِ لایسنس یعنی «فقط ورود مدیر»، نه Crash.
            _log.LogError(ex, "[License] بارگذاری وضعیت لایسنس ناموفق بود.");
            return new EffectiveEntitlement
            {
                Status = LicenseOverallStatus.Invalid,
                ComputedAt = DateTime.Now,
                Issues = new[]
                {
                    new LicenseIssue("LICENSE_LOAD_FAILED", LicenseIssueSeverity.Error,
                        "وضعیت لایسنس قابل خواندن نبود. برای ورود، اطلاعات مدیر سیستم را وارد کنید و فایل لایسنس را بارگذاری کنید.")
                }
            };
        }
    }
}
