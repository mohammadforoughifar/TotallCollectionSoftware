using System.Security.Cryptography;
using System.Text;

namespace Inventory.Api.Licensing.Services;

/// <summary>
/// HMAC ردیف‌های مصرف سهمیه.
///
/// <para>
/// <b>هدف:</b> کشف دست‌کاری مستقیم در دیتابیس. اگر کسی با SSMS بیاید و
/// <c>LicenseUsages.Used</c> را از ۹۰۰ به ۰ تغییر دهد تا سقف را دور بزند، HMAC نمی‌خواند
/// و رخداد <c>LICENSE_QUOTA_TAMPERED</c> ثبت می‌شود.
/// </para>
///
/// <para>
/// <b>کلید از کجا؟</b> مثل کلید گاوصندوق و کلید اعلان‌ها: یک کلید تصادفی per-installation که
/// در <c>ProgramData\TotallCollection\License</c> ساخته و نگه‌داری می‌شود. کلید هیچ‌جا در
/// مخزن یا appsettings نیست و مشترک بین نصب‌ها هم نیست.
/// </para>
///
/// <para>
/// ⚠️ <b>صداقت فنی — این کنترل DRM نیست:</b> کلید HMAC روی <b>همان ماشین</b> نگه‌داری
/// می‌شود؛ کسی که به فایل سیستم سرور دسترسی کامل دارد می‌تواند هم عدد و هم HMAC را
/// بازسازی کند و هیچ ردی نماند. همین‌طور کسی که سورس را تغییر دهد. آنچه این کنترل
/// می‌گیرد صریحاً این‌هاست:
/// </para>
/// <list type="bullet">
///   <item>ویرایش دستی <c>Used</c>/<c>Limit</c> با SSMS توسط مدیر IT مشتری</item>
///   <item>حذف ردیف‌های مصرف یا دفتر برای شروع از صفر</item>
///   <item>خطای اسکریپت یا برنامه‌نویسی که عدد را دستکاری کند</item>
/// </list>
/// <para>
/// جزئیات کامل و فهرست آنچه کنترل <b>نمی‌شود</b>: <c>docs/LICENSING-SECURITY-LIMITS-FA.md</c>
/// </para>
/// </summary>
public sealed class QuotaIntegrity
{
    private readonly byte[]? _key;
    private readonly ILogger<QuotaIntegrity> _log;

    public QuotaIntegrity(LicensePaths paths, ILogger<QuotaIntegrity> log)
    {
        _log = log;
        _key = TryLoadOrCreateKey(paths);
    }

    /// <summary>آیا کلید در دسترس است؟ (نبود آن مانع کار برنامه نمی‌شود)</summary>
    public bool Enabled => _key is not null;

    /// <summary>HMAC یک ردیف مصرف از روی مقادیر مؤثر آن.</summary>
    public string? ForUsage(string module, string metric, string periodKey, long limit, long used)
        => Compute($"u|{module}|{metric}|{periodKey}|{limit}|{used}");

    /// <summary>
    /// HMAC یک ردیف رزرو — تا کسی نتواند رزروِ کهنه را با تغییر <c>ReservedAt</c>
    /// «تازه» جلوه بدهد یا وضعیتش را بی‌ردپا عوض کند.
    /// </summary>
    public string? ForReservation(string module, string metric, string periodKey, int amount, DateTime reservedAt)
        => Compute($"r|{module}|{metric}|{periodKey}|{amount}|{reservedAt:O}");

    /// <summary>HMAC یک ردیف دفتر مصرف.</summary>
    public string? ForLedger(string module, string metric, string periodKey, int delta,
        string? entityType, string? entityId, DateTime at)
        => Compute($"l|{module}|{metric}|{periodKey}|{delta}|{entityType}|{entityId}|{at:O}");

    /// <summary>مقایسهٔ ثابت‌زمان (جلوگیری از حملات زمان‌سنجی).</summary>
    public bool Matches(string? expected, string? actual)
    {
        if (!Enabled) return true;                       // بدون کلید، بررسی ممکن نیست
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(actual)) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
    }

    private string? Compute(string payload)
    {
        if (_key is null) return null;
        using var hmac = new HMACSHA256(_key);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    private byte[]? TryLoadOrCreateKey(LicensePaths paths)
    {
        try
        {
            var path = Path.Combine(paths.Root, "quota.key");
            if (File.Exists(path))
            {
                var existing = Convert.FromBase64String(File.ReadAllText(path).Trim());
                if (existing.Length >= 32) return existing;
                _log.LogWarning("[License] فایل کلید HMAC سهمیه کوتاه بود؛ کلید تازه ساخته می‌شود.");
            }

            var key = RandomNumberGenerator.GetBytes(32);
            Directory.CreateDirectory(paths.Root);
            var temp = path + ".tmp";
            File.WriteAllText(temp, Convert.ToBase64String(key));
            File.Move(temp, path, overwrite: true);
            _log.LogInformation("[License] کلید HMAC سهمیه ساخته شد: {Path}", path);
            return key;
        }
        catch (Exception ex)
        {
            // بدون کلید، برنامه کار می‌کند؛ فقط بررسی دست‌کاری غیرفعال می‌شود.
            _log.LogWarning(ex, "[License] کلید HMAC سهمیه در دسترس نیست؛ بررسی دست‌کاری مصرف غیرفعال است.");
            return null;
        }
    }
}
