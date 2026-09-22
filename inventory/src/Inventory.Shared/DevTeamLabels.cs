namespace Inventory.Shared;

/// <summary>
/// برچسب‌های فارسی ماژول مدیریت برنامه‌نویسان.
/// <para>
/// عمداً در Inventory.Shared است تا هم API و هم کلاینت از یک متن واحد استفاده کنند
/// و عنوان وضعیت‌ها در دو جا از هم واگرا نشود.
/// </para>
/// </summary>
public static class DevTeamLabels
{
    public static string Status(DevTaskStatus s) => s switch
    {
        DevTaskStatus.Backlog => "در صف",
        DevTaskStatus.InProgress => "در حال انجام",
        DevTaskStatus.InReview => "در بازبینی",
        DevTaskStatus.Blocked => "مسدود",
        DevTaskStatus.Done => "انجام شد",
        _ => s.ToString()
    };

    /// <summary>ترتیب ستون‌های بورد — همان ترتیبی که در منو و گزارش‌ها استفاده می‌شود.</summary>
    public static readonly DevTaskStatus[] BoardOrder =
    [
        DevTaskStatus.InProgress,
        DevTaskStatus.InReview,
        DevTaskStatus.Blocked,
        DevTaskStatus.Backlog,
        DevTaskStatus.Done
    ];

    public static string Priority(DevPriority p) => p switch
    {
        DevPriority.Low => "کم",
        DevPriority.Normal => "معمولی",
        DevPriority.High => "زیاد",
        DevPriority.Urgent => "فوری",
        _ => p.ToString()
    };

    public static string Size(DevTaskSize s) => s switch
    {
        DevTaskSize.Small => "کوچک",
        DevTaskSize.Medium => "متوسط",
        DevTaskSize.Large => "بزرگ",
        _ => s.ToString()
    };

    /// <summary>وزن اندازهٔ کار — برای سنجش بار واقعی، نه فقط تعداد آیتم.</summary>
    public static int SizeWeight(DevTaskSize s) => s switch
    {
        DevTaskSize.Small => 1,
        DevTaskSize.Medium => 2,
        DevTaskSize.Large => 5,
        _ => 2
    };

    public static string Role(DevRole r) => r switch
    {
        DevRole.Developer => "برنامه‌نویس",
        DevRole.SeniorDeveloper => "برنامه‌نویس ارشد",
        DevRole.TeamLead => "سرپرست تیم",
        DevRole.Qa => "تست و کنترل کیفیت",
        DevRole.UiDesigner => "طراح رابط کاربری",
        DevRole.DevOps => "عملیات و استقرار",
        DevRole.ProductOwner => "مسئول محصول",
        DevRole.AiAgent => "ایجنت هوش مصنوعی",
        _ => r.ToString()
    };

    public static string Action(DevLogAction a) => a switch
    {
        DevLogAction.Created => "ایجاد آیتم",
        DevLogAction.Assigned => "واگذاری",
        DevLogAction.StatusChanged => "تغییر وضعیت",
        DevLogAction.Progress => "ثبت پیشرفت",
        DevLogAction.Committed => "ثبت کامیت",
        DevLogAction.Reviewed => "بازبینی کد",
        DevLogAction.Completed => "اتمام کار",
        DevLogAction.Comment => "یادداشت",
        _ => a.ToString()
    };

    /// <summary>آیا این وضعیت «باز» محسوب می‌شود (هنوز تمام نشده)؟</summary>
    public static bool IsOpen(DevTaskStatus s) => s != DevTaskStatus.Done;

    // ================== فهرست‌های کمکی برای فرم‌ها ==================
    // در Shared قرار دارند تا صفحه‌های Razor گزینه‌ها را حلقه بزنند و فهرست
    // نقش‌ها/اولویت‌ها در هر صفحه جداگانه و واگرا نوشته نشود.

    /// <summary>همهٔ نقش‌های تیم، به ترتیب منطقی نمایش در فرم.</summary>
    public static readonly DevRole[] AllRoles =
    [
        DevRole.ProductOwner,
        DevRole.TeamLead,
        DevRole.SeniorDeveloper,
        DevRole.Developer,
        DevRole.Qa,
        DevRole.UiDesigner,
        DevRole.DevOps,
        DevRole.AiAgent
    ];

    /// <summary>همهٔ اولویت‌ها، از فوری به کم.</summary>
    public static readonly DevPriority[] AllPriorities =
    [
        DevPriority.Urgent,
        DevPriority.High,
        DevPriority.Normal,
        DevPriority.Low
    ];

    /// <summary>همهٔ اندازه‌های آیتم، از کوچک به بزرگ.</summary>
    public static readonly DevTaskSize[] AllSizes =
    [
        DevTaskSize.Small,
        DevTaskSize.Medium,
        DevTaskSize.Large
    ];

    /// <summary>همهٔ کنش‌های قابل ثبت در تاریخچهٔ یک آیتم.</summary>
    public static readonly DevLogAction[] AllActions =
    [
        DevLogAction.Progress,
        DevLogAction.Committed,
        DevLogAction.Reviewed,
        DevLogAction.Comment
    ];

    /// <summary>رنگ پیش‌فرض اعضا و ماژول‌ها وقتی کاربر رنگی انتخاب نکرده است.</summary>
    public const string DefaultColor = "#2f6fed";

    /// <summary>
    /// عنوان فارسی ماژول‌های نرم‌افزار، کلید‌شده با نام پوشهٔ کد.
    /// <para>
    /// وقتی فهرست ماژول‌ها از <c>tools/ownership-map.csv</c> وارد می‌شود، کلیدها لاتین‌اند
    /// (HrCore، Treasury، …). بدون این نقشه، عنوان همهٔ ماژول‌ها همان کلید لاتین می‌ماند و
    /// بورد برای تیم فارسی‌زبان خوانا نیست. نام‌ها عمداً با برچسب‌های موجود در
    /// «تنظیمات ← نقش‌ها و دسترسی‌ها» یکسان گرفته شده‌اند تا در دو جای برنامه واگرا نشوند.
    /// </para>
    /// </summary>
    public static readonly Dictionary<string, string> ModuleTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Accounting"] = "حسابداری",
        ["BonHr"] = "منابع انسانی بن‌سازه",
        ["Catalog"] = "کاتالوگ و کالاها",
        ["Cctv"] = "دوربین‌های مداربسته",
        ["Chat"] = "پیام‌رسان سازمانی",
        ["Core"] = "هستهٔ مشترک و زیرساخت",
        ["Dashboards"] = "داشبوردها",
        ["DocArchive"] = "آرشیو اسناد و مدارک",
        ["Export"] = "خروجی و فایل‌های اکسل",
        ["FaAtt"] = "حضور و غیاب فروغ آریا",
        ["FaCom"] = "ارتباطات داخلی فروغ آریا",
        ["FaLms"] = "آموزش و توسعه فروغ آریا",
        ["FaPay"] = "حقوق و دستمزد فروغ آریا",
        ["Finance"] = "مالی و هزینه‌ها",
        ["Hr"] = "منابع انسانی (عمومی)",
        ["HrCore"] = "کارگزینی (پرسنل، قراردادها، احکام)",
        ["HrMain"] = "اطلاعات پایه سازمانی",
        ["HrReports"] = "گزارش‌های منابع انسانی",
        ["HrTalent"] = "جذب، استخدام و استعداد",
        ["Invoicing"] = "فاکتور و صورتحساب",
        ["ItAssets"] = "اموال و تجهیزات IT",
        ["Notification"] = "اعلان‌ها و پیامک",
        ["Office"] = "اتوماسیون اداری و نامه‌ها",
        ["Orders"] = "سفارش‌ها و فاکتورها",
        ["Panel"] = "پنل معرف",
        ["Pdf"] = "تولید و چاپ PDF",
        ["Projects"] = "مدیریت پروژه‌ها",
        ["Repairs"] = "تعمیرات",
        ["ReportStudio"] = "گزارش‌ساز",
        ["Reports"] = "گزارش‌ها",
        ["Sales"] = "فروش",
        ["Settings"] = "تنظیمات",
        ["Stocktaking"] = "انبارگردانی",
        ["System"] = "مدیریت سیستم و کاربران",
        ["Treasury"] = "خزانه‌داری (صندوق، بانک، چک)",
        ["Warehousing"] = "انبار و موجودی",
        ["Watermark"] = "واترمارک و امنیت اسناد"
    };

    /// <summary>عنوان فارسی یک ماژول؛ اگر در نقشه نبود، همان کلید برگردانده می‌شود.</summary>
    public static string ModuleTitle(string key) =>
        ModuleTitles.TryGetValue(key, out var t) ? t : key;

    /// <summary>
    /// رنگ ستون بر پایهٔ وضعیت — تا بورد، جدول و نمودارها هم‌رنگ بمانند.
    /// </summary>
    public static string StatusColor(DevTaskStatus s) => s switch
    {
        DevTaskStatus.InProgress => "#fd7e14",
        DevTaskStatus.InReview => "#6f42c1",
        DevTaskStatus.Blocked => "#dc3545",
        DevTaskStatus.Backlog => "#adb5bd",
        DevTaskStatus.Done => "#20c997",
        _ => "#6c757d"
    };
}
