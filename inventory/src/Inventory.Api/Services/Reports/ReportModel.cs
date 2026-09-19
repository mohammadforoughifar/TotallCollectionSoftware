using System.Linq.Expressions;
using Inventory.Shared.Dtos;

namespace Inventory.Api.Services.Reports;

// =====================================================================
//  گزارش‌ساز — مدل ستون و دیتاست
//
//  هر دیتاست یک «ردیف تخت» (flat row) از چند جدول join‌شده است.
//  ستون‌ها با expression به فیلدهای همان ردیف وصل می‌شوند تا EF بتواند
//  فقط ستون‌های لازم را از دیتابیس بخواند (projection) و فیلترهای
//  قابل ترجمه را به SQL بفرستد.
//
//  نکتهٔ SQLite: عملیات ریاضی/مقایسه روی decimal در SQLite پشتیبانی
//  نمی‌شود، پس ستون‌های decimal فقط در حافظه فیلتر/تجمیع می‌شوند.
// =====================================================================

/// <summary>برچسب فارسی مقدار‌های enum برای ستون‌های گزارش.</summary>
public static class EnumFa
{
    public static readonly Dictionary<int, string> InvoiceKind = new()
    { [0] = "خرید", [1] = "فروش", [2] = "برگشت از خرید", [3] = "برگشت از فروش" };

    public static readonly Dictionary<int, string> DocStatus = new()
    { [0] = "پیش‌نویس", [1] = "قطعی", [2] = "باطل‌شده" };

    /// <summary>نوع نامه بر اساس LetterSource.SourceType</summary>
    public static readonly Dictionary<int, string> LetterSourceType = new()
    { [1] = "داخلی", [2] = "صادره", [3] = "وارده" };

    /// <summary>وضعیت تایید ارجاع (Erja.TypeTaeed)</summary>
    public static readonly Dictionary<int, string> ErjaTaeed = new()
    { [0] = "بدون اقدام", [1] = "تایید", [2] = "رد" };

    public static readonly Dictionary<int, string> Settlement = new()
    { [0] = "نسیه", [1] = "نقدی" };

    public static readonly Dictionary<int, string> ChequeKind = new()
    { [0] = "دریافتی", [1] = "صدور" };

    public static readonly Dictionary<int, string> ChequeStatus = new()
    {
        [0] = "نزد ما", [1] = "در جریان وصول", [2] = "وصول‌شده",
        [3] = "برگشتی", [4] = "ظهرنویسی‌شده", [5] = "باطل‌شده"
    };

    public static readonly Dictionary<int, string> TreasuryKind = new()
    { [0] = "دریافت", [1] = "پرداخت", [2] = "انتقال" };

    public static readonly Dictionary<int, string> PayMethod = new()
    { [0] = "نقد", [1] = "کارت", [2] = "انتقال", [3] = "چک", [4] = "تخفیف" };

    public static readonly Dictionary<int, string> CashType = new()
    { [0] = "نقد", [1] = "کارت‌خوان", [2] = "کارت به کارت" };

    public static readonly Dictionary<int, string> PartyType = new()
    { [0] = "مشتری", [1] = "تامین‌کننده" };

    public static readonly Dictionary<int, string> WarehouseKind = new()
    {
        [0] = "اصلی", [1] = "فرعی", [2] = "امانی",
        [3] = "ضایعات", [4] = "تولید", [5] = "بین‌راهی"
    };

    public static readonly Dictionary<int, string> StockNature = new()
    { [0] = "افزایشی", [1] = "کاهشی", [2] = "خنثی" };

    public static readonly Dictionary<int, string> AccountType = new()
    { [0] = "دارایی", [1] = "بدهی", [2] = "حقوق صاحبان سهام", [3] = "درآمد", [4] = "هزینه" };

    public static readonly Dictionary<int, string> AccountNature = new()
    { [0] = "بدهکار", [1] = "بستانکار", [2] = "دوطرفه" };

    public static readonly Dictionary<int, string> RepairStatus = new()
    { [0] = "تحویل گرفته", [1] = "در حال تعمیر", [2] = "آمادهٔ تحویل", [3] = "تحویل شده", [4] = "لغوشده" };

    public static readonly Dictionary<int, string> AssetStatus = new()
    { [0] = "در حال استفاده", [1] = "راکد", [2] = "فروخته‌شده", [3] = "اسقاطی" };

    public static readonly Dictionary<int, string> Depreciation = new()
    { [0] = "خط مستقیم", [1] = "نزولی" };

    public static readonly Dictionary<int, string> EmployeeStatus = new()
    { [0] = "فعال", [1] = "در مرخصی", [2] = "معلق", [3] = "قطع همکاری", [4] = "بازنشسته" };

    public static readonly Dictionary<int, string> EmploymentType = new()
    {
        [0] = "رسمی", [1] = "قراردادی", [2] = "پیمانی", [3] = "ساعتی", [4] = "مشاوره‌ای",
        [5] = "تمام‌وقت", [6] = "پاره‌وقت", [7] = "پروژه‌ای", [8] = "آزمایشی"
    };

    public static readonly Dictionary<int, string> AttDayStatus = new()
    {
        [0] = "حاضر", [1] = "تأخیر", [2] = "تعجیل", [3] = "تأخیر و تعجیل", [4] = "غایب",
        [5] = "مأموریت", [6] = "مرخصی", [7] = "تعطیل", [8] = "روز استراحت",
        [9] = "تعطیل‌کار", [10] = "تردد ناقص"
    };

    public static readonly Dictionary<int, string> AttLogType = new()
    { [0] = "ورود", [1] = "خروج", [2] = "نامشخص" };

    public static readonly Dictionary<int, string> YesNo = new()
    { [0] = "خیر", [1] = "بله" };

    // ---------- وضعیت‌های متنی (در دیتابیس به‌صورت رشته ذخیره می‌شوند) ----------
    public static readonly Dictionary<string, string> WorkOrderStatus = new()
    { ["Open"] = "باز", ["Closed"] = "بسته", ["Cancelled"] = "لغوشده", ["InProgress"] = "در حال انجام" };

    public static readonly Dictionary<string, string> ItRequestStatus = new()
    {
        ["New"] = "جدید", ["Assigned"] = "ارجاع‌شده", ["ManagerApproved"] = "تأیید مدیر",
        ["Completed"] = "تکمیل‌شده", ["Rejected"] = "ردشده", ["Cancelled"] = "لغوشده"
    };

    public static readonly Dictionary<string, string> ItRequestType = new()
    { ["Hardware"] = "سخت‌افزار", ["Software"] = "نرم‌افزار", ["Network"] = "شبکه", ["Telecom"] = "مخابرات", ["Access"] = "دسترسی" };

    public static readonly Dictionary<string, string> LeaveStatus = new()
    { ["Pending"] = "در انتظار بررسی", ["Approved"] = "تأییدشده", ["Rejected"] = "ردشده", ["Cancelled"] = "لغوشده" };

    public static readonly Dictionary<string, string> LeaveType = new()
    { ["Daily"] = "روزانه", ["Hourly"] = "ساعتی", ["Mission"] = "مأموریت" };
}

/// <summary>نوع خانهٔ RawRow که مقدار ستون در آن قرار می‌گیرد.</summary>
public enum ReportSlot { S, M, I, R, D, B }

/// <summary>
/// یک ستون دیتاست — شامل دسترسی‌کنندهٔ expression به فیلد ردیف تخت.
/// فقط یکی از دسترسی‌کننده‌ها پر می‌شود (متناظر با DataType).
/// </summary>
public sealed class ReportColumn<T>
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public ReportColKind Kind { get; init; } = ReportColKind.Dimension;
    public ReportDataType DataType { get; init; } = ReportDataType.Text;
    public string? Unit { get; init; }
    public int Decimals { get; init; }
    public bool IsDefault { get; init; }
    public string? Hint { get; init; }
    public bool IsDateAnchor { get; init; }
    public Dictionary<int, string>? Labels { get; init; }
    /// <summary>برچسب فارسی برای ستون‌های متنی با مقدار ثابت (مثل وضعیت «Open» → «باز»)</summary>
    public Dictionary<string, string>? TextLabels { get; init; }
    public List<ReportAgg> Aggs { get; init; } = new();

    public Expression<Func<T, string?>>? Text { get; init; }
    public Expression<Func<T, decimal?>>? Dec { get; init; }
    public Expression<Func<T, int?>>? Int { get; init; }
    public Expression<Func<T, double?>>? Real { get; init; }
    public Expression<Func<T, DateTime?>>? Date { get; init; }
    public Expression<Func<T, bool?>>? Bool { get; init; }

    public LambdaExpression? Accessor
    {
        get
        {
            if (Text is not null) return Text;
            if (Dec is not null) return Dec;
            if (Int is not null) return Int;
            if (Real is not null) return Real;
            if (Date is not null) return Date;
            return Bool;
        }
    }

    /// <summary>خانهٔ RawRow متناظر با این ستون.</summary>
    public ReportSlot Slot => DataType switch
    {
        ReportDataType.Text => ReportSlot.S,
        ReportDataType.Date => ReportSlot.D,
        ReportDataType.Bool => ReportSlot.B,
        ReportDataType.Enum => ReportSlot.I,
        _ => Dec is not null ? ReportSlot.M : (Int is not null ? ReportSlot.I : ReportSlot.R)
    };

    /// <summary>
    /// آیا فیلتر این ستون را می‌توان به SQL فرستاد؟
    /// decimal در SQLite قابل مقایسه نیست → فقط در حافظه فیلتر می‌شود.
    /// </summary>
    public bool ServerFilterable => Slot != ReportSlot.M;

    public ReportColumnDefDto ToDto() => new()
    {
        Key = Key,
        Label = Label,
        Kind = Kind,
        DataType = DataType,
        Unit = Unit,
        Decimals = Decimals,
        IsDefault = IsDefault,
        Hint = Hint,
        IsDateAnchor = IsDateAnchor,
        Aggs = Aggs,
        Labels = Labels is not null
            ? Labels.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value)
            : TextLabels
    };
}

/// <summary>کارخانهٔ ساخت ستون‌ها — تا تعریف دیتاست‌ها کوتاه بماند.</summary>
public static class ReportCols
{
    private static Expression<Func<T, TTo>> Conv<T, TTo>(LambdaExpression src) =>
        Expression.Lambda<Func<T, TTo>>(Expression.Convert(src.Body, typeof(TTo)), src.Parameters);

    private static readonly List<ReportAgg> NumAggs = new()
    { ReportAgg.Sum, ReportAgg.Count, ReportAgg.Avg, ReportAgg.Min, ReportAgg.Max, ReportAgg.CountDistinct };

    private static readonly List<ReportAgg> DimAggs = new()
    { ReportAgg.Count, ReportAgg.CountDistinct };

    public static ReportColumn<T> Text<T>(string key, string label, Expression<Func<T, string?>> sel,
        bool def = false, string? hint = null, Dictionary<string, string>? labels = null) =>
        new()
        {
            Key = key, Label = label, DataType = ReportDataType.Text, Text = sel,
            IsDefault = def, Hint = hint, Aggs = DimAggs, TextLabels = labels
        };

    public static ReportColumn<T> Money<T>(string key, string label, Expression<Func<T, decimal>> sel,
        bool def = false, string? hint = null) =>
        new()
        {
            Key = key, Label = label, Kind = ReportColKind.Measure, DataType = ReportDataType.Number,
            Unit = "ریال", Decimals = 0, IsDefault = def, Hint = hint, Aggs = NumAggs,
            Dec = Conv<T, decimal?>(sel)
        };

    public static ReportColumn<T> Qty<T>(string key, string label, Expression<Func<T, decimal>> sel,
        int decimals = 0, string? unit = null, bool def = false, string? hint = null) =>
        new()
        {
            Key = key, Label = label, Kind = ReportColKind.Measure, DataType = ReportDataType.Number,
            Unit = unit, Decimals = decimals, IsDefault = def, Hint = hint, Aggs = NumAggs,
            Dec = Conv<T, decimal?>(sel)
        };

    public static ReportColumn<T> IntMeasure<T>(string key, string label, Expression<Func<T, int>> sel,
        string? unit = null, bool def = false, string? hint = null) =>
        new()
        {
            Key = key, Label = label, Kind = ReportColKind.Measure, DataType = ReportDataType.Number,
            Unit = unit, Decimals = 0, IsDefault = def, Hint = hint, Aggs = NumAggs,
            Int = Conv<T, int?>(sel)
        };

    public static ReportColumn<T> RealMeasure<T>(string key, string label, Expression<Func<T, double>> sel,
        int decimals = 1, string? unit = null, bool def = false, string? hint = null) =>
        new()
        {
            Key = key, Label = label, Kind = ReportColKind.Measure, DataType = ReportDataType.Number,
            Unit = unit, Decimals = decimals, IsDefault = def, Hint = hint, Aggs = NumAggs,
            Real = Conv<T, double?>(sel)
        };

    public static ReportColumn<T> IntDim<T>(string key, string label, Expression<Func<T, int>> sel,
        bool def = false, string? hint = null) =>
        new()
        {
            Key = key, Label = label, DataType = ReportDataType.Number, Decimals = 0,
            IsDefault = def, Hint = hint, Aggs = DimAggs, Int = Conv<T, int?>(sel)
        };

    public static ReportColumn<T> DateDim<T>(string key, string label, Expression<Func<T, DateTime>> sel,
        bool anchor = false, bool def = false, string? hint = null) =>
        new()
        {
            Key = key, Label = label, DataType = ReportDataType.Date, IsDateAnchor = anchor,
            IsDefault = def || anchor, Hint = hint, Aggs = DimAggs, Date = Conv<T, DateTime?>(sel)
        };

    public static ReportColumn<T> DateN<T>(string key, string label, Expression<Func<T, DateTime?>> sel,
        bool anchor = false, bool def = false, string? hint = null) =>
        new()
        {
            Key = key, Label = label, DataType = ReportDataType.Date, IsDateAnchor = anchor,
            IsDefault = def || anchor, Hint = hint, Aggs = DimAggs, Date = sel
        };

    public static ReportColumn<T> BoolDim<T>(string key, string label, Expression<Func<T, bool>> sel,
        bool def = false, string? hint = null) =>
        new()
        {
            Key = key, Label = label, DataType = ReportDataType.Bool, IsDefault = def, Hint = hint,
            Aggs = DimAggs, Bool = Conv<T, bool?>(sel)
        };

    public static ReportColumn<T> EnumDim<T>(string key, string label, Expression<Func<T, int>> sel,
        Dictionary<int, string> labels, bool def = false, string? hint = null) =>
        new()
        {
            Key = key, Label = label, DataType = ReportDataType.Enum, Labels = labels,
            IsDefault = def, Hint = hint, Aggs = DimAggs, Int = Conv<T, int?>(sel)
        };

    public static ReportColumn<T> EnumN<T>(string key, string label, Expression<Func<T, int?>> sel,
        Dictionary<int, string> labels, bool def = false, string? hint = null) =>
        new()
        {
            Key = key, Label = label, DataType = ReportDataType.Enum, Labels = labels,
            IsDefault = def, Hint = hint, Aggs = DimAggs, Int = sel
        };
}

/// <summary>ردیف خام — خانه‌های نوع‌دار برای projection از دیتابیس.</summary>
public sealed class RawRow
{
    public string? S0, S1, S2, S3, S4, S5, S6, S7, S8, S9;
    public decimal? M0, M1, M2, M3, M4, M5, M6, M7, M8, M9;
    public int? I0, I1, I2, I3, I4, I5, I6, I7, I8, I9;
    public double? R0, R1, R2, R3, R4;
    public DateTime? D0, D1, D2, D3, D4;
    public bool? B0, B1, B2, B3;
}

/// <summary>یک دیتاست گزارش — سرور می‌سازد، موتور اجرا می‌کند.</summary>
public interface IReportDataset
{
    string Key { get; }
    string Title { get; }
    string Category { get; }
    /// <summary>ماژول RBAC لازم برای دیدن دادهٔ این دیتاست</summary>
    string Module { get; }
    string Icon { get; }
    string Description { get; }
    string? DateField { get; }
    string? DefaultMeasure { get; }
    /// <summary>سقف ردیفِ قابل خواندن از دیتابیس</summary>
    int MaxScan { get; }
    ReportDatasetDto ToDto();
    Task<ReportRunResultDto> RunAsync(object db, ReportQueryDto q);
}

/// <summary>پیاده‌سازی نوع‌دار دیتاست روی یک ردیف تخت.</summary>
public sealed class ReportDataset<T> : IReportDataset where T : class
{
    private readonly Func<object, IQueryable<T>> _source;
    private readonly Dictionary<string, ReportColumn<T>> _cols;

    public ReportDataset(string key, string title, string category, string module,
        Func<object, IQueryable<T>> source, IEnumerable<ReportColumn<T>> columns,
        string icon = "bi-table", string description = "",
        string? dateField = null, string? defaultMeasure = null, int maxScan = 20000)
    {
        Key = key; Title = title; Category = category; Module = module;
        Icon = icon; Description = description; MaxScan = maxScan;
        _source = source;
        _cols = columns.ToDictionary(c => c.Key);
        DateField = dateField ?? _cols.Values.FirstOrDefault(c => c.IsDateAnchor)?.Key;
        DefaultMeasure = defaultMeasure ?? _cols.Values.FirstOrDefault(c => c.Kind == ReportColKind.Measure)?.Key;
    }

    public string Key { get; }
    public string Title { get; }
    public string Category { get; }
    public string Module { get; }
    public string Icon { get; }
    public string Description { get; }
    public string? DateField { get; }
    public string? DefaultMeasure { get; }
    public int MaxScan { get; }

    public ReportDatasetDto ToDto() => new()
    {
        Key = Key,
        Title = Title,
        Category = Category,
        Module = Module,
        Icon = Icon,
        Description = Description,
        DateField = DateField,
        DefaultMeasure = DefaultMeasure,
        Columns = _cols.Values.Select(c => c.ToDto()).ToList()
    };

    public Task<ReportRunResultDto> RunAsync(object db, ReportQueryDto q) =>
        ReportExecutor.ExecuteAsync(_source(db), _cols, this, q);
}

/// <summary>
/// سازندهٔ روانِ مجموعهٔ ستون‌ها — تا تعریف دیتاست‌ها کوتاه و خوانا بماند
/// (استنتاج نوع T فقط یک بار هنگام ساخت لازم است).
/// </summary>
public sealed class ColumnSet<T> : System.Collections.Generic.IEnumerable<ReportColumn<T>> where T : class
{
    private readonly List<ReportColumn<T>> _list = new();

    public System.Collections.Generic.IEnumerator<ReportColumn<T>> GetEnumerator() => _list.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _list.GetEnumerator();

    public ColumnSet<T> Text(string key, string label, Expression<Func<T, string?>> sel,
        bool def = false, string? hint = null, Dictionary<string, string>? labels = null)
    { _list.Add(ReportCols.Text(key, label, sel, def, hint, labels)); return this; }

    public ColumnSet<T> Int(string key, string label, Expression<Func<T, int>> sel,
        bool def = false, string? hint = null)
    { _list.Add(ReportCols.IntDim(key, label, sel, def, hint)); return this; }

    public ColumnSet<T> Money(string key, string label, Expression<Func<T, decimal>> sel,
        bool def = false, string? hint = null)
    { _list.Add(ReportCols.Money(key, label, sel, def, hint)); return this; }

    public ColumnSet<T> Qty(string key, string label, Expression<Func<T, decimal>> sel,
        int decimals = 0, string? unit = null, bool def = false, string? hint = null)
    { _list.Add(ReportCols.Qty(key, label, sel, decimals, unit, def, hint)); return this; }

    public ColumnSet<T> IntM(string key, string label, Expression<Func<T, int>> sel,
        string? unit = null, bool def = false, string? hint = null)
    { _list.Add(ReportCols.IntMeasure(key, label, sel, unit, def, hint)); return this; }

    public ColumnSet<T> RealM(string key, string label, Expression<Func<T, double>> sel,
        int decimals = 1, string? unit = null, bool def = false, string? hint = null)
    { _list.Add(ReportCols.RealMeasure(key, label, sel, decimals, unit, def, hint)); return this; }

    public ColumnSet<T> Date(string key, string label, Expression<Func<T, DateTime>> sel,
        bool anchor = false, bool def = false, string? hint = null)
    { _list.Add(ReportCols.DateDim(key, label, sel, anchor, def, hint)); return this; }

    public ColumnSet<T> DateN(string key, string label, Expression<Func<T, DateTime?>> sel,
        bool anchor = false, bool def = false, string? hint = null)
    { _list.Add(ReportCols.DateN(key, label, sel, anchor, def, hint)); return this; }

    public ColumnSet<T> Bool(string key, string label, Expression<Func<T, bool>> sel,
        bool def = false, string? hint = null)
    { _list.Add(ReportCols.BoolDim(key, label, sel, def, hint)); return this; }

    public ColumnSet<T> Enum(string key, string label, Expression<Func<T, int>> sel,
        Dictionary<int, string> labels, bool def = false, string? hint = null)
    { _list.Add(ReportCols.EnumDim(key, label, sel, labels, def, hint)); return this; }

    public ReportColumn<T>[] Build() => _list.ToArray();
}
