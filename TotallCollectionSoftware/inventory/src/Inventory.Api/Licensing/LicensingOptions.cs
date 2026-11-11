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

    /// <summary>
    /// ❌ <b>بازنشسته — هیچ اثری ندارد.</b> همیشه ۰ رفتار می‌شود.
    ///
    /// <para>
    /// پیش از این: مهلت سازگاری برای نصب‌های موجود (روز) که یک‌بار در دیتابیس ثبت
    /// می‌شد و برنامه تا آن تاریخ بدون لایسنس و با نوشتنِ آزاد کار می‌کرد.
    /// </para>
    ///
    /// <para>
    /// <b>تصمیم بازبینی چهارم:</b> هیچ نصبی به خودش مهلت نمی‌دهد. جایگزین، «لایسنس پل»
    /// امضاشده است (<c>tools/license-sign.py --bridge</c>) که همان اثر را دارد ولی
    /// <b>امضای ما</b> را دارد و قابل جعل/ریست توسط مشتری نیست.
    /// </para>
    ///
    /// <para>
    /// چرا فیلد حذف نشد؟ چون حذفش از <c>appsettings</c>های موجود خطا می‌دهد و کاربر
    /// فکر می‌کند تنظیمش کار می‌کند. نگه داشتن + ۰ کردن + هشدار در استارتاپ، صادقانه‌تر است.
    /// </para>
    /// </summary>
    [Obsolete("مهلت «نصب موجود» حذف شد؛ از «لایسنس پل» (license-sign.py --bridge) استفاده کنید.")]
    public int ExistingInstallGraceDays { get; set; }

    /// <summary>نسخهٔ نرم‌افزار — در فایل درخواست و مقایسهٔ Build استفاده می‌شود.</summary>
    public string? ProductVersion { get; set; }
}
