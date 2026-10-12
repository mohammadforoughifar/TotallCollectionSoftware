namespace Inventory.Api.Data;

// ============================================================
// جداول ماژول مودیان (نسخه 0.0.27) — از پروژهٔ مبدأ (CoreApp).
// معیار فیلدها: فایل‌های DataLayerApp.Entities ارسالی کاربر
// (CPInfo/ConnectInfo/Customer/FiscalYear/GoodsOrService/
// invoice/InvoiceItem/InvoicePayment/UnitOfMeasurement) — هم‌ترازی کامل.
// ستون‌های جاافتاده با MoadianSchemaV1 روی دیتابیس‌های موجود idempotent اضافه می‌شوند.
// ============================================================

/// <summary>شرکت/خدمات‌دهندهٔ متصل به سامانه مودیان (cpinfo).</summary>
public class CpInfo
{
    public int Id { get; set; }
    /// <summary>اتصال به شرکت داخلی سیستم (معادل CompanyId پروژه مبدأ).</summary>
    public int? CompanyId { get; set; }
    /// <summary>«حقیقی» / «حقوقی»</summary>
    public string? Type { get; set; }
    public string? EconomicNumber { get; set; }
    public string? PersianName { get; set; }
    public string? EnglishName { get; set; }
    public string? Nationalid { get; set; }
    public string? PostalCode { get; set; }
    public bool IsDelete { get; set; }
}

/// <summary>مشتری صورتحساب مودیان (حقیقی/حقوقی).</summary>
public class Customer
{
    public int Id { get; set; }
    public int? CpInfoId { get; set; }
    public string? Type { get; set; }
    public string? Name { get; set; }
    public string? NationalID { get; set; }
    public string? EconomicNumber { get; set; }
    public string? Phone { get; set; }
    public string? PostalCode { get; set; }
    public string? Address { get; set; }
    public bool IsDelete { get; set; }
}

/// <summary>کالا/خدمت به‌همراه شناسهٔ CPC و واحد.</summary>
public class GoodsOrService
{
    public int Id { get; set; }
    public int CpInfo { get; set; }
    public string? Name { get; set; }
    public string? UniqueIdentifier { get; set; }
    public string? UnitOfMeasurement { get; set; }
    public byte ValueAddedPercentage { get; set; }
    public decimal Price { get; set; }
    public bool IsDelete { get; set; }
}

/// <summary>واحد سنجش معتبر سامانه مودیان (نام + کد).</summary>
public class UnitOfMeasurement
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Code { get; set; }
    public bool IsDelete { get; set; }
}

/// <summary>سال مالی صورتحساب مودیان.</summary>
public class FiscalYear
{
    public int Id { get; set; }
    /// <summary>عنوان سال مالی (مثلاً «سال مالی ۱۴۰۵») — معادل Title مبدأ.</summary>
    public string? Title { get; set; }
    public int? CpInfo { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public DateTime CreatedDate { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDelete { get; set; }
}

/// <summary>صورتحساب مودیان (اصلی/اصلاحی/ابطالی/برگشت).</summary>
public class Invoice
{
    public int Id { get; set; }
    public int? FiscalYearId { get; set; }
    public int? InvoiceNumber { get; set; }
    public int? CustomerId { get; set; }
    public int? CpInfo { get; set; }
    public int TypePattern { get; set; }
    public DateOnly? DateOrder { get; set; }
    public string? PaymentType { get; set; }
    public string? Description { get; set; }
    public string? StatusSentToSystem { get; set; }
    public string? TaxNumber { get; set; }
    /// <summary>شناسهٔ حافظه مغطبی زمان صدور — معادل TaxMemoryID مبدأ (ثبت روی خود صورتحساب).</summary>
    public string? TaxMemoryID { get; set; }
    public string? ReferenceId { get; set; }
    public string? Subject { get; set; }
    public bool ShowButtons { get; set; }
    public string? NewTaxNumber { get; set; }
    // فیلد کمکی رسیدگی‌نمایمأ UI (در مبدأ نبود ولی ConnectService از آن استفاده می‌کند)
    public string? Name { get; set; }
    public decimal? PriceNaghd { get; set; }
    public decimal? PriceNesye { get; set; }
    public FiscalYear? fiscalYear { get; set; }
    public List<InvoiceItem> invoiceItem { get; set; } = new();
    public List<InvoicePayment> invoicePayment { get; set; } = new();
    public bool IsDelete { get; set; }
}

/// <summary>ردیف کالای صورتحساب مودیان.</summary>
public class InvoiceItem
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public int GoodsOrServicesId { get; set; }
    public string? UniqueIdentifier { get; set; }
    public decimal Count { get; set; }
    public decimal PriceUnit { get; set; }
    /// <summary>مبلغ نقدی ردیف — معادل مبدأ.</summary>
    public decimal? PriceNaghd { get; set; }
    /// <summary>مبلغ نسیهٔ ردیف — معادل مبدأ.</summary>
    public decimal? PriceNesye { get; set; }
    public int ValueAddedPercentage { get; set; }
    public bool IsDelete { get; set; }
    public decimal Discount { get; set; }
    public string? GoodsOrServicesDescripton { get; set; }
}

/// <summary>پرداخت‌های صورتحساب (روش/ترمینال/مرجع) — جدول مبدأ که در انتقال جا افتاده بود.</summary>
public class InvoicePayment
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public string? TaxNumber { get; set; }
    public int Amount { get; set; }
    public string? PaymentDate { get; set; }
    public string? PaymentMethod { get; set; }
    public string? TerminalNumber { get; set; }
    public string? ReferenceNumber { get; set; }
    public bool IsDelete { get; set; }
    public Invoice? invoice { get; set; }
}

/// <summary>اتصال به سامانه مودیان (کلید خصوصی + گواهی + آدرس + شناسه یکتا).</summary>
public class ConnectInfo
{
    public int Id { get; set; }
    public int CpInfo { get; set; }
    public string? TaxMemoryID { get; set; }
    public string? PrivateKeyPath { get; set; }
    /// <summary>مسیر گواهی (CSR) — معادل CertificatePath مبدأ.</summary>
    public string? CertificatePath { get; set; }
    public string? WebServiceAddress { get; set; }
    public bool IsDelete { get; set; }
}
