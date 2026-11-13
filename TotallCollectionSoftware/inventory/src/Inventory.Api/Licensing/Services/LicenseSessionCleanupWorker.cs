namespace Inventory.Api.Licensing.Services;

/// <summary>
/// پاک‌سازی دوره‌ای نشست‌های بیکار.
///
/// <para>
/// «هنگام راه‌اندازی برنامه نشست‌های قدیمی پاک شوند» — بخشی از خواستهٔ مرحله ۳ که
/// زیرساختش همین‌جاست: اولین اجرا بلافاصله پس از بالا آمدن، و بعد هر
/// <c>SessionCleanupSeconds</c> یک‌بار.
/// </para>
/// </summary>
public sealed class LicenseSessionCleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly LicensingOptions _options;
    private readonly ILogger<LicenseSessionCleanupWorker> _log;

    public LicenseSessionCleanupWorker(IServiceScopeFactory scopes, LicensingOptions options,
        ILogger<LicenseSessionCleanupWorker> log)
    {
        _scopes = scopes;
        _options = options;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // کمی صبر تا دیتابیس و مایگریشن‌ها کامل شوند و برنامه سرویس‌دهی را شروع کند.
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        var period = TimeSpan.FromSeconds(Math.Clamp(_options.SessionCleanupSeconds, 30, 3600));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var sessions = scope.ServiceProvider.GetRequiredService<LicenseSessionService>();
                await sessions.CleanupIdleAsync(stoppingToken);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "[License] پاک‌سازی نشست‌های بیکار ناموفق بود.");
            }

            try { await Task.Delay(period, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
