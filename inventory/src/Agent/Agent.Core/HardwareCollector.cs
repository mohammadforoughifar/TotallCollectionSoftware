using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Agent.Core;

/// <summary>
/// جمع‌آوری کامل مشخصات سخت‌افزار سیستم (مادربرد، CPU، رم، هارد، گرافیک، مانیتور، شبکه، درایوها).
/// این کلاس مستقل از هر UI است — از هر برنامه‌ای (کنسول، سرویس ویندوز، WPF و...) قابل استفاده است.
/// </summary>
public static class HardwareCollector
{
    private static readonly JsonSerializerOptions CamelJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>شناسه‌ی پیش‌فرض ایجنت بر اساس نام کامپیوتر.</summary>
    public static string DefaultAgentId => "AGENT-" + Environment.MachineName;

    /// <summary>
    /// جمع‌آوری همه‌ی مشخصات و ساخت داده‌ی آماده‌ی ارسال به سرور (شامل فیلدهای خلاصه + DetailsJson کامل).
    /// </summary>
    public static SystemInfoData Collect(string? agentId = null)
    {
        var details = CollectDetails();
        return new SystemInfoData
        {
            AgentId = string.IsNullOrWhiteSpace(agentId) ? DefaultAgentId : agentId!.Trim(),
            Motherboard = string.IsNullOrWhiteSpace(details.Board) ? null :
                $"{details.Board}{(string.IsNullOrWhiteSpace(details.ComputerModel) ? "" : " — " + details.ComputerModel)}",
            Cpu = details.Cpus.Count > 0 ?
                string.Join(" + ", details.Cpus.Select(c => c.Name + (c.Cores > 0 ? $" — {c.Cores} هسته / {c.Threads} رشته" : "") + (c.ClockGhz > 0 ? $" @ {c.ClockGhz:0.00}GHz" : ""))) : null,
            Ram = details.RamSticks.Count > 0 ?
                $"{details.RamSticks.Sum(r => r.CapacityGb)} GB — {details.RamSticks.Count} × " + string.Join(" + ", details.RamSticks.Select(r => $"{r.CapacityGb}GB{r.Type}")) : null,
            HardDisk = details.Disks.Count > 0 ?
                string.Join(" + ", details.Disks.Select(d => $"{d.Model} ({d.SizeGb}GB)")) : null,
            Graphics = details.Gpus.Count > 0 ? string.Join(" + ", details.Gpus.Select(g => g.Name + (string.IsNullOrEmpty(g.Resolution) ? "" : $" — {g.Resolution}"))) : null,
            Monitor = details.Monitors.Count > 0 ? string.Join(" + ", details.Monitors.Select(m => m.Name + (string.IsNullOrEmpty(m.Resolution) ? "" : $" ({m.Resolution})") + (m.IsPrimary ? " ★" : ""))) : null,
            OsName = GetOs(),
            TotalRamGb = details.RamSticks.Sum(r => r.CapacityGb),
            DetailsJson = JsonSerializer.Serialize(details, CamelJson)
        };
    }

    // ================= جمع‌آوری ساختاریافته =================

    /// <summary>جمع‌آوری جزئیات کامل ساختاریافته (بدون سریال‌سازی).</summary>
    public static HardwareDetails CollectDetails()
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
    public static List<NetAdapterDetail> GetNetAdapters()
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
    public static List<MonitorDetail> GetMonitors()
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
    public static List<(string Res, bool Primary)> GetScreenResolutions()
    {
        var result = new List<(string, bool)>();
        if (!OperatingSystem.IsWindows()) return result;

        for (uint i = 0; i < 16; i++)
        {
            var dd = new DISPLAY_DEVICE { Size = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, i, ref dd, 0)) break;
            if ((dd.StateFlags & DISPLAY_DEVICE_ATTACHED) == 0) continue;

            var dm = new DEVMODE { Size = (short)Marshal.SizeOf<DEVMODE>() };
            if (EnumDisplaySettings(dd.DeviceName, ENUM_CURRENT_SETTINGS, ref dm) && dm.PelsWidth > 0)
            {
                var primary = (dd.StateFlags & DISPLAY_DEVICE_PRIMARY) != 0;
                result.Add(($"{dm.PelsWidth}×{dm.PelsHeight}", primary));
            }
        }
        return result;
    }

    /// <summary>تبدیل آرایه‌ی ushort مربوط به WMI به رشته.</summary>
    private static string? Decode(object? arr)
    {
        if (arr is not ushort[] a) return null;
        return string.Concat(a.TakeWhile(v => v != 0).Select(v => (char)v));
    }

    private static string MemType(object? smbiosType) => Convert.ToInt32(smbiosType) switch
    {
        20 => "DDR", 21 or 22 => "DDR2", 24 => "DDR3", 26 => "DDR4", 34 => "DDR5", _ => ""
    };

    /// <summary>نام و نسخه‌ی دقیق سیستم‌عامل (از رجیستری ویندوز).</summary>
    public static string GetOs()
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
