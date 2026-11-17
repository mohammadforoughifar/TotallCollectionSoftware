using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Inventory.Api.Services.ItAssets;

/// <summary>
/// تنظیمات پیامک، نگهداری‌شده در حافظه.
///
/// چرا در حافظه و نه مستقیم از دیتابیس؟ چون IsEnabled / IsConfigured / IsLookupConfigured
/// در مسیرهای <b>همگام</b> و پرتکرار صدا زده می‌شوند (صف دستور کار، صف تعمیرات، واچرها و
/// چند سرویس دیگر). گذاشتن یک کوئری همگام داخل آن property‌ها هر بار یک thread را بلاک
/// می‌کند؛ زیر بار، استخر thread گرسنه می‌شود و درست همان چیزی پیش می‌آید که در عمل دیدیم:
/// تأخیرهای ده‌ها ثانیه‌ای و بعد شکستی که به‌اشتباه «ارسال پیامک غیرفعال است» تعبیر می‌شد،
/// بدون هیچ ردی در SQL Server (چون درخواست اصلاً به دیتابیس نمی‌رسید).
///
/// این کلاس فقط یک مرجع از حافظه برمی‌گرداند: صفر I/O، صفر بلاک شدن. خواندن از دیتابیس
/// به‌صورت ناهمگام و بیرون از مسیر داغ انجام می‌شود.
/// </summary>
public sealed class KavenegarSmsSettingsCache
{
    private readonly ILogger<KavenegarSmsSettingsCache> _log;

    // انتساب مرجع اتمیک است؛ volatile تضمین می‌کند thread‌های دیگر فوراً مقدار تازه را ببینند.
    private volatile KavenegarSmsOptions? _stored;
    private volatile bool _everLoaded;

    public KavenegarSmsSettingsCache(ILogger<KavenegarSmsSettingsCache> log) => _log = log;

    /// <summary>
    /// تنظیمات ذخیره‌شده در دیتابیس، یا <c>null</c> وقتی هنوز کلیدی ذخیره نشده
    /// (در آن حالت فراخوان باید مقدارهای appsettings.json را به کار ببرد).
    /// خواندن این مقدار هیچ تماسی با دیتابیس نمی‌گیرد.
    /// </summary>
    public KavenegarSmsOptions? Current => _stored;

    /// <summary>آیا دست‌کم یک بار با موفقیت از دیتابیس خوانده شده است؟</summary>
    public bool EverLoaded => _everLoaded;

    /// <summary>
    /// خواندن ناهمگام از دیتابیس. در صورت خطا، <b>آخرین مقدار معتبر حفظ می‌شود</b> و
    /// خطا لاگ می‌شود؛ یک اشکال گذرای دیتابیس نباید ارسال پیامک را خاموش کند.
    /// </summary>
    public async Task RefreshAsync(AppDbContext db, KavenegarSmsOptions file, CancellationToken ct = default)
    {
        try
        {
            // ترتیب صریح لازم است: این پروجکشن باریک و خواندن موجودیت کامل در صفحهٔ تنظیمات
            // دو شکل متفاوت دارند و بدون ORDER BY می‌توانند ردیف‌های متفاوتی برگردانند.
            // آن وقت صفحه روی یک ردیف می‌نویسد و ارسال از ردیف دیگری می‌خواند.
            var row = await db.AppSettings.AsNoTracking()
                .OrderBy(s => s.Id)
                .Select(s => new { s.Id, s.KavenegarApiKey, s.KavenegarSender, s.KavenegarSdkSendEnabled })
                .FirstOrDefaultAsync(ct);

            if (row is null || string.IsNullOrWhiteSpace(row.KavenegarApiKey))
            {
                // تازه‌سازی دوره‌ای هرگز یک مقدار سالم را خاموش نمی‌کند. پاک کردن کلید فقط
                // کار صریح مدیر است و از مسیر ذخیره بلافاصله اعمال می‌شود؛ خواندن خالی در
                // پس‌زمینه تقریباً همیشه نشانهٔ ناهنجاری است، نه خواستهٔ مدیر.
                if (_stored is not null)
                    _log.LogWarning("[SMS] تازه‌سازی، کلیدی در دیتابیس پیدا نکرد؛ مقدار سالم قبلی حفظ شد و ارسال خاموش نشد.");
                else if (!_everLoaded)
                    _log.LogWarning("[SMS] هنوز کلیدی در «تنظیمات ← سامانهٔ پیامکی» ذخیره نشده؛ مقدارهای appsettings.json به کار می‌روند.");
                _everLoaded = true;
                return;
            }

            var before = _stored?.SdkSendEnabled;
            Apply(row.KavenegarApiKey, row.KavenegarSender, row.KavenegarSdkSendEnabled, file);
            if (before != row.KavenegarSdkSendEnabled)
                _log.LogInformation(
                    "[SMS] وضعیت ارسال از دیتابیس به‌روز شد: فعال={Enabled} (ردیف {Id}، طول کلید {Length}).",
                    row.KavenegarSdkSendEnabled, row.Id, row.KavenegarApiKey.Trim().Length);
            _everLoaded = true;
        }
        catch (Exception ex)
        {
            // هرگز بی‌صدا نماند. نسخهٔ قبلی همین خطا را می‌بلعید و نتیجه‌اش «پیامک غیرفعال»
            // بود بدون هیچ سرنخی در لاگ — که تشخیص را ساعت‌ها طول داد.
            _log.LogWarning(ex,
                "[SMS] خواندن تنظیمات پیامک از دیتابیس ناموفق بود؛ مقدار قبلی حفظ شد (ارسال خاموش نمی‌شود).");
        }
    }

    /// <summary>
    /// اعمال فوری مقدارها پس از ذخیرهٔ تنظیمات، بدون انتظار برای تازه‌سازی دوره‌ای.
    /// کلید خالی یعنی «چیزی در دیتابیس نیست» و مقدارهای فایل دوباره معتبر می‌شوند.
    ///
    /// وقتی کلید ذخیره شده باشد، هر سه مقدار یکجا جای فایل را می‌گیرند تا ترکیب ناسازگار
    /// (مثلاً کلید حساب تازه با سوئیچ قدیمی) ساخته نشود. نام الگوها همیشه از فایل می‌آید.
    /// </summary>
    public void Apply(string? apiKey, string? sender, bool sdkSendEnabled, KavenegarSmsOptions file)
    {
        var key = apiKey?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            _stored = null;
            return;
        }

        _stored = new KavenegarSmsOptions
        {
            ApiKey = key,
            Sender = (sender ?? "").Trim(),
            SdkSendEnabled = sdkSendEnabled,
            TimeoutSeconds = file.TimeoutSeconds,
            RepairAcceptTemplate = file.RepairAcceptTemplate,
            RepairReadyTemplate = file.RepairReadyTemplate,
            RepairDeliveredTemplate = file.RepairDeliveredTemplate,
            WorkOrderRegistrationTemplate = file.WorkOrderRegistrationTemplate,
            WorkOrderReferralTemplate = file.WorkOrderReferralTemplate,
            WorkOrderReplyTemplate = file.WorkOrderReplyTemplate
        };
    }
}

/// <summary>
/// تازه‌سازی دوره‌ای تنظیمات پیامک در پس‌زمینه.
///
/// ذخیرهٔ تنظیمات از روی صفحه، کش را <b>بلافاصله</b> به‌روز می‌کند؛ این سرویس برای
/// بازیابی پس از خطای موقت دیتابیس و برای حالتی است که مقدارها مستقیم در دیتابیس
/// تغییر کنند. چون ناهمگام و بیرون از مسیر ارسال اجرا می‌شود، هیچ thread ای را بلاک نمی‌کند.
/// </summary>
public sealed class KavenegarSmsSettingsRefresher : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopes;
    private readonly KavenegarSmsSettingsCache _cache;
    private readonly IOptions<KavenegarSmsOptions> _file;
    private readonly ILogger<KavenegarSmsSettingsRefresher> _log;

    public KavenegarSmsSettingsRefresher(IServiceScopeFactory scopes, KavenegarSmsSettingsCache cache,
        IOptions<KavenegarSmsOptions> file, ILogger<KavenegarSmsSettingsRefresher> log)
    {
        _scopes = scopes;
        _cache = cache;
        _file = file;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await _cache.RefreshAsync(db, _file.Value, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "[SMS] تازه‌سازی دوره‌ای تنظیمات پیامک ناموفق بود.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
