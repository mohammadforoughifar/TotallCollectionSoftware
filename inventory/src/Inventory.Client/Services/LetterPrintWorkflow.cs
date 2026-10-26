using System.Diagnostics;
using Microsoft.JSInterop;

namespace Inventory.Client.Services;

/// <summary>Shared popup, loading, PDF fallback, and error flow for internal and outgoing letters.</summary>
public sealed class LetterPrintWorkflow
{
    private static readonly TimeSpan PreparationTimeout = TimeSpan.FromSeconds(15);

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

        string? printId;
        try
        {
            printId = await _js.InvokeAsync<string?>("ltxPrintBegin");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not create the browser print surface.");
            _toast.Error("پنجرهٔ چاپ باز نشد. پنجره‌های بازشو را برای این سایت مجاز کنید و دوباره تلاش کنید.");
            return;
        }

        if (string.IsNullOrWhiteSpace(printId))
        {
            _toast.Error("صفحهٔ چاپ ساخته نشد. پنجره‌های بازشو را مجاز کنید و دوباره تلاش کنید.");
            return;
        }
        MarkStage("print-surface-ready");

        while (true)
        {
            var attempt = Stopwatch.StartNew();
            using var timeout = new CancellationTokenSource(PreparationTimeout);
            try
            {
                var pdfBytes = await loadPdf(timeout.Token);
                MarkStage("pdf-download");
                if (pdfBytes == null || pdfBytes.Length == 0)
                    throw new InvalidOperationException("فایل PDF چاپ خالی است.");

                // Blazor transfers byte[] as Uint8Array, avoiding a base64 expansion/copy.
                _logger.LogInformation(
                    "LetterPrintClientTiming Stage=pdf-binary-ready Bytes={Bytes}", pdfBytes.Length);

                var remainingMs = Math.Max(1, (int)(PreparationTimeout - attempt.Elapsed).TotalMilliseconds);
                var ready = await _js.InvokeAsync<bool>(
                    "ltxPrintReady", timeout.Token, printId, pdfBytes, fileName, remainingMs);
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
                var message = ex is OperationCanceledException
                    ? "آماده‌سازی چاپ بیش از ۱۵ ثانیه طول کشید. اتصال را بررسی کنید یا دوباره تلاش کنید."
                    : ex.Message;

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
