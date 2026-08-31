namespace Inventory.Shared.Dtos;

/// <summary>تعمیرکار مجموعه.</summary>
public class Technician
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }

    /// <summary>تخصص (لپ‌تاپ، موبایل، دوربین، ...)</summary>
    public string? Specialty { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    /// <summary>تعداد پذیرش‌های فعال این تعمیرکار (پر شده توسط سرور)</summary>
    public int ActiveRepairs { get; set; }
}

/// <summary>پذیرش دستگاه تعمیری.</summary>
public class RepairOrderDto
{
    public int Id { get; set; }
    public string Number { get; set; } = "";

    public int PartyId { get; set; }
    public string? PartyName { get; set; }

    public int? TechnicianId { get; set; }
    public string? TechnicianName { get; set; }

    public string DeviceType { get; set; } = "";
    public string? DeviceModel { get; set; }
    public string? SerialNumber { get; set; }
    public string? ProblemDescription { get; set; }
    public string? Accessories { get; set; }

    public RepairStatus Status { get; set; } = RepairStatus.Received;

    /// <summary>تاریخ ورود به مجموعه</summary>
    public DateTime ReceivedAt { get; set; }

    /// <summary>تاریخ خروج (تحویل)</summary>
    public DateTime? DeliveredAt { get; set; }

    /// <summary>مبلغ برآوردی اعلام‌شده به مشتری</summary>
    public decimal QuotedPrice { get; set; }

    /// <summary>شناسه فاکتور فروش صادرشده</summary>
    public int? InvoiceTransactionId { get; set; }

    /// <summary>شماره فاکتور فروش صادرشده (پر شده توسط سرور)</summary>
    public string? InvoiceNumber { get; set; }

    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<RepairItemDto> Items { get; set; } = new();

    // ---------- جمع‌های محاسباتی (پر شده توسط سرور) ----------

    /// <summary>جمع دریافتی از مشتری (اجرت + قطعات)</summary>
    public decimal TotalPrice { get; set; }

    /// <summary>جمع هزینه داخلی (بهای قطعات + هزینه کار)</summary>
    public decimal TotalCost { get; set; }

    /// <summary>سود = دریافتی − هزینه</summary>
    public decimal Profit { get; set; }
}

/// <summary>ردیف کار انجام‌شده / قطعه مصرفی.</summary>
public class RepairItemDto
{
    public int Id { get; set; }

    /// <summary>شرح کار یا قطعه</summary>
    public string Description { get; set; } = "";

    /// <summary>کالای مصرفی از انبار (null = فقط اجرت/کار)</summary>
    public int? ProductId { get; set; }
    public string? ProductName { get; set; }

    public decimal Quantity { get; set; } = 1;

    /// <summary>هزینه داخلی</summary>
    public decimal Cost { get; set; }

    /// <summary>مبلغ دریافتی از مشتری</summary>
    public decimal Price { get; set; }
}

/// <summary>درخواست صدور فاکتور فروش از روی پذیرش تعمیر.</summary>
public class RepairInvoiceRequest
{
    /// <summary>انبار برداشت قطعات (پیش‌فرض: انبار مرکزی)</summary>
    public int WarehouseId { get; set; } = 1;
}
