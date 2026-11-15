using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Licensing.Services;

/// <summary>
/// جاروی رزروهای کهنه — یک سرویس پس‌زمینهٔ سبک.
///
/// <para>
/// <b>سناریویی که این سرویس برای آن است:</b> کاربر «ارسال به مودیان» را می‌زند؛ سهمیه
/// رزرو می‌شود؛ درست همان لحظه برق می‌رود/برنامه کرش می‌کند. آن رزرو هیچ‌وقت تسویه
/// نمی‌شود. بدون این سرویس، رزرو تا ابد در وضعیت <c>Reserved</c> می‌ماند و نه پشتیبانی
/// می‌فهمد، نه مدیر می‌تواند کاری کند.
/// </para>
///
/// <para>
/// <b>چه می‌کند؟</b> هر <c>Licensing:ReservationSweepIntervalMinutes</c> دقیقه
/// (پیش‌فرض ۵) رزروهایی را که بیش از <c>Licensing:ReservationTimeoutMinutes</c>
/// دقیقه (پیش‌فرض ۱۵) در وضعیت <c>Reserved</c> مانده‌اند به <c>Pending</c> می‌برد.
/// <b>هیچ واحدی برنمی‌گردد</b> — قاعدهٔ «نتیجهٔ نامعلوم ⇒ بازگشت نکن».
/// </para>
///
/// <para>
/// ⚠️ چرا این مقادیر از appsettings خوانده می‌شوند ولی سقف‌ها نه؟ چون این‌ها <b>سقف
/// سهمیه نیستند</b>؛ فقط زمان‌بندی پاک‌سازی‌اند. هیچ مقدار آن‌ها نمی‌تواند سهمیهٔ
/// بیشتری به مشتری بدهد (بازگشتی انجام نمی‌شود) — فقط سرعت تشخیص رزروهای گم‌شده
/// را تغییر می‌دهد.
/// </para>
/// </summary>
public sealed class LicenseReservationSweeper : BackgroundService
{
    /// <summary>عمر مجاز پیش‌فرض یک رزرو باز (دقیقه).</summary>
    public const int DefaultTimeoutMinutes = 15;

    /// <summary>فاصلهٔ پیش‌فرض بین دو جارو (دقیقه).</summary>
    public const int DefaultIntervalMinutes = 5;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LicenseReservationSweeper> _log;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _interval;

    public LicenseReservationSweeper(IServiceScopeFactory scopeFactory,
        ILogger<LicenseReservationSweeper> log, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _log = log;

        var timeoutMinutes = configuration.GetValue("Licensing:ReservationTimeoutMinutes", DefaultTimeoutMinutes);
        var intervalMinutes = configuration.GetValue("Licensing:ReservationSweepIntervalMinutes", DefaultIntervalMinutes);

        // محدودهٔ امن: کمتر از ۱ دقیقه یعنی رزروهای سالم هم «کهنه» به‌نظر می‌رسند و
        // بیشتر از ۲۴ ساعت یعنی تشخیص عملاً بی‌اثر. مقادیر بیرون بازه، به پیش‌فرض برمی‌گردند.
        if (timeoutMinutes is < 1 or > 1440) timeoutMinutes = DefaultTimeoutMinutes;
        if (intervalMinutes is < 1 or > 360) intervalMinutes = DefaultIntervalMinutes;

        _timeout = TimeSpan.FromMinutes(timeoutMinutes);
        _interval = TimeSpan.FromMinutes(intervalMinutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // یک بار، کمی بعد از بالا آمدن برنامه (تا اسکیمای لایسنس حتماً ساخته شده باشد).
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken).ContinueWith(_ => { }, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // اگر جدول لایسنس هنوز ساخته نشده (مثلاً SQLite تازه)، این‌جا خطا می‌دهد؛
                // یک بار لاگ می‌کنیم و ادامه می‌دهیم — سرویس نباید باعث کرش شود.
                if (await db.Database.CanConnectAsync(stoppingToken))
                {
                    var quota = scope.ServiceProvider.GetRequiredService<QuotaService>();
                    var swept = await quota.SweepStaleReservationsAsync(_timeout, stoppingToken);
                    if (swept > 0)
                        _log.LogWarning("[License] جاروی رزرو: {Count} رزرو کهنه به «در انتظار» منتقل شد.", swept);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // جارو یک کار نگه‌داری است؛ شکستش نباید برنامه را پایین بیاورد.
                _log.LogDebug(ex, "[License] جاروی رزروهای کهنه انجام نشد (تلاش بعدی در {Interval}).", _interval);
            }

            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
