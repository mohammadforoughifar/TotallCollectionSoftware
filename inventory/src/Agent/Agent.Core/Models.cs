namespace Agent.Core;

// ================= مدل‌های داده ایجنت شناسنامه سخت‌افزار =================

/// <summary>داده‌ی نهایی که به اندپوینت api/SystemInfo سرور ارسال می‌شود.</summary>
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

/// <summary>جزئیات کامل و ساختاریافته‌ی سخت‌افزار (در DetailsJson سریال می‌شود).</summary>
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

/// <summary>دستور از راه دور (ری‌استارت/خاموش/قفل) که سرور برای این سیستم صادر کرده.</summary>
public class AgentCommand
{
    public int Id { get; set; }
    public string Action { get; set; } = "";
    public string? Message { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>نتیجه‌ی ارسال اطلاعات به سرور.</summary>
public class SendResult
{
    public bool Success { get; set; }
    public int StatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public string? Error { get; set; }
}

/// <summary>نقشه‌کردن وضعیت WMI درایو به وضعیت S.M.A.R.T</summary>
public static class SmartMapper
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
