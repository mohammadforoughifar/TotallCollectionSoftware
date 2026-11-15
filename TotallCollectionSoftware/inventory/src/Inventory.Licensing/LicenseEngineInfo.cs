namespace Inventory.Licensing;

/// <summary>
/// شناسنامهٔ موتور لایسنس — <b>یک منبع واحد</b> برای نسخه و مرحله.
///
/// <para>
/// چرا لازم است؟ چون محصول دو نسخهٔ ظاهراً یکسان می‌تواند داشته باشد: یکی از سورس
/// مرجع (<c>inventory/src</c>) که لایسنس دارد، و یکی از سورس قدیمی ریشه (<c>src</c>)
/// که ندارد. اگر کسی اشتباهاً دومی را منتشر کند، هیچ خطایی نمی‌بیند و فقط بعداً
/// می‌فهمد که سامانه بدون لایسنس توزیع شده است.
/// </para>
///
/// <para>
/// راه‌حل: <c>/api/health</c> این مقدار را برمی‌گرداند و اسکریپت‌های انتشار/به‌روزرسانی
/// وجود فایل <c>Inventory.Licensing.dll</c> را در خروجی بررسی می‌کنند. هر دو با هم
/// «ناخواسته منتشر شدن نسخهٔ بدون لایسنس» را می‌گیرند.
/// </para>
/// </summary>
public static class LicenseEngineInfo
{
    /// <summary>نسخهٔ معنایی موتور. با هر تغییر رفتار سهمیه/صندلی بالا می‌رود.</summary>
    public const string Version = "2.0";

    /// <summary>مرحلهٔ پیاده‌سازی (۱: موتور و امضا، ۲: سهمیه، ۳: صندلی و JWT، …).</summary>
    public const int Stage = 2;

    /// <summary>مهاجرت‌های اسکیمای لایسنس که این نسخه می‌شناسد (برای تشخیص ناسازگاری).</summary>
    public const int SchemaVersion = 1;

    /// <summary>نام اسمبلی — اسکریپت‌های انتشار همین را جست‌وجو می‌کنند.</summary>
    public const string AssemblyName = "Inventory.Licensing";

    /// <summary>نسخهٔ کامل برای نمایش/لاگ.</summary>
    public static string FullVersion => $"{Version}+stage{Stage}";
}
