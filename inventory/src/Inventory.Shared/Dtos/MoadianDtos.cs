namespace Inventory.Shared.Dtos;

// =====================================================================
// DTO های سامانه مودیان (فاکتور الکترونیکی) و چاپگر مالی
// =====================================================================

/// <summary>تنظیمات سامانه مودیان.</summary>
public class MoadianSetting
{
    public int Id { get; set; }
    public string TaxId { get; set; } = "";
    public string? EconomicCode { get; set; }
    public string SellerName { get; set; } = "";
    public string? SellerAddress { get; set; }
    public string? SellerPostalCode { get; set; }
    public string? SellerPhone { get; set; }
    public string? TaxCardToken { get; set; }
    public string? BaseUrl { get; set; }
    public string? PrivateKeyPem { get; set; }
    public string? PublicKeyPem { get; set; }
    public bool AutoSend { get; set; }
    public int SendIntervalMinutes { get; set; } = 5;
    public decimal DefaultVatRate { get; set; } = 9;
    public bool LoggingEnabled { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    /// <summary>حالت شبیه‌سازی: بدون آدرس سرویس — ارسال به‌صورت محلی شبیه‌سازی می‌شود</summary>
    public bool IsSimulation => string.IsNullOrWhiteSpace(BaseUrl);
}

/// <summary>دوره مالیاتی.</summary>
public class MoadianFiscalPeriod
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public bool IsClosed { get; set; }
    public string? Notes { get; set; }

    public string Title => $"{Year}/{Month:00}";
    public int InvoiceCount { get; set; }
    public decimal NetTotal { get; set; }
}

/// <summary>فاکتور الکترونیکی.</summary>
public class MoadianInvoice
{
    public int Id { get; set; }
    public int Number { get; set; }
    public MoadianInvoiceKind Kind { get; set; } = MoadianInvoiceKind.Sale;
    public DateTime Date { get; set; }
    public int FiscalPeriodId { get; set; }
    public string? FiscalPeriodTitle { get; set; }
    public int? FacInvoiceId { get; set; }
    public string? FacInvoiceRef { get; set; }
    public string? Settlement { get; set; }

    public string TaxId { get; set; } = "";
    public string SellerName { get; set; } = "";
    public string? EconomicCode { get; set; }

    public string? BuyerTaxId { get; set; }
    public string? BuyerName { get; set; }
    public string? BuyerAddress { get; set; }
    public string? BuyerPostalCode { get; set; }
    public string? BuyerPhone { get; set; }

    public decimal TotalGross { get; set; }
    public decimal TotalDiscount { get; set; }
    public decimal TotalTaxable { get; set; }
    public decimal TotalVat { get; set; }
    public decimal TotalNet { get; set; }

    public MoadianInvoiceStatus Status { get; set; } = MoadianInvoiceStatus.Draft;
    public string? ReferenceId { get; set; }
    public string? TrackingId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int Attempts { get; set; }
    public DateTime? QueuedAt { get; set; }
    public DateTime? SendAt { get; set; }
    public DateTime? ReturnedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Description { get; set; }

    public List<MoadianInvoiceLine> Lines { get; set; } = new();
}

/// <summary>قلم فاکتور الکترونیکی.</summary>
public class MoadianInvoiceLine
{
    public int Id { get; set; }
    public int RowNo { get; set; }
    public string SstId { get; set; } = "";
    public string SstTitle { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }
    public decimal Total { get; set; }
    public decimal Taxable => (Quantity * UnitPrice) - Discount;
}

/// <summary>لاگ مودیان.</summary>
public class MoadianLog
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public int InvoiceNumber { get; set; }
    public MoadianLogAction Action { get; set; }
    public string Message { get; set; } = "";
    public string? Detail { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>شناسه کالا/خدمت (CPC).</summary>
public class MoadianCpc
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string? EnTitle { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Unit { get; set; }
}

/// <summary>درخواست ثبت دستی فاکتور الکترونیکی.</summary>
public class MoadianInvoiceRequest
{
    public MoadianInvoiceKind Kind { get; set; } = MoadianInvoiceKind.Sale;
    public DateTime Date { get; set; } = DateTime.Now;
    public int FiscalPeriodId { get; set; }
    public string? BuyerTaxId { get; set; }
    public string? BuyerName { get; set; }
    public string? BuyerAddress { get; set; }
    public string? BuyerPostalCode { get; set; }
    public string? BuyerPhone { get; set; }
    public string? Settlement { get; set; }
    public string? Description { get; set; }
    public List<MoadianInvoiceLine> Lines { get; set; } = new();
}

/// <summary>نمای کلی داشبورد مودیان.</summary>
public class MoadianDashboard
{
    public int TotalCount { get; set; }
    public int DraftCount { get; set; }
    public int QueuedCount { get; set; }
    public int SentCount { get; set; }
    public int FailedCount { get; set; }
    public int ReturnedCount { get; set; }

    public decimal NetTotal { get; set; }
    public decimal VatTotal { get; set; }
    public bool IsSimulation { get; set; }
    public string? TaxId { get; set; }
    public string? SellerName { get; set; }
    public int AutoSendMinutes { get; set; }

    /// <summary>آخرین خطاها/برگشت‌ها</summary>
    public List<MoadianInvoice> RecentIssues { get; set; } = new();
}

/// <summary>خروجی ساخت payload استاندارد مودیان (برای تست/دیباگ).</summary>
public class MoadianPayloadResult
{
    public int InvoiceId { get; set; }
    public string Json { get; set; } = "";
    public string? Signature { get; set; }
    public string? Uid { get; set; }
    public string? Warning { get; set; }
}

// =====================================================================
// چاپگر مالی / حرارتی
// =====================================================================

/// <summary>تنظیمات چاپگر مالی/حرارتی.</summary>
public class FiscalPrinterSetting
{
    public int Id { get; set; }
    public string PrinterName { get; set; } = "چاپگر حرارتی ۸۰";
    public string? PortName { get; set; }
    public int PaperWidthMm { get; set; } = 80;
    public int Copies { get; set; } = 1;
    public string? HeaderLines { get; set; }
    public string? FooterLines { get; set; }
    public bool CutPaper { get; set; }
    public bool Enabled { get; set; } = true;
    public string? Notes { get; set; }

    /// <summary>ایمان پورت پیکربندی‌شده (فایل/دستگاه/شبکه) یا حالت شبیه‌سازی</summary>
    public bool HasPort => !string.IsNullOrWhiteSpace(PortName);
}

/// <summary>نتیجه چاپ / پیش‌نمایش رسید.</summary>
public class FiscalPrintResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";

    /// <summary>متن رسید (طرح‌بندی ثابت — ۸۰ ستون)</summary>
    public string TicketText { get; set; } = "";

    /// <summary>پیش‌نمایش HTML رسید (برای نمایش/چاپ مرورگر)</summary>
    public string PreviewHtml { get; set; } = "";

    /// <summary>محتوای QR (شناسه مالیاتی + شماره + UID)</summary>
    public string? QrContent { get; set; }

    /// <summary>خروجی به فایل/دستگاه شد یا شبیه‌سازی</summary>
    public bool Simulated { get; set; }
}
