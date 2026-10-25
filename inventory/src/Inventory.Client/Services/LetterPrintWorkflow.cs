using System.Diagnostics;
using Microsoft.JSInterop;

namespace Inventory.Client.Services;

/// <summary>Shared popup, loading, PDF fallback, and error flow for internal and outgoing letters.</summary>
public sealed class LetterPrintWorkflow
{
    private readonly IJSRuntime _js;
    private readonly IToastService _toast;
    private readonly ILogger<LetterPrintWorkflow> _logger;

    public LetterPrintWorkflow(IJSRuntime js, IToastService toast, ILogger<LetterPrintWorkflow> logger)
    {
        _js = js;
        _toast = toast;
        _logger = logger;
    }

    public async Task PrintPdfAsync(Func<Task<byte[]>> loadPdf, string fileName)
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

        string? printId = null;
        try { printId = await _js.InvokeAsync<string?>("ltxPrintBegin"); }
        catch { /* A blocked popup falls through to the same PDF-open fallback for both letter types. */ }
        MarkStage("popup-reservation");

        try
        {
            var pdfBytes = await loadPdf();
            MarkStage("pdf-download");
            if (pdfBytes == null || pdfBytes.Length == 0)
                throw new InvalidOperationException("فایل PDF چاپ خالی است.");

            var encodeTimer = Stopwatch.StartNew();
            var payload = Convert.ToBase64String(pdfBytes);
            _logger.LogInformation(
                "LetterPrintClientTiming Stage=pdf-base64-encode StageMs={StageMs} Bytes={Bytes}",
                encodeTimer.ElapsedMilliseconds, pdfBytes.Length);

            var ready = false;
            if (printId != null)
            {
                try { ready = await _js.InvokeAsync<bool>("ltxPrintReady", printId, payload, fileName); }
                catch { ready = false; }
            }
            MarkStage("browser-print-handoff");

            if (ready)
            {
                MarkStage("complete");
                return;
            }
            if (printId != null)
            {
                try { await _js.InvokeVoidAsync("ltxPrintFail", printId, "پنجرهٔ چاپ آماده نشد."); }
                catch { }
            }
            _toast.Info("پنجرهٔ چاپ توسط مرورگر مسدود شد؛ نسخهٔ PDF باز می‌شود.");
            await _js.InvokeVoidAsync("ltxOpenPdf", payload, fileName);
            MarkStage("pdf-fallback-open");
            MarkStage("complete");
        }
        catch (Exception ex)
        {
            if (printId != null)
            {
                try { await _js.InvokeVoidAsync("ltxPrintFail", printId, ex.Message); }
                catch { }
            }
            _logger.LogWarning(ex, "LetterPrintClientTiming Stage=failed TotalMs={TotalMs}", total.ElapsedMilliseconds);
            _toast.Error(ex.Message);
        }
    }
}
