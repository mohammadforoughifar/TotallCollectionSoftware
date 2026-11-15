namespace Inventory.Api.Licensing;

/// <summary>
/// مسیرهای فایل لایسنس.
///
/// <para>
/// قاعده: <b>هیچ فایل لایسنسی داخل پوشهٔ انتشار ذخیره نمی‌شود.</b>
/// انتشار این پروژه Single-File است و روی IIS با هر نسخهٔ تازه، پوشهٔ برنامه
/// بازنویسی می‌شود؛ فایلی که آن‌جا باشد با اولین آپدیت از بین می‌رود.
/// مسیر پیش‌فرض: <c>C:\ProgramData\TotallCollection\License</c>.
/// </para>
/// </summary>
public sealed class LicensePaths
{
    private readonly ILogger<LicensePaths> _log;

    public LicensePaths(LicensingOptions options, ILogger<LicensePaths> log)
    {
        _log = log;

        var configured = string.IsNullOrWhiteSpace(options.StorageFolder) ? null : options.StorageFolder!.Trim();
        Root = configured is not null
            ? SafeFullPath(configured)
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData,
                    Environment.SpecialFolderOption.Create),
                "TotallCollection", "License");

        // فال‌بک بی‌صدا عمداً حذف شد (گام ۳-ب): اگر پوشهٔ لایسنس ساخته نشود، برنامه
        // باید با پیام روشن بالا نیاید، نه اینکه فایل لایسنس را کنار پوشهٔ انتشار
        // بسازد و با اولین به‌روزرسانی از دست بدهد. بررسی قابل‌نوشتن بودن در
        // ProgramDataLayout.Ensure انجام می‌شود که در راه‌اندازی صدا زده می‌شود.
        try
        {
            Directory.CreateDirectory(Root);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"[راه‌اندازی متوقف شد] ساخت پوشهٔ لایسنس ممکن نشد: {Root} — {ex.Message}. " +
                "به هویت App Pool دسترسی Modify بدهید یا Licensing:StorageFolder را تنظیم کنید.", ex);
        }

        _log.LogInformation("[License] پوشهٔ نگه‌داری لایسنس: {Root}", Root);
    }

    /// <summary>ریشهٔ نگه‌داری.</summary>
    public string Root { get; }

    /// <summary>فایل شناسهٔ نصب (نسخهٔ پشتیبان دیتابیس).</summary>
    public string InstallationIdFile => Path.Combine(Root, "installation.id");

    /// <summary>پوشهٔ آرشیو فایل‌های لایسنس آپلودشده (نسخهٔ آرشیوی؛ مرجع اصلی دیتابیس است).</summary>
    public string GrantsFolder
    {
        get
        {
            var path = Path.Combine(Root, "grants");
            try { Directory.CreateDirectory(path); } catch { /* بی‌صدا */ }
            return path;
        }
    }

    /// <summary>مسیر کامل یک فایل داخل ریشه — با محافظت در برابر «..» در نام.</summary>
    public string Resolve(string fileName)
    {
        var safe = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safe)) safe = "license.lic";
        return Path.Combine(Root, safe);
    }

    private static string SafeFullPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }
}
