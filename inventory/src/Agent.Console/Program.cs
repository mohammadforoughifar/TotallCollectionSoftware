using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace Agent.Console;

public class AgentProgram
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

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
                System.Console.WriteLine($"⚠ فایل پیکربندی خوانده نشد: {ex.Message}");
            }
        }

        var api = (args.FirstOrDefault(a => !a.StartsWith("--")) ?? configApi ?? "http://localhost:5100").Trim().TrimEnd('/');
        var watch = args.Contains("--watch") || (configWatch ?? false);

        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        System.Console.WriteLine("=================================================");
        System.Console.WriteLine("   ایجنت شناسنامه سیستم — فروغ آریا");
        System.Console.WriteLine("=================================================");
        System.Console.WriteLine($"  کامپیوتر:      {Environment.MachineName}");
        System.Console.WriteLine($"  سرور API:      {api}");
        System.Console.WriteLine($"  حالت اجرا:     {(watch ? "Watch — ماندگار + دریافت دستور از راه دور" : "یک‌بار و خروج")}");
        System.Console.WriteLine();

        var details = CollectDetails();
        var info = new SystemInfoData
        {
            AgentId = "AGENT-" + Environment.MachineName,
            Motherboard = string.IsNullOrWhiteSpace(details.Board) ? null :
                $"{details.Board}{(string.IsNullOrWhiteSpace(details.ComputerModel) ? "" : " — " + details.ComputerModel)}",
            Cpu = details.Cpus.Count > 0 ?
                string.Join(" + ", details.Cpus.Select(c => c.Name + (c.Cores > 0 ? $" — {c.Cores} هسته / {c.Threads} رشته" : "") + (c.ClockGhz > 0 ? $" @ {c.ClockGhz:0.00}GHz" : ""))) : null,
            Ram = details.RamSticks.Count > 0 ?
                $"{details.RamSticks.Sum(r => r.CapacityGb)} GB — {details.RamSticks.Count} × " + string.Join(" + ", details.RamSticks.Select(r => $"{r.CapacityGb}GB{r.Type}") ) : null,
            HardDisk = details.Disks.Count > 0 ?
                string.Join(" + ", details.Disks.Select(d => $"{d.Model} ({d.SizeGb}GB)")) : null,
            Graphics = details.Gpus.Count > 0 ? string.Join(" + ", details.Gpus.Select(g => g.Name + (string.IsNullOrEmpty(g.Resolution) ? "" : $" — {g.Resolution}"))) : null,
            Monitor = details.Monitors.Count > 0 ? string.Join(" + ", details.Monitors.Select(m => m.Name + (string.IsNullOrEmpty(m.Resolution) ? "" : $" ({m.Resolution})") + (m.IsPrimary ? " ★" : ""))) : null,
            OsName = GetOs(),
            TotalRamGb = details.RamSticks.Sum(r => r.CapacityGb),
            DetailsJson = JsonSerializer.Serialize(details, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
        };

        System.Console.WriteLine($"AgentId:     {info.AgentId}");
        System.Console.WriteLine($"CPU:         {info.Cpu}");
        System.Console.WriteLine($"RAM:         {info.Ram}");
        System.Console.WriteLine($"Motherboard: {info.Motherboard}");
        System.Console.WriteLine($"HardDisks ({details.Disks.Count}): {info.HardDisk}");
        foreach (var dsk in details.Disks)
            System.Console.WriteLine($"  هارد: {dsk.Model} — S.M.A.R.T: {dsk.Smart}");
        System.Console.WriteLine($"Graphics:    {info.Graphics}");
        System.Console.WriteLine($"Monitor ({details.Monitors.Count}):  {info.Monitor}");
        foreach (var n in details.NetAdapters)
            System.Console.WriteLine($"  شبکه: {n.Name} ({n.Type}) — IP: {n.Ipv4} — MAC: {n.MacAddress}");
        foreach (var v in details.Volumes)
            System.Console.WriteLine($"  درایو {v.Letter} — {v.UsedGb} GB استفاده‌شده از {v.TotalGb} GB");
        System.Console.WriteLine($"OS:          {info.OsName}\n");

        await SendInfoAsync(api, info);

        if (!watch)
        {
            System.Console.WriteLine();
            System.Console.WriteLine("─────────────────────────────────────────────");
            System.Console.WriteLine("برای بستن این پنجره، Enter بزنید:");
            try { System.Console.ReadLine(); } catch { }
            return;
        }

        System.Console.WriteLine("👁 حالت Watch فعال — هر ۱۵ ثانیه دستورهای از راه دور بررسی می‌شوند (Ctrl+C برای خروج).");
        while (true)
        {
            try { await ProcessRemoteCommandsAsync(api, info.AgentId); }
            catch { }
            await Task.Delay(TimeSpan.FromSeconds(15));
        }
    }

    static async Task SendInfoAsync(string api, SystemInfoData info)
    {
        System.Console.WriteLine($"در حال ارسال به API: {api}/api/SystemInfo ...");
        try
        {
            var json = JsonSerializer.Serialize(info,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            var resp = await Http.PostAsync(api + "/api/SystemInfo",
                new StringContent(json, Encoding.UTF8, "application/json"));
            var body = await resp.Content.ReadAsStringAsync();

            if (resp.IsSuccessStatusCode)
            {
                System.Console.WriteLine("✔ ارسال موفق — پاسخ سرور: " + body);
            }
            else
            {
                System.Console.WriteLine($"✖ سرور درخواست را نپذیرفت ({(int)resp.StatusCode}): {body}");
            }
        }
        catch (Exception ex)
        {
            System.Console.WriteLine();
            System.Console.WriteLine("✖ ارتباط با سرور برقرار نشد: " + ex.Message);
            System.Console.WriteLine();
            System.Console.WriteLine("   راهنمای رفع مشکل:");
            System.Console.WriteLine("   ۱) مطمئن شوید API روی سرور مرکزی در حال اجراست (پیش‌فرض پورت 5100).");
            System.Console.WriteLine("   ۲) آدرس درست سرور را در فایل agent.config.json تنظیم کنید، مثلاً:");
            System.Console.WriteLine("        { \"api\": \"http://192.168.1.10:5100\", \"watch\": true }");
            System.Console.WriteLine("   ۳) یا آدرس را هنگام اجرا بدهید:  Agent.Console.exe http://192.168.1.10:5100");
            System.Console.WriteLine("   ۴) از نظر شبکه، پورت 5100 سرور برای این کامپیوتر باز باشد (فایروال).");
        }
    }

    public class AgentCommand
    {
        public int Id { get; set; }
        public string Action { get; set; } = "";
        public string? Message { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>فایل agent.config.json کنار exe — برای تنظیم آدرس سرور بدون خط فرمان.</summary>
    public class AgentConfig
    {
        public string? Api { get; set; }
        public bool? Watch { get; set; }
    }

    /// <summary>بررسی و اجرای دستورهای از راه دور در انتظار این سیستم.</summary>
    static async Task ProcessRemoteCommandsAsync(string api, string agentId)
    {
        var resp = await Http.GetAsync($"{api}/api/SystemInfo/agent-commands?agentId={Uri.EscapeDataString(agentId)}");
        if (!resp.IsSuccessStatusCode) return;
        var cmds = JsonSerializer.Deserialize<List<AgentCommand>>(await resp.Content.ReadAsStringAsync());
        if (cmds == null || cmds.Count == 0) return;

        foreach (var c in cmds)
        {
            System.Console.WriteLine($"⚡ دستور از راه دور دریافت شد: {c.Action}");
            bool ok; string msg;
            if (!OperatingSystem.IsWindows())
            {
                ok = false; msg = "فقط روی ویندوز قابل اجرا است.";
            }
            else
            {
                (ok, msg) = c.Action switch
                {
                    "Reboot" => RunCmd("shutdown", "/r /t 5 /c \"ری‌استارت از سامانه انبار\""),
                    "Shutdown" => RunCmd("shutdown", "/s /t 5 /c \"خاموش‌شدن از سامانه انبار\""),
                    "Lock" => RunCmd("rundll32.exe", "user32.dll,LockWorkStation"),
                    _ => (false, "عملیات نامعتبر")
                };
            }
            try
            {
                var payload = JsonSerializer.Serialize(new { ok, message = msg });
                await Http.PostAsync($"{api}/api/SystemInfo/commands/{c.Id}/result",
                    new StringContent(payload, Encoding.UTF8, "application/json"));
            }
            catch { }
            System.Console.WriteLine($"   نتیجه: {(ok ? "✔ موفق" : "✖ ناموفق")} — {msg}");
            if (c.Action is "Reboot" or "Shutdown" && ok)
            {
                System.Console.WriteLine("سیستم به‌زودی خاموش/ری‌استارت می‌شود — ایجنت تمام می‌شود.");
                return;
            }
        }
    }

    static (bool, string) RunCmd(string exe, string args)
    {
        try
        {
            var p = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(exe, args) { UseShellExecute = false });
            return (true, $"دستور ارسال شد: {exe}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // ================= جمع‌آوری ساختاریافته =================

    static HardwareDetails CollectDetails()
    {
        var d = new HardwareDetails();

        // شبکه و آی‌پی — روی همه‌ی سیستم‌عامل‌ها کار می‌کند
        foreach (var n in GetNetAdapters())
        {
            d.NetAdapters.Add(n);
        }

        if (!OperatingSystem.IsWindows())
        {
            d.Board = "نامشخص (فقط ویندوز)";
            return d;
        }

        try
        {
            using (var s = new System.Management.ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem"))
                foreach (var mo in s.Get())
                {
                    using (mo)
                    {
                        var maker = Convert.ToString(mo["Manufacturer"])?.Trim();
                        var model = Convert.ToString(mo["Model"])?.Trim();
                        if (!string.IsNullOrWhiteSpace(model))
                            d.ComputerModel = $"{maker} {model}".Trim();
                    }
                }

            using (var s = new System.Management.ManagementObjectSearcher("SELECT Manufacturer, Product, SerialNumber FROM Win32_BaseBoard"))
                foreach (var mo in s.Get())
                {
                    using (mo)
                    {
                        var maker = Convert.ToString(mo["Manufacturer"])?.Trim();
                        var product = Convert.ToString(mo["Product"])?.Trim();
                        if (!string.IsNullOrWhiteSpace(product))
                            d.Board = $"{maker} {product}".Trim();
                        d.BoardSerial = Convert.ToString(mo["SerialNumber"])?.Trim();
                    }
                }

            using (var s = new System.Management.ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor"))
                foreach (var mo in s.Get())
                {
                    using (mo)
                    {
                        var name = Convert.ToString(mo["Name"])?.Trim();
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        d.Cpus.Add(new CpuDetail
                        {
                            Name = name,
                            Cores = Convert.ToInt32(mo["NumberOfCores"]),
                            Threads = Convert.ToInt32(mo["NumberOfLogicalProcessors"]),
                            ClockGhz = Convert.ToDouble(mo["MaxClockSpeed"]) / 1000.0
                        });
                    }
                }

            using (var s = new System.Management.ManagementObjectSearcher(
                "SELECT DeviceLocator, Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType, Manufacturer, PartNumber, SerialNumber FROM Win32_PhysicalMemory"))
                foreach (var mo in s.Get())
                {
                    using (mo)
                        d.RamSticks.Add(new RamStickDetail
                        {
                            Slot = Convert.ToString(mo["DeviceLocator"])?.Trim() ?? "?",
                            CapacityGb = (int)Math.Round(Convert.ToInt64(mo["Capacity"]) / (1024.0 * 1024 * 1024)),
                            SpeedMhz = Convert.ToInt32(mo["Speed"]),
                            ConfiguredMhz = Convert.ToInt32(mo["ConfiguredClockSpeed"]),
                            Type = MemType(mo["SMBIOSMemoryType"]),
                            Manufacturer = Convert.ToString(mo["Manufacturer"])?.Trim() ?? "",
                            PartNumber = Convert.ToString(mo["PartNumber"])?.Trim() ?? "",
                            SerialNumber = Convert.ToString(mo["SerialNumber"])?.Trim() ?? ""
                        });
                }

            using (var s = new System.Management.ManagementObjectSearcher(
                "SELECT Index, Model, Size, InterfaceType, MediaType, SerialNumber, Status FROM Win32_DiskDrive"))
                foreach (var mo in s.Get())
                {
                    using (mo)
                    {
                        var model = Convert.ToString(mo["Model"])?.Trim();
                        if (string.IsNullOrWhiteSpace(model)) continue;
                        d.Disks.Add(new DiskDetail
                        {
                            Model = model,
                            SizeGb = (int)Math.Round(Convert.ToDouble(mo["Size"]) / Math.Pow(1024, 3)),
                            Interface = Convert.ToString(mo["InterfaceType"])?.Trim() ?? "",
                            Media = Convert.ToString(mo["MediaType"])?.Trim() ?? "",
                            SerialNumber = Convert.ToString(mo["SerialNumber"])?.Trim() ?? "",
                            Smart = SmartMapper.FromStatus(Convert.ToString(mo["Status"])?.Trim() ?? "")
                        });
                    }
                }

            using (var s = new System.Management.ManagementObjectSearcher(
                "SELECT Name, CurrentHorizontalResolution, CurrentVerticalResolution, DriverVersion FROM Win32_VideoController"))
                foreach (var mo in s.Get())
                {
                    using (mo)
                    {
                        var name = Convert.ToString(mo["Name"])?.Trim();
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        var w = Convert.ToInt32(mo["CurrentHorizontalResolution"]);
                        var h = Convert.ToInt32(mo["CurrentVerticalResolution"]);
                        d.Gpus.Add(new GpuDetail
                        {
                            Name = name,
                            Resolution = w > 0 && h > 0 ? $"{w}×{h}" : "",
                            DriverVersion = Convert.ToString(mo["DriverVersion"])?.Trim() ?? ""
                        });
                    }
                }

            foreach (var m in GetMonitors())
            {
                d.Monitors.Add(m);
            }

            // حجم کل و استفاده‌شده‌ی هر درایو (پارتیشن‌های محلی)
            using (var s = new System.Management.ManagementObjectSearcher(
                "SELECT DeviceID, VolumeName, Size, FreeSpace FROM Win32_LogicalDisk WHERE DriveType=3"))
                foreach (var mo in s.Get())
                {
                    using (mo)
                    {
                        var total = Convert.ToDouble(mo["Size"]);
                        var free = Convert.ToDouble(mo["FreeSpace"]);
                        if (total <= 0) continue;
                        d.Volumes.Add(new VolumeDetail
                        {
                            Letter = Convert.ToString(mo["DeviceID"])?.Trim() ?? "?",
                            Label = Convert.ToString(mo["VolumeName"])?.Trim() ?? "",
                            TotalGb = (int)Math.Round(total / Math.Pow(1024, 3)),
                            UsedGb = (int)Math.Round((total - free) / Math.Pow(1024, 3))
                        });
                    }
                }
        }
        catch { }

        return d;
    }

    // ---------- شبکه: آداپتورهای فعال + IPv4 + مک + گیت‌وی ----------
    static List<NetAdapterDetail> GetNetAdapters()
    {
        var list = new List<NetAdapterDetail>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (nic.OperationalStatus != OperationalStatus.Up) continue;

                var props = nic.GetIPProperties();
                var ipv4 = string.Join(" , ", props.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString()));

                var macBytes = nic.GetPhysicalAddress().GetAddressBytes();
                var mac = macBytes.Length > 0 ? string.Join(":", macBytes.Select(b => b.ToString("X2"))) : "";

                if (string.IsNullOrWhiteSpace(ipv4) && string.IsNullOrWhiteSpace(mac)) continue;

                list.Add(new NetAdapterDetail
                {
                    Name = nic.Name,
                    Description = nic.Description,
                    Type = nic.NetworkInterfaceType.ToString(),
                    MacAddress = mac,
                    Ipv4 = ipv4,
                    Gateway = props.GatewayAddresses
                        .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? ""
                });
            }
        }
        catch { }
        return list;
    }

    // ---------- مانیتورها: نام واقعی و سریال از WmiMonitorID + رزولوشن واقعی هر صفحه ----------
    static List<MonitorDetail> GetMonitors()
    {
        var list = new List<MonitorDetail>();
        if (!OperatingSystem.IsWindows()) { list.Add(new MonitorDetail { Name = "نامشخص (فقط ویندوز)" }); return list; }

        try
        {
            using var s = new System.Management.ManagementObjectSearcher(
                @"root\wmi", "SELECT UserFriendlyName, ManufacturerName, SerialNumberId FROM WmiMonitorID");
            foreach (var mo in s.Get())
            {
                using (mo)
                {
                    var name = Decode(mo["UserFriendlyName"]);
                    var maker = Decode(mo["ManufacturerName"]);
                    var serial = Decode(mo["SerialNumberId"]);
                    if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(maker)) continue;
                    list.Add(new MonitorDetail
                    {
                        Name = $"{maker} {name}".Trim(),
                        SerialNumber = serial ?? ""
                    });
                }
            }
        }
        catch { }

        // رزولوشن واقعی هر صفحه‌نمایش و اصلی‌بودن آن (با API ویندوز — بدون نیاز به WinForms)
        try
        {
            var screens = GetScreenResolutions();
            for (var i = 0; i < screens.Count; i++)
            {
                var (res, primary) = screens[i];
                if (i < list.Count)
                {
                    list[i].Resolution = res;
                    list[i].IsPrimary = primary;
                }
                else
                {
                    list.Add(new MonitorDetail { Name = $"مانیتور {i + 1}", Resolution = res, IsPrimary = primary });
                }
            }
        }
        catch { }

        if (list.Count == 0) list.Add(new MonitorDetail { Name = "نامشخص" });
        return list;
    }

    // ---------- رزولوشن صفحات نمایش با user32 (بدون وابستگی به WindowsDesktop) ----------
    private const uint DISPLAY_DEVICE_ATTACHED = 0x1;
    private const uint DISPLAY_DEVICE_PRIMARY = 0x4;
    private const int ENUM_CURRENT_SETTINGS = -1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public short SpecVersion, DriverVersion, Size, DriverExtra;
        public int Fields, PositionX, PositionY, DisplayOrientation, DisplayFixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public short LogPixels;
        public int BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
        public int ICMMethod, ICMIntent, MediaType, DitherType;
        public int Reserved1, Reserved2, PanningWidth, PanningHeight;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DISPLAY_DEVICE
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplayDevices(string? device, uint index, ref DISPLAY_DEVICE deviceInfo, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

    /// <summary>رزولوشن جاری و اصلی‌بودن هر صفحه‌نمایش متصل.</summary>
    static List<(string Res, bool Primary)> GetScreenResolutions()
    {
        var result = new List<(string, bool)>();
        if (!OperatingSystem.IsWindows()) return result;

        for (uint i = 0; i < 16; i++)
        {
            var dd = new DISPLAY_DEVICE { Size = System.Runtime.InteropServices.Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, i, ref dd, 0)) break;
            if ((dd.StateFlags & DISPLAY_DEVICE_ATTACHED) == 0) continue;

            var dm = new DEVMODE { Size = (short)System.Runtime.InteropServices.Marshal.SizeOf<DEVMODE>() };
            if (EnumDisplaySettings(dd.DeviceName, ENUM_CURRENT_SETTINGS, ref dm) && dm.PelsWidth > 0)
            {
                var primary = (dd.StateFlags & DISPLAY_DEVICE_PRIMARY) != 0;
                result.Add(($"{dm.PelsWidth}×{dm.PelsHeight}", primary));
            }
        }
        return result;
    }

    /// <summary>تبدیل آرایه‌ی ushort مربوط به WMI به رشته.</summary>
    static string? Decode(object? arr)
    {
        if (arr is not ushort[] a) return null;
        return string.Concat(a.TakeWhile(v => v != 0).Select(v => (char)v));
    }

    static string MemType(object? smbiosType) => Convert.ToInt32(smbiosType) switch
    {
        20 => "DDR", 21 or 22 => "DDR2", 24 => "DDR3", 26 => "DDR4", 34 => "DDR5", _ => ""
    };

    static string GetOs()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine
                    .OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                if (key != null)
                {
                    var product = key.GetValue("ProductName") as string;
                    var display = key.GetValue("DisplayVersion") as string;
                    var build = key.GetValue("CurrentBuildNumber") as string;
                    var arch = Environment.Is64BitOperatingSystem ? "x64" : "x86";
                    var s = product ?? "Windows";
                    if (!string.IsNullOrWhiteSpace(display)) s += $" {display}";
                    if (!string.IsNullOrWhiteSpace(build)) s += $" (build {build})";
                    return $"{s} {arch}";
                }
            }
            catch { }
        }
        return Environment.OSVersion.VersionString;
    }
}

// ================= مدل‌های داده =================

public class SystemInfoData
{
    public string AgentId { get; set; } = "";
    public string? Motherboard { get; set; }
    public string? Cpu { get; set; }
    public string? Ram { get; set; }
    public string? HardDisk { get; set; }
    public string? Graphics { get; set; }
    public string? Monitor { get; set; }
    public string? OsName { get; set; }
    public int TotalRamGb { get; set; }
    public string? DetailsJson { get; set; }
}

public class HardwareDetails
{
    public string? Board { get; set; }
    public string? BoardSerial { get; set; }
    public string? ComputerModel { get; set; }
    public List<CpuDetail> Cpus { get; set; } = new();
    public List<RamStickDetail> RamSticks { get; set; } = new();
    public List<DiskDetail> Disks { get; set; } = new();
    public List<GpuDetail> Gpus { get; set; } = new();
    public List<MonitorDetail> Monitors { get; set; } = new();
    public List<VolumeDetail> Volumes { get; set; } = new();
    public List<NetAdapterDetail> NetAdapters { get; set; } = new();
}

public class CpuDetail
{
    public string Name { get; set; } = "";
    public int Cores { get; set; }
    public int Threads { get; set; }
    public double ClockGhz { get; set; }
}

public class RamStickDetail
{
    public string Slot { get; set; } = "";
    public int CapacityGb { get; set; }
    public string Type { get; set; } = "";
    public int SpeedMhz { get; set; }
    public int ConfiguredMhz { get; set; }
    public string Manufacturer { get; set; } = "";
    public string PartNumber { get; set; } = "";
    public string SerialNumber { get; set; } = "";
}

public class DiskDetail
{
    public string Model { get; set; } = "";
    public int SizeGb { get; set; }
    public string Interface { get; set; } = "";
    public string Media { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    /// <summary>وضعیت S.M.A.R.T: Healthy | Degraded | PredFail | Failed | Unknown</summary>
    public string Smart { get; set; } = "Unknown";
}

/// <summary>نقشه‌کردن وضعیت WMI درایو به وضعیت S.M.A.R.T</summary>
static class SmartMapper
{
    public static string FromStatus(string wmiStatus)
    {
        var s = (wmiStatus ?? "").Trim();
        return s switch
        {
            "OK" => "Healthy",
            "Pred Fail" => "PredFail",
            "Error" => "Failed",
            "" => "Unknown",
            _ => "Unknown"
        };
    }
}

public class GpuDetail
{
    public string Name { get; set; } = "";
    public string Resolution { get; set; } = "";
    public string DriverVersion { get; set; } = "";
}

public class MonitorDetail
{
    public string Name { get; set; } = "";
    public string Resolution { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public bool IsPrimary { get; set; }
}

public class NetAdapterDetail
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Type { get; set; } = "";
    public string MacAddress { get; set; } = "";
    public string Ipv4 { get; set; } = "";
    public string Gateway { get; set; } = "";
}

public class VolumeDetail
{
    public string Letter { get; set; } = "";
    public string Label { get; set; } = "";
    public int TotalGb { get; set; }
    public int UsedGb { get; set; }
}
