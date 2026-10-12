using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Shared.Dtos.Moadian;

// ============================================================
// مدل‌های ماژول مودیان (نسخه 0.0.27) — منتقل‌شده بدون تغییر
// از پروژهٔ مبدأ (CoreApp) به‌جز namespace.
// ============================================================

/// <summary>مدل پاسخ استاندارد سرویس‌های مودیان.</summary>
public class ApiModel
{
    public object? Data { get; set; }
    public int Status { get; set; }
    public string? Message { get; set; }
    public bool Success { get; set; }
    public int SematId { get; set; }
}

/// <summary>پیام‌های فارسی کدهای وضعیت (مطابق مصرف در ConnectService).</summary>
public static class MessageCode
{
    public static readonly Dictionary<int, string> StatusCode = new()
    {
        [200] = "عملیات با موفقیت انجام شد",
        [201] = "با موفقیت ثبت شد",
        [400] = "درخواست نامعتبر است",
        [404] = "مورد درخواستی یافت نشد",
        [500] = "خطای سرور"
    };
}

/// <summary>لینک‌های HATEOAS پاسخ.</summary>
public class Links
{
    public string? Href { get; set; }
    public string? Rel { get; set; }
    public string? Method { get; set; }
}

/// <summary>پارامترهای صفحه‌بندی/جستجو/فیلتر (مطابق فراخوانی‌های کنترلر).</summary>
public class GetResultQuery
{
    public GetResultQuery(
        int Page = 1,
        int PageSize = 10,
        string? SearchTerm = null,
        string? FilterColumn = null,
        string? FilterValue = null,
        string? SortColumn = null,
        string? SortOrder = null,
        DateTime? firstDate = null,
        DateTime? endDate = null,
        object? extra = null,
        Guid? extraId = null)
    {
        Page = Page;
        PageSize = PageSize;
        SearchTerm = SearchTerm;
        FilterColumn = FilterColumn;
        FilterValue = FilterValue;
        SortColumn = SortColumn;
        SortOrder = SortOrder;
        FirstDate = firstDate;
        EndDate = endDate;
        Extra = extra;
        ExtraId = extraId ?? Guid.Empty;
    }

    public int Page { get; set; }
    public int PageSize { get; set; }
    public string? SearchTerm { get; set; }
    public string? FilterColumn { get; set; }
    public string? FilterValue { get; set; }
    public string? SortColumn { get; set; }
    public string? SortOrder { get; set; }
    public DateTime? FirstDate { get; set; }
    public DateTime? EndDate { get; set; }
    public object? Extra { get; set; }
    public Guid ExtraId { get; set; }
}

/// <summary>صفحه‌بندی عمومی لیست‌ها.</summary>
public class PagedList<T>
{
    public PagedList() { }
    public PagedList(List<T> items, int count, int page, int pageSize)
    {
        Items = items;
        TotalCount = count;
        Page = page;
        PageSize = pageSize;
        TotalPages = pageSize > 0 ? (int)Math.Ceiling(count / (double)pageSize) : 0;
    }

    public IEnumerable<T> Items { get; set; } = Enumerable.Empty<T>();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }

    /// <summary>صفحه‌بندی سرورساید (معادل PagedList نمونهٔ پروژهٔ مبدأ).</summary>
    public static async Task<PagedList<T>> CreateAsync(IQueryable<T> source, int pageNumber, int pageSize)
    {
        var totalCount = await source.CountAsync();
        var items = await source.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();
        return new PagedList<T>(items, totalCount, pageNumber, pageSize);
    }
}

/// <summary>جزئیات فایل آپلودشده (کلید خصوصی).</summary>
public class FileDetail
{
    public string? FileName { get; set; }
    public string? DisplayName { get; set; }
    public byte[]? FileContent { get; set; }
}

/// <summary>فرم اتصال به سامانه مودیان.</summary>
public class VMConnection
{
    public string WebServiceAddress { get; set; } = "";
    public string TaxMemoryID { get; set; } = "";
    public int cpinfoid { get; set; }
    public FileDetail? FilePrivatekey { get; set; }
}

/// <summary>نمایش cpinfo بر اساس شرکت.</summary>
public class VMShowCpinfoByCompany
{
    public int cpinfoid { get; set; }
    public string? PersianName { get; set; }
    public string? Nationalid { get; set; }
    public string? EconomicNumber { get; set; }
    /// <summary>نوع شرکت: «حقیقی» یا «حقوقی».</summary>
    public string? Type { get; set; }
}

/// <summary>سال مالی صورتحساب (رابط کاربری مودیان).</summary>
public class VMFiscalYear
{
    public int Id { get; set; }
    public int CpInfoId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}

/// <summary>فرم ثبت/ویرایش کالا یا خدمت.</summary>
public class VMGoodsOrService
{
    public int GoodsOrServicesId { get; set; }
    public int CpInfoId { get; set; }
    public string? Name { get; set; }
    public string? UniqueIdentifier { get; set; }
    public int UnitOfMeasurementId { get; set; }
    public string? UnitOfMeasurement { get; set; }
    public double? ValueAddedPercentage { get; set; }
    public decimal Price { get; set; }
    public IEnumerable<Links>? Links { get; set; }
}

/// <summary>ردیف کالا/خدمت برای combo/لیست.</summary>
public class VMGoodsOrServicesList
{
    public string? Name { get; set; }
    public int GoodsOrServicesId { get; set; }
    public decimal Price { get; set; }
    public string? UniqueIdentifier { get; set; }
    public byte ValueAddedPercentage { get; set; }
}

/// <summary>ردیف مشتری برای combo/لیست.</summary>
public class VMCustomerList
{
    public string? Name { get; set; }
    public int CustomerId { get; set; }
    public string? PostalCode { get; set; }
    public string? Phone { get; set; }
    public string? NationalID { get; set; }
    public int CpInfoId { get; set; }
    public string? Type { get; set; }
    public string? EconomicNumber { get; set; }
    public string? Address { get; set; }
}

/// <summary>فرم ثبت/ویرایش واحد سنجش.</summary>
public class VMUnitOfMeasurement
{
    public int UnitOfMeasurementId { get; set; }
    public string? Name { get; set; }
    public string? Code { get; set; }
    public IEnumerable<Links>? Links { get; set; }
}

/// <summary>کالاهای صورتحساب + بستر ردیف‌ها (کلاس جاساز VmInvoiceItems).</summary>
public class VmInvoiceItems
{
    public class InvoiceResponse
    {
        public PagedList<InvoiceVM> Invoices { get; set; } = null!;
        public InvoiceVM? Labels { get; set; }
    }

    public class InvoiceVM
    {
        public int Id { get; set; } = 0;
        public int? CustomerId { get; set; }
        public int? NumberInvoice { get; set; }
        public string? FiscalYearId { get; set; }
        public DateTime? DatestartFiscalYear { get; set; }
        public DateTime? DateEndFiscalYear { get; set; }
        public int? CPInfoId { get; set; }
        public string? CustomerName { get; set; }
        public string? TypePattern { get; set; } = "نوع اول";
        public DateOnly? DateOrder { get; set; }
        public DateTime? DateOrderPicker
        {
            get => DateOrder.HasValue ? DateOrder.Value.ToDateTime(TimeOnly.MinValue) : null;
            set => DateOrder = value.HasValue ? DateOnly.FromDateTime(value.Value) : null;
        }
        public string? Name { get; set; }
        public string? PaymentType { get; set; } = "نقدی";
        public string? Description { get; set; }
        public string? StatusSentToSystem { get; set; }
        public string? StatusSentToSystemInt { get; set; }
        public string? TaxNumber { get; set; }
        public string? ReferenceId { get; set; }
        public double? TotallValueAddedPercentage { get; set; }
        public decimal? TotallMoney { get; set; }
        public string? Subject { get; set; }
        public bool ShowButtons { get; set; } = true;
        public bool IsNew { get; set; } = false;
        public int? GoodsOrServicesId { get; set; }
        public decimal Discount { get; set; } = 0;
        public string? DiscountText
        {
            get => Discount.ToString("N0");
            set
            {
                var raw = value?.Replace(",", "") ?? "0";
                if (decimal.TryParse(raw, out var discount))
                {
                    Discount = discount;
                }
            }
        }
        public decimal? Count { get; set; } = 1;
        public string? NewTaxNumber { get; set; }
        public string? TaxMemoryID { get; set; }
        public string? Serial { get; set; }
        public decimal Naghd { get; set; }
        public decimal? PriceNaghd { get; set; } = 0;
        public string? NaghdText
        {
            get => PriceNaghd?.ToString("N0");
            set
            {
                var raw = value?.Replace(",", "") ?? "0";
                if (decimal.TryParse(raw, out var naghd))
                {
                    PriceNaghd = naghd;
                }
            }
        }
        public decimal? PriceNesye { get; set; } = 0;
        public string? NesyeText
        {
            get => PriceNesye?.ToString("N0");
            set
            {
                var raw = value?.Replace(",", "") ?? "0";
                if (decimal.TryParse(raw, out var nesye))
                {
                    PriceNesye = nesye;
                }
            }
        }
        public ObservableCollection<VmInvoiceItems> InvoiceItems { get; set; } = new();
        public int lblAll { get; set; } = 0;
        public int lblSentToSystem { get; set; } = 0;
        public int lblInquiry { get; set; } = 0;
        public int lblLackOfResponsiveness { get; set; } = 0;
        public int lblError { get; set; } = 0;
        public int lblNotSentToSystem { get; set; } = 0;
    }

    public Guid? RowId { get; set; } = Guid.NewGuid();
    public bool IsNew { get; set; } = false;
    public bool IsNewRow { get; set; } = false;
    public string? DescriptionKala { get; set; }
    public int? InvoiceId { get; set; }
    public int? GoodsOrServicesId { get; set; } = 0;
    public decimal? CalculatedPay { get; set; }
    public string? Name { get; set; }
    public string? UniqueIdentifier { get; set; }
    public string? UnitOfMeasurement { get; set; }
    public decimal? Count { get; set; }
    public decimal? PriceUnit { get; set; }
    public decimal TotalPrice { get; set; }
    public decimal BeforeDisPrice { get; set; }
    public double? ValueAddedPercentage { get; set; }
    public decimal? ValueAddedAmount { get; set; }
    public decimal? Discount { get; set; }
    public decimal? DiscountValue { get; set; }
    public bool IsDiscountPercentage { get; set; } = false;
    public string? DiscountText
    {
        get => Discount?.ToString("N0");
        set
        {
            var raw = value?.Replace(",", "") ?? "0";
            if (decimal.TryParse(raw, out var discount))
            {
                Discount = discount;
            }
        }
    }
    public decimal? PriceNesye { get; set; }
    public string? NesyeText
    {
        get => PriceNesye?.ToString("N0");
        set
        {
            var raw = value?.Replace(",", "") ?? "0";
            if (decimal.TryParse(raw, out var nesye))
            {
                PriceNesye = nesye;
            }
        }
    }
    public decimal? PriceNaghd { get; set; }
    public string? NaghdText
    {
        get => PriceNaghd?.ToString("N0");
        set
        {
            var raw = value?.Replace(",", "") ?? "0";
            if (decimal.TryParse(raw, out var naghd))
            {
                PriceNaghd = naghd;
            }
        }
    }

    public class UCInvoiceInfo
    {
        public int lblAll { get; set; }
        public int lblSentToSystem { get; set; }
        public int lblInquiry { get; set; }
        public int lblLackOfResponsiveness { get; set; }
        public int lblError { get; set; }
        public int lblNotSentToSystem { get; set; }
        public IEnumerable<Links>? Links { get; set; }
        public List<InvoiceVM> inviceInfos { get; set; } = new();
    }

    public class InviceInfo
    {
        public string? TaxNumber { get; set; }
        public string? NewTaxNumber { get; set; }
        public string? Name { get; set; }
        public int Id { get; set; }
        public DateTime DateOrder { get; set; }
        public string? StatusSentToSystem { get; set; }
        public string? Subject { get; set; }
        public bool ShowButtons { get; set; }
    }

    public class ErrorItem
    {
        public string? code { get; set; }
        public string? message { get; set; }
    }

    public class ErroreMesage
    {
        public string? code { get; set; }
        public string? message { get; set; }
        public string? errorsAndWarnings { get; set; }
    }

    public class InquiryErrorResponse
    {
        public List<ErrorItem>? error { get; set; }
        public List<object>? warning { get; set; }
        public bool success { get; set; }
    }
}
