using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Invoicing;

/// <summary>
/// سرویس پس‌زمینه ارسال خودکار صف مودیان.
/// با فاصله‌ی «SendIntervalMinutes» تنظیمات، فاکتورهای Queued را به سامانه
/// (یا در حالت شبیه‌سازی به‌صورت محلی) ارسال می‌کند.
/// </summary>
public class MoadianAutoSender : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<MoadianAutoSender> _log;
    private readonly IConfiguration _cfg;

    public MoadianAutoSender(IServiceProvider sp, ILogger<MoadianAutoSender> log, IConfiguration cfg)
    {
        _sp = sp; _log = log; _cfg = cfg;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // کمی صبر تا مایگریشن‌ها/آماده‌سازی کامل شود
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = TimeSpan.FromMinutes(5);
            try
            {
                using var scope = _sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var setting = await db.MoadianSettings.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync(stoppingToken);
                interval = TimeSpan.FromMinutes(Math.Max(1, setting?.SendIntervalMinutes ?? 5));

                if (setting is { AutoSend: true, IsActive: true })
                {
                    var svc = scope.ServiceProvider.GetRequiredService<IMoadianService>();
                    var sent = await svc.SendPendingAsync();
                    if (sent > 0) _log.LogInformation("ارسال خودکار مودیان: {Count} فاکتور ارسال شد", sent);
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _log.LogError(ex, "خطا در ارسال خودکار مودیان");
            }

            // اگر در تنظیمات، بازه‌ی کوتاه‌تری برای تست خواسته شد از پیکربندی خوانده می‌شود
            var overrideSec = _cfg.GetValue<int>("Moadian:AutoSendSeconds");
            var wait = overrideSec > 0 ? TimeSpan.FromSeconds(overrideSec) : interval;
            try { await Task.Delay(wait, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
