using System.Text.Json;
using Agent.Core;

namespace Agent.Runner;

/// <summary>
/// رانر مستقل ایجنت شناسنامه سیستم — پوسته‌ی نازک روی کلاس لایبرری Agent.Core.
/// همه‌ی منطق جمع‌آوری و ارتباط با سرور داخل Agent.Core است و از هر برنامه‌ی دیگری هم قابل استفاده است.
/// </summary>
public class Program
{
    /// <summary>فایل agent.config.json کنار exe — برای تنظیم آدرس سرور بدون خط فرمان.</summary>
    public class AgentConfig
    {
        public string? Api { get; set; }
        public bool? Watch { get; set; }
    }

    public static async Task Main(string[] args)
    {
        // ================== تنظیمات: خط فرمان > فایل پیکربندی > پیش‌فرض ==================
        string? configApi = null; bool? configWatch = null;
        var configPath = Path.Combine(AppContext.BaseDirectory, "agent.config.json");
        if (File.Exists(configPath))
        {
            try
            {
                var cfg = JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(configPath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                configApi = cfg?.Api;
                configWatch = cfg?.Watch;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠ فایل پیکربندی خوانده نشد: {ex.Message}");
            }
        }

        var api = (args.FirstOrDefault(a => !a.StartsWith("--")) ?? configApi ?? "http://localhost:5100").Trim().TrimEnd('/');
        var watch = args.Contains("--watch") || (configWatch ?? false);

        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("=================================================");
        Console.WriteLine("   ایجنت شناسنامه سیستم — فروغ آریا");
        Console.WriteLine("=================================================");
        Console.WriteLine($"  کامپیوتر:      {Environment.MachineName}");
        Console.WriteLine($"  سرور API:      {api}");
        Console.WriteLine($"  حالت اجرا:     {(watch ? "Watch — ماندگار + دریافت دستور از راه دور" : "یک‌بار و خروج")}");
        Console.WriteLine();

        // ================== جمع‌آوری مشخصات با کلاس لایبرری ==================
        var info = HardwareCollector.Collect();
        var details = JsonSerializer.Deserialize<HardwareDetails>(info.DetailsJson ?? "{}",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new HardwareDetails();

        Console.WriteLine($"AgentId:     {info.AgentId}");
        Console.WriteLine($"CPU:         {info.Cpu}");
        Console.WriteLine($"RAM:         {info.Ram}");
        Console.WriteLine($"Motherboard: {info.Motherboard}");
        Console.WriteLine($"HardDisks ({details.Disks.Count}): {info.HardDisk}");
        foreach (var dsk in details.Disks)
            Console.WriteLine($"  هارد: {dsk.Model} — S.M.A.R.T: {dsk.Smart}");
        Console.WriteLine($"Graphics:    {info.Graphics}");
        Console.WriteLine($"Monitor ({details.Monitors.Count}):  {info.Monitor}");
        foreach (var n in details.NetAdapters)
            Console.WriteLine($"  شبکه: {n.Name} ({n.Type}) — IP: {n.Ipv4} — MAC: {n.MacAddress}");
        foreach (var v in details.Volumes)
            Console.WriteLine($"  درایو {v.Letter} — {v.UsedGb} GB استفاده‌شده از {v.TotalGb} GB");
        Console.WriteLine($"OS:          {info.OsName}\n");

        // ================== ارسال به سرور ==================
        using var client = new AgentApiClient(api);
        Console.WriteLine($"در حال ارسال به API: {api}/api/SystemInfo ...");
        var result = await client.SendInfoAsync(info);
        if (result.Success)
        {
            Console.WriteLine("✔ ارسال موفق — پاسخ سرور: " + result.ResponseBody);
        }
        else if (result.Error != null)
        {
            Console.WriteLine();
            Console.WriteLine("✖ ارتباط با سرور برقرار نشد: " + result.Error);
            Console.WriteLine();
            Console.WriteLine("   راهنمای رفع مشکل:");
            Console.WriteLine("   ۱) مطمئن شوید API روی سرور مرکزی در حال اجراست (پیش‌فرض پورت 5100).");
            Console.WriteLine("   ۲) آدرس درست سرور را در فایل agent.config.json تنظیم کنید، مثلاً:");
            Console.WriteLine("        { \"api\": \"http://192.168.1.10:5100\", \"watch\": true }");
            Console.WriteLine("   ۳) یا آدرس را هنگام اجرا بدهید:  Agent.Runner.exe http://192.168.1.10:5100");
            Console.WriteLine("   ۴) از نظر شبکه، پورت 5100 سرور برای این کامپیوتر باز باشد (فایروال).");
        }
        else
        {
            Console.WriteLine($"✖ سرور درخواست را نپذیرفت ({result.StatusCode}): {result.ResponseBody}");
        }

        if (!watch)
        {
            Console.WriteLine();
            Console.WriteLine("─────────────────────────────────────────────");
            Console.WriteLine("برای بستن این پنجره، Enter بزنید:");
            try { Console.ReadLine(); } catch { }
            return;
        }

        // ================== حالت Watch: دریافت دستورهای از راه دور ==================
        Console.WriteLine("👁 حالت Watch فعال — هر ۱۵ ثانیه دستورهای از راه دور بررسی می‌شوند (Ctrl+C برای خروج).");
        while (true)
        {
            try
            {
                var cmds = await client.GetPendingCommandsAsync(info.AgentId);
                foreach (var c in cmds)
                {
                    Console.WriteLine($"⚡ دستور از راه دور دریافت شد: {c.Action}");
                    var (ok, msg) = RemoteCommandExecutor.Execute(c.Action);
                    await client.ReportCommandResultAsync(c.Id, ok, msg);
                    Console.WriteLine($"   نتیجه: {(ok ? "✔ موفق" : "✖ ناموفق")} — {msg}");
                    if (c.Action is "Reboot" or "Shutdown" && ok)
                    {
                        Console.WriteLine("سیستم به‌زودی خاموش/ری‌استارت می‌شود — ایجنت تمام می‌شود.");
                        return;
                    }
                }
            }
            catch { }
            await Task.Delay(TimeSpan.FromSeconds(15));
        }
    }
}
