using System.Diagnostics;
using Microsoft.JSInterop;

namespace Inventory.Client.Services;

/// <summary>Shared popup, loading, PDF fallback, and error flow for internal and outgoing letters.</summary>
public sealed class LetterPrintWorkflow
{
    // Download (server PDF generation + transfer) and the browser hand-off get SEPARATE budgets.
    // Previously one 15s budget covered both: a cold server (first QuestPDF/font/letterhead run)
    // could eat the whole budget, so the hand-off was born already expired and the print window
    // seemed to "never load". The API client allows 100s per call; 90s stays inside that envelope.
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan HandoffTimeout = TimeSpan.FromSeconds(15);

    private readonly IJSRuntime _js;
    private readonly IToastService _toast;
    private readonly ILogger<LetterPrintWorkflow> _logger;

    public LetterPrintWorkflow(IJSRuntime js, IToastService toast, ILogger<LetterPrintWorkflow> logger)
    {
        _js = js;
        _toast = toast;
        _logger = logger;
    }

    // Keep the original overload for other callers, while print paths that support it
    // pass the cancellation token through the HTTP download as well.
    public Task PrintPdfAsync(Func<Task<byte[]>> loadPdf, string fileName) =>
        PrintPdfAsync(_ => loadPdf(), fileName);

    public async Task PrintPdfAsync(Func<CancellationToken, Task<byte[]>> loadPdf, string fileName)
    {
        var total = Stopwatch.StartNew();
        long lastStageMs = 0;
        void MarkStage(string stage)
        {
            var elapsed = total.ElapsedMilliseconds;
            _logger.LogInformation(
                "LetterPrintClientTiming Stage={Stage} StageMs={StageMs} TotalMs={TotalMs}",
                stage, elapsed - lastStageMs, elapsed);
            lastStageMs = elapsed;
        }

        // اگر مرورگر app-shell.js قدیمی (بدون جریان چاپ) از کش آورده باشد، به‌جای خطای
        // گمراه‌کنندهٔ «پنجره‌های بازشو را مجاز کنید»، راهنمای درستِ تازه‌سازی کش را می‌دهیم
        // و پنجرهٔ رزروشدهٔ خالی هم بسته می‌شود (تب یتیمِ «بارگزاری‌نشده» باقی نمی‌ماند).
        bool runtimeReady;
        try { runtimeReady = await _js.InvokeAsync<bool>("ltxPrintHasRuntime"); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Print runtime probe failed; the browser is likely running a stale app-shell.js.");
            runtimeReady = false;
        }
        if (!runtimeReady)
        {
            try { await _js.InvokeVoidAsync("ltxPrintReleasePending"); } catch { /* best-effort */ }
            _toast.Error("نسخهٔ صفحه در مرورگر قدیمی است (کش). صفحهٔ برنامه را با Ctrl+Shift+R تازه‌سازی کنید و دوباره «چاپ» را بزنید.");
            return;
        }

        string? printId;
        try
        {
            printId = await _js.InvokeAsync<string?>("ltxPrintBegin");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not create the browser print surface.");
            try { await _js.InvokeVoidAsync("ltxPrintReleasePending"); } catch { /* best-effort */ }
            _toast.Error("پنجرهٔ چاپ باز نشد. پنجره‌های بازشو را برای این سایت مجاز کنید و دوباره تلاش کنید.");
            return;
        }

        if (string.IsNullOrWhiteSpace(printId))
        {
            try { await _js.InvokeVoidAsync("ltxPrintReleasePending"); } catch { /* best-effort */ }
            _toast.Error("صفحهٔ چاپ ساخته نشد. پنجره‌های بازشو را مجاز کنید و دوباره تلاش کنید.");
            return;
        }
        MarkStage("print-surface-ready");

        while (true)
        {
            byte[] pdfBytes;
            try
            {
                try
                {
                    using var downloadCts = new CancellationTokenSource(DownloadTimeout);
                    pdfBytes = await loadPdf(downloadCts.Token);
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException(
                        "دریافت فایل چاپ از سرور بیش از ۹۰ ثانیه طول کشید. اتصال شبکه یا وضعیت سرور را بررسی کنید و دوباره تلاش کنید.");
                }
                MarkStage("pdf-download");
                if (pdfBytes.Length == 0)
                    throw new InvalidOperationException("فایل PDF چاپ خالی است.");

                // Blazor transfers byte[] as Uint8Array, avoiding a base64 expansion/copy.
                _logger.LogInformation(
                    "LetterPrintClientTiming Stage=pdf-binary-ready Bytes={Bytes}", pdfBytes.Length);

                bool ready;
                try
                {
                    using var handoffCts = new CancellationTokenSource(HandoffTimeout);
                    ready = await _js.InvokeAsync<bool>(
                        "ltxPrintReady", handoffCts.Token, printId, pdfBytes, fileName,
                        (int)HandoffTimeout.TotalMilliseconds);
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException(
                        "نمایش PDF در مرورگر بیش از ۱۵ ثانیه طول کشید. اتصال را بررسی کنید یا دوباره تلاش کنید.");
                }
                MarkStage("browser-print-handoff");

                if (ready)
                {
                    MarkStage("complete");
                    return;
                }

                throw new InvalidOperationException("مرورگر نتوانست فایل PDF را برای چاپ آماده کند.");
            }
            catch (Exception ex)
            {
                var message = ex.Message;

                _logger.LogWarning(ex,
                    "LetterPrintClientTiming Stage=preparation-failed TotalMs={TotalMs}", total.ElapsedMilliseconds);
                try { await _js.InvokeVoidAsync("ltxPrintFail", printId, message); }
                catch (Exception jsEx) { _logger.LogWarning(jsEx, "Could not display the print preparation error."); }

                _toast.Error(message);
                string? action;
                try
                {
                    action = await _js.InvokeAsync<string?>("ltxPrintWaitForAction", printId);
                }
                catch (Exception jsEx)
                {
                    _logger.LogWarning(jsEx, "Could not receive a print recovery action.");
                    try { await _js.InvokeVoidAsync("ltxPrintCancel", printId); } catch { }
                    return;
                }

                if (string.Equals(action, "retry", StringComparison.OrdinalIgnoreCase))
                {
                    MarkStage("retry-requested");
                    continue;
                }

                if (string.Equals(action, "direct", StringComparison.OrdinalIgnoreCase))
                {
                    try { await _js.InvokeVoidAsync("ltxPrintDirectPage", printId); }
                    catch (Exception jsEx) { _logger.LogWarning(jsEx, "Direct print fallback failed."); }
                    MarkStage("direct-print-requested");
                    return;
                }

                try { await _js.InvokeVoidAsync("ltxPrintCancel", printId); } catch { }
                MarkStage("print-cancelled");
                return;
            }
        }
    }
}
