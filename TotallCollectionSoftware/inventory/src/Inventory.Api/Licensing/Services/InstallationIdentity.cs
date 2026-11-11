using System.Text.Json;
using Inventory.Api.Data;
using Inventory.Licensing.Abstractions;
using Inventory.Licensing.Model;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Licensing.Services;

/// <summary>تشخیص نوع استقرار در زمان اجرا.</summary>
public static class LicenseDeployment
{
    public const string ConfigKey = "Licensing:DeploymentMode";

    /// <summary>
    /// نوع استقرار این بستهٔ انتشار.
    ///
    /// <para>
    /// در بستهٔ <b>OnPrem</b> همیشه <c>OnPrem</c> برمی‌گردد و هیچ تنظیمی نمی‌تواند آن را
    /// عوض کند؛ چون در نصب داخلی، مرجع «نوع استقرار» فایل امضاشده است، نه
    /// <c>appsettings.json</c>. (طبق تصمیم کارفرما)
    /// </para>
    ///
    /// <para>
    /// در بستهٔ <b>SaaS</b> (کامپایل با ثابت <c>SAAS</c>) پیش‌فرض SaaS است و فقط اگر
    /// پیکربندی صریحاً OnPrem بدهد (برای تحویل نسخهٔ نصب‌شدنی از همان بیلد) عوض می‌شود.
    /// </para>
    /// </summary>
    public static DeploymentKind FromConfiguration(IConfiguration configuration)
    {
#if SAAS
        var raw = configuration[ConfigKey];
        return string.Equals(raw, "OnPrem", StringComparison.OrdinalIgnoreCase)
            ? DeploymentKind.OnPrem
            : DeploymentKind.Saas;
#else
        _ = configuration;
        return DeploymentKind.OnPrem;
#endif
    }
}

/// <summary>
/// شناسهٔ نصب — «اثر انگشت» این سرور و مبنای فعال‌سازی آفلاین.
///
/// <para>
/// مؤلفهٔ اصلی: یک GUID که در اولین اجرا ساخته می‌شود و در دو جا نگه‌داری می‌شود:
/// <list type="number">
/// <item>جدول <c>LicenseInstallations</c> در دیتابیس</item>
/// <item>فایل <c>installation.id</c> در ProgramData</item>
/// </list>
/// اگر مشتری دیتابیس را روی سرور تازه Restore کند، شناسه همراه دیتابیس می‌آید و
/// لایسنسش معتبر می‌ماند. اگر دیتابیس از نو ساخته شود ولی سرور همان سرور باشد،
/// شناسه از فایل برمی‌گردد.
/// </para>
///
/// <para>
/// مؤلفه‌های ثانویه (نام ماشین، نسخهٔ سیستم‌عامل، نام سرور SQL) فقط برای «هشدار
/// جابه‌جایی» نگه‌داری می‌شوند و <b>هرگز</b> تنها مبنای بی‌اعتبار کردن نیستند —
/// وگرنه با جابه‌جایی VM یا تغییر نام سرور، لایسنس سالم مشتری می‌سوخت.
/// </para>
/// </summary>
public sealed class InstallationIdentity : IInstallationIdentity
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly AppDbContext _db;
    private readonly LicensingOptions _options;
    private readonly LicensePaths _paths;
    private readonly DeploymentKind _deployment;
    private readonly ILogger<InstallationIdentity> _log;

    public InstallationIdentity(AppDbContext db, LicensePaths paths, LicensingOptions options,
        IConfiguration configuration, ILogger<InstallationIdentity> log)
    {
        _db = db;
        _options = options;
        _paths = paths;
        _deployment = LicenseDeployment.FromConfiguration(configuration);
        _log = log;
    }

    /// <summary>شناسهٔ نصب؛ در نبود ردیف/فایل، یک‌بار ساخته می‌شود.</summary>
    public async Task<string> GetInstallationIdAsync(CancellationToken cancellationToken = default)
        => (await EnsureAsync(cancellationToken)).InstallationId;

    /// <summary>نام مشتریِ ثبت‌شده در نصب (برای پیش‌پر کردن فایل درخواست).</summary>
    public async Task<string?> GetCustomerNameAsync(CancellationToken cancellationToken = default)
        => (await EnsureAsync(cancellationToken)).CustomerName;

    /// <summary>
    /// ردیف نصب + برچسب «نصب موجود» — <b>فقط برای تشخیص و نمایش</b> (دیگر مهلت نمی‌دهد).
    ///
    /// <para>
    /// ⚠️ <b>تغییر بازبینی چهارم:</b> این متد قبلاً <c>GraceUntil</c> را در دیتابیس
    /// می‌نوشت و برنامه تا آن تاریخ بدون لایسنس (با نوشتنِ آزاد) کار می‌کرد.
    /// تصمیم کارفرما: هیچ نصبی به خودش مهلت نمی‌دهد. پس:
    /// </para>
    /// <list type="bullet">
    ///   <item><c>IsLegacyInstall</c> و <c>GraceReason</c> هنوز ثبت می‌شوند (یک‌بار) —
    ///   برای اینکه بدانیم این نصب از قبل داده داشته، در گزارش و «فایل درخواست» مفید است.</item>
    ///   <item><c>GraceUntil</c> <b>نوشته نمی‌شود</b>. اگر ردیفی از نسخهٔ قبل مقدار داشته
    ///   باشد، عمداً پاک نمی‌شود (تغییر افزایشی، بدون حذف داده) ولی هیچ‌جا خوانده نمی‌شود.</item>
    /// </list>
    ///
    /// <para>
    /// 🟢 <b>جایگزین برای مشتری موجود:</b> «لایسنس پل» —
    /// <c>tools/license-sign.py --bridge --days 30 --installation-id …</c>
    /// که یک Subscription کوتاه با همهٔ ماژول‌های فعلی و بدون سقف صادر می‌کند.
    /// </para>
    /// </summary>
    public async Task<LicenseInstallation> EnsureWithGraceAsync(CancellationToken cancellationToken = default)
    {
        var row = await EnsureAsync(cancellationToken);

        // تصمیم «نصب موجود بودن» فقط یک‌بار و فقط برای برچسب‌گذاری.
        if (row.IsLegacyInstall is null)
        {
            var business = await CountBusinessDataAsync(cancellationToken);

            row.IsLegacyInstall = business.IsLegacy;
            row.GraceReason = business.Describe;

            if (business.IsLegacy)
                _log.LogWarning(
                    "[License] این نصب از قبل داده دارد ({Reason}). مهلت خودکار داده نمی‌شود؛ " +
                    "برای باز ماندن برنامه تا صدور لایسنس، «لایسنس پل» صادر کنید " +
                    "(tools/license-sign.py --bridge).", business.Describe);

            row.UpdatedAt = DateTime.Now;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return row;
    }

    /// <summary>
    /// آیا این دیتابیس «از قبل» دادهٔ واقعی مشتری دارد؟
    ///
    /// <para>
    /// ⚠️ معیار عمداً <b>سند تجاری</b> است، نه کاربر یا شرکت. دلیلش را در اجرای واقعی دیدم:
    /// نصب تازه هم در اولین راه‌اندازی، کاربر «admin»، انبار پیش‌فرض، سال مالی و کدینگ حساب
    /// می‌سازد. اگر «کاربر داریم» معیار بود، هر نصب تازه در بوت دوم خودش را «نصب موجود»
    /// می‌دید و سی روز بی‌لایسنس کار می‌کرد — یعنی لایسنس عملاً بی‌اثر می‌شد.
    /// </para>
    ///
    /// <para>
    /// پس فقط چیزی شمرده می‌شود که seed نمی‌سازد و فقط از کار واقعی مشتری می‌آید:
    /// فاکتور، کالا، نامهٔ وارده/صادره/داخلی.
    /// </para>
    /// </summary>
    private async Task<(bool IsLegacy, string Describe)> CountBusinessDataAsync(CancellationToken cancellationToken)
    {
        try
        {
            var invoices = await _db.FacInvoices.AsNoTracking().CountAsync(cancellationToken);
            var products = await _db.Products.AsNoTracking().CountAsync(cancellationToken);
            var incoming = await _db.IncomingLetters.AsNoTracking().CountAsync(cancellationToken);
            var outgoing = await _db.OutgoingLetters.AsNoTracking().CountAsync(cancellationToken);
            var inner = await _db.InnerLetters.AsNoTracking().CountAsync(cancellationToken);

            var documents = invoices + products + incoming + outgoing + inner;
            var isLegacy = documents > 0;
            var description = $"فاکتور={invoices}، کالا={products}، نامه‌ها={incoming + outgoing + inner}";
            return (isLegacy, description);
        }
        catch (Exception ex)
        {
            // اگر حتی شمارش هم ممکن نشد، محافظه‌کارانه «نصب موجود» فرض نمی‌کنیم —
            // ولی هیچ‌وقت هم برنامه را نمی‌اندازیم.
            _log.LogWarning(ex, "[License] شمارش دادهٔ کسب‌وکار برای تشخیص نصب موجود ناموفق بود.");
            return (false, "تشخیص ممکن نشد");
        }
    }

    /// <summary>ردیف نصب؛ ساخت/همگام‌سازی در صورت نبود.</summary>
    public async Task<LicenseInstallation> EnsureAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            // «تک‌ردیف» بودن با منطق برنامه تضمین می‌شود، نه با Id دستی: روی SQL Server
            // ستون Id، IDENTITY است و درج مقدار صریح، خطای «Cannot insert explicit value»
            // می‌دهد. پس اولین ردیف را می‌خوانیم و در نبودش، خودِ دیتابیس شماره می‌دهد.
            var row = await _db.LicenseInstallations.OrderBy(x => x.Id).FirstOrDefaultAsync(cancellationToken);
            var fileId = ReadFileId();

            if (row is null)
            {
                var id = string.IsNullOrWhiteSpace(fileId) ? NewId() : fileId!;
                row = new LicenseInstallation
                {
                    InstallationId = id,
                    DeploymentMode = _deployment.ToString(),
                    MachineFingerprint = JsonSerializer.Serialize(MachineFingerprint.Capture()),
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
                _db.LicenseInstallations.Add(row);
                await _db.SaveChangesAsync(cancellationToken);
                WriteFileId(id);
                _log.LogInformation("[License] شناسهٔ نصب ساخته شد: {Id}", id);
                return row;
            }

            var changed = false;

            if (string.IsNullOrWhiteSpace(row.InstallationId))
            {
                row.InstallationId = string.IsNullOrWhiteSpace(fileId) ? NewId() : fileId!;
                changed = true;
            }
            else if (string.IsNullOrWhiteSpace(fileId))
            {
                // فایل پشتیبان پاک شده — بازنویسی می‌شود (دیتابیس مرجع است).
                WriteFileId(row.InstallationId);
            }
            else if (!string.Equals(fileId, row.InstallationId, StringComparison.OrdinalIgnoreCase))
            {
                // ⚠️ تعارض: دیتابیس و فایل دو شناسهٔ مختلف دارند.
                // دیتابیس مرجع است (چون Restore از این مسیر می‌آید) و فایل بازنویسی می‌شود؛
                // ولی در لاگ می‌ماند تا در مرحله ۴ علت بررسی شود.
                _log.LogWarning("[License] تعارض شناسهٔ نصب: دیتابیس={Db} فایل={File} — دیتابیس مرجع است.", row.InstallationId, fileId);
                WriteFileId(row.InstallationId);
            }

            if (string.IsNullOrWhiteSpace(row.MachineFingerprint))
            {
                row.MachineFingerprint = JsonSerializer.Serialize(MachineFingerprint.Capture());
                changed = true;
            }

            if (changed)
            {
                row.UpdatedAt = DateTime.Now;
                await _db.SaveChangesAsync(cancellationToken);
            }

            return row;
        }
        finally { Gate.Release(); }
    }

    // ==================== کمکی‌ها ====================

    private static string NewId() => Guid.NewGuid().ToString("N");

    private string? ReadFileId()
    {
        try
        {
            if (!File.Exists(_paths.InstallationIdFile)) return null;
            var text = File.ReadAllText(_paths.InstallationIdFile).Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[License] خواندن فایل شناسهٔ نصب ناموفق بود.");
            return null;
        }
    }

    private void WriteFileId(string installationId)
    {
        try
        {
            Directory.CreateDirectory(_paths.Root);
            var temp = _paths.InstallationIdFile + ".tmp";
            File.WriteAllText(temp, installationId);
            File.Move(temp, _paths.InstallationIdFile, overwrite: true);
        }
        catch (Exception ex)
        {
            // نبودِ فایل پشتیبان برنامه را متوقف نمی‌کند؛ دیتابیس مرجع است.
            _log.LogWarning(ex, "[License] نوشتن فایل شناسهٔ نصب ناموفق بود: {Path}", _paths.InstallationIdFile);
        }
    }
}

/// <summary>مؤلفه‌های ثانویهٔ شناسایی ماشین (فقط برای هشدار، نه قفل).</summary>
public static class MachineFingerprint
{
    /// <summary>جمع‌آوری اطلاعات ماشین — هر خطا به null تبدیل می‌شود و برنامه را نمی‌شکند.</summary>
    public static Dictionary<string, string?> Capture()
    {
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["machineName"] = Safe(() => Environment.MachineName),
            ["osVersion"] = Safe(() => Environment.OSVersion.VersionString),
            ["environment"] = Safe(() => Environment.GetEnvironmentVariable("COMPUTERNAME")),
            // MachineGuid ویندوز در مرحله ۴ (با هدف‌گیری صریح ویندوز) اضافه می‌شود.
            ["capturedAt"] = DateTime.UtcNow.ToString("O")
        };
    }

    private static string? Safe(Func<string?> read)
    {
        try { return read(); }
        catch { return null; }
    }
}
