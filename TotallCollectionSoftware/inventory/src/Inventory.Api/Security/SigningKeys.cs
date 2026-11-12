using System.Security.Cryptography;
using System.Text;

namespace Inventory.Api.Security;

/// <summary>
/// منبع کلیدهای امضای برنامه — <b>کلید JWT و کلید توکن فایل‌های چت</b>.
///
/// <para>
/// <b>چرا این کلاس ساخته شد؟</b> تا پیش از این، کلید امضای JWT یک
/// <c>public const string</c> داخل <c>AuthService</c> بود، یعنی در مخزن و در هر نسخهٔ
/// توزیع‌شده وجود داشت. با آن رشته، هر کسی که یک نسخه از برنامه یا مخزن را داشت
/// می‌توانست برای هر نصب توکن ورود معتبر بسازد. (بازبینی چهارم، بند ۶؛ و بند ۱
/// دستور بعدی کارفرما.)
/// </para>
///
/// <para><b>ترتیب تأمین کلید:</b></para>
/// <list type="number">
///   <item><c>Jwt:Key</c> در پیکربندی (appsettings / متغیر محیطی <c>Jwt__Key</c>) —
///   اگر داده شده باشد، همان استفاده می‌شود؛ ولی <b>حداقل ۳۲ بایت</b> باید باشد وگرنه
///   برنامه بالا نمی‌آید (خطای صریح، نه سکوت).</item>
///   <item>در غیر این صورت، کلید <b>مخصوص همین نصب</b>: یک‌بار با
///   <see cref="RandomNumberGenerator"/> ساخته و در
///   <c>ProgramData/Totall/security/jwt.key</c> نگه‌داری می‌شود (اگر ProgramData
///   قابل‌نوشتن نبود، <c>App_Data/security/jwt.key</c> کنار برنامه).</item>
/// </list>
///
/// <para>
/// <b>چرا فایل و نه دیتابیس؟</b> کلید باید پیش از آمادهٔ‌بودن دیتابیس در دسترس باشد:
/// اعتبارسنجی JWT در اولین درخواست، پیش از هر مهاجرت/به‌روزرسانی، لازم است. فایل
/// ProgramData همان الگویی است که برای کلید AES پیوست‌ها هم پذیرفته شد — و مزیت دوم
/// دارد: اگر نسخهٔ پشتیبان دیتابیس به بیرون برود، کلید امضا همراهش نمی‌رود.
/// </para>
///
/// <para>
/// ⚠️ <b>ارتقای نسخه‌های موجود:</b> با تغییر کلید، همهٔ توکن‌های صادرشده با کلید قدیمی
/// باطل می‌شوند؛ یعنی <b>همهٔ کاربران یک بار دیگر وارد می‌شوند</b>. این عمدی است (کلید
/// قدیمی دیگر معتبر شمرده نمی‌شود) و در راهنمای ارتقا مستند شده است.
/// </para>
/// </summary>
public interface ISigningKeys
{
    /// <summary>کلید امضای JWT (بایت). همیشه ≥ ۳۲ بایت.</summary>
    byte[] JwtKey { get; }

    /// <summary>منبع کلید، برای گزارش/لاگ (هرگز خود کلید چاپ نمی‌شود).</summary>
    string Source { get; }

    /// <summary>
    /// مشتق‌گیری کلید مستقل برای یک کاربرد دیگر (مثل توکن فایل چت) از همان ریشه.
    ///
    /// <para>
    /// چرا مشتق؟ تا توکن چت و JWT <b>یک کلید</b> نداشته باشند؛ اگر روزی یکی لو برود،
    /// دیگری سالم بماند. HKDF-SHA256 با برچسب کاربرد، دقیقاً برای همین است.
    /// </para>
    /// </summary>
    byte[] Derive(string purpose);
}

/// <inheritdoc />
public sealed class SigningKeys : ISigningKeys
{
    /// <summary>حداقل طول کلید ورودی از پیکربندی — ۳۲ بایت = ۲۵۶ بیت، حداقل برای HMAC-SHA256.</summary>
    public const int MinKeyBytes = 32;

    /// <summary>طول کلید تازه‌ساخته‌شده برای همین نصب (۳۸۴ بیت).</summary>
    public const int GeneratedKeyBytes = 48;

    private const string SaltText = "InventoryApp/signing-keys/v1";

    private readonly byte[] _jwtKey;

    private SigningKeys(byte[] jwtKey, string source)
    {
        _jwtKey = jwtKey;
        Source = source;
    }

    public byte[] JwtKey => _jwtKey;

    public string Source { get; }

    public byte[] Derive(string purpose)
    {
        if (string.IsNullOrWhiteSpace(purpose))
            throw new ArgumentException("برچسب کاربرد کلید لازم است.", nameof(purpose));

        return HKDF.DeriveKey(HashAlgorithmName.SHA256, _jwtKey,
            outputLength: 32, salt: Encoding.UTF8.GetBytes(SaltText),
            info: Encoding.UTF8.GetBytes(purpose));
    }

    // ==================== ساخت ====================

    /// <summary>
    /// ساخت از پیکربندی و فایل‌سیستم. در استارتاپ یک‌بار صدا زده می‌شود.
    /// </summary>
    /// <param name="configuredKey">مقدار <c>Jwt:Key</c> (اگر داده شده باشد).</param>
    /// <param name="keyFilePath">مسیر فایل کلید نصب؛ <c>null</c> ⇒ مسیر پیش‌فرض ProgramData/App_Data.</param>
    /// <param name="appDataRoot">ریشهٔ App_Data (برای تست)؛ پیش‌فرض: پوشهٔ برنامه.</param>
    /// <param name="log">لاگ اختیاری — فقط منبع کلید ثبت می‌شود، نه خودش.</param>
    public static SigningKeys Create(string? configuredKey, string? keyFilePath = null,
        string? appDataRoot = null, Action<string>? log = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredKey))
        {
            var bytes = Encoding.UTF8.GetBytes(configuredKey.Trim());
            if (bytes.Length < MinKeyBytes)
                throw new InvalidOperationException(
                    $"کلید «Jwt:Key» باید حداقل {MinKeyBytes} بایت (به ازای هر کاراکتر ASCII یک بایت) باشد؛ " +
                    $"مقدار فعلی {bytes.Length} بایت است. یا کلید بلندتری بدهید یا این تنظیم را حذف کنید " +
                    "تا کلید مخصوص همین نصب به‌صورت خودکار ساخته شود.");
            if (bytes.Length != configuredKey.Trim().Length)
                log?.Invoke("[Security] کلید Jwt:Key کاراکتر غیر-ASCII دارد و UTF-8 چند بایت حساب می‌شود.");

            log?.Invoke("[Security] کلید امضای JWT از پیکربندی («Jwt:Key») خوانده شد.");
            return new SigningKeys(bytes, "configuration:Jwt:Key");
        }

        var path = keyFilePath ?? ResolveDefaultPath(appDataRoot);
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        if (File.Exists(path))
        {
            var text = File.ReadAllText(path).Trim();
            var existing = Convert.FromBase64String(text);
            if (existing.Length < MinKeyBytes)
                throw new InvalidOperationException(
                    $"فایل کلید «{path}» فقط {existing.Length} بایت است؛ حداقل {MinKeyBytes} بایت لازم است. " +
                    "فایل را پاک کنید تا کلید تازه ساخته شود (کاربران باید دوباره وارد شوند).");

            log?.Invoke($"[Security] کلید امضای JWT از فایل همین نصب خوانده شد: {path}");
            return new SigningKeys(existing, $"installation-file:{path}");
        }

        var fresh = RandomNumberGenerator.GetBytes(GeneratedKeyBytes);
        File.WriteAllText(path, Convert.ToBase64String(fresh));

        // روی لینوکس/ویندوز: فقط کاربر اجراکننده دسترسی داشته باشد (در ویندوز ACL پیش‌فرض ProgramData همین است).
        if (!OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
            catch { /* بعضی فایل‌سیستم‌ها پشتیبانی نمی‌کنند */ }
        }

        log?.Invoke($"[Security] کلید امضای JWT برای این نصب ساخته و ذخیره شد: {path} " +
                    "(این فایل را همراه نسخهٔ پشتیبان نگه دارید؛ با پاک‌شدنش همهٔ کاربران دوباره وارد می‌شوند).");
        return new SigningKeys(fresh, $"installation-file:{path}");
    }

    /// <summary>
    /// ساخت از یک کلید داده‌شده — <b>فقط برای تست</b> (تا هر تست کلید خودش را داشته باشد
    /// و به فایل روی دیسک وابسته نباشد).
    /// </summary>
    public static SigningKeys FromKey(byte[] key, string source = "test")
    {
        if (key.Length < MinKeyBytes)
            throw new ArgumentException($"کلید تست باید ≥ {MinKeyBytes} بایت باشد.", nameof(key));
        return new SigningKeys(key.ToArray(), source);
    }

    /// <summary>ساخت از رشته — میان‌بر تست.</summary>
    public static SigningKeys FromSecret(string secret) => FromKey(Encoding.UTF8.GetBytes(secret));

    private static string ResolveDefaultPath(string? appDataRoot)
    {
        try
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (!string.IsNullOrWhiteSpace(programData))
            {
                var dir = Path.Combine(programData, "Totall", "security");
                Directory.CreateDirectory(dir);

                // آزمون نوشتن — سرویس با کاربر محدود ممکن است اجازه نداشته باشد.
                var probe = Path.Combine(dir, ".w");
                File.WriteAllText(probe, "1");
                File.Delete(probe);

                return Path.Combine(dir, "jwt.key");
            }
        }
        catch
        {
            // می‌افتیم روی گزینهٔ بعدی
        }

        var root = appDataRoot ?? AppContext.BaseDirectory;
        return Path.Combine(root, "App_Data", "security", "jwt.key");
    }
}
