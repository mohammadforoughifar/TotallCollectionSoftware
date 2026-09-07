namespace RadisHr.Client.Services;

public record NavEntry(string Page, string Label, string Route, string Title);

/// <summary>نقشهٔ منوی کناری — همان ۱۳ آیتم نسخهٔ اصلی با همان ترتیب و عنوان</summary>
public static class AppNav
{
    public static readonly NavEntry[] Items =
    {
        new("dashboard",             "داشبورد",                     "/",                       "داشبورد"),
        new("employees",             "پرسنل",                       "/employees",              "پرسنل"),
        new("employeeEntry",         "ورود اطلاعات",                 "/employee-entry",         "ورود اطلاعات"),
        new("attendance",            "حضور و غیاب",                  "/attendance",             "حضور و غیاب"),
        new("payroll",               "حقوق و دستمزد",                "/payroll",                "حقوق و دستمزد"),
        new("statutoryRules",        "الزامات سالانه حقوق",           "/statutory-rules",        "الزامات سالانه حقوق"),
        new("organizationStructure", "واحدها و ماتریس پرسنل",         "/organization-structure", "واحدها و ماتریس پرسنل"),
        new("organizationSettings",  "تقویم و ساعات کاری",            "/organization-settings",  "تقویم و ساعات کاری"),
        new("hse",                   "ایمنی و حوادث",                 "/hse",                    "ایمنی و حوادث"),
        new("finance",               "مالی",                         "/finance",                "مالی"),
        new("accounting",            "مالی حسابداری",                 "/accounting",             "مالی حسابداری"),
        new("productionDaily",       "گزارش روزانه حضور پرسنل",        "/production-daily",       "گزارش روزانه حضور پرسنل"),
        new("notices",               "اطلاعیه‌ها",                    "/notices",                "اطلاعیه‌ها")
    };

    public static string RouteOf(string page) =>
        Items.FirstOrDefault(i => i.Page == page)?.Route ?? "/";

    public static NavEntry? ByRoute(string route)
    {
        var normalized = "/" + route.Trim('/');
        return Items.FirstOrDefault(i => string.Equals(i.Route, normalized, StringComparison.OrdinalIgnoreCase));
    }
}
