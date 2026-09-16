namespace Inventory.Shared.Dtos;

// =====================================================================
//  گزارش‌ساز شخصی — مدل‌های مشترک کلاینت/سرور
//
//  ایده: سرور مجموعه‌ای «دیتاست» از پیش join‌شده ثبت می‌کند (مثلاً
//  «فروش» = قلم فاکتور + فاکتور + کالا + مشتری + انبار). کاربر روی
//  هر دیتاست ستون، فیلتر، گروه‌بندی، سنجه (تجمیع) و نوع خروجی را
//  آزادانه انتخاب می‌کند و نتیجه را به‌عنوان ویجت در داشبورد خود
//  ذخیره می‌کند.
//
//  نکتهٔ امنیتی: هر دیتاست یک «ماژول» مجوز دارد؛ هم فهرست دیتاست‌ها
//  و هم اجرای گزارش در سرور با مجوز واقعی کاربر کنترل می‌شود، پس
//  ساختن گزارش با دستکاری کلاینت باعث نشت داده نمی‌شود.
// =====================================================================

/// <summary>نقش ستون: بعد (گروه‌بندی/فیلتر) یا سنجه (تجمیع).</summary>
public enum ReportColKind { Dimension = 0, Measure = 1 }

/// <summary>نوع دادهٔ ستون — تعیین‌کنندهٔ عملگرهای مجاز و قالب نمایش.</summary>
public enum ReportDataType { Text = 0, Number = 1, Date = 2, Bool = 3, Enum = 4 }

/// <summary>روش تجمیع یک سنجه.</summary>
public enum ReportAgg
{
    Sum = 0,
    Count = 1,
    Avg = 2,
    Min = 3,
    Max = 4,
    CountDistinct = 5
}

/// <summary>عملگر فیلتر.</summary>
public enum ReportOp
{
    Eq = 0,
    Ne = 1,
    Contains = 2,
    StartsWith = 3,
    Gt = 4,
    Gte = 5,
    Lt = 6,
    Lte = 7,
    Between = 8,
    In = 9,
    IsNull = 10,
    NotNull = 11,
    True = 12,
    False = 13
}

/// <summary>بازهٔ زمانی آماده (بر اساس تقویم شمسی) برای فیلتر تاریخ.</summary>
public enum ReportDatePreset
{
    None = 0,
    Today = 1,
    Yesterday = 2,
    ThisWeek = 3,
    LastWeek = 4,
    ThisMonth = 5,
    LastMonth = 6,
    ThisYear = 7,
    LastYear = 8,
    Last7Days = 9,
    Last30Days = 10,
    Last90Days = 11,
    Last12Months = 12
}

/// <summary>شکل خروجی گزارش — همان رندرر ویجت داشبورد استفاده می‌شود.</summary>
public enum ReportOutput { Table = 0, Kpi = 1, Chart = 2 }

/// <summary>دامنهٔ دیدن گزارش.</summary>
public enum ReportVisibility
{
    /// <summary>فقط سازنده</summary>
    Private = 0,
    /// <summary>سازنده + اعضای نقش‌های انتخاب‌شده</summary>
    RoleShared = 1
}

/// <summary>تعریف یک ستون در دیتاست (سرور می‌سازد، کلاینت نمایش می‌دهد).</summary>
public class ReportColumnDefDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public ReportColKind Kind { get; set; }
    public ReportDataType DataType { get; set; }
    /// <summary>واحد نمایش (ریال، عدد، ساعت…)</summary>
    public string? Unit { get; set; }
    /// <summary>تعداد رقم اعشار در نمایش</summary>
    public int Decimals { get; set; }
    /// <summary>به‌صورت پیش‌فرض در گزارش انتخاب شود</summary>
    public bool IsDefault { get; set; }
    /// <summary>توضیح کوتاه برای راهنمای UI</summary>
    public string? Hint { get; set; }
    /// <summary>تجمیع‌های مجاز برای این سنجه</summary>
    public List<ReportAgg> Aggs { get; set; } = new();
    /// <summary>برچسب فارسی مقادیر (برای ستون‌های Enum)</summary>
    public Dictionary<string, string>? Labels { get; set; }
    /// <summary>این ستون تاریخِ مرجعِ بازهٔ زمانی گزارش است</summary>
    public bool IsDateAnchor { get; set; }
}

/// <summary>یک دیتاست گزارش (مجموعهٔ جدول‌های join‌شده).</summary>
public class ReportDatasetDto
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    /// <summary>ماژول مجوز (RBAC) که دیدن این داده به آن وابسته است</summary>
    public string Module { get; set; } = "";
    public string Icon { get; set; } = "bi-table";
    public string Description { get; set; } = "";
    /// <summary>ستون تاریخ پیش‌فرض برای بازهٔ زمانی</summary>
    public string? DateField { get; set; }
    /// <summary>سنجهٔ پیش‌فرض</summary>
    public string? DefaultMeasure { get; set; }
    public List<ReportColumnDefDto> Columns { get; set; } = new();
}

/// <summary>یک شرط فیلتر.</summary>
public class ReportFilterDto
{
    public string Field { get; set; } = "";
    public ReportOp Op { get; set; }
    /// <summary>مقدار (رشته) — برای In چند مقدار با «,» جدا می‌شوند</summary>
    public string? Value { get; set; }
    /// <summary>مقدار دوم برای Between</summary>
    public string? Value2 { get; set; }
    /// <summary>بازهٔ آمادهٔ شمسی (به‌جای Value/Value2) برای ستون تاریخ</summary>
    public ReportDatePreset Preset { get; set; }
}

/// <summary>یک سنجهٔ تجمیعی.</summary>
public class ReportMeasureDto
{
    public string Field { get; set; } = "";
    public ReportAgg Agg { get; set; }
    /// <summary>عنوان دلخواه کاربر</summary>
    public string? Label { get; set; }
}

/// <summary>مرتب‌سازی.</summary>
public class ReportSortDto
{
    /// <summary>کلید ستون یا «m:0» برای سنجهٔ اول</summary>
    public string Field { get; set; } = "";
    public bool Desc { get; set; }
}

/// <summary>تعریف کامل یک گزارش (کوئری) — هم برای اجرا و هم برای ذخیره.</summary>
public class ReportQueryDto
{
    public string DatasetKey { get; set; } = "";
    /// <summary>ستون‌های انتخاب‌شده (در حالت تجمیعی = گروه‌بندی)</summary>
    public List<string> Columns { get; set; } = new();
    /// <summary>آیا نتیجه گروه‌بندی/تجمیع شود؟ اگر false → گزارش ریز (detail)</summary>
    public bool Aggregate { get; set; }
    public List<ReportMeasureDto> Measures { get; set; } = new();
    public List<ReportFilterDto> Filters { get; set; } = new();
    public List<ReportSortDto> Sorts { get; set; } = new();
    public int Take { get; set; } = 50;
    public ReportOutput Output { get; set; } = ReportOutput.Table;
    public DashChartType ChartType { get; set; } = DashChartType.Bar;
    /// <summary>در خروجی KPI: مقایسه با بازهٔ قبل (نیازمند ستون تاریخ و Preset)</summary>
    public bool ComparePrev { get; set; }
    /// <summary>ستون تاریخ مرجع (برای Preset و ComparePrev)</summary>
    public string? DateField { get; set; }
    /// <summary>عنوان ویجت در داشبورد</summary>
    public string? Title { get; set; }
}

/// <summary>گزارش ذخیره‌شدهٔ کاربر.</summary>
public class UserReportDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string DatasetKey { get; set; } = "";
    public string DatasetTitle { get; set; } = "";
    public string Module { get; set; } = "";
    public ReportVisibility Visibility { get; set; }
    public List<int> SharedRoleIds { get; set; } = new();
    public List<string> SharedRoleNames { get; set; } = new();
    public bool IsMine { get; set; }
    public string OwnerName { get; set; } = "";
    public ReportQueryDto Query { get; set; } = new();
    public DateTime UpdatedAt { get; set; }
    /// <summary>کلید ویجت متناظر در داشبورد: rep:{Id}</summary>
    public string WidgetKey => $"rep:{Id}";
}

/// <summary>نتیجهٔ اجرای گزارش — همان ساختار دادهٔ ویجت داشبورد.</summary>
public class ReportRunResultDto
{
    public WidgetDataDto Data { get; set; } = new();
    /// <summary>تعداد ردیف‌هایی که از دیتابیس خوانده شد</summary>
    public int ScannedRows { get; set; }
    /// <summary>آیا به سقف ردیف رسیده‌ایم (نتیجه ممکن است ناقص باشد)</summary>
    public bool Truncated { get; set; }
    /// <summary>تعداد ردیف/گروه خروجی</summary>
    public int ResultRows { get; set; }
    /// <summary>زمان اجرا به میلی‌ثانیه</summary>
    public long ElapsedMs { get; set; }
}

/// <summary>بدنهٔ درخواست ذخیره/ویرایش گزارش.</summary>
public class ReportSaveDto
{
    public string Name { get; set; } = "";
    public string DatasetKey { get; set; } = "";
    public ReportVisibility Visibility { get; set; } = ReportVisibility.Private;
    public List<int> RoleIds { get; set; } = new();
    public ReportQueryDto Query { get; set; } = new();
}

/// <summary>یک نقش فعال برای اشتراک‌گذاری گزارش.</summary>
public class ReportRoleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
