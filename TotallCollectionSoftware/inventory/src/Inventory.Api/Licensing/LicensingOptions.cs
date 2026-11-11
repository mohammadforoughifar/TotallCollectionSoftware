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

    // ==================================================================
    //  مرحله ۳ — صندلی. همهٔ این‌ها «رفتار شمارش» را تنظیم می‌کنند، نه سقف را.
    //  سقف و فهرست معاف فقط از سند امضاشده می‌آید؛ هیچ‌کدام از این کلیدها
    //  نمی‌تواند صندلی اضافه کند. مثل RefundPolicy، هر کدام کفِ کد دارند.
    // ==================================================================

    /// <summary>پیش‌فرض پنجرهٔ بی‌فعالیتی (دقیقه).</summary>
    public const int DefaultIdleMinutes = 15;

    /// <summary>کف پنجرهٔ بی‌فعالیتی — کمتر از ۱۰ دقیقه یعنی «هر تب باز یک صندلی». </summary>
    public const int MinIdleMinutes = 10;

    /// <summary>سقف پنجرهٔ بی‌فعالیتی (دقیقه).</summary>
    public const int MaxIdleMinutes = 240;

    /// <summary>پیش‌فرض عمر کش تصمیم صندلی (ثانیه).</summary>
    public const int DefaultSeatCacheSeconds = 45;

    /// <summary>کف عمر کش — کمتر یعنی هر درخواست یک کوئری.</summary>
    public const int MinSeatCacheSeconds = 15;

    /// <summary>سقف عمر کش — بیشتر یعنی «بیرون‌انداخته‌شده» تا دو دقیقه کار می‌کند.</summary>
    public const int MaxSeatCacheSeconds = 120;

    /// <summary>سیاست پیش‌فرض: کسی را بیرون نینداز (بند ۱۲).</summary>
    public const string SeatEvictionDeny = "deny";

    /// <summary>سیاست اختیاری: قدیمی‌ترین نشست بیکار بیرون برود.</summary>
    public const string SeatEvictionOldestIdle = "evict-oldest-idle";

    /// <summary>پیش‌فرض کف بیکاری برای بیرون‌انداختن (دقیقه).</summary>
    public const int DefaultSeatEvictionMinIdleMinutes = 5;

    /// <summary>کف بیکاری برای بیرون‌انداختن — کمتر از ۵ دقیقه یعنی «کاربر فعال را بینداز بیرون».</summary>
    public const int MinSeatEvictionMinIdleMinutes = 5;

    /// <summary>سقف کف بیکاری برای بیرون‌انداختن (دقیقه).</summary>
    public const int MaxSeatEvictionMinIdleMinutes = 120;

    /// <summary>عمر کش تصمیم صندلی (ثانیه) — کلمپ‌شده در ۱۵..۱۲۰؛ صفر/منفی ⇒ پیش‌فرض.</summary>
    public int SeatCacheSeconds { get; set; } = DefaultSeatCacheSeconds;

    /// <summary>
    /// وقتی ظرفیت «هم‌زمان» پر است: <c>deny</c> (پیش‌فرض) یا <c>evict-oldest-idle</c>.
    /// هر مقدار ناشناخته = <c>deny</c>؛ یعنی «غلط نوشتن» هرگز رفتار تهاجمی نمی‌سازد.
    /// </summary>
    public string? SeatEviction { get; set; } = SeatEvictionDeny;

    /// <summary>کف بی‌فعالیتی برای اینکه کسی «بیکار» و قابل‌بیرون‌انداختن شمرده شود (دقیقه).</summary>
    public int SeatEvictionMinIdleMinutes { get; set; } = DefaultSeatEvictionMinIdleMinutes;

    /// <summary>پنجرهٔ بی‌فعالیتی مؤثر — صفر/منفی ⇒ پیش‌فرض، بیرون بازه ⇒ کلمپ.</summary>
    public int EffectiveIdleMinutes()
        => SessionIdleMinutes <= 0
            ? DefaultIdleMinutes
            : Math.Clamp(SessionIdleMinutes, MinIdleMinutes, MaxIdleMinutes);

    /// <summary>عمر کش مؤثر — صفر/منفی ⇒ پیش‌فرض، بیرون بازه ⇒ کلمپ.</summary>
    public int EffectiveSeatCacheSeconds()
        => SeatCacheSeconds <= 0
            ? DefaultSeatCacheSeconds
            : Math.Clamp(SeatCacheSeconds, MinSeatCacheSeconds, MaxSeatCacheSeconds);

    /// <summary>سیاست بیرون‌انداختن مؤثر — هر مقدار ناشناخته ⇒ <c>deny</c>.</summary>
    public string EffectiveSeatEviction()
        => string.Equals(SeatEviction?.Trim(), SeatEvictionOldestIdle, StringComparison.OrdinalIgnoreCase)
            ? SeatEvictionOldestIdle
            : SeatEvictionDeny;

    /// <summary>کف بیکاری مؤثر برای بیرون‌انداختن — صفر/منفی ⇒ پیش‌فرض، بیرون بازه ⇒ کلمپ.</summary>
    public int EffectiveSeatEvictionMinIdleMinutes()
        => SeatEvictionMinIdleMinutes <= 0
            ? DefaultSeatEvictionMinIdleMinutes
            : Math.Clamp(SeatEvictionMinIdleMinutes, MinSeatEvictionMinIdleMinutes, MaxSeatEvictionMinIdleMinutes);
}
