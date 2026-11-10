namespace Inventory.Api.Licensing;

/// <summary>
/// تنظیمات <b>غیرامنیتی</b> لایسنس.
///
/// <para>
/// ⚠️ قاعدهٔ سخت: هر چیزی که محدودیت لایسنس را کم می‌کند این‌جا نیست و از appsettings هم
/// خوانده نمی‌شود؛ از جمله:
/// <list type="bullet">
/// <item>نقشهٔ وابستگی ماژول‌ها (ثابت در <c>ModuleDependencyMap</c>)</item>
/// <item>معافیت صندلی (فقط از فیلد امضاشدهٔ <c>users.exempt</c>)</item>
/// <item>سقف کاربر و سقف سهمیه (فقط از سند امضاشده)</item>
/// <item>پنجرهٔ ۳۰ روز تا انقضا و مهلت ۱۵ روز تنفس (ثابت در <c>LicenseTimingRules</c>)</item>
/// </list>
/// هر کدام از این‌ها اگر از فایل تنظیمات خوانده می‌شد، با یک ویرایش ساده در
/// <c>appsettings.json</c> دور زده می‌شد.
/// </para>
/// </summary>
public sealed class LicensingOptions
{
    public const string SectionName = "Licensing";

    /// <summary>
    /// بعد از چند دقیقه بی‌فعالیتی، نشست کاربر «بیکار» تلقی و صندلی‌اش آزاد می‌شود.
    /// پیش‌فرض ۱۵ دقیقه.
    /// </summary>
    public int SessionIdleMinutes { get; set; } = 15;

    /// <summary>فاصلهٔ پاک‌سازی نشست‌های بیکار (ثانیه).</summary>
    public int SessionCleanupSeconds { get; set; } = 300;

    /// <summary>
    /// مدت اعتبار کش Entitlement (ثانیه). با هر تغییر لایسنس فوراً باطل می‌شود؛
    /// این مقدار فقط سقف «کهنگی» در حالت عادی است و در امنیت اثری ندارد.
    /// </summary>
    public int EntitlementCacheSeconds { get; set; } = 60;

    /// <summary>
    /// پوشهٔ نگه‌داری فایل لایسنس و شناسهٔ نصب. خالی = مسیر پیش‌فرض
    /// <c>C:\ProgramData\TotallCollection\License</c> (و روی لینوکس
    /// <c>/usr/share/TotallCollection/License</c>).
    /// <para>
    /// عمداً بیرون از پوشهٔ انتشار است: با هر آپدیت، پوشهٔ برنامه بازنویسی می‌شود.
    /// </para>
    /// </summary>
    public string? StorageFolder { get; set; }

    /// <summary>نسخهٔ نرم‌افزار — در فایل درخواست و مقایسهٔ Build استفاده می‌شود.</summary>
    public string? ProductVersion { get; set; }
}
