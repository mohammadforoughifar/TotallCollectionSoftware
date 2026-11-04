using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.AspNetCore.SignalR.Client;

namespace InventoryRemoteAgent;

// ============================================================================
// ایجنت ریموت دسکتاپ — ماژول RemoteDesktop (فاز ۱)
//
//   InventoryRemoteAgent run      اتصال به مرکز و انتظار برای درخواست جلسه
//   InventoryRemoteAgent init     ساخت فایل نمونه remote-agent.json
//
// سوییچ‌ها:
//   --config PATH     مسیر فایل تنظیمات (پیش‌فرض: remote-agent.json کنار exe)
//   --server URL      آدرس سرور مرکزی
//   --key KEY         کلید اختصاصی دستگاه (rmd_...)
//   --name NAME       نام نمایشی دستگاه (اختیاری)
//   --insecure        نادیده‌گرفتن خطای گواهی HTTPS (فقط شبکه داخلی/آزمایشی)
//
// فاز ۱: ویندوز، جلسه با رضایت کاربر، کپچر GDI/JPEG و relay از SignalR.
// ============================================================================
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        var opts = ParseArgs(args);
        if (opts.ShowHelp) { PrintHelp(); return 0; }
        var configPath = opts.ConfigPath ?? Path.Combine(AppContext.BaseDirectory, "remote-agent.json");
        if (opts.Command == "init")
        {
            RemoteAgentConfig.WriteSample(configPath);
            Console.WriteLine($"نمونه تنظیمات ساخته شد: {configPath}");
            return 0;
        }

        var config = RemoteAgentConfig.Load(configPath);
        if (!string.IsNullOrWhiteSpace(opts.ServerUrl)) config.ServerUrl = opts.ServerUrl;
        if (!string.IsNullOrWhiteSpace(opts.DeviceKey)) config.DeviceKey = opts.DeviceKey;
        if (!string.IsNullOrWhiteSpace(opts.DeviceName)) config.DeviceName = opts.DeviceName;
        if (opts.Insecure) config.Insecure = true;

        if (string.IsNullOrWhiteSpace(config.ServerUrl) || string.IsNullOrWhiteSpace(config.DeviceKey))
        {
            Console.WriteLine("ServerUrl و DeviceKey الزامی هستند. از --server/--key یا فایل remote-agent.json استفاده کنید.");
            PrintHelp();
            return 1;
        }
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("فاز ۱ ایجنت ریموت فقط از ویندوز پشتیبانی می‌کند. Linux/macOS در فازهای بعدی اضافه می‌شوند.");
            return 1;
        }

        var platform = new WindowsRemotePlatform();
        var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.1.0";
        var hubUrl = config.ServerUrl.TrimEnd('/') + "/hubs/remote-agent";
        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.Headers["X-Remote-Device-Key"] = config.DeviceKey;
                options.Headers["X-Remote-Agent-Os"] = RuntimeInformation.OSDescription;
                options.Headers["X-Remote-Agent-Version"] = version;
                if (config.Insecure)
                {
                    options.HttpMessageHandlerFactory = _ => new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                    };
                    options.WebSocketConfiguration = ws =>
                    {
                        ws.RemoteCertificateValidationCallback = (_, _, _, _) => true;
                    };
                }
            })
            .WithAutomaticReconnect()
            .Build();

        string? activeSession = null;
        var allowControl = false;
        CancellationTokenSource? captureCts = null;
        var frameIntervalMs = 250;
        var jpegQuality = 55;
        var maxWidth = 1280;

        void StopCapture()
        {
            try { captureCts?.Cancel(); } catch { }
            captureCts = null;
            activeSession = null;
            allowControl = false;
        }

        connection.On<string, string, int>("RequestConsent", async (sessionId, technicianName, timeoutSeconds) =>
        {
            var accepted = false;
            try { accepted = await platform.RequestConsentAsync(technicianName, timeoutSeconds, CancellationToken.None); }
            catch { accepted = false; }
            try { await connection.SendAsync("ConsentResult", sessionId, accepted); } catch { }
        });

        connection.On<string, int, int, int, bool>("StartCapture", (sessionId, interval, quality, width, control) =>
        {
            StopCapture();
            activeSession = sessionId;
            allowControl = control;
            frameIntervalMs = Math.Clamp(interval, 100, 2000);
            jpegQuality = Math.Clamp(quality, 10, 95);
            maxWidth = Math.Clamp(width, 640, 2560);
            captureCts = new CancellationTokenSource();
            var ct = captureCts.Token;
            _ = Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        var frame = platform.CaptureFrame(maxWidth, jpegQuality);
                        if (connection.State == HubConnectionState.Connected)
                            await connection.SendCoreAsync("Frame", new object?[] { sessionId, frame.Jpeg, frame.Width, frame.Height }, ct);
                    }
                    catch (OperationCanceledException) { break; }
                    catch { break; }
                    var wait = frameIntervalMs - (int)sw.ElapsedMilliseconds;
                    if (wait > 0)
                    {
                        try { await Task.Delay(wait, ct); } catch (OperationCanceledException) { break; }
                    }
                }
            }, ct);
        });

        connection.On<string, string>("Input", (sessionId, inputJson) =>
        {
            if (activeSession == sessionId && allowControl)
            {
                try { platform.InjectInput(inputJson); } catch { }
            }
        });

        connection.On<string>("StopCapture", sessionId =>
        {
            if (activeSession == sessionId) StopCapture();
        });

        connection.Closed += ex =>
        {
            StopCapture();
            Console.WriteLine(ex == null ? "اتصال به مرکز بسته شد." : $"اتصال قطع شد: {ex.Message}");
            return Task.CompletedTask;
        };

        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
        Console.WriteLine($"InventoryRemoteAgent v{version} — {Environment.MachineName}");
        Console.WriteLine($"مرکز: {hubUrl}");

        while (!shutdown.IsCancellationRequested)
        {
            try
            {
                await connection.StartAsync(shutdown.Token);
                Console.WriteLine("متصل شد. در انتظار درخواست جلسه… (Ctrl+C برای خروج)");
                break;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Console.WriteLine($"اتصال ناموفق: {ex.Message} — تلاش مجدد تا ۵ ثانیه دیگر");
                try { await Task.Delay(5000, shutdown.Token); } catch (OperationCanceledException) { break; }
            }
        }

        if (connection.State == HubConnectionState.Connected)
        {
            try { await Task.Delay(Timeout.Infinite, shutdown.Token); }
            catch (OperationCanceledException) { }
        }
        StopCapture();
        try { await connection.StopAsync(); } catch { }
        await connection.DisposeAsync();
        return 0;
    }

    private sealed class Options
    {
        public string Command { get; set; } = "run";
        public string? ConfigPath { get; set; }
        public string? ServerUrl { get; set; }
        public string? DeviceKey { get; set; }
        public string? DeviceName { get; set; }
        public bool Insecure { get; set; }
        public bool ShowHelp { get; set; }
    }

    private static Options ParseArgs(string[] args)
    {
        var o = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            string? Next() => i + 1 < args.Length ? args[++i] : null;
            switch (a)
            {
                case "run": case "init": o.Command = a; break;
                case "--config": o.ConfigPath = Next(); break;
                case "--server": o.ServerUrl = Next(); break;
                case "--key": o.DeviceKey = Next(); break;
                case "--name": o.DeviceName = Next(); break;
                case "--insecure": o.Insecure = true; break;
                case "-h": case "--help": case "help": o.ShowHelp = true; break;
                default:
                    if (a.StartsWith("--config=")) o.ConfigPath = a["--config=".Length..];
                    else if (a.StartsWith("--server=")) o.ServerUrl = a["--server=".Length..];
                    else if (a.StartsWith("--key=")) o.DeviceKey = a["--key=".Length..];
                    else if (a.StartsWith("--name=")) o.DeviceName = a["--name=".Length..];
                    break;
            }
        }
        return o;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
InventoryRemoteAgent — ایجنت ریموت دسکتاپ (فاز ۱ ویندوز)

دستورها:
  run                    اتصال به مرکز و انتظار برای جلسه (پیش‌فرض)
  init                   ساخت remote-agent.json نمونه

سوییچ‌ها:
  --config PATH          مسیر فایل تنظیمات
  --server URL           آدرس سرور مرکزی
  --key KEY              کلید اختصاصی دستگاه
  --name NAME            نام نمایشی دستگاه
  --insecure             نادیده‌گرفتن خطای گواهی HTTPS (فقط آزمایشی)
""");
    }
}
