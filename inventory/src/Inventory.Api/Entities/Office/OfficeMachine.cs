using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>ماشین‌های اداری — مدل، تاریخ نصب، وضعیت و تاریخ رفت/برگشت.</summary>
public class OfficeMachine
{
    public int Id { get; set; }
    [MaxLength(150)] public string Model { get; set; } = "";
    [MaxLength(150)] public string? SerialNumber { get; set; }
    [MaxLength(250)] public string? Location { get; set; }
    public DateTime? InstallDate { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>تاریخ رفت (ارسال به تعمیر/خارج از محل)</summary>
    public DateTime? GoneDate { get; set; }
    /// <summary>تاریخ برگشت</summary>
    public DateTime? ReturnDate { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }

    /// <summary>نوع اتصال: Network (شبکه) | Cable (کابلی)</summary>
    [MaxLength(20)] public string ConnectionType { get; set; } = "Network";

    /// <summary>آی‌پی — برای ماشین‌های شبکه</summary>
    [MaxLength(50)] public string? IpAddress { get; set; }

    /// <summary>سیستم متصل — برای ماشین‌های کابلی</summary>
    public int? LinkedSystemInfoId { get; set; }

    /// <summary>برچسب سیستم متصل (نام مالک) برای نمایش</summary>
    [MaxLength(250)] public string? LinkedSystemLabel { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>سابقه‌ی تعمیر هر ماشین — تاریخ + شرح مشکل.</summary>
public class OfficeMachineRepair
{
    public int Id { get; set; }
    public int MachineId { get; set; }
    public DateTime RepairDate { get; set; } = DateTime.Now;
    /// <summary>تاریخ رفت ماشین به تعمیر</summary>
    public DateTime? GoneDate { get; set; }
    /// <summary>تاریخ برگشت</summary>
    public DateTime? ReturnDate { get; set; }
    /// <summary>شرح ایراد</summary>
    [MaxLength(1000)] public string Problem { get; set; } = "";
    /// <summary>شرح کارهای انجام‌شده</summary>
    [MaxLength(2000)] public string? PerformedWork { get; set; }
    /// <summary>هزینه‌ی همین تعمیر</summary>
    public decimal Cost { get; set; }
    public bool Fixed { get; set; } = true;
}

/// <summary>هزینه‌های انجام‌شده برای هر ماشین.</summary>
public class OfficeMachineCost
{
    public int Id { get; set; }
    public int MachineId { get; set; }
    public DateTime CostDate { get; set; } = DateTime.Now;
    [MaxLength(300)] public string Title { get; set; } = "";
    public decimal Amount { get; set; }
}
