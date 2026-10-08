namespace Inventory.Shared.Dtos;

// =====================================================================
// سال مالی مودیان + فرم مستقل «ثبت صورتحساب جدید» (بدون وابستگی به ERP)
// =====================================================================

/// <summary>سال مالی مودیان (شمسی) همراه با شمارش دوره‌ها و فاکتورها.</summary>
public class MoadianFiscalYearDto
{
    public int Id { get; set; }
    /// <summary>ServiceProvider (0 = legacy/global).</summary>
    public int ServiceProviderId { get; set; }
    /// <summary>سال شمسی، مثل 1405.</summary>
    public int Year { get; set; }
    /// <summary>تاریخ شروع (میلادی؛ نمایش شمسی در UI).</summary>
    public DateTime StartDate { get; set; }
    /// <summary>تاریخ پایان.</summary>
    public DateTime EndDate { get; set; }
    public bool IsClosed { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }

    public int PeriodCount { get; set; }
    public int ClosedPeriodCount { get; set; }
    public int InvoiceCount { get; set; }
    public decimal NetTotal { get; set; }
    public decimal VatTotal { get; set; }
    /// <summary>آخرین شمارهٔ سند صادرشده در این سال (برای پیش‌نمایش شمارهٔ بعدی).</summary>
    public int LastYearSerial { get; set; }
    public DateTime? LastInvoiceDate { get; set; }

    public string Title => $"{Year}";
    public string PeriodTitle => $"{Year}/01 — {Year}/12";
    public string StatusTitle => IsClosed ? "بسته" : "باز";
}

/// <summary>درخواست تعریف سال مالی جدید.</summary>
public class MoadianFiscalYearRequest
{
    public int Id { get; set; }
    /// <summary>ServiceProvider (0 = legacy/global).</summary>
    public int ServiceProviderId { get; set; }
    /// <summary>سال شمسی (۱۳۰۰ تا ۱۵۰۰).</summary>
    public int Year { get; set; }
    public bool IsClosed { get; set; }
    public string? Notes { get; set; }
    /// <summary>ساخت خودکار ۱۲ دورهٔ ماهانهٔ شمسی (پیش‌فرض: بله).</summary>
    public bool CreateMonthlyPeriods { get; set; } = true;
}

/// <summary>یک دورهٔ ماهانهٔ شمسی از سال مالی.</summary>
public class MoadianFiscalPeriodDto
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthName { get; set; } = "";
    public bool IsClosed { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? Notes { get; set; }

    public int InvoiceCount { get; set; }
    public decimal NetTotal { get; set; }
    public decimal VatTotal { get; set; }

    public string Title => $"{Year}/{Month:00}";
    public string RangeTitle => $"{PersianDate.ToShort(StartDate)} — {PersianDate.ToShort(EndDate)}";
}

/// <summary>سال مالی همراه با دوره‌های ماهانه‌اش.</summary>
public class MoadianFiscalYearDetailDto
{
    public MoadianFiscalYearDto Year { get; set; } = new();
    public List<MoadianFiscalPeriodDto> Periods { get; set; } = new();
}

/// <summary>پیش‌نمایش شمارهٔ صورتحساب بعدی برای یک تاریخ/سال مالی.</summary>
public class MoadianNextNumberDto
{
    /// <summary>سریال سراسری داخلی (همان Number فعلی).</summary>
    public int NextGlobalNumber { get; set; }
    public int? FiscalYearId { get; set; }
    public int? FiscalYear { get; set; }
    /// <summary>سریال داخل سال مالی.</summary>
    public int NextYearSerial { get; set; }
    /// <summary>شمارهٔ سند سالانه، مثل 1405/000123.</summary>
    public string DocumentNumber { get; set; } = "";
    public int? FiscalPeriodId { get; set; }
    public string? FiscalPeriodTitle { get; set; }
    /// <summary>پیام هشدار/خطا در صورت نبود سال مالی یا بسته بودن دوره.</summary>
    public string? Warning { get; set; }
    public bool CanCreate { get; set; } = true;
}

/// <summary>یک قلم درخواست ثبت مستقل صورتحساب.</summary>
public class MoadianStandaloneLineRequest
{
    /// <summary>کالا/خدمت انتخاب‌شده از اطلاعات پایه (اختیاری — برای تکمیل خودکار).</summary>
    public int? GoodsOrServiceId { get; set; }
    public string SstId { get; set; } = "";
    public string SstTitle { get; set; } = "";
    /// <summary>کد/نام واحد کالا و خدمت (mu).</summary>
    public string? UnitCode { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    /// <summary>درصد مالیات بر ارزش افزوده (vra).</summary>
    public decimal VatRate { get; set; }
}

/// <summary>درخواست ثبت صورتحساب جدید از فرم مستقل (بدون ERP).</summary>
public class MoadianStandaloneInvoiceRequest
{
    /// <summary>سال مالی انتخاب‌شده؛ اگر 0 باشد از تاریخ فاکتور استخراج می‌شود.</summary>
    public int FiscalYearId { get; set; }
    /// <summary>تاریخ فاکتور (میلادی؛ ورودی شمسی از UI).</summary>
    public DateTime Date { get; set; }
    /// <summary>موضوع صورتحساب (ins).</summary>
    public MoadianInvoiceSubject Subject { get; set; } = MoadianInvoiceSubject.Original;
    /// <summary>نوع صورتحساب (inty) — پیش‌فرض نوع ۱ برای فروش.</summary>
    public MoadianTaxInvoiceType InvoiceType { get; set; } = MoadianTaxInvoiceType.Type1;
    /// <summary>الگوی صورتحساب (inp) — پیش‌فرض الگوی فروش.</summary>
    public MoadianInvoicePattern InvoicePattern { get; set; } = MoadianInvoicePattern.Sale;
    /// <summary>شناسهٔ مالیاتی صورتحساب مرجع برای موضوع‌های ارجاعی.</summary>
    public string? ReferenceTaxId { get; set; }

    /// <summary>مشتری انتخاب‌شده از اطلاعات پایه (اختیاری).</summary>
    public int? CustomerId { get; set; }
    public string? BuyerTaxId { get; set; }
    public string? BuyerName { get; set; }
    public string? BuyerAddress { get; set; }
    public string? BuyerPostalCode { get; set; }
    public string? BuyerPhone { get; set; }

    /// <summary>نوع پرداخت صورتحساب (setm).</summary>
    public MoadianPayType PayType { get; set; } = MoadianPayType.Cash;
    public string? Description { get; set; }

    public List<MoadianStandaloneLineRequest> Lines { get; set; } = new();
}
