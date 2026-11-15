using Inventory.Licensing.Model;

namespace Inventory.Licensing.Abstractions;

/// <summary>
/// منبع اسناد لایسنس.
///
/// <para>
/// دو پیاده‌سازی دارد و <b>فقط یکی</b> در هر بستهٔ انتشار وجود دارد:
/// <list type="bullet">
/// <item><c>SignedFileLicenseProvider</c> — نصب داخلی (OnPrem): فایل امضاشده‌ای که
/// مدیر آپلود کرده، در دیتابیس نگه‌داری می‌شود و همین‌جا خوانده می‌شود. هیچ بررسی
/// آنلاینی انجام نمی‌شود.</item>
/// <item><c>DatabaseLicenseProvider</c> — سرویس SaaS: مجوز از رکوردهای مرکزی مشتری
/// خوانده می‌شود. این کلاس در بستهٔ OnPrem کامپایل نمی‌شود (‎#if SAAS‎).</item>
/// </list>
/// </para>
/// </summary>
public interface ILicenseProvider
{
    /// <summary>نام فنی Provider برای لاگ و صفحهٔ وضعیت.</summary>
    string Name { get; }

    /// <summary>
    /// همهٔ اسناد معتبرِ قابل اعمال روی این نصب.
    /// سند نامعتبر (امضای خراب/ساختار ناقص) هرگز در خروجی نمی‌آید؛ فقط در گزارش خطا ثبت می‌شود.
    /// </summary>
    Task<IReadOnlyList<LicenseDocument>> LoadDocumentsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// دسترسی به شناسهٔ نصب — برای تولید فایل درخواست و اعتبارسنجی installationId.
/// </summary>
public interface IInstallationIdentity
{
    /// <summary>شناسهٔ نصب (GUID) — یک‌بار ساخته و در دیتابیس + ProgramData نگه‌داری می‌شود.</summary>
    Task<string> GetInstallationIdAsync(CancellationToken cancellationToken = default);

    /// <summary>نام مشتریِ ثبت‌شده در نصب (برای پیش‌پر کردن فایل درخواست).</summary>
    Task<string?> GetCustomerNameAsync(CancellationToken cancellationToken = default);
}
