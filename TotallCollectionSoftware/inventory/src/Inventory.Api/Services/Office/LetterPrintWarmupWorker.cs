using Inventory.Api.Services.Office.Outgoing;

namespace Inventory.Api.Services.Office;

/// <summary>
/// گرم‌کردن مسیر چاپ نامه کمی پس از بالا آمدن برنامه.
///
/// مسئله: کش‌های چاپ نامه همگی «تنبل» هستند و تا اولین چاپ پر نمی‌شوند. نتیجه این بود که
/// اولین کاربرِ هر بار ری‌استارت، تمام هزینهٔ یک‌بارهٔ راه‌اندازی را می‌پرداخت (JIT شدن
/// QuestPDF/SkiaSharp/PDFsharp، ثبت فونت، استخراج سربرگ از MRT و رمزگشایی JPEG سربرگ) و
/// پیش‌نمایش ده‌ها ثانیه «در حال دریافت سند از سرور…» می‌ماند.
///
/// این ورکر همان کارها را یک بار در پس‌زمینه انجام می‌دهد تا کلیکِ کاربر فقط هزینهٔ رندر
/// خود نامه را بدهد. عمداً با تأخیر شروع می‌شود تا با بقیهٔ کارهای زمان بالا آمدن (مایگریشن،
/// ورکرهای دیگر) رقابت نکند، و هر خطا فقط لاگ می‌شود: گرم‌کردن هرگز نباید برنامه را
/// از کار بیندازد.
/// </summary>
public sealed class LetterPrintWarmupWorker : BackgroundService
{
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(20);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LetterPrintWarmupWorker> _logger;

    public LetterPrintWarmupWorker(IServiceScopeFactory scopeFactory, ILogger<LetterPrintWarmupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartDelay, stoppingToken);

            using var scope = _scopeFactory.CreateScope();
            var print = scope.ServiceProvider.GetRequiredService<OutgoingLetterPrintService>();
            await print.WarmupAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // خاموش شدن برنامه حین گرم‌کردن — وضعیت عادی است.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "گرم‌کردن مسیر چاپ نامه ناموفق بود؛ اولین چاپ کندتر خواهد بود.");
        }
    }
}
