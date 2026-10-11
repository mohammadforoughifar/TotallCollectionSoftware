using System.ComponentModel.DataAnnotations;
using Inventory.Shared;
namespace Inventory.Api.Data;

/// <summary>پذیرش دستگاه تعمیری (لپ‌تاپ، کامپیوتر، مانیتور، دوربین و ...)</summary>
public class RepairOrder
{
    public int Id { get; set; }

    /// <summary>شماره پذیرش (RP-0001)</summary>
    [MaxLength(30)]
    public string Number { get; set; } = "";

    /// <summary>مشتری (صاحب دستگاه)</summary>
    public int PartyId { get; set; }

    /// <summary>تعمیرکار مسئول</summary>
    public int? TechnicianId { get; set; }

    /// <summary>نوع دستگاه: لپ‌تاپ، کامپیوتر، مانیتور، دوربین، ...</summary>
    [MaxLength(100)]
    public string DeviceType { get; set; } = "";

    /// <summary>برند و مدل دستگاه</summary>
    [MaxLength(200)]
    public string? DeviceModel { get; set; }

    /// <summary>سریال / شناسه دستگاه</summary>
    [MaxLength(100)]
    public string? SerialNumber { get; set; }

    /// <summary>شرح ایراد اعلامی مشتری</summary>
    [MaxLength(1000)]
    public string? ProblemDescription { get; set; }

    /// <summary>لوازم همراه دستگاه (شارژر، کیف، ...)</summary>
    [MaxLength(500)]
    public string? Accessories { get; set; }

    public RepairStatus Status { get; set; } = RepairStatus.Received;

    /// <summary>تاریخ ورود به مجموعه (پذیرش)</summary>
    public DateTime ReceivedAt { get; set; }

    /// <summary>تاریخ خروج از مجموعه (تحویل)</summary>
    public DateTime? DeliveredAt { get; set; }

    /// <summary>مبلغ توافقی/برآوردی اعلام‌شده به مشتری (اجرت + قطعات)</summary>
    public decimal QuotedPrice { get; set; }

    /// <summary>شناسه فاکتور فروش صادرشده (بعد از تحویل)</summary>
    public int? InvoiceTransactionId { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public List<RepairItem> Items { get; set; } = new();
}