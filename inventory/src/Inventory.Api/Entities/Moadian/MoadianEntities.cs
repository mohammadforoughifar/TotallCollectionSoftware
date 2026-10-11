namespace Inventory.Api.Data;

// ============================================================
// جداول ماژول مودیان (نسخه 0.0.27) — دقیقاً مطابق مصرف در
// سرویس ConnectService؛ نام‌ها و ستون‌ها بدون تغییر از پروژهٔ مبدأ.
// ============================================================

/// <summary>شرکت/خدمات‌دهندهٔ متصل به سامانه مودیان (cpinfo).</summary>
public class CpInfo
{
    public int Id { get; set; }
    public string? PersianName { get; set; }
    public string? Nationalid { get; set; }
    public string? EconomicNumber { get; set; }
}

/// <summary>مشتری صورتحساب مودیان (حقیقی/حقوقی).</summary>
public class Customer
{
    public int Id { get; set; }
    public int? CpInfoId { get; set; }
    public string? Name { get; set; }
    public string? PostalCode { get; set; }
    public string? Phone { get; set; }
    public string? NationalID { get; set; }
    public string? Type { get; set; }
    public string? EconomicNumber { get; set; }
    public string? Address { get; set; }
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
}

/// <summary>واحد سنجش معتبر سامانه مودیان (نام + کد).</summary>
public class UnitOfMeasurement
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Code { get; set; }
}

/// <summary>سال مالی صورتحساب مودیان.</summary>
public class FiscalYear
{
    public int Id { get; set; }
    public int? CpInfo { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}

/// <summary>صورتحساب مودیان (اصلی/اصلاحی/ابطالی/برگشت).</summary>
public class Invoice
{
    public int Id { get; set; }
    public int? CustomerId { get; set; }
    public int CpInfo { get; set; }
    public int? InvoiceNumber { get; set; }
    public int? FiscalYearId { get; set; }
    public FiscalYear? fiscalYear { get; set; }
    public DateOnly? DateOrder { get; set; }
    public int TypePattern { get; set; }
    public string? PaymentType { get; set; }
    public decimal? PriceNaghd { get; set; }
    public decimal? PriceNesye { get; set; }
    public string? Description { get; set; }
    public string? StatusSentToSystem { get; set; }
    public bool ShowButtons { get; set; }
    public string? TaxNumber { get; set; }
    public string? NewTaxNumber { get; set; }
    public string? ReferenceId { get; set; }
    public string? Subject { get; set; }
    public string? Name { get; set; }
}

/// <summary>ردیف کالای صورتحساب مودیان.</summary>
public class InvoiceItem
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public int GoodsOrServicesId { get; set; }
    public string? UniqueIdentifier { get; set; }
    public decimal Count { get; set; }
    public int PriceUnit { get; set; }
    public int ValueAddedPercentage { get; set; }
    public decimal Discount { get; set; }
    public string? GoodsOrServicesDescripton { get; set; }
}

/// <summary>اتصال به سامانه مودیان (کلید خصوصی + آدرس + شناسه یکتا).</summary>
public class ConnectInfo
{
    public int Id { get; set; }
    public int CpInfo { get; set; }
    public string? TaxMemoryID { get; set; }
    public string? PrivateKeyPath { get; set; }
    public string? WebServiceAddress { get; set; }
    public bool IsDelete { get; set; }
}
