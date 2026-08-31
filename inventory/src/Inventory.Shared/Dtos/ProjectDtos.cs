namespace Inventory.Shared.Dtos;

// ============================================================
//  ماژول مدیریت پروژه‌ها (ورود و خروج، گزارش کار، کارفرما، فاکتور)
// ============================================================

/// <summary>کارفرما</summary>
public class KarFarmaDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Address { get; set; }

    /// <summary>شماره تماس مدیرعامل</summary>
    public string? ModirAmelPhone { get; set; }
    public string? Telephone { get; set; }
    public string? Fax { get; set; }

    /// <summary>شماره ثبت شرکت</summary>
    public string? ShomareSabt { get; set; }

    /// <summary>تعداد پروژه‌های این کارفرما (فقط برای نمایش)</summary>
    public int ProjectCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>نوع فاکتور / نوع پروژه</summary>
public class TypeFactorDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int ProjectCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>رکورد ورود و خروج پروژه</summary>
public class ProjectEntryExitDto
{
    public int Id { get; set; }

    /// <summary>کد پروژه — خودکار: ورود جدید = آخرین کد+۱ ؛ برگشتی = REn-کد مبدأ (خروجی-فقط)</summary>
    public string CodeProject { get; set; } = "";

    /// <summary>عدد برگشتی (n در REn) — کاربر دستی وارد می‌کند؛ ۰ یعنی برگشتی نیست</summary>
    public int ReturnProjectId { get; set; }

    /// <summary>شناسه‌ی پروژه مبدأ در ثبت برگشتی (فقط ورودی ایجاد — کد از «عدد برگشتی + کد مبدأ» ساخته می‌شود)</summary>
    public int ReturnOfProjectId { get; set; }

    public string SerialNumber { get; set; } = "";
    public string ProjectName { get; set; } = "";

    /// <summary>شماره قبض خروج</summary>
    public string? GhabzExit { get; set; }

    /// <summary>شماره فاکتور</summary>
    public string? FactorNumber { get; set; }

    /// <summary>شماره کارشناسی اولیه</summary>
    public string? KarshenasiAvalie { get; set; }

    /// <summary>تحویل گیرنده پروژه</summary>
    public string ProjectReceiver { get; set; } = "";
    public string? Description { get; set; }

    public int KarFarmaId { get; set; }

    /// <summary>نوع فاکتور (اختیاری — از فرم مجزای فاکتور تکمیل می‌شود)</summary>
    public int? FactorTypeId { get; set; }

    /// <summary>کاربر لاگین مرتبط (اپراتور)</summary>
    public int UserId { get; set; }

    /// <summary>تاریخ خروج</summary>
    public DateTime? ExitDate { get; set; }

    /// <summary>تاریخ ورود</summary>
    public DateTime? EntryDate { get; set; }

    /// <summary>تاریخ پرونده</summary>
    public DateTime? FileDate { get; set; }

    /// <summary>تاریخ تحویل</summary>
    public DateTime? DeliveryDate { get; set; }

    /// <summary>تاریخ خروج موقت</summary>
    public DateTime? TemporaryExitDate { get; set; }

    /// <summary>تاریخ ثبت پروژه</summary>
    public DateTime? ProjectRegistrationDate { get; set; }

    /// <summary>تاریخ مورد نیاز مشتری</summary>
    public DateTime? CustomerRequiredDate { get; set; }

    /// <summary>آیا پوشه دارد؟</summary>
    public bool? IsFolder { get; set; }

    /// <summary>جمع ساعات کار صرف‌شده روی پروژه (از گزارش‌های کار)</summary>
    public TimeSpan TotalSpentTime { get; set; }

    // ---------- داده‌های نمایشی (جوین) ----------
    public string? KarFarmaName { get; set; }
    public string? FactorTypeName { get; set; }
    public string? UserName { get; set; }
    public int AttachCount { get; set; }
    public int ReportWorkCount { get; set; }

    // ---------- گردش‌کار کارتابل (خروجی-فقط؛ تغییر فقط از مسیر API کارتابل) ----------
    /// <summary>وضعیت گردش‌کار: ۰=در انتظار تایید مدیر، ۱=در انتظار کارشناسی، ۲=رد شده، ۳=نهایی</summary>
    public int FlowStatus { get; set; }
    public string? ManagerNote { get; set; }
    public string? ExpertNote { get; set; }
    public DateTime? ManagerActionAt { get; set; }
    public DateTime? ExpertActionAt { get; set; }
}

/// <summary>اقدام کارتابل پروژه: تایید/رد مدیر یا اتمام کارشناسی (یادداشت اختیاری — برای رد الزامی)</summary>
public class ProjectFlowActionDto
{
    public string? Note { get; set; }
}

/// <summary>آیتم کارتابل پروژه (+نام ثبت‌کننده و مدت انتظار)</summary>
public class ProjectCartableCountsDto
{
    public int Manager { get; set; }
    public int Expert { get; set; }
}

/// <summary>
/// ثبت/ویرایش تاریخ‌های چرخهٔ پروژه — فرم مجزا از منوی سطر (مثل فرم فاکتور).
/// هر فیلد اختیاری است؛ <c>null</c> یعنی «ثبت نشده / پاک شود».
/// </summary>
public class ProjectDatesDto
{
    /// <summary>تاریخ خروج (خروج نهایی پروژه از مجموعه)</summary>
    public DateTime? ExitDate { get; set; }

    /// <summary>تاریخ خروج موقت</summary>
    public DateTime? TemporaryExitDate { get; set; }

    /// <summary>تاریخ مورد نیاز مشتری</summary>
    public DateTime? CustomerRequiredDate { get; set; }

    /// <summary>تاریخ تحویل پروژه</summary>
    public DateTime? DeliveryDate { get; set; }

    /// <summary>تاریخ تحویل پرونده</summary>
    public DateTime? FileDate { get; set; }
}

/// <summary>ثبت/ویرایش اطلاعات فاکتور پروژه — فرم مجزا از منوی سطر (بعداً تکمیل می‌شود)</summary>
public class ProjectFactorDto
{
    /// <summary>شناسه نوع فاکتور (الزامی)</summary>
    public int FactorTypeId { get; set; }

    /// <summary>شماره فاکتور (اختیاری — خالی یعنی پاک شود)</summary>
    public string? FactorNumber { get; set; }
}

/// <summary>گزارش کار روی پروژه</summary>
public class ReportWorkDto
{
    public int Id { get; set; }

    /// <summary>تاریخ گزارش</summary>
    public DateTime ReportDate { get; set; } = DateTime.Today;

    /// <summary>شناسه کاربر لاگین (اپراتور)</summary>
    public int UserId { get; set; }
    public string WorkDescription { get; set; } = "";
    public int ProjectId { get; set; }

    /// <summary>کد پروژه — موقع ثبت از پروژه کپی می‌شود (خروجی-فقط؛ گزارشات بر اساس کد پروژه هستند)</summary>
    public string CodeProject { get; set; } = "";

    public TimeOnly StartTime { get; set; } = new(8, 0);
    public TimeOnly EndTime { get; set; } = new(17, 0);

    /// <summary>مدت زمان صبحانه (ساعت:دقیقه)</summary>
    public TimeOnly BreakfastTime { get; set; }

    /// <summary>مدت زمان ناهار (ساعت:دقیقه)</summary>
    public TimeOnly LunchTime { get; set; }

    /// <summary>زمان صرف‌شده خالص = (پایان − شروع) − (صبحانه + ناهار)</summary>
    public TimeSpan SpentTime { get; set; }

    // ---------- نمایشی ----------
    public string? ProjectName { get; set; }
    public string? UserName { get; set; }
}

/// <summary>پیوست پروژه (فایل رمزنگاری‌شده روی سرور)</summary>
public class ProjectAttachDto
{
    public int Id { get; set; }

    /// <summary>نام اصلی فایل (بعد از رمزگشایی)</summary>
    public string FileName { get; set; } = "";
    public long FileSize { get; set; }
    public int Type { get; set; }
    public DateTime DateSabt { get; set; }
    public int ProjectId { get; set; }
    public string? UserName { get; set; }
}

/// <summary>آیتم‌های کمبو برای فرم‌های ماژول پروژه</summary>
public class ProjectLookups
{
    public List<LookupItem> Users { get; set; } = new();
    public List<LookupItem> KarFarmas { get; set; } = new();
    public List<LookupItem> TypeFactors { get; set; } = new();
    public List<LookupItem> Projects { get; set; } = new();

    /// <summary>فقط پروژه‌های اصلی (بدون RE) — برای کمبوی «پروژه برگشتی»</summary>
    public List<LookupItem> BaseProjects { get; set; } = new();

    /// <summary>
    /// فقط پروژه‌های تاییدشدهٔ مدیر (FlowStatus ۱ یا ۳) — برای کمبوی «پروژه» در فرم ثبت گزارش کار.
    /// پروژه‌های «در انتظار تایید مدیر» و «رد شده» قابل گزارش‌دهی نیستند.
    /// </summary>
    public List<LookupItem> ReportableProjects { get; set; } = new();
}
