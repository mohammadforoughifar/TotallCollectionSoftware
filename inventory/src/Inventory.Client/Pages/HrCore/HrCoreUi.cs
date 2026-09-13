namespace Inventory.Client.Pages;

/// <summary>هلپرهای نمایشی ماژول کارگزینی (HrCore)</summary>
public static class HrCoreUi
{
    public static string EmpType(int t) => t switch
    {
        0 => "رسمی", 1 => "قراردادی", 2 => "پیمانی", 3 => "ساعتی", 4 => "مشاوره‌ای",
        5 => "تمام‌وقت", 6 => "پاره‌وقت", 7 => "پروژه‌ای", 8 => "آزمایشی", _ => "—"
    };

    public static string EmpStatus(int s) => s switch
    {
        0 => "فعال", 1 => "مرخصی بلندمدت", 2 => "معلق", 3 => "قطع همکاری", 4 => "بازنشسته", _ => "—"
    };

    public static string StatusBadge(int s) => s switch
    {
        0 => "text-bg-success",
        1 => "text-bg-info",
        2 => "text-bg-warning",
        3 => "text-bg-danger",
        4 => "text-bg-secondary",
        _ => "text-bg-light"
    };

    public static string SkillLevel(int l) => l switch
    {
        0 => "مبتدی", 1 => "متوسط", 2 => "پیشرفته", 3 => "خبره", _ => "—"
    };

    public static string DocType(int t) => t switch
    {
        0 => "قرارداد", 1 => "کارت ملی", 2 => "مدرک تحصیلی", 3 => "شناسنامه",
        4 => "پایان خدمت", 5 => "بیمه", 9 => "متفرقه", _ => "—"
    };

    public static string DecreeType(int t) => t switch
    {
        0 => "استخدام", 1 => "ارتقا", 2 => "انتقال", 3 => "تغییر حقوق",
        4 => "تشویق", 5 => "تنبیه", 6 => "قطع همکاری", 7 => "بازنشستگی", _ => "—"
    };

    public static string OrgType(int t) => t switch
    {
        0 => "شرکت", 1 => "شعبه", 2 => "دپارتمان", 3 => "مرکز هزینه", _ => "واحد"
    };

    public static string Initials(string first, string last)
    {
        var a = string.IsNullOrWhiteSpace(first) ? "" : first.Trim()[..1];
        var b = string.IsNullOrWhiteSpace(last) ? "" : last.Trim()[..1];
        return (a + b) is "" or null ? "؟" : a + b;
    }

    private static readonly string[] AvatarColors =
        { "#2563eb", "#0d9488", "#7c3aed", "#db2777", "#ea580c", "#0891b2", "#4d7c0f" };

    public static string AvatarColor(string seed)
    {
        if (string.IsNullOrEmpty(seed)) return AvatarColors[0];
        var h = 0;
        foreach (var ch in seed) h = (h * 31 + ch) & 0x7fffffff;
        return AvatarColors[h % AvatarColors.Length];
    }

    public static string PostStatus(int s) => s switch
    {
        0 => "پیش‌نویس", 1 => "منتشرشده", 2 => "بسته", _ => "—"
    };

    public static string PostStatusBadge(int s) => s switch
    {
        0 => "text-bg-secondary", 1 => "text-bg-success", 2 => "text-bg-dark", _ => "text-bg-light"
    };

    public static string AppStatus(int s) => s switch
    {
        0 => "جدید", 1 => "در حال بررسی", 2 => "دعوت به مصاحبه", 3 => "مصاحبه‌شده",
        4 => "پذیرفته", 5 => "ردشده", 6 => "استخدام‌شده", _ => "—"
    };

    public static string AppStatusBadge(int s) => s switch
    {
        0 => "text-bg-info", 1 => "text-bg-primary", 2 => "text-bg-warning", 3 => "text-bg-warning",
        4 => "text-bg-success", 5 => "text-bg-danger", 6 => "text-bg-success", _ => "text-bg-light"
    };

    public static string InterviewResult(int r) => r switch
    {
        0 => "نامشخص", 1 => "قبول", 2 => "رد", 3 => "رزرو", _ => "—"
    };
}
