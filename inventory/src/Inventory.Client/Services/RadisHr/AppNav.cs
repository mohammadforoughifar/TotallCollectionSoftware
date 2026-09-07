namespace Inventory.Client.Services.RadisHr;

public record NavEntry(string Page, string Label, string Route, string Title);

/// <summary>نقشهٔ منوی کناری — همان ۱۳ آیتم نسخهٔ اصلی با همان ترتیب و عنوان</summary>
public static class AppNav
{
    public static readonly NavEntry[] Items =
    {
        new("dashboard",             "داشبورد منابع انسانی",         "/hr",                       "داشبورد"),
        new("employees",             "پرسنل",                       "/hr/employees",              "پرسنل"),
        new("employeeEntry",         "ورود اطلاعات",                 "/hr/employee-entry",         "ورود اطلاعات"),
        new("attendance",            "حضور و غیاب",                  "/hr/attendance",             "حضور و غیاب"),
        new("payroll",               "حقوق و دستمزد",                "/hr/payroll",                "حقوق و دستمزد"),
        new("statutoryRules",        "الزامات سالانه حقوق",           "/hr/statutory-rules",        "الزامات سالانه حقوق"),
        new("organizationStructure", "واحدها و ماتریس پرسنل",         "/hr/organization-structure", "واحدها و ماتریس پرسنل"),
        new("organizationSettings",  "تقویم و ساعات کاری",            "/hr/organization-settings",  "تقویم و ساعات کاری"),
        new("hse",                   "ایمنی و حوادث",                 "/hr/hse",                    "ایمنی و حوادث"),
        new("finance",               "مساعده و اقساط",               "/hr/finance",                "مالی"),
        new("accounting",            "حسابداری حقوق",                "/hr/accounting",             "مالی حسابداری"),
        new("productionDaily",       "گزارش روزانه حضور پرسنل",        "/hr/production-daily",       "گزارش روزانه حضور پرسنل"),
        new("notices",               "اطلاعیه‌ها",                    "/hr/notices",                "اطلاعیه‌ها")
    };

    public static string RouteOf(string page) =>
        Items.FirstOrDefault(i => i.Page == page)?.Route ?? "/hr";

    public static NavEntry? ByRoute(string route)
    {
        var normalized = "/" + route.Split('?', '#')[0].Trim('/');
        if (normalized.Equals("/radis-hr", StringComparison.OrdinalIgnoreCase)) normalized = "/hr";
        else if (normalized.StartsWith("/radis-hr/", StringComparison.OrdinalIgnoreCase))
            normalized = "/hr/" + normalized[10..];
        if (normalized.Equals("/hr/dashboard", StringComparison.OrdinalIgnoreCase)) normalized = "/hr";
        return Items.FirstOrDefault(i => string.Equals(i.Route, normalized, StringComparison.OrdinalIgnoreCase));
    }
}
