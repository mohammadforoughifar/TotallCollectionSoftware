namespace Inventory.Licensing.Modules;

/// <summary>یک کد ماژول قابل لایسنس.</summary>
/// <param name="Code">کد پایدار در فایل لایسنس (انگلیسی، تغییرناپذیر).</param>
/// <param name="Title">نام فارسی برای UI و پیام خطا.</param>
/// <param name="AlwaysOn">پایه‌ی همیشه‌فعال — هیچ آیتمی آن را قفل نمی‌کند.</param>
/// <param name="Sellable">قابل فروش به‌صورت جداگانه (در LicenseTool به‌عنوان Preset).</param>
public sealed record ModuleDefinition(string Code, string Title, bool AlwaysOn = false, bool Sellable = true);

/// <summary>
/// کاتالوگ ثابت ماژول‌های قابل لایسنس — منبع واحد حقیقت.
///
/// <para>
/// کدها عمداً <b>انگلیسی و کوتاه</b> هستند و با کلیدهای RBAC (`RbacSeeder.ModuleActions`)
/// هم‌نام شده‌اند تا نگاشت «مجوز» و «لایسنس» در ذهن تیم یکی باشد؛ ولی این دو مستقل‌اند:
/// RBAC می‌گوید «این کاربر چه اجازه‌ای دارد»، لایسنس می‌گوید «این نصب چه چیزی خریده است».
/// </para>
///
/// <para>
/// ⚠️ این فهرست در کد است و از appsettings قابل تغییر نیست. اگر از تنظیمات قابل
/// گسترش بود، هر کسی می‌توانست کد دلخواه اضافه کند و منطق قفل را دور بزند.
/// برای افزودن ماژول تازه، فقط همین فایل و <see cref="ModuleDependencyMap"/> عوض می‌شوند.
/// </para>
/// </summary>
public static class ModuleCodes
{
    // ---------- پایه‌ی همیشه‌فعال (قابل قفل نیست) ----------
    public const string Core = "core";
    public const string Dashboard = "dashboard";
    public const string Cartable = "cartable";
    public const string Backup = "backup";

    /// <summary>خودِ ماژول لایسنس — همیشه باز، وگرنه آپلود لایسنس ممکن نمی‌شد.</summary>
    public const string Licensing = "licensing";

    // ---------- بسته‌های فروش ----------
    public const string Catalog = "catalog";
    public const string Sales = "sales";
    public const string SalesVisit = "salesvisit";
    public const string Warehousing = "warehousing";
    public const string Stocktaking = "stocktaking";
    public const string Invoicing = "invoicing";
    public const string Moadian = "moadian";
    public const string Accounting = "accounting";
    public const string Treasury = "treasury";
    public const string Expenses = "expenses";
    public const string Repairs = "repairs";
    public const string Projects = "projects";
    public const string WorkOrders = "workorders";
    public const string Letters = "letters";
    public const string OfficeAssets = "office_assets";
    public const string DocArchive = "docarchive";
    public const string ItAssets = "it_assets";
    public const string Chat = "chat";
    public const string HrFa = "hr_fa";
    public const string HrCore = "hr_core";
    public const string FahamAtt = "faham_att";
    public const string FahamPay = "faham_pay";
    public const string FahamLms = "faham_lms";
    public const string FahamCom = "faham_com";
    public const string BonHr = "bon_hr";
    public const string Ai = "ai";
    public const string Reports = "reports";
    public const string DevTeam = "devteam";

    /// <summary>کاتالوگ کامل — ترتیب نمایش در UI هم همین است.</summary>
    public static readonly IReadOnlyList<ModuleDefinition> All = new List<ModuleDefinition>
    {
        new(Core,       "هستهٔ سیستم",      AlwaysOn: true),
        new(Dashboard,  "داشبوردها",        AlwaysOn: true),
        new(Cartable,   "کارتابل و اعلان‌ها", AlwaysOn: true),
        new(Backup,     "پشتیبان‌گیری",      AlwaysOn: true, Sellable: false),
        new(Licensing,  "لایسنس",           AlwaysOn: true, Sellable: false),

        new(Catalog,      "کالا، طرف حساب و انبار پایه"),
        new(Sales,        "فروش و خرید"),
        new(SalesVisit,   "ویزیتوری"),
        new(Warehousing,  "انبارداری سندمحور"),
        new(Stocktaking,  "انبارگردانی"),
        new(Invoicing,    "صورتحساب و فاکتور"),
        new(Moadian,      "سامانهٔ مودیان"),
        new(Accounting,   "حسابداری"),
        new(Treasury,     "خزانه‌داری"),
        new(Expenses,     "هزینه‌ها"),
        new(Repairs,      "تعمیرات، گارانتی و تعویض"),
        new(Projects,     "مدیریت پروژه‌ها"),
        new(WorkOrders,   "دستور کار و نگار ذهنی"),
        new(Letters,      "اتوماسیون اداری"),
        new(OfficeAssets, "پایش پرینتر و دوربین"),
        new(DocArchive,   "آرشیو اسناد و مدارک"),
        new(ItAssets,     "دارایی‌های IT، IPAM و سرورها"),
        new(Chat,         "پیام‌رسان سازمانی"),
        new(HrFa,         "منابع انسانی فروغ آریا"),
        new(HrCore,       "پروندهٔ پرسنلی و استخدام"),
        new(FahamAtt,     "حضور و غیاب فهام"),
        new(FahamPay,     "حقوق و دستمزد فهام"),
        new(FahamLms,     "آموزش فهام"),
        new(FahamCom,     "ارتباطات سازمانی فهام"),
        new(BonHr,        "منابع انسانی بن‌سازه"),
        new(Ai,           "دستیار هوشمند"),
        new(Reports,      "گزارش‌ها و گزارش‌ساز"),
        new(DevTeam,      "میز کار توسعه")
    };

    /// <summary>ماژول‌هایی که هیچ آیتمی نمی‌تواند قفلشان کند.</summary>
    public static readonly IReadOnlySet<string> AlwaysOnCodes =
        All.Where(m => m.AlwaysOn).Select(m => m.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>آیا این کد در کاتالوگ هست؟ (مقایسهٔ نام‌حساس نیست)</summary>
    public static bool IsKnown(string? code)
        => !string.IsNullOrWhiteSpace(code) && All.Any(m => string.Equals(m.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>آیا این کد «همیشه‌فعال» است؟ کد ناشناس همیشه‌فعال حساب نمی‌شود.</summary>
    public static bool IsAlwaysOn(string? code)
        => !string.IsNullOrWhiteSpace(code) && AlwaysOnCodes.Contains(code);

    /// <summary>نام فارسی ماژول؛ برای کد ناشناس، خود کد برگردانده می‌شود.</summary>
    public static string TitleOf(string? code)
        => All.FirstOrDefault(m => string.Equals(m.Code, code, StringComparison.OrdinalIgnoreCase))?.Title
           ?? (string.IsNullOrWhiteSpace(code) ? "نامشخص" : code);

    /// <summary>کد را به شکل متعارف (حروف کوچک) برمی‌گرداند.</summary>
    public static string Normalize(string? code) => (code ?? "").Trim().ToLowerInvariant();
}
