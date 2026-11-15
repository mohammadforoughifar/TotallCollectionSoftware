namespace Inventory.Client.Services;

/// <summary>
/// نقشهٔ «ماژول لایسنس ⇄ منو».
///
/// <para>
/// <b>چرا یک نقشهٔ ثابت؟</b> چون منوها با آدرس (route) شناخته می‌شوند و لایسنس با کد
/// ماژول. اگر این نگاشت در چند جای UI پخش شود، روزی یکی از جاها جا می‌ماند و کاربر
/// به صفحهٔ یک ماژول غیرفعال می‌رسد. یک نقشه، یک منبع حقیقت.
/// </para>
///
/// <para>
/// قاعده: کنار هر مسیر، <b>کد ماژول</b> می‌آید؛ اگر مسیری در نقشه نباشد، «همیشه باز»
/// فرض می‌شود (<see cref="ModuleForPath"/> = <c>null</c>) — یعنی نبودِ نگاشت هیچ‌وقت
/// منویی را بی‌دلیل مخفی نمی‌کند.
/// </para>
/// </summary>
public static class LicenseMenuMap
{
    /// <summary>یک ردیف نگاشت: پیشوند مسیر ⇒ کد ماژول.</summary>
    public sealed record MenuEntry(string PathPrefix, string ModuleCode, string Label);

    /// <summary>
    /// نگاشت مسیرها به ۳۳ ماژول لایسنس. ترتیب: طولانی‌ترین پیشوند اول بررسی می‌شود
    /// تا <c>/hr</c> مسیر <c>/hr/payroll</c> را ندزدد.
    /// </summary>
    public static readonly IReadOnlyList<MenuEntry> Entries = new List<MenuEntry>
    {
        // ---- همیشه‌فعال (قفل نمی‌شوند) ----
        new("dashboard", "dashboard", "داشبوردها"),
        new("cartable", "cartable", "کارتابل"),
        new("settings/backup", "backup", "بک‌آپ"),
        new("settings/license", "licensing", "وضعیت لایسنس"),

        // ---- بسته‌های فروش ----
        new("catalog", "catalog", "کالا و خدمات"),
        new("sales", "sales", "فروش"),
        new("sales-visit", "salesvisit", "ویزیتور"),
        new("warehousing", "warehousing", "انبار"),
        new("stocktaking", "stocktaking", "انبارگردانی"),
        new("invoicing", "invoicing", "صورتحساب و مودیان"),
        new("moadian", "moadian", "مودیان"),
        new("accounting", "accounting", "حسابداری"),
        new("treasury", "treasury", "خزانه"),
        new("expenses", "expenses", "هزینه‌ها"),
        new("repairs", "repairs", "تعمیرات"),
        new("projects", "projects", "پروژه‌ها"),
        new("work-orders", "workorders", "دستور کار"),
        new("letters", "letters", "دبیرخانه و نامه‌ها"),
        new("office", "office_assets", "اموال اداری"),
        new("doc-archive", "docarchive", "بایگانی اسناد"),
        new("it", "it_assets", "اموال IT"),
        new("chat", "chat", "گفتگو"),
        new("hr", "hr_fa", "منابع انسانی"),
        new("hr-core", "hr_core", "هستهٔ منابع انسانی"),
        new("fa-att", "faham_att", "فهم‌آت"),
        new("fa-pay", "faham_pay", "فهم‌پی"),
        new("fa-lms", "faham_lms", "فهم‌ال‌ام‌اس"),
        new("fa-com", "faham_com", "فهم‌کام"),
        new("bon-hr", "bon_hr", "بن‌اچ‌آر"),
        new("ai", "ai", "هوش مصنوعی"),
        new("reports", "reports", "گزارش‌ها"),
        new("dev-team", "devteam", "تیم توسعه"),
        new("finance", "accounting", "مالی")
    };

    /// <summary>
    /// کد ماژولِ یک آدرس. <c>null</c> = مسیر آزاد (همیشه باز) یا ناشناخته.
    /// نمونه: <c>/settings/license/status</c> ⇒ <c>licensing</c>
    /// </summary>
    public static string? ModuleForPath(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var path = url.Trim();
        var query = path.IndexOfAny(new[] { '?', '#' });
        if (query >= 0) path = path[..query];
        path = path.Trim('/').ToLowerInvariant();
        if (path.Length == 0) return null;

        string? best = null;
        var bestLength = -1;
        foreach (var entry in Entries)
        {
            // تطبیق روی مرز بخش: "/hr" نباید "/hr-core" را بگیرد.
            var hit = path == entry.PathPrefix
                      || path.StartsWith(entry.PathPrefix + "/", StringComparison.Ordinal);
            if (hit && entry.PathPrefix.Length > bestLength)
            {
                best = entry.ModuleCode;
                bestLength = entry.PathPrefix.Length;
            }
        }
        return best;
    }

    /// <summary>آیا این مسیر با ماژول‌های غیرفعال سازگار است؟ (منو را نشان بدهیم یا نه)</summary>
    public static bool IsPathAllowed(string? url, IReadOnlyCollection<string> disabledModules)
    {
        var module = ModuleForPath(url);
        if (module is null) return true;                       // نگاشت‌نشده = همیشه باز
        if (disabledModules.Count == 0) return true;
        return !disabledModules.Contains(module, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>فهرست کدهای ماژول نگاشت‌شده (برای تست هم‌خوانی با ModuleCodes).</summary>
    public static IReadOnlyList<string> MappedModuleCodes
        => Entries.Select(e => e.ModuleCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
