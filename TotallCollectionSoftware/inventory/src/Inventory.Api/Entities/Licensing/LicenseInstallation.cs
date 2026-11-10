using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// هویت این نصب و تنظیمات پایهٔ لایسنس.
///
/// <para>
/// فقط یک ردیف در این جدول هست (Id = 1). شناسهٔ نصب (<see cref="InstallationId"/>) در
/// اولین اجرا ساخته می‌شود و دو جا نگه‌داری می‌شود: همین‌جا و یک نسخهٔ پشتیبان در
/// <c>C:\ProgramData\TotallCollection\License\installation.id</c>.
/// </para>
///
/// <para>
/// چرا دو جا؟ چون اگر مشتری دیتابیس را Restore کند روی سرور تازه، شناسه از دیتابیس
/// می‌آید و لایسنسش معتبر می‌ماند؛ و اگر دیتابیس از نو ساخته شود ولی سرور همان سرور
/// باشد، شناسه از ProgramData برمی‌گردد و لازم نیست لایسنس تازه بگیرد.
/// </para>
/// </summary>
public class LicenseInstallation
{
    /// <summary>همیشه ۱ — تک‌ردیفی.</summary>
    public int Id { get; set; }

    /// <summary>شناسهٔ یکتای نصب (GUID بدون خط تیره).</summary>
    [MaxLength(64)]
    public string InstallationId { get; set; } = "";

    /// <summary>OnPrem یا Saas.</summary>
    [MaxLength(20)]
    public string DeploymentMode { get; set; } = "OnPrem";

    /// <summary>شناسهٔ مستأجر در سرویس SaaS (برای نصب داخلی خالی است).</summary>
    public int? TenantId { get; set; }

    /// <summary>نام مشتری — از آخرین فایل لایسنس یا ورودی دستی.</summary>
    [MaxLength(200)]
    public string? CustomerName { get; set; }

    /// <summary>
    /// مؤلفه‌های ثانویهٔ شناسایی ماشین (نام ماشین، MachineGuid ویندوز، نام سرور SQL)
    /// به‌صورت JSON — مبنای هشدار «جابه‌جایی سرور» در مرحله ۴.
    /// </summary>
    [MaxLength(600)]
    public string? MachineFingerprint { get; set; }

    /// <summary>
    /// پایان مهلت سازگاری برای نصب‌های موجود (فقط یک‌بار ست می‌شود).
    ///
    /// <para>
    /// چرا؟ نسخه‌ای که از قبل روی سرور مشتری اجرا می‌شود، بعد از آپدیت نه لایسنس دارد و نه
    /// شناسهٔ نصب. اگر بی‌درنگ بسته می‌شد، مشتری با یک آپدیت از کار می‌افتاد — که خلاف
    /// قرارداد است. پس در «اولین اجرای نسخهٔ لایسنس‌دار روی یک دیتابیس موجود»، یک مهلت
    /// مشخص ثبت می‌شود (رویداد LICENSE_EXISTING_INSTALL_GRACE) تا فرصت صدور لایسنس باشد.
    /// </para>
    /// </summary>
    public DateTime? GraceUntil { get; set; }

    /// <summary>
    /// آیا نسخهٔ لایسنس‌دار، روز اول این نصب را «نصب موجود» تشخیص داد؟
    /// اسنپ‌شات دادهٔ کسب‌وکار هنگام تشخیص در <see cref="GraceReason"/> می‌آید.
    /// </summary>
    public bool? IsLegacyInstall { get; set; }

    /// <summary>توضیح تشخیص (تعداد کاربران/فاکتورهای موجود در لحظهٔ اولین اجرا).</summary>
    public string? GraceReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
