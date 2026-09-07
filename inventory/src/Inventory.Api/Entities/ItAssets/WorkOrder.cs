using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>دستور کار — قابل محول‌کردن به خود یا دیگران.</summary>
public class WorkOrder
{
    public int Id { get; set; }

    /// <summary>شماره منحصربه‌فرد: WO/سال شمسی/سریال</summary>
    [MaxLength(30)]
    public string Number { get; set; } = "";

    [MaxLength(200)]
    public string Title { get; set; } = "";

    /// <summary>شرح — HTML از ادیتور</summary>
    [MaxLength(8000)]
    public string Description { get; set; } = "";

    /// <summary>دستوردهنده</summary>
    public int OwnerUserId { get; set; }

    [MaxLength(150)]
    public string OwnerName { get; set; } = "";

    /// <summary>تاریخ و ساعت مقرر</summary>
    public DateTime DueAt { get; set; }

    /// <summary>Open | Closed — بعد از بستن هیچ عملیاتی مجاز نیست</summary>
    [MaxLength(20)]
    public string Status { get; set; } = "Open";

    [MaxLength(1000)]
    public string? CloseNote { get; set; }
    public DateTime? ClosedAt { get; set; }

    /// <summary>تعداد تمدیدها — حداکثر ۵ بار</summary>
    public int ExtensionCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// ماژول مبدأ (سورس) که دستور کار از آن ساخته شده — برای اتصالِ عمومی بخش‌ها به دستور کار.
    /// مثال: "InnerLetter" (نامه داخلی). برای سایر ماژول‌ها در آینده بدون تغییر ساختار قابل استفاده است.
    /// </summary>
    [MaxLength(50)]
    public string? SourceModule { get; set; }

    /// <summary>شناسه رکورد مبدأ در ماژول سورس (مثلاً شناسه نامه داخلی).</summary>
    public int? SourceId { get; set; }
}

/// <summary>گیرنده دستور کار — پاسخ، رویت و تصمیم دستوردهنده.</summary>
public class WorkOrderAssignee
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int UserId { get; set; }

    [MaxLength(150)]
    public string Name { get; set; } = "";

    /// <summary>تاریخ و ساعت رویت</summary>
    public DateTime? SeenAt { get; set; }

    /// <summary>تاریخ و ساعت پاسخ</summary>
    public DateTime? RepliedAt { get; set; }

    /// <summary>true=انجام شد | false=انجام نشد | null=بدون پاسخ</summary>
    public bool? Done { get; set; }

    [MaxLength(2000)]
    public string? ReplyText { get; set; }

    /// <summary>تصمیم دستوردهنده: Approved | Rejected | null</summary>
    [MaxLength(20)]
    public string? OwnerDecision { get; set; }

    [MaxLength(1000)]
    public string? OwnerDecisionNote { get; set; }
}

/// <summary>تاریخچه کامل رفت‌وبرگشت‌های دستور کار (آرشیو).</summary>
public class WorkOrderLog
{
    public int Id { get; set; }
    public int OrderId { get; set; }

    [MaxLength(150)]
    public string ActorName { get; set; } = "";

    /// <summary>Created | Seen | Reply | Approved | Rejected | Extended | Closed</summary>
    [MaxLength(30)]
    public string Action { get; set; } = "";

    [MaxLength(4000)]
    public string? Text { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>پیوست دستور کار.</summary>
public class WorkOrderAttachment
{
    public int Id { get; set; }
    public int OrderId { get; set; }

    [MaxLength(255)]
    public string FileName { get; set; } = "";

    [MaxLength(100)]
    public string ContentType { get; set; } = "";

    public byte[] Data { get; set; } = Array.Empty<byte>();
    /// <summary>مسیر نسبی فایل داخل uploads/ (فایل‌های جدید روی دیسک؛ Data فقط برای رکوردهای قدیمی).</summary>
    [MaxLength(255)]
    public string? FilePath { get; set; }


    [MaxLength(150)]
    public string UploaderName { get; set; } = "";
    public int UploaderUserId { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.Now;
}

/// <summary>لیست افرادی که هر کاربر مجاز است به آن‌ها دستور کار بدهد.</summary>
public class WorkOrderAllowedAssignee
{
    public int Id { get; set; }

    /// <summary>دستوردهنده</summary>
    public int OwnerUserId { get; set; }

    /// <summary>گیرنده مجاز</summary>
    public int TargetUserId { get; set; }
}
