using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.Licensing.Services;

/// <summary>
/// سنجش دوره‌ای ساعت (گام ۴-۴).
///
/// <para>
/// چرا پس‌زمینه و نه در هر درخواست؟ چون سنجش، سه ذخیره‌گاه و چند پرس‌وجوی «بیشترین
/// زمان» می‌خواهد؛ در هر درخواست HTTP گران بود. اما وضعیتِ سنجیده‌شده (یعنی «آیا
/// نوشتن بسته است؟») در <see cref="ClockGuardState"/> می‌ماند و فیلتر نوشتن در هر
/// درخواست همان را می‌خواند — پس قفل، بی‌درنگ اعمال می‌شود.
/// </para>
///
/// <para>
/// فاصلهٔ پیش‌فرض: ۱۰ دقیقه (<c>Licensing:ClockCheckIntervalMinutes</c>) و اجرای اول
/// چند ثانیه بعد از بالا آمدن برنامه. مقدار خیلی کم = فشار روی دیتابیس؛ مقدار خیلی
/// زیاد = تأخیر در تشخیص. هر دو کران‌دار است.
/// </para>
/// </summary>
public sealed class ClockGuardWorker : BackgroundService
{
    public const int DefaultIntervalMinutes = 10;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ClockGuardWorker> _log;
    private readonly TimeSpan _interval;

    public ClockGuardWorker(IServiceScopeFactory scopeFactory, ILogger<ClockGuardWorker> log,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _log = log;

        var minutes = configuration.GetValue("Licensing:ClockCheckIntervalMinutes", DefaultIntervalMinutes);
        if (minutes is < 1 or > 1440) minutes = DefaultIntervalMinutes;
        _interval = TimeSpan.FromMinutes(minutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken).ContinueWith(_ => { }, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
                if (await db.Database.CanConnectAsync(stoppingToken))
                {
                    var guard = scope.ServiceProvider.GetRequiredService<ClockGuard>();
                    await guard.EvaluateAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // سنجش ساعت یک کار حفاظتی است؛ شکستش نباید برنامه را بخواباند.
                _log.LogDebug(ex, "[License] سنجش ساعت انجام نشد (تلاش بعدی در {Interval}).", _interval);
            }

            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
