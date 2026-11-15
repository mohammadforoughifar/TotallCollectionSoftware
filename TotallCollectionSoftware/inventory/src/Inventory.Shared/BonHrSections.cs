namespace Inventory.Shared;

/// <summary>
/// یک «بخش» از ماژول منابع انسانی بن‌سازه (RadisHr) — هر آیتم منوی این ماژول یک بخش است.
/// </summary>
/// <param name="Key">کلید ثابت و لاتین — بخشی از نام مجوز: <c>RadisHr.{Key}.{Action}</c></param>
/// <param name="Title">نام فارسی بخش (همان چیزی که در منو و تنظیمات دیده می‌شود)</param>
/// <param name="Icon">آیکن بوت‌استرپ منو</param>
/// <param name="SupportedActions">اکشن‌هایی که این بخش onderste می‌دهد (ذخیره/ثبت، ویرایش، حذف)</param>
/// <param name="Routes">مسیرهای صفحهٔ این بخش (بدون اسلش ابتدایی)</param>
/// <param name="LegacyModule">مجوز sezioneِ « قدیم » که همین بخش را کنترل می‌کرد.
/// برای سازگاری با نقش‌های موجود: اگر نقشی <c>RadisHr.{Section}</c> داشته باشد،
/// همان‌گونه قبلی همهٔ اکشن‌های این بخش را بگیرد.</param>
public sealed record BonHrSection(
    string Key,
    string Title,
    string Icon,
    string[] SupportedActions,
    string[] Routes,
    string? LegacyModule = null)
{
    /// <summary>مجوز «مشاهدهٔ بخش» — دیدن در منو و باز شدن صفحهٔ آن.</summary>
    public string ViewPermission => $"RadisHr.{Key}.View";

    /// <summary>مجوز «ثبت/ایجاد» — فقط اگر <see cref="SupportedActions"/> شامل <c>Create</c> باشد.</summary>
    public string? CreatePermission =>
        SupportedActions.Contains("Create") ? $"RadisHr.{Key}.Create" : null;

    /// <summary>مجوز «ویرایش/ذخیره» — فقط اگر <see cref="SupportedActions"/> شامل <c>Update</c> باشد.</summary>
    public string? UpdatePermission =>
        SupportedActions.Contains("Update") ? $"RadisHr.{Key}.Update" : null;

    /// <summary>مجوز «حذف» — فقط اگر <see cref="SupportedActions"/> شامل <c>Delete</c> باشد.</summary>
    public string? DeletePermission =>
        SupportedActions.Contains("Delete") ? $"RadisHr.{Key}.Delete" : null;

    /// <summary>همهٔ مجوزهای این بخش.</summary>
    public IEnumerable<string> AllPermissions =>
        new[] { ViewPermission }
            .Concat(CreatePermission is { } c ? new[] { c } : Array.Empty<string>())
            .Concat(UpdatePermission is { } u ? new[] { u } : Array.Empty<string>())
            .Concat(DeletePermission is { } d ? new[] { d } : Array.Empty<string>());
}

/// <summary>
/// ==================== بخش‌های ماژول منابع انسانی بن‌سازه (RadisHr) ====================
/// منبع واحد حقیقت برای «همهٔ بخش‌ها و اکشن‌هایشان»: منوی کناری، نگهبان مسیر صفحات،
/// تعریف مجوزها در RBAC و برچسب‌های فارسی «تنظیمات ← نقش‌ها و دسترسی‌ها» همه از همین
/// فهرست ساخته می‌شوند.
/// <para>
/// قوانین:
/// </para>
/// <list type="bullet">
///   <item>هر بخش یک مجوز <c>View</c> دارد.</item>
///   <item>بخش‌هایی که دکمه «ثبت/ذخیره» دارند، مجوز <c>Create</c> دارند.</item>
///   <item>بخش‌هایی که دکمه «ویرایش» دارند، مجوز <c>Update</c> دارند.</item>
///   <item>بخش‌هایی که دکمه «حذف» دارند، مجوز <c>Delete</c> دارند.</item>
///   <item>
///     مجوز<section> قدیمی (<see cref="BonHrSection.LegacyModule"/>) همچنان معتبر است:
///     اگر نقشی <c>RadisHr.Employees</c> داشته باشد، همهٔ اکشن‌های بخش Employees را می‌گیرد.
///   </item>
/// </list>
/// </summary>
public static class BonHrSections
{
    /// <summary>نام ماژول در جدول Permissions.</summary>
    public const string Module = "RadisHr";

    /// <summary>همهٔ بخش‌ها — ترتیب همین فهرست، ترتیب نمایش در تنظیمات است.</summary>
    public static readonly BonHrSection[] All =
    [
        new("Dashboard",       "داشبورد",                       "bi-speedometer2", new[] { "View" },                ["bon-hr"],                        null),
        new("Employees",       "پرسنل",                          "bi-people",       new[] { "View", "Create", "Update", "Delete" }, ["bon-hr/employees"], "Employees"),
        new("EmployeeEntry",   "ورود اطلاعات (ثبت/ویرایش پرونده)", "bi-person-plus", new[] { "View", "Create", "Update" }, ["bon-hr/employee-entry"], "EmployeeEntry"),
        new("Attendance",      "حضور و غیاب",                    "bi-clock-history",new[] { "View", "Create", "Delete" }, ["bon-hr/attendance"], "Attendance"),
        new("Payroll",         "حقوق و دستمزد",                  "bi-cash-stack",   new[] { "View" },                   ["bon-hr/payroll"], "Payroll"),
        new("StatutoryRules",  "الزامات سالانه",                 "bi-journal-text", new[] { "View", "Create", "Update" }, ["bon-hr/statutory-rules"], "StatutoryRules"),
        new("OrgStructure",    "واحدها و ماتریس",                "bi-diagram-3",    new[] { "View", "Create", "Delete" }, ["bon-hr/organization-structure"], "OrgStructure"),
        new("OrgSettings",     "تقویم و ساعات",                  "bi-calendar3",    new[] { "View", "Create", "Update", "Delete" }, ["bon-hr/organization-settings"], "OrgSettings"),
        new("Hse",             "ایمنی و حوادث",                  "bi-shield-plus",  new[] { "View", "Create", "Delete" }, ["bon-hr/hse"], "Hse"),
        new("Finance",         "مالی",                           "bi-wallet2",      new[] { "View", "Create" },        ["bon-hr/finance"], "Finance"),
        new("Accounting",      "حسابداری حقوق",                 "bi-calculator",   new[] { "View", "Create" },        ["bon-hr/accounting"], "Accounting"),
        new("ProductionDaily", "گزارش روزانه",                   "bi-clipboard-data",new[] { "View" },                  ["bon-hr/production-daily"], "ProductionDaily"),
        new("Notices",         "اطلاعیه‌ها",                      "bi-bell",         new[] { "View", "Create", "Delete" }, ["bon-hr/notices"], "Notices"),
    ];

    private static readonly Dictionary<string, BonHrSection> ByKeyMap =
        All.ToDictionary(s => s.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>همهٔ اکشن‌های ماژول RadisHr — برای همگام‌سازی جدول Permissions.</summary>
    public static string[] AllActions => All.SelectMany(s => s.AllPermissions).ToArray();

    public static BonHrSection? ByKey(string? key) =>
        key is not null && ByKeyMap.TryGetValue(key, out var s) ? s : null;

    /// <summary>اکشن‌هایی که مفهوم «مشاهده» دارند.</summary>
    private static readonly HashSet<string> ViewLikeActions =
        new(StringComparer.OrdinalIgnoreCase) { "View", "Read", "Access" };

    /// <summary>
    /// آیا مجوز <paramref name="granted"/> (که باید از ماژول RadisHr باشد) مجوز
    /// <paramref name="requested"/> (مجوز یک اکشنsection قدیمی) را پوشش می‌دهد؟
    /// <para>
    /// مثال: <c>RadisHr.Employees.Manage</c> ⇒ <c>RadisHr.Employees.Create/Update/Delete</c> و
    /// <c>RadisHr.Employees.View</c> ⇒ <c>RadisHr.Employees</c> (قدیمی).
    /// </para>
    /// </summary>
    public static bool AliasAllows(string? requested, string? granted)
    {
        if (string.IsNullOrWhiteSpace(requested) || string.IsNullOrWhiteSpace(granted)) return false;
        if (!granted.StartsWith(Module + ".", StringComparison.OrdinalIgnoreCase)) return false;

        var dot1 = requested.IndexOf('.');
        var dot2 = requested.LastIndexOf('.');
        if (dot1 <= 0 || dot2 <= dot1 || dot2 == requested.Length - 1) return false;
        var section = requested[(dot1 + 1)..dot2];
        var action = requested[(dot2 + 1)..];

        foreach (var sec in All)
        {
            if (!sec.Key.Equals(section, StringComparison.OrdinalIgnoreCase)) continue;

            // مجوز مدیریت کل بخش ⇒ همه اکشن‌ها
            if (granted.Equals(sec.ViewPermission, StringComparison.OrdinalIgnoreCase)
                && ViewLikeActions.Contains(action)) return true;

            if (sec.CreatePermission is not null && granted.Equals(sec.CreatePermission, StringComparison.OrdinalIgnoreCase)
                && action.Equals("Create", StringComparison.OrdinalIgnoreCase)) return true;
            if (sec.UpdatePermission is not null && granted.Equals(sec.UpdatePermission, StringComparison.OrdinalIgnoreCase)
                && action.Equals("Update", StringComparison.OrdinalIgnoreCase)) return true;
            if (sec.DeletePermission is not null && granted.Equals(sec.DeletePermission, StringComparison.OrdinalIgnoreCase)
                && action.Equals("Delete", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>بخش مربوط به یک مسیر صفحه.</summary>
    public static BonHrSection? ForRoute(string? relativeUrl)
    {
        if (string.IsNullOrWhiteSpace(relativeUrl)) return null;
        var path = relativeUrl.Split('?')[0].Split('#')[0].Trim().Trim('/').ToLowerInvariant();
        if (path.Length == 0) return null;

        BonHrSection? best = null;
        var bestLen = -1;
        foreach (var section in All)
        {
            foreach (var route in section.Routes)
            {
                if (!path.Equals(route, StringComparison.Ordinal) &&
                    !path.StartsWith(route + "/", StringComparison.Ordinal)) continue;
                if (route.Length <= bestLen) continue;
                best = section;
                bestLen = route.Length;
            }
        }
        return best;
    }
}
