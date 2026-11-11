namespace Inventory.Licensing.Modules;

/// <summary>نوع یال وابستگی.</summary>
public enum DependencyKind
{
    /// <summary>
    /// وابسته «دادهٔ» پیش‌نیاز را در عملیات نوشتن مصرف می‌کند.
    /// رفتار پیش‌فرض سند: ماژول وابسته کارش را ادامه می‌دهد، ولی عملیاتی که روی دادهٔ
    /// پیش‌نیاز می‌نویسد رد می‌شود (مثلاً صدور فاکتور، موجودی «کالا و انبار» را کم می‌کند).
    /// </summary>
    Writes = 0,

    /// <summary>
    /// وابستگی سخت: بدون پیش‌نیاز، خودِ ماژول بی‌معناست (تا امروز استفاده نشده و
    /// عمداً خالی نگه داشته شده تا رفتار پیش‌فرض سند نقض نشود).
    /// </summary>
    Hard = 1
}

/// <summary>یک یال: «ماژول» به «پیش‌نیاز» نیاز دارد.</summary>
public sealed record ModuleDependency(string Module, string Requires, DependencyKind Kind = DependencyKind.Writes, string? Reason = null);

/// <summary>
/// نقشهٔ وابستگی بین ماژول‌ها — <b>ثابت در کد</b>.
///
/// <para>
/// عمداً از appsettings خوانده نمی‌شود: اگر کسی می‌توانست یال‌ها را حذف کند، می‌توانست
/// با منقضی‌کردن «کالا و انبار» باز هم فاکتور صادر کند و محدودیت دور زده می‌شد.
/// تنها راه تغییر، ویرایش همین فایل و انتشار نسخهٔ تازه است.
/// </para>
///
/// <para>
/// قاعدهٔ اجرا (همان چیزی که در سند آمده): اگر پیش‌نیاز <c>ReadOnly</c> یا <c>Disabled</c>
/// شود، ماژول وابسته از کار نمی‌افتد؛ فقط عملیات <b>نوشتن</b> آن با خطای روشن رد می‌شود که
/// بگوید کدام ماژول لایسنس لازم است. خواندن و خروجی گرفتن آزاد می‌ماند.
/// </para>
/// </summary>
public static class ModuleDependencyMap
{
    /// <summary>همهٔ یال‌ها.</summary>
    public static readonly IReadOnlyList<ModuleDependency> All = new List<ModuleDependency>
    {
        new(ModuleCodes.Sales,       ModuleCodes.Catalog,     Reason: "فاکتور، موجودی و قیمت کالا را از کاتالوگ می‌خواند و موجودی را کم می‌کند"),
        new(ModuleCodes.SalesVisit,  ModuleCodes.Catalog,     Reason: "پیش‌سند ویزیتور بر پایهٔ کالا و طرف حساب است"),
        new(ModuleCodes.Warehousing, ModuleCodes.Catalog,     Reason: "سند انبار روی کالا، انبار و واحد کار می‌کند"),
        new(ModuleCodes.Stocktaking, ModuleCodes.Warehousing, Reason: "انبارگردانی، سند اصلاحی انبار می‌سازد"),
        new(ModuleCodes.Stocktaking, ModuleCodes.Catalog,     Reason: "شمارش روی کالای کاتالوگ انجام می‌شود"),
        new(ModuleCodes.Invoicing,   ModuleCodes.Sales,       Reason: "صورتحساب از سند فروش/خرید ساخته می‌شود"),
        new(ModuleCodes.Invoicing,   ModuleCodes.Catalog,     Reason: "سطرهای فاکتور به کالا و طرف حساب اشاره دارند"),
        new(ModuleCodes.Moadian,     ModuleCodes.Invoicing,   Reason: "ارسال مودیان روی صورتحساب ساخته‌شده انجام می‌شود"),
        new(ModuleCodes.Accounting,  ModuleCodes.Catalog,     Reason: "اسناد حسابداری روی طرف حساب و کالا سند می‌خورند"),
        new(ModuleCodes.Treasury,    ModuleCodes.Accounting,  Reason: "دریافت/پرداخت و چک به سند حسابداری وصل می‌شود"),
        new(ModuleCodes.Expenses,    ModuleCodes.Accounting,  Reason: "هزینه، سند حسابداری می‌سازد"),
        new(ModuleCodes.Repairs,     ModuleCodes.Catalog,     Reason: "پذیرش تعمیر روی کالا و مشتری است"),
        new(ModuleCodes.Repairs,     ModuleCodes.Sales,       Reason: "تعویض و گارانتی به فاکتور فروش وصل است"),
        new(ModuleCodes.Projects,    ModuleCodes.Catalog,     Reason: "پروژه به کالا، مشتری و انبار اشاره دارد"),
        new(ModuleCodes.WorkOrders,  ModuleCodes.Letters,     Reason: "دستور کار از نامه ساخته می‌شود و کارتابل نامه را به‌روز می‌کند"),
        new(ModuleCodes.FahamAtt,    ModuleCodes.HrCore,      Reason: "کارکرد و حضور روی پروندهٔ پرسنلی سند می‌خورد"),
        new(ModuleCodes.FahamPay,    ModuleCodes.FahamAtt,    Reason: "حقوق از کارکرد محاسبه می‌شود"),
        new(ModuleCodes.FahamPay,    ModuleCodes.HrCore,      Reason: "فیش حقوقی روی پروندهٔ پرسنلی ساخته می‌شود"),
        new(ModuleCodes.FahamLms,    ModuleCodes.HrCore,      Reason: "دوره و گواهی به پرسنل وصل است"),
        new(ModuleCodes.Ai,          ModuleCodes.Letters,     Reason: "دستیار، نامه‌ها را می‌خواند و خلاصه/پاسخ پیشنهاد می‌دهد"),
        new(ModuleCodes.Ai,          ModuleCodes.DocArchive,  Reason: "دستیار، متن مدارک آرشیو را جست‌وجو می‌کند")
    };

    /// <summary>پیش‌نیازهای مستقیم یک ماژول.</summary>
    public static IReadOnlyList<ModuleDependency> RequiresOf(string? module)
    {
        var key = ModuleCodes.Normalize(module);
        return All.Where(d => string.Equals(d.Module, key, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>ماژول‌هایی که به این ماژول وابسته‌اند (برای نمایش اثر انقضا در ابزار صدور).</summary>
    public static IReadOnlyList<ModuleDependency> DependentsOf(string? module)
    {
        var key = ModuleCodes.Normalize(module);
        return All.Where(d => string.Equals(d.Requires, key, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
