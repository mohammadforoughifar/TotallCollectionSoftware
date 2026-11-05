namespace Inventory.Shared;

/// <summary>
/// یک «بخش» از سامانه فروشگاهی (هر آیتم منوی سامانه فروشگاهی یک بخش است).
/// </summary>
/// <param name="Key">کلید ثابت و لاتین — بخشی از نام مجوز: <c>Shop.{Key}.View</c></param>
/// <param name="Title">نام فارسی بخش (همان چیزی که در منو و تنظیمات دیده می‌شود)</param>
/// <param name="Icon">آیکن بوت‌استرپ منو</param>
/// <param name="Writable">آیا این بخش عملیات ثبت/ویرایش/حذف دارد؟ (بخش‌های گزارشی فقط «مشاهده» می‌گیرند)</param>
/// <param name="LegacyModules">
/// ماژول‌های مجوزِ «قبلی» که همین بخش را کنترل می‌کردند. برای سازگاری با نصب‌های موجود:
/// اگر نقشی از قبل یکی از این مجوزها را داشته باشد، بخش همچنان باز می‌ماند و لازم نیست
/// مدیر همهٔ نقش‌ها را از نو تنظیم کند.
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
///     مجوز قدیمی هر بخش (<see cref="ShopSection.LegacyModules"/>) همچنان معتبر است تا با
///     ارتقا هیچ کاربری دسترسی‌اش را از دست ندهد.
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
            ["Orders", "Repairs"], ["operations/reports/dashboard"]),

        new("Purchase",        "خرید (رسید خرید)",                        "bi-bag-plus",       true,
            ["Orders"], ["orders/purchase"]),

        new("Sale",            "فروش (فاکتور فروش)",                       "bi-cart3",          true,
            ["Orders"], ["orders/sale"]),

        new("GoodsIssue",      "بیجک (حوالهٔ تحویل کالا)",                 "bi-box-seam",       true,
            ["GoodsIssues"], ["goods-issues"]),

        new("Repairs",         "تعمیرات",                                  "bi-tools",          true,
            ["Repairs"], ["repairs"]),

        new("Expenses",        "هزینه‌ها",                                 "bi-cash-coin",      true,
            ["Expenses"], ["expenses"]),

        new("Installments",    "مدیریت اقساط و چک‌ها",                     "bi-wallet2",        true,
            ["Orders", "TrsVouchers", "TrsCheques"], ["shop/credit-management"]),

        new("OpeningBalance",  "افتتاح حساب و مانده‌های اولیه",             "bi-box-arrow-in-right", true,
            ["Orders", "Stock"], ["shop/opening-balance"]),

        new("Settlement",      "تسویه فاکتورها",                           "bi-cash-stack",     true,
            ["TrsVouchers"], ["trs/vouchers"]),

        new("Kardex",          "کاردکس کالا (تعداد و ریالی)",              "bi-list-ol",        false,
            ["ReportPages"], ["operations/reports/kardex-quantity", "operations/reports/kardex-rial", "reports/kardex"]),

        new("OrderReports",    "گزارش خرید و فروش",                        "bi-graph-up-arrow", false,
            ["Orders"], ["operations/reports/orders/purchase", "operations/reports/orders/sale"]),

        new("PartyStatements", "گزارش طلب و بدهی اشخاص",                   "bi-journal-check",  false,
            ["Orders", "Parties"], ["operations/reports/party-statements"]),

        new("Products",        "کالاها",                                   "bi-box-seam",       true,
            ["Products"], ["products"]),

        new("Categories",      "گروه‌های کالا",                            "bi-tags",           true,
            ["Products"], ["categories"]),

        new("Units",           "واحدهای شمارش",                            "bi-rulers",         true,
            ["Products"], ["units"]),

        new("Warehouses",      "انبارها",                                  "bi-building",       true,
            ["Warehouses"], ["warehouses"]),

        new("Parties",         "طرف حساب‌ها (مشتری و تأمین‌کننده)",         "bi-people",         true,
            ["Parties", "Customers", "Suppliers"], ["parties"]),

        new("Referrers",       "معرف‌ها",                                  "bi-person-badge",   true,
            ["Referrers"], ["referrers"]),

        new("Banks",           "بانک، صندوق و کارتخوان (POS)",             "bi-wallet2",        true,
            ["TrsAccounts"], ["trs/accounts"]),

        new("SalesVisit",      "فروش و ویزیتوری (سفارش میدانی)",           "bi-person-walking", true,
            ["SalesVisit"], ["sales/visits", "sales/requests", "sales/approvals", "sales/finance-queue"]),

        new("Guide",           "راهنمای سامانه فروشگاهی",                   "bi-journal-check",  false,
            ["ShopGuide"], ["shop-guide"])
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

    /// <summary>
    /// آیا مجوز <paramref name="granted"/> (که باید از ماژول Shop باشد) مجوز
    /// <paramref name="requested"/> (مجوز یک ماژول قدیمی) را پوشش می‌دهد؟
    /// <para>
    /// مثال: <c>Shop.Sale.Manage</c> ⇒ <c>Orders.Create/Update/Delete</c> و
    /// <c>Shop.Kardex.View</c> ⇒ <c>ReportPages.Kardex</c>.
    /// </para>
    /// <para>
    /// توجه: مدل «بخش» از ماژول قدیمی بازتر است (همان‌طور که در منو هم دیده می‌شود)؛
    /// یعنی <c>Shop.Sale.Manage</c> مجوزهای عملیاتی ماژول <c>Orders</c> را می‌دهد، ولی
    /// <c>Shop.Sale.View</c> فقط مجوزهای خواندنی را. مشاهدهٔ سود استثنا است و همیشه
    /// به مجوز مستقل <c>Orders.ViewProfit</c> نیاز دارد.
    /// </para>
    /// </summary>
    public static bool AliasAllows(string? requested, string? granted)
    {
        if (string.IsNullOrWhiteSpace(requested) || string.IsNullOrWhiteSpace(granted)) return false;
        if (!granted.StartsWith(Module + ".", StringComparison.OrdinalIgnoreCase)) return false;

        var dot = requested.LastIndexOf('.');
        if (dot <= 0 || dot == requested.Length - 1) return false;
        var module = requested[..dot];
        var action = requested[(dot + 1)..];

        // A section's broad View/Manage permission must not imply this sensitive action,
        // including through read-only sections that also share the legacy Orders module.
        if (module.Equals("Orders", StringComparison.OrdinalIgnoreCase)
            && action.Equals("ViewProfit", StringComparison.OrdinalIgnoreCase)) return false;

        foreach (var section in All)
        {
            var matchesLegacy = section.LegacyModules.Any(m => m.Equals(module, StringComparison.OrdinalIgnoreCase));
            if (!matchesLegacy) continue;

            if (section.Writable && granted.Equals(section.ManagePermission, StringComparison.OrdinalIgnoreCase)) return true;
            // گزارش فقط خواندنی است؛ هیچ مجوز مشاهده‌ای ثبت/ویرایش/حذف نمی‌دهد.
            var kardexRead = section.Key == "Kardex" && module.Equals("ReportPages", StringComparison.OrdinalIgnoreCase)
                && action.Equals("Kardex", StringComparison.OrdinalIgnoreCase);
            if (granted.Equals(section.ViewPermission, StringComparison.OrdinalIgnoreCase)
                && (IsViewLike(action) || kardexRead)) return true;
        }
        return false;
    }
}
