namespace Inventory.Shared;

/// <summary>
/// یک «بخش» از سامانه فروشگاهی (هر آیتم منوی سامانه فروشگاهی یک بخش است).
/// </summary>
/// <param name="Key">کلید ثابت و لاتین — بخشی از نام مجوز: <c>Shop.{Key}.View</c></param>
/// <param name="Title">نام فارسی بخش (همان چیزی که در منو و تنظیمات دیده می‌شود)</param>
/// <param name="Icon">آیکن بوت‌استرپ منو</param>
/// <param name="Writable">آیا این بخش عملیات ثبت/ویرایش/حذف دارد؟ (بخش‌های گزارشی فقط «مشاهده» می‌گیرند)</param>
/// <param name="LegacyModules">
/// برای جدا ماندن دسترسی‌های فروشگاه و ERP عمداً خالی است.
/// </param>
/// <param name="Routes">مسیرهای صفحهٔ این بخش (بدون اسلش ابتدایی) — برای نگهبانی مسیر مستقیم</param>
public sealed record ShopSection(
    string Key,
    string Title,
    string Icon,
    bool Writable,
    string[] LegacyModules,
    string[] Routes)
{
    /// <summary>مجوز «مشاهدهٔ بخش» — همان تیکی که در تنظیمات ← نقش‌ها دیده می‌شود.</summary>
    public string ViewPermission => $"{ShopSections.Module}.{Key}.View";

    /// <summary>مجوز «ثبت/ویرایش/حذف در بخش» (فقط برای بخش‌های عملیاتی).</summary>
    public string ManagePermission => $"{ShopSections.Module}.{Key}.Manage";

    /// <summary>نام اکشن‌های همین بخش داخل ماژول <c>Shop</c>.</summary>
    public IEnumerable<string> Actions =>
        Writable ? new[] { $"{Key}.View", $"{Key}.Manage" } : new[] { $"{Key}.View" };
}

/// <summary>
/// ==================== بخش‌های سامانه فروشگاهی ====================
/// منبع واحد حقیقت برای «همهٔ بخش‌های سامانه فروشگاهی»: منوی کناری، نگهبان مسیر صفحات،
/// تعریف مجوزها در RBAC و برچسب‌های فارسی «تنظیمات ← نقش‌ها و دسترسی‌ها» همه از همین
/// فهرست ساخته می‌شوند؛ بنابراین افزودن یک بخش جدید در یک جا انجام می‌شود و در هر سه
/// لایه (منو، صفحه، تنظیمات) هم‌زمان دیده می‌شود.
/// <para>
/// قواعد:
/// </para>
/// <list type="bullet">
///   <item>هر بخش یک مجوز «مشاهده» دارد: <c>Shop.{Key}.View</c></item>
///   <item>بخش‌های عملیاتی یک مجوز «مدیریت» هم دارند: <c>Shop.{Key}.Manage</c></item>
///   <item>
///     مجوزهای فروشگاه و ماژول‌های ERP مستقل از هم هستند.
///   </item>
/// </list>
/// </summary>
public static class ShopSections
{
    /// <summary>نام ماژول در جدول Permissions.</summary>
    public const string Module = "Shop";

    /// <summary>همهٔ بخش‌ها — ترتیب همین فهرست، ترتیب نمایش در تنظیمات است.</summary>
    public static readonly ShopSection[] All =
    [
        new("Dashboard",       "داشبورد عملیاتی (خرید، فروش و تعمیرات)", "bi-bar-chart-line", false,
            Array.Empty<string>(), ["operations/reports/dashboard"]),

        new("Purchase",        "خرید (رسید خرید)",                        "bi-bag-plus",       true,
            Array.Empty<string>(), ["orders/purchase"]),

        new("Sale",            "فروش (فاکتور فروش)",                       "bi-cart3",          true,
            Array.Empty<string>(), ["orders/sale"]),

        new("GoodsIssue",      "بیجک (حوالهٔ تحویل کالا)",                 "bi-box-seam",       true,
            Array.Empty<string>(), ["goods-issues"]),

        new("Repairs",         "تعمیرات",                                  "bi-tools",          true,
            Array.Empty<string>(), ["repairs", "operations/reports/warranty"]),

        new("Expenses",        "هزینه‌ها",                                 "bi-cash-coin",      true,
            Array.Empty<string>(), ["expenses", "operations/reports/expenses"]),

        new("Installments",    "مدیریت اقساط و چک‌ها",                     "bi-wallet2",        true,
            Array.Empty<string>(), ["shop/credit-management"]),

        new("OpeningBalance",  "افتتاح حساب و مانده‌های اولیه",             "bi-box-arrow-in-right", true,
            Array.Empty<string>(), ["shop/opening-balance"]),

        new("Settlement",      "تسویه فاکتورها",                           "bi-cash-stack",     true,
            Array.Empty<string>(), ["trs/vouchers"]),

        new("Kardex",          "کاردکس کالا (تعداد و ریالی)",              "bi-list-ol",        false,
            Array.Empty<string>(), ["operations/reports/kardex-quantity", "operations/reports/kardex-rial", "reports/kardex"]),

        new("OrderReports",    "گزارش خرید و فروش",                        "bi-graph-up-arrow", false,
            Array.Empty<string>(), ["operations/reports/orders/purchase", "operations/reports/orders/sale"]),

        new("PartyStatements", "گزارش طلب و بدهی اشخاص",                   "bi-journal-check",  false,
            Array.Empty<string>(), ["operations/reports/party-statements"]),

        new("Products",        "کالاها",                                   "bi-box-seam",       true,
            Array.Empty<string>(), ["products"]),

        new("Categories",      "گروه‌های کالا",                            "bi-tags",           true,
            Array.Empty<string>(), ["categories"]),

        new("Units",           "واحدهای شمارش",                            "bi-rulers",         true,
            Array.Empty<string>(), ["units"]),

        new("Warehouses",      "انبارها",                                  "bi-building",       true,
            Array.Empty<string>(), ["warehouses"]),

        new("Parties",         "طرف حساب‌ها (مشتری و تأمین‌کننده)",         "bi-people",         true,
            Array.Empty<string>(), ["parties"]),

        new("PrintSettings", "تنظیمات چاپ", "bi-printer", true,
            Array.Empty<string>(), ["shop/print-settings"]),

        new("Pricing",         "قیمت‌گذاری و حداقل سود",                     "bi-tags",           true,
            Array.Empty<string>(), ["shop/pricing"]),

        new("Referrers",       "معرف‌ها",                                  "bi-person-badge",   true,
            Array.Empty<string>(), ["referrers"]),

        new("Banks",           "بانک، صندوق و کارتخوان (POS)",             "bi-wallet2",        true,
            Array.Empty<string>(), ["trs/accounts"]),

        new("SalesVisit",      "فروش و ویزیتوری (سفارش میدانی)",           "bi-person-walking", true,
            Array.Empty<string>(), ["sales/visits", "sales/requests", "sales/approvals", "sales/finance-queue"]),

        new("Guide",           "راهنمای سامانه فروشگاهی",                   "bi-journal-check",  false,
            Array.Empty<string>(), ["shop-guide"])
    ];

    private static readonly Dictionary<string, ShopSection> ByKeyMap =
        All.ToDictionary(s => s.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>همهٔ اکشن‌های ماژول <c>Shop</c> — برای همگام‌سازی جدول Permissions.</summary>
    public static string[] AllActions => All.SelectMany(s => s.Actions).ToArray();

    public static ShopSection? ByKey(string? key) =>
        key is not null && ByKeyMap.TryGetValue(key, out var s) ? s : null;

    /// <summary>اکشن‌هایی که مفهوم «دیدن» دارند — برای ترجمهٔ مجوز قدیمی به مجوز بخش.</summary>
    private static readonly HashSet<string> ViewLikeActions =
        new(StringComparer.OrdinalIgnoreCase) { "Read", "View", "Access", "Export", "Dashboard" };

    public static bool IsViewLike(string action) => ViewLikeActions.Contains(action);

    /// <summary>
    /// بخش مربوط به یک مسیر صفحه (طولانی‌ترین پیشوند برندهٔ می‌شود تا مسیرهای تودرتو
    /// مثل <c>orders/purchase</c> و <c>operations/reports/orders/purchase</c> قاطی نشوند).
    /// ورودی می‌تواند شامل query باشد؛ خروجی <c>null</c> یعنی این مسیر جزو سامانه فروشگاهی نیست.
    /// </summary>
    public static ShopSection? ForRoute(string? relativeUrl)
    {
        if (string.IsNullOrWhiteSpace(relativeUrl)) return null;
        var path = relativeUrl.Split('?')[0].Split('#')[0].Trim().Trim('/').ToLowerInvariant();
        if (path.Length == 0) return null;

        ShopSection? best = null;
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

    /// <summary>Shop permissions never imply ERP module permissions.</summary>
    public static bool AliasAllows(string? requested, string? granted) => false;
}
