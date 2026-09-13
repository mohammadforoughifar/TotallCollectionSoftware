namespace Inventory.Shared.Dtos;

// ==================== داشبورد یکپارچه مدیر (§۶) ====================

public class HrMonthPointDto
{
    /// <summary>ماه شمسی ۱..۱۲</summary>
    public int Month { get; set; }
    public double Value { get; set; }
}

public class HrNameValueDto
{
    public string Name { get; set; } = "";
    public double Value { get; set; }
}

public class HrManagerDashboardDto
{
    public int Year { get; set; }
    public int ActiveEmployees { get; set; }
    public int HiresThisYear { get; set; }
    public int ExitsThisYear { get; set; }
    /// <summary>نرخ ترک‌خدمت ٪ (خروجی / میانگین نفرات)</summary>
    public double TurnoverRate { get; set; }
    /// <summary>نرخ غیبت ٪ (روز مرخصی تأییدشده / نفر-روز کاری)</summary>
    public double AbsenceRate { get; set; }
    public double AvgLeaveDaysPerEmp { get; set; }
    /// <summary>میانگین هزینه سرانه ناخالص آخرین دوره (ریال)</summary>
    public double? AvgCostPerHead { get; set; }
    /// <summary>جمع ناخالص آخرین دوره (ریال)</summary>
    public double? TotalPayrollLastRun { get; set; }
    public string? LastRunLabel { get; set; }
    /// <summary>روند اضافه‌کاری ماهانه (ساعت)</summary>
    public List<HrMonthPointDto> OvertimeTrend { get; set; } = new();
    /// <summary>روند مرخصی ماهانه (روز)</summary>
    public List<HrMonthPointDto> LeaveTrend { get; set; } = new();
    /// <summary>روند حقوق ناخالص ماهانه (ریال)</summary>
    public List<HrMonthPointDto> PayrollTrend { get; set; } = new();
    public List<HrNameValueDto> LeaveByType { get; set; } = new();
    public List<HrNameValueDto> HeadcountByUnit { get; set; } = new();
}
