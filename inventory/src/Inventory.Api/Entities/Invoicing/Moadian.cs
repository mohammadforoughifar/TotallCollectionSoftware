using System.ComponentModel.DataAnnotations;
using Inventory.Shared;

namespace Inventory.Api.Data;

// =====================================================================
// موجودیت‌های سامانه مودیان (فاکتور الکترونیکی) و چاپگر مالی
//   MoadianSetting    تنظیمات اتصال (شماره مالیاتی، توکن، کلید امضا، آدرس سرویس)
//   MoadianFiscalPeriod  دوره‌های مالیاتی (ماه شمسی)
//   MoadianInvoice    فاکتور الکترونیکی با مشخصه‌های استاندارد سامانه
//   MoadianInvoiceLine    اقلام فاکتور (کد CPC / شرح / مبلغ / مالیات)
//   MoadianLog        لاگ کامل چرخه ارسال
//   MoadianCpc        شناسه‌های کالا/خدمت (CPC) — از سامانه قابل به‌روزرسانی
// =====================================================================

/// <summary>تنظیمات سامانه مودیان.</summary>
public class MoadianSetting
{
    public int Id { get; set; }

    /// <summary>شماره ملی/شناسه ملی (شماره مالیاتی فروشنده) — ۱۱ رقم</summary>
    [MaxLength(20)] public string TaxId { get; set; } = "";

    /// <summary>کد اقتصادی</summary>
    [MaxLength(20)] public string? EconomicCode { get; set; }

    /// <summary>نام فروشنده (برای header فاکتور)</summary>
    [MaxLength(200)] public string SellerName { get; set; } = "";

    [MaxLength(300)] public string? SellerAddress { get; set; }
    [MaxLength(20)] public string? SellerPostalCode { get; set; }
    [MaxLength(30)] public string? SellerPhone { get; set; }

    /// <summary>شناسه کارتابل سامانه (توکن حافظه مالیاتی) — از پرتال مودیان</summary>
    [MaxLength(200)] public string? TaxCardToken { get; set; }

    /// <summary>آدرس سرویس (آزمایشی/عملیاتی) — خالی = حالت شبیه‌سازی ارسال</summary>
    [MaxLength(300)] public string? BaseUrl { get; set; }

    /// <summary>کلید خصوصی امضای فاکتور (PEM) — از پرتال مودیان دریافت می‌شود</summary>
    public string? PrivateKeyPem { get; set; }

    /// <summary>گواهی سرور/کلید عمومی (اختیاری برای اعتبارسنجی)</summary>
    public string? PublicKeyPem { get; set; }

    /// <summary>ارسال خودکار صف (با سرویس پس‌زمینه)</summary>
    public bool AutoSend { get; set; }

    /// <summary>بازه ارسال خودکار (دقیقه)</summary>
    public int SendIntervalMinutes { get; set; } = 5;

    /// <summary>نرخ مالیات بر ارزش افزوده پیش‌فرض (٪)</summary>
    public decimal DefaultVatRate { get; set; } = 9;

    /// <summary>ذخیره‌ی خودکار پاسخ/لاگ</summary>
    public bool LoggingEnabled { get; set; } = true;

    public bool IsActive { get; set; } = true;
    [MaxLength(500)] public string? Notes { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>دوره مالیاتی مودیان (یک ماه شمسی).</summary>
public class MoadianFiscalPeriod
{
    public int Id { get; set; }

    /// <summary>سال شمسی</summary>
    public int Year { get; set; }

    /// <summary>ماه شمسی ۱..۱۲</summary>
    public int Month { get; set; }

    /// <summary>بسته — فاکتور جدید در این دوره مجاز نیست</summary>
    public bool IsClosed { get; set; }

    [MaxLength(500)] public string? Notes { get; set; }
}

/// <summary>فاکتور الکترونیکی (مشخصه‌های استاندارد سامانه مودیان).</summary>
public class MoadianInvoice
{
    public int Id { get; set; }

    /// <summary>شماره فاکتور در دوره مالیاتی — یکتا در هر دوره</summary>
    public int Number { get; set; }

    /// <summary>نوع فاکتور (فروش/فروش برگشتی/خرید/…)</summary>
    public MoadianInvoiceKind Kind { get; set; } = MoadianInvoiceKind.Sale;

    /// <summary>تاریخ صدور (شمسی در UI، میلادی ذخیره)</summary>
    public DateTime Date { get; set; } = DateTime.Now;

    /// <summary>دوره مالیاتی</summary>
    public int FiscalPeriodId { get; set; }
    public MoadianFiscalPeriod? FiscalPeriod { get; set; }

    /// <summary>فاکتور داخلی مبدأ (اختیاری — فاکتور فروش/خرید سیستم)</summary>
    public int? FacInvoiceId { get; set; }

    /// <summary>مرجع فاکتور داخلی مبدأ (نمایشی)</summary>
    [MaxLength(60)] public string? FacInvoiceRef { get; set; }

    /// <summary>محل تحویل / نوع تسویه</summary>
    [MaxLength(60)] public string? Settlement { get; set; }

    // ---------- فروشنده (از تنظیمات) ----------
    [MaxLength(20)] public string TaxId { get; set; } = "";
    [MaxLength(200)] public string SellerName { get; set; } = "";
    [MaxLength(20)] public string? EconomicCode { get; set; }

    // ---------- خریدار ----------
    [MaxLength(20)] public string? BuyerTaxId { get; set; }
    [MaxLength(200)] public string? BuyerName { get; set; }
    [MaxLength(300)] public string? BuyerAddress { get; set; }
    [MaxLength(20)] public string? BuyerPostalCode { get; set; }
    [MaxLength(30)] public string? BuyerPhone { get; set; }

    // ---------- مبالغ ----------
    public decimal TotalGross { get; set; }
    public decimal TotalDiscount { get; set; }
    public decimal TotalTaxable { get; set; }
    public decimal TotalVat { get; set; }
    public decimal TotalNet { get; set; }

    // ---------- چرخه سامانه ----------
    public MoadianInvoiceStatus Status { get; set; } = MoadianInvoiceStatus.Draft;

    /// <summary>شناسه یکتای مالیاتی (UID) — مرجع سامانه پس از ارسال</summary>
    [MaxLength(64)] public string? ReferenceId { get; set; }

    /// <summary>شماره پیگیری (Logistic Id) سامانه</summary>
    [MaxLength(64)] public string? TrackingId { get; set; }

    /// <summary>کد خطای سامانه (در صورت برگشت/خطا)</summary>
    [MaxLength(30)] public string? ErrorCode { get; set; }

    [MaxLength(500)] public string? ErrorMessage { get; set; }

    /// <summary>تعداد تلاش ارسال</summary>
    public int Attempts { get; set; }

    public DateTime? QueuedAt { get; set; }
    public DateTime? SendAt { get; set; }
    public DateTime? ReturnedAt { get; set; }

    [MaxLength(120)] public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    [MaxLength(600)] public string? Description { get; set; }

    public List<MoadianInvoiceLine> Lines { get; set; } = new();
}

/// <summary>قلم فاکتور الکترونیکی.</summary>
public class MoadianInvoiceLine
{
    public int Id { get; set; }

    public int InvoiceId { get; set; }
    public MoadianInvoice? Invoice { get; set; }

    public int RowNo { get; set; }

    /// <summary>شناسه کالا/خدمت (کد CPC سامانه)</summary>
    [MaxLength(40)] public string SstId { get; set; } = "";

    /// <summary>شرح کالا/خدمت</summary>
    [MaxLength(400)] public string SstTitle { get; set; } = "";

    /// <summary>تعداد</summary>
    public decimal Quantity { get; set; }

    /// <summary>مبلغ واحد</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>تخفیف سطر</summary>
    public decimal Discount { get; set; }

    /// <summary>نرخ مالیات (٪)</summary>
    public decimal VatRate { get; set; }

    /// <summary>مبلغ مالیات سطر</summary>
    public decimal VatAmount { get; set; }

    /// <summary>مبلغ کل سطر (مأخذ + مالیات)</summary>
    public decimal Total { get; set; }

    /// <summary>مأخذ مالیات سطر (پس از تخفیف)</summary>
    public decimal Taxable => (Quantity * UnitPrice) - Discount;
}

/// <summary>لاگ چرخه‌ی مودیان.</summary>
public class MoadianLog
{
    public int Id { get; set; }

    public int InvoiceId { get; set; }
    public MoadianInvoice? Invoice { get; set; }

    public MoadianLogAction Action { get; set; }

    /// <summary>خلاصه (فارسی)</summary>
    [MaxLength(300)] public string Message { get; set; } = "";

    /// <summary>جزئیات فنی / پاسخ سامانه</summary>
    public string? Detail { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>شناسه‌های کالا/خدمت (CPC) — از فهرست سامانه مودیان.</summary>
public class MoadianCpc
{
    public int Id { get; set; }

    [MaxLength(40)] public string Code { get; set; } = "";
    [MaxLength(400)] public string Title { get; set; } = "";

    /// <summary>نام لاتین</summary>
    [MaxLength(200)] public string? EnTitle { get; set; }

    public bool IsActive { get; set; } = true;
    [MaxLength(200)] public string? Unit { get; set; }
}

/// <summary>تنظیمات چاپگر مالی/حرارتی (رسید فاکتور الکترونیکی).</summary>
public class FiscalPrinterSetting
{
    public int Id { get; set; }

    /// <summary>نام/شرح چاپگر (برای شناسایی در UI)</summary>
    [MaxLength(100)] public string PrinterName { get; set; } = "چاپگر حرارتی ۸۰";

    /// <summary>مسیر دستگاه/فایل یا آدرس شبکه: FILE:/path یا IP:9100 یا /dev/usb/lp0</summary>
    [MaxLength(300)] public string? PortName { get; set; }

    /// <summary>عرض کاغذ (میلی‌متر) — ۸۰ یا ۵۸</summary>
    public int PaperWidthMm { get; set; } = 80;

    public int Copies { get; set; } = 1;

    [MaxLength(500)] public string? HeaderLines { get; set; }

    [MaxLength(500)] public string? FooterLines { get; set; }

    /// <summary>بریدن کاغذ پس از چاپ (ESC/POS GS V)</summary>
    public bool CutPaper { get; set; }

    public bool Enabled { get; set; } = true;
    [MaxLength(500)] public string? Notes { get; set; }
}
