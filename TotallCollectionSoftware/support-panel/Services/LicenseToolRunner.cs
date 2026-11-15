using System.Diagnostics;
using System.Text;

namespace LicenseSigner.Services;

/// <summary>نتیجهٔ اجرای <c>LicenseTool</c>.</summary>
public sealed record ToolRun(int ExitCode, string StdOut, string StdErr)
{
    public string Text => (StdOut + Environment.NewLine + StdErr).Trim();
    public bool PreviewedOnly => ExitCode == 3;
    public bool Ok => ExitCode == 0;
}

/// <summary>
/// اجرای CLI موجود (<c>tools/LicenseTool</c>) — <b>هیچ منطق امضایی این‌جا نیست</b>.
/// مسیر کلید در آرگومان می‌رود، ولی گذرواژهٔ کلید فقط از راه متغیر محیطی
/// همان فرایند فرزند پاس می‌شود و آرگومان‌ها هرگز لاگ نمی‌شوند.
/// </summary>
public sealed class LicenseToolRunner(SignerOptions options, ILogger<LicenseToolRunner> log)
{
    private const int MaxText = 20_000;

    public async Task<ToolRun> RunAsync(string toolPath, IEnumerable<string> args, string? keyPassword,
        CancellationToken ct = default)
    {
        var isDll = toolPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        var psi = new ProcessStartInfo
        {
            FileName = isDll ? options.DotnetPath : toolPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (isDll) psi.ArgumentList.Add(toolPath);
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        psi.Environment.Remove("LICENSETOOL_PRIVATE_KEY");
        psi.Environment.Remove(options.KeyPasswordEnvironmentVariable);
        if (!string.IsNullOrEmpty(keyPassword))
            psi.Environment[options.KeyPasswordEnvironmentVariable] = keyPassword;

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        if (!process.Start()) return new ToolRun(-1, "", "اجرای ابزار ممکن نشد.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.EffectiveTimeoutSeconds()));

        try
        {
            await process.WaitForExitAsync(timeout.Token);

            // ⚠️ مسابقهٔ خواندن خروجی: WaitForExitAsync بازگشتش تضمین نمی‌کند که
            // callbackهای OutputDataReceived کامل شده باشند؛ بدون این خط، متن خروجی
            // ابزار به‌طور متناوب خالی برمی‌گشت.
            process.WaitForExit();
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* بی‌صدا */ }
            return new ToolRun(-2, Clip(stdout.ToString()), "مهلت اجرای ابزار تمام شد.");
        }

        var run = new ToolRun(process.ExitCode, Clip(stdout.ToString()), Clip(stderr.ToString()));
        log.LogInformation("LicenseTool {Verb} ⇒ exit {Code}", args.FirstOrDefault() ?? "?", run.ExitCode);
        return run;
    }

    private static string Clip(string text) => text.Length <= MaxText ? text : text[..MaxText];
}
