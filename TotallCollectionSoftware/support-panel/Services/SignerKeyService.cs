using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Inventory.Licensing.Crypto;
using LicenseSigner.Data;
using Microsoft.EntityFrameworkCore;

namespace LicenseSigner.Services;

/// <summary>کلید فعال امضا (مسیرها در جدول <see cref="Setting"/> نگه داشته می‌شوند؛ <b>رمز هرگز</b>).</summary>
public sealed record SignerKeyRecord(string KeyId, string PrivateKeyPath, string PublicKeyPath,
    string Fingerprint, DateTime CreatedAtUtc, string Folder);

/// <summary>توصیف کلید فعال پنل (از پیکربندی یا از تنظیمات) — برای ثبت در manifest محصول.</summary>
public sealed record ActiveKeyInfo(bool Exists, string KeyId, string? PublicKeyPath, string? PrivateKeyPath,
    string? Fingerprint, bool Encrypted);

/// <summary>نتیجهٔ بررسی «آیا این رمز به کلید خصوصی فعال می‌خورد؟»</summary>
public sealed record KeyPasswordCheck(bool KeyExists, bool Encrypted, bool Matches, string Message);

/// <summary>نتیجهٔ ساخت جفت‌کلید.</summary>
public sealed record KeyGenResult(bool Ok, string Message, SignerKeyRecord? Key, bool Encrypted, List<string> Warnings)
{
    public static KeyGenResult Fail(string message) => new(false, message, null, false, new List<string>());
}

/// <summary>
/// ساخت جفت‌کلید امضا از داخل پنل (تنظیمات ← کلیدهای امضا) و اتصال خودکار آن به پنل.
/// <para>
/// قواعد امنیتی: کلید خصوصی <b>رمزنگاری‌شده</b> نوشته می‌شود (AES‑256‑CBC/SHA‑256، ۱۰۰٬۰۰۰ تکرار)؛
/// رمز فقط در حافظهٔ همان درخواست است و نه در DB، نه در لاگ، نه در فایل. مسیر و اثر انگشت (که راز نیست)
/// در جدول تنظیمات ذخیره می‌شود تا بعد از ری‌استارت هم پنل همان کلید را بشناسد.
/// </para>
/// </summary>
public sealed partial class SignerKeyService(SupportDbContext db, SignerOptions options, AuditChainService audit,
    ILogger<SignerKeyService> log)
{
    private const string SettingKey = "signer-keys";
    public const int MinPasswordLength = 12;

    /// <summary>
    /// اثر انگشت <b>کلید رسمی محصول</b> برای یک شناسه (از <c>Keys/manifest.json</c> جاسازی‌شده در
    /// همین بیلد) — یا <c>null</c> اگر آن شناسه در محصول ثبت نشده باشد.
    ///
    /// <para>
    /// چرا لازم است؟ تجربهٔ واقعی: با هر publish (پوشهٔ <c>data</c> تازه ⇒ تنظیمات کلید از دست
    /// می‌رود) پیشنهاد پیش‌فرض فرم «ساخت کلید»، شناسهٔ <c>sup-202611</c> بود و پشتیبان جفت‌کلید
    /// <b>تازه‌ای</b> با همان شناسه می‌ساخت؛ نتیجه: کلید پنل با کلید داخل محصول یکی نبود و هر
    /// فایل امضاشده در نصب مشتری رد می‌شد. این متد همان چیزی است که «کدام کلید رسمی است» را
    /// بدون خط فرمان و بدون رمز نشان می‌دهد.
    /// </para>
    /// </summary>
    public static (string KeyId, string Fingerprint)? OfficialProductKey(string? keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId)) return null;
        try
        {
            var ring = Inventory.Licensing.Crypto.PublicKeyRing.FromEmbedded();
            var entry = ring.Entries.FirstOrDefault(e =>
                string.Equals(e.KeyId, keyId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (entry is null) return null;

            var fingerprint = entry.Sha256;
            if (string.IsNullOrWhiteSpace(fingerprint))
                fingerprint = Inventory.Licensing.Crypto.PublicKeyRing.Fingerprint(entry.PublicKeyPem, entry.Algorithm);

            return string.IsNullOrWhiteSpace(fingerprint) ? null : (entry.KeyId, fingerprint!);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// شناسه‌های کلیدی که <b>در محصول ثبت‌شده‌اند</b> (برای نمایش در فرم ساخت کلید تا کسی
    /// دوباره با همان شناسه کلید تازه نسازد).
    /// </summary>
    public static IReadOnlyList<(string KeyId, string Fingerprint)> OfficialProductKeys()
    {
        try
        {
            return Inventory.Licensing.Crypto.PublicKeyRing.FromEmbedded().Entries
                .Select(e => (e.KeyId, Fingerprint: e.Sha256 ??
                    Inventory.Licensing.Crypto.PublicKeyRing.Fingerprint(e.PublicKeyPem, e.Algorithm) ?? ""))
                .Where(e => e.Fingerprint.Length > 0)
                .ToList();
        }
        catch
        {
            return Array.Empty<(string, string)>();
        }
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{1,39}$")]
    private static partial Regex KeyIdPattern();

    /// <summary>
    /// پوشهٔ پیشنهادی کلیدها: <b>گاوصندوق ثابت بیرون از پوشهٔ برنامه</b>.
    ///
    /// <para>
    /// پیش از این، پوشه زیر <c>bin/&lt;Configuration&gt;/license-keys</c> پیشنهاد می‌شد — یعنی
    /// داخل خروجی بیلد. با هر <c>dotnet clean</c>، هر دانلود تازهٔ سورس یا هر publish، آن پوشه
    /// نیست و اپراتور ناچار می‌شد کلید تازه بسازد؛ نتیجه: شناسه/اثر انگشت تازه و ناهم‌خوانی
    /// همیشگی با کلید نصب مشتری (همین باعث شد کلید رسمی پروژه گم شود).
    /// </para>
    ///
    /// <para>ترتیب انتخاب: متغیر محیطی <c>TOTALL_LICENSE_KEYS</c> ← <c>C:\LicensingKeys</c> (ویندوز)
    /// ← <c>~/.totall-license-keys</c>.</para>
    /// </summary>
    public string SuggestedFolder()
    {
        var fromEnv = Environment.GetEnvironmentVariable("TOTALL_LICENSE_KEYS");
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv.Trim();

        return OperatingSystem.IsWindows()
            ? @"C:\LicensingKeys"
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".totall-license-keys");
    }

    /// <summary>
    /// آیا این مسیر داخل پوشهٔ برنامه یا خروجی بیلد است؟ کلید خصوصیِ آن‌جا با اولین
    /// <c>clean</c>/<c>publish</c> از بین می‌رود.
    /// </summary>
    public static bool IsInsideProgramFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            var full = Path.GetFullPath(path);
            var baseDir = Path.GetFullPath(AppContext.BaseDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var buildRoot = Directory.GetParent(baseDir)?.FullName ?? baseDir;   // …/bin/Debug

            static bool Inside(string candidate, string root) =>
                candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase);

            if (Inside(full, baseDir) || Inside(full, buildRoot)) return true;

            // هر مسیری که یکی از بخش‌هایش bin/obj باشد هم ریسک است (انتشار/کلین آن را پاک می‌کند).
            var parts = full.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return parts.Any(p => string.Equals(p, "bin", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(p, "obj", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    public async Task<SignerKeyRecord?> LoadAsync(CancellationToken ct = default)
    {
        var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == SettingKey, ct);
        if (row is null || string.IsNullOrWhiteSpace(row.Value)) return null;
        try
        {
            return JsonSerializer.Deserialize<SignerKeyRecord>(row.Value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// کلید ذخیره‌شده را روی گزینه‌های پنل اعمال می‌کند (اگر هر دو فایل وجود داشته باشند).
    /// در شروع برنامه صدا زده می‌شود تا نیازی به ست‌کردن دستی env نباشد.
    /// </summary>
    public async Task<bool> ApplySavedAsync(CancellationToken ct = default)
    {
        var record = await LoadAsync(ct);
        if (record is null) return false;
        if (!File.Exists(record.PrivateKeyPath) || !File.Exists(record.PublicKeyPath))
        {
            log.LogWarning("کلید ثبت‌شده در تنظیمات روی دیسک نیست ({KeyId}) — به پیکربندی محیطی برمی‌گردیم.", record.KeyId);
            return false;
        }

        options.KeyId = record.KeyId;
        options.PrivateKeyPath = record.PrivateKeyPath;
        options.PublicKeyPath = record.PublicKeyPath;
        log.LogInformation("کلید امضا از تنظیمات پنل بارگذاری شد ({KeyId}).", record.KeyId);
        return true;
    }

    /// <summary>ساخت جفت‌کلید جدید و اتصال آن به پنل. رمز در هیچ‌جا نوشته نمی‌شود.</summary>
    public async Task<KeyGenResult> CreateAsync(string keyId, string folder, string password, bool overwrite,
        SupportUser actor, CancellationToken ct = default)
    {
        keyId = (keyId ?? "").Trim();
        if (!KeyIdPattern().IsMatch(keyId))
            return KeyGenResult.Fail("شناسهٔ کلید نامعتبر است؛ ۲ تا ۴۰ کاراکتر از حروف/عدد/‏-/_/. و شروع با حرف یا عدد.");

        // ⛔ شناسه‌ای که همین حالا در manifest محصول است، مالِ کلید رسمی است. ساختن جفت‌کلید
        // تازه با همان شناسه، اثر انگشت را عوض می‌کند و هر فایل امضاشده در نصب مشتری رد می‌شود
        // (همان خطای «کلید امضا با کلیدهای جاسازی‌شده یکی نیست»). پس اجازه نمی‌دهیم.
        if (OfficialProductKey(keyId) is { } official)
        {
            var expected = official.Fingerprint.Length > 16 ? official.Fingerprint[..16] + "…" : official.Fingerprint;
            return KeyGenResult.Fail(
                $"شناسهٔ «{official.KeyId}» در همین بیلد محصول ثبت شده و اثر انگشت کلید رسمی‌اش {expected} است. " +
                "ساختن جفت‌کلید تازه با همین شناسه یعنی کلید پنل با کلید داخل محصول یکی نیست و " +
                "هر فایل امضاشده در نصب مشتری رد می‌شود. " +
                "کلید خصوصیِ رسمی خود را وصل کنید (Signer:PrivateKeyPath یا «اتصال کلید موجود»)، " +
                "یا اگر واقعاً کلید عوض شده، برای کلید تازه یک شناسهٔ تازه بدهید، «ثبت در manifest» را بزنید " +
                "و نسخهٔ تازهٔ محصول را منتشر کنید.");
        }

        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            return KeyGenResult.Fail($"برای کلید خصوصی رمز لازم است (حداقل {MinPasswordLength} کاراکتر). " +
                                     "رمز فقط در حافظه استفاده می‌شود و ذخیره نمی‌شود.");

        folder = (folder ?? "").Trim();
        if (folder.Length == 0) folder = SuggestedFolder();
        try
        {
            folder = Path.IsPathRooted(folder) ? Path.GetFullPath(folder) : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, folder));
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            return KeyGenResult.Fail($"ساخت پوشهٔ کلیدها ممکن نشد: {ex.Message}");
        }

        if (folder.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            return KeyGenResult.Fail("پوشهٔ کلیدها داخل مخزن Git است؛ پوشه‌ای بیرون از مخزن انتخاب کنید.");

        var privatePath = Path.Combine(folder, $"{keyId}.private.pem");
        var publicPath = Path.Combine(folder, $"{keyId}.pem");
        if (!overwrite && (File.Exists(privatePath) || File.Exists(publicPath)))
            return KeyGenResult.Fail("کلیدی با این شناسه در آن پوشه هست؛ شناسهٔ دیگر بدهید یا «بازنویسی» را بزنید.");

        var warnings = new List<string>();
        string fingerprint;
        try
        {
            using var rsa = RSA.Create(3072);
            var publicPem = rsa.ExportSubjectPublicKeyInfoPem();
            fingerprint = Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
            var privatePem = rsa.ExportEncryptedPkcs8PrivateKeyPem(password,
                new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 100_000));

            await File.WriteAllTextAsync(privatePath, privatePem, ct);
            Restrict(privatePath, isPrivate: true);
            await File.WriteAllTextAsync(publicPath, publicPem, ct);
            Restrict(publicPath, isPrivate: false);
        }
        catch (Exception ex)
        {
            return KeyGenResult.Fail($"ساخت جفت‌کلید ناموفق بود: {ex.Message}");
        }

        var record = new SignerKeyRecord(keyId, privatePath, publicPath, fingerprint, DateTime.UtcNow, folder);

        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == SettingKey, ct);
        var json = JsonSerializer.Serialize(record, Json);
        if (row is null) db.Settings.Add(new Setting { Key = SettingKey, Value = json, UpdatedAt = DateTime.UtcNow });
        else
        {
            row.Value = json;
            row.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);

        // اتصال فوری (بدون ری‌استارت) + ثبت در زنجیرهٔ ممیزی (بدون رمز).
        options.KeyId = record.KeyId;
        options.PrivateKeyPath = record.PrivateKeyPath;
        options.PublicKeyPath = record.PublicKeyPath;

        await audit.AppendAsync(actor.UserName, "SIGNER_KEY_CREATED", "Setting", SettingKey,
            JsonSerializer.Serialize(new { record.KeyId, record.Fingerprint, Folder = record.Folder, Encrypted = true }),
            actor.Id, ct);

        warnings.Add("این کلید برای «امضا کردن» است: رمز را هنگام امضا در فرم وارد کنید یا متغیر محیطی " +
                     $"{options.KeyPasswordEnvironmentVariable} را روی همین ماشین ست کنید.");
        warnings.Add("اگر این کلید، کلید رسمی محصول است، کلید عمومی + اثر انگشت sha256 باید در Keys/manifest.json " +
                     "نرم‌افزار مشتری با debugOnly=false ثبت و نسخهٔ Release دوباره ساخته شود.");
        warnings.Add("از کلید خصوصی نسخهٔ پشتیبان آفلاین بگیرید و آن را بیرون از مخزن نگه دارید.");

        return new KeyGenResult(true, "جفت‌کلید ساخته و به پنل وصل شد.", record, true, warnings);
    }

    /// <summary>
    /// کلید فعال را توصیف می‌کند (شناسه، مسیرها، اثر انگشت sha256 و رمزدار بودن).
    /// مستقل از این است که کلید از صفحهٔ تنظیمات ساخته شده باشد یا از پیکربندی محیطی آمده باشد؛
    /// همین باعث می‌شود قطعهٔ manifest در هر دو حالت نمایش داده شود.
    /// </summary>
    public ActiveKeyInfo DescribeActiveKey()
    {
        var publicPath = options.PublicKeyPath;
        var privatePath = options.PrivateKeyPath;
        var keyId = string.IsNullOrWhiteSpace(options.KeyId)
            ? (string.IsNullOrWhiteSpace(publicPath) ? "" : Path.GetFileNameWithoutExtension(publicPath))
            : options.KeyId;

        if (string.IsNullOrWhiteSpace(publicPath) || !File.Exists(publicPath))
            return new ActiveKeyInfo(false, keyId, publicPath, privatePath, null, false);

        string? fingerprint = null;
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(publicPath));
            fingerprint = Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
        }
        catch
        {
            // فایل کلید عمومی خراب/غیرقابل خواندن — اثر انگشت خالی می‌ماند.
        }

        var encrypted = false;
        try
        {
            encrypted = !string.IsNullOrWhiteSpace(privatePath) && File.Exists(privatePath) &&
                        File.ReadAllText(privatePath).Contains("ENCRYPTED PRIVATE KEY", StringComparison.Ordinal);
        }
        catch
        {
            // بی‌صدا
        }

        return new ActiveKeyInfo(true, keyId, publicPath, privatePath, fingerprint, encrypted);
    }

    /// <summary>
    /// نتیجهٔ سنجش «کلید امضا» با «کلیدهای جاسازی‌شده در محصول».
    /// </summary>
    public sealed record ProductKeyCheck(bool Ok, string Headline, List<string> Details,
        string? SignerFingerprint, string? MatchedKeyId, List<(string KeyId, string Fingerprint)> ProductKeys);

    /// <summary>
    /// آیا لایسنسی که این پنل امضا می‌کند، روی نصب مشتری پذیرفته می‌شود؟
    ///
    /// <para>
    /// این همان چیزی است که خطای <c>LICENSE_REJECTED — امضای فایل لایسنس معتبر نیست</c> را
    /// از قبل تشخیص می‌دهد: مقایسهٔ <b>اثر انگشت کلید</b> (نه فقط <c>keyId</c>) بین کلید امضا و
    /// کلیدهای جاسازی‌شده در <c>Inventory.Licensing</c>ِ همین بیلد.
    /// </para>
    /// <para>
    /// اثر انگشت کلید امضا از خود کلید خصوصی مشتق می‌شود (نیازمند رمز، اگر کلید رمزدار باشد)؛
    /// اگر رمز داده نشود، از فایل کلید عمومی خوانده و صریح «کم‌اعتبار» علامت‌گذاری می‌شود.
    /// </para>
    /// </summary>
    public ProductKeyCheck CheckAgainstProductKeys(string? password)
    {
        var details = new List<string>();
        PublicKeyRing ring;
        try
        {
            ring = PublicKeyRing.FromEmbedded();
        }
        catch (Exception ex)
        {
            return new ProductKeyCheck(false, "بررسی ممکن نشد: " + ex.Message, details, null, null, new());
        }

        var product = ring.Entries
            .Select(e => (e.KeyId, Fingerprint: PublicKeyRing.Fingerprint(e.PublicKeyPem, e.Algorithm) ?? "?"))
            .ToList();

        if (product.Count == 0)
            return new ProductKeyCheck(false,
                "در بیلد پنل هیچ کلید تولیدی جاسازی نشده است؛ مقایسه ممکن نیست.", details, null, null, product);

        var info = DescribeActiveKey();
        if (!info.Exists)
            return new ProductKeyCheck(false, "کلید عمومی فعال پیدا نشد؛ ابتدا کلید را بسازید یا وصل کنید.",
                details, null, null, product);

        string? signerFp = null;
        var derivedFromPrivate = false;

        var privatePath = options.PrivateKeyPath;
        if (!string.IsNullOrWhiteSpace(privatePath) && File.Exists(privatePath))
        {
            try
            {
                var pem = File.ReadAllText(privatePath);
                var encrypted = pem.Contains("ENCRYPTED PRIVATE KEY", StringComparison.Ordinal);
                var pass = string.IsNullOrEmpty(password) ? EnvironmentPassword : password;
                using var rsa = RSA.Create();
                if (encrypted)
                {
                    if (string.IsNullOrEmpty(pass))
                    {
                        details.Add("کلید خصوصی رمزدار است و رمزی وارد نشد؛ اثر انگشت از فایل کلید عمومی خوانده شد (اعتبار کمتر).");
                    }
                    else
                    {
                        rsa.ImportFromEncryptedPem(pem, pass);
                        derivedFromPrivate = true;
                    }
                }
                else
                {
                    rsa.ImportFromPem(pem);
                    derivedFromPrivate = true;
                }

                if (derivedFromPrivate)
                    signerFp = Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
            }
            catch (CryptographicException)
            {
                details.Add("رمز کلید خصوصی درست نیست؛ اثر انگشت از فایل کلید عمومی خوانده شد (اعتبار کمتر).");
            }
            catch (Exception ex)
            {
                details.Add("خواندن کلید خصوصی ممکن نشد: " + ex.Message);
            }
        }

        signerFp ??= info.Fingerprint;
        if (signerFp is null)
            return new ProductKeyCheck(false, "اثر انگشت کلید امضا خوانده نشد (فایل کلید عمومی خراب است).",
                details, null, null, product);

        if (!derivedFromPrivate)
            details.Add("مبنای مقایسه: فایل کلید عمومی همین ماشین (نه مشتق از کلید خصوصی).");

        var match = product.FirstOrDefault(k => string.Equals(k.Fingerprint, signerFp, StringComparison.OrdinalIgnoreCase));
        if (match.KeyId is not null)
        {
            details.Add($"کلید امضا با کلید «{match.KeyId}» داخل محصول می‌خواند ⇒ لایسنس‌های صادرشده پذیرفته می‌شوند.");
            return new ProductKeyCheck(true, "✔ کلید امضا با کلید داخل محصول می‌خواند", details, signerFp, match.KeyId, product);
        }

        details.Add("کلید امضا با هیچ کلید جاسازی‌شده‌ای در این بیلد محصول نمی‌خواند؛ فایلی که با این کلید امضا شود روی نصب مشتری با خطای «امضای فایل لایسنس معتبر نیست» رد می‌شود.");
        details.Add("رفع ۱: با کلید خصوصیِ همان جفتِ نصب مشتری امضا کنید (نسخهٔ پشتیبان کلید قبلی).");
        details.Add("رفع ۲: همین کلید عمومی را در Keys/manifest.json محصول ثبت کنید (این صفحه ← «ثبت این کلید در نرم‌افزار مشتری»)، بیلد Release تازه بگیرید و همان نسخه را روی نصب مشتری جایگزین کنید؛ سپس همان فایل .lic را دوباره بارگذاری کنید.");
        return new ProductKeyCheck(false, "❌ کلید امضا با کلید داخل محصول نمی‌خواند", details, signerFp, null, product);
    }

    /// <summary>
    /// بررسی یک فایل لایسنس با «کلیدهای جاسازی‌شده در محصول» — همان چیزی که نصب مشتری انجام می‌دهد.
    /// برای پاسخ به این پرسش که «همین فایلی که مشتری رد کرد، روی یک نصب سالم پذیرفته می‌شود؟»
    /// </summary>
    public (bool Ok, string Headline, List<string> Details) CheckLicenseAgainstProductKeys(string filePath)
    {
        var details = new List<string>();
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return (false, "فایل لایسنس پیدا نشد.", details);

        PublicKeyRing ring;
        try
        {
            ring = PublicKeyRing.FromEmbedded();
        }
        catch (Exception ex)
        {
            return (false, "خواندن کلیدهای محصول ممکن نشد: " + ex.Message, details);
        }

        if (ring.KeyIds.Count == 0)
            return (false, "این بیلد پنل هیچ کلید تولیدی ندارد؛ بررسی ممکن نیست.", details);

        var result = LicenseVerifier.Verify(File.ReadAllText(filePath), ring);
        if (result.Ok && result.Document is not null)
        {
            details.Add($"سریال: {result.Document.Serial} | شناسهٔ نصب: {result.Document.InstallationId ?? "—"}");
            details.Add($"کلیدهای معتبر این بیلد: {string.Join("، ", ring.KeyIds)}");
            return (true, "✔ این فایل روی نصب مشتری پذیرفته می‌شود.", details);
        }

        details.Add("پیام مشتری: " + result.ErrorText);
        var envelopeKeyId = result.Envelope?.KeyId;
        details.Add(envelopeKeyId is null
            ? "شناسهٔ کلید از فایل خوانده نشد (ساختار فایل خراب است)."
            : $"شناسهٔ کلیدِ فایل: {envelopeKeyId} | کلیدهای این بیلد: {string.Join("، ", ring.KeyIds)}");
        return (false, "❌ این فایل روی نصب مشتری هم رد می‌شود.", details);
    }

    /// <summary>رمز موجود در متغیر محیطی پنل (اگر ست شده باشد) — فقط برای مقایسه، نه ذخیره.</summary>
    public string? EnvironmentPassword =>
        Environment.GetEnvironmentVariable(options.KeyPasswordEnvironmentVariable) is { Length: > 0 } value ? value : null;

    /// <summary>
    /// بدون امضا بررسی می‌کند که رمز داده‌شده با کلید خصوصی فعال می‌خواند یا نه.
    /// برای «رمز اشتباه» هیچ پیام رمزنگاری خامی به کاربر نشان داده نمی‌شود.
    /// </summary>
    public KeyPasswordCheck CheckPassword(string? password)
    {
        var path = options.PrivateKeyPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new KeyPasswordCheck(false, false, false, "کلید خصوصی فعال پیدا نشد؛ ابتدا در همین صفحه جفت‌کلید بسازید.");

        string pem;
        try
        {
            pem = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            return new KeyPasswordCheck(true, false, false, $"خواندن فایل کلید ممکن نشد: {ex.Message}");
        }

        var encrypted = pem.Contains("ENCRYPTED PRIVATE KEY", StringComparison.Ordinal);
        if (!encrypted)
            return new KeyPasswordCheck(true, false, true, "این کلید رمزنگاری نشده است؛ رمز لازم نیست (توصیه: کلید رمزدار بسازید).");

        if (string.IsNullOrEmpty(password))
            return new KeyPasswordCheck(true, true, false, "کلید رمزنگاری‌شده است ولی رمزی داده نشد.");

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromEncryptedPem(pem, password);
            return new KeyPasswordCheck(true, true, true, "رمز با کلید خصوصی فعال می‌خواند.");
        }
        catch (CryptographicException)
        {
            return new KeyPasswordCheck(true, true, false, "رمز با کلید خصوصی فعال نمی‌خواند.");
        }
        catch (Exception ex)
        {
            return new KeyPasswordCheck(true, true, false, $"بررسی رمز ممکن نشد: {ex.Message}");
        }
    }

    /// <summary>
    /// ثبت کلید فعال در <c>Keys/manifest.json</c> نرم‌افزار مشتری: کلید عمومی را کنار فایل کپی می‌کند و
    /// ردیف متناظر را (بر اساس <c>keyId</c>) اضافه یا به‌روزرسانی می‌کند. ردیف‌های دیگر دست‌نخورده می‌مانند
    /// تا لایسنس‌های صادرشده با کلیدهای قدیمی معتبر بمانند. نسخهٔ پشتیبان manifest ساخته می‌شود.
    /// </summary>
    public async Task<(bool Ok, string Message)> RegisterInManifestAsync(string keysFolder, SupportUser actor,
        CancellationToken ct = default, bool allowReplace = false)
    {
        var info = DescribeActiveKey();
        if (!info.Exists || info.Fingerprint is not { Length: 64 })
            return (false, "کلید عمومی فعال پیدا نشد یا خوانده نشد؛ ابتدا جفت‌کلید را بسازید/وصل کنید.");
        if (string.IsNullOrWhiteSpace(info.KeyId))
            return (false, "شناسهٔ کلید (Key ID) خالی است؛ آن را در فرم بالا پر کنید.");

        keysFolder = (keysFolder ?? "").Trim().Trim('"');
        if (keysFolder.Length == 0) return (false, "مسیر پوشهٔ Keys محصول را وارد کنید.");

        try
        {
            keysFolder = Path.IsPathRooted(keysFolder)
                ? Path.GetFullPath(keysFolder)
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, keysFolder));
        }
        catch (Exception ex)
        {
            return (false, $"مسیر نامعتبر است: {ex.Message}");
        }

        var manifestPath = Path.Combine(keysFolder, "manifest.json");
        if (!File.Exists(manifestPath))
            return (false, $"در این پوشه manifest.json پیدا نشد: {manifestPath}");

        var publicPath = info.PublicKeyPath!;
        var fileName = Path.GetFileName(publicPath);
        var targetPem = Path.Combine(keysFolder, fileName);

        try
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(manifestPath, ct));
            if (root is not System.Text.Json.Nodes.JsonObject json)
                return (false, "ساختار manifest.json قابل خواندن نیست (ریشه باید شیء JSON باشد).");

            if (json["keys"] is not System.Text.Json.Nodes.JsonArray keys)
            {
                keys = new System.Text.Json.Nodes.JsonArray();
                json["keys"] = keys;
            }

            System.Text.Json.Nodes.JsonObject? existing = null;
            foreach (var node in keys)
            {
                if (node is System.Text.Json.Nodes.JsonObject entry &&
                    string.Equals(entry["keyId"]?.GetValue<string>(), info.KeyId, StringComparison.OrdinalIgnoreCase))
                {
                    existing = entry;
                    break;
                }
            }

            // ⛔ محافظ «کلید منتشرشده»:
            // اگر ردیفی با همین keyId هست ولی کلید عمومی‌اش چیز دیگری است، بی‌صدا جایگزین نمی‌کنیم.
            // یک‌بار همین جایگزینی باعث شد نصب‌های منتشرشده کلید قدیمی داشته باشند و همهٔ
            // لایسنس‌های تازه با «امضای فایل لایسنس معتبر نیست» رد شوند، در حالی که پنل «سبز» بود.
            if (existing is not null && !allowReplace)
            {
                var oldSha = existing["sha256"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(oldSha) &&
                    !string.Equals(oldSha, info.Fingerprint, StringComparison.OrdinalIgnoreCase))
                {
                    return (false,
                        $"ردیف «{info.KeyId}» در manifest هست ولی کلید عمومی‌اش با کلید فعال یکی نیست " +
                        $"(اثر انگشت موجود {oldSha![..12]}… ≠ فعال {info.Fingerprint![..12]}…). " +
                        "جایگزینی یک کلیدِ منتشرشده، لایسنس‌های امضاشده با کلید قبلی را روی نصب‌هایی که هنوز " +
                        "نسخهٔ قدیمی را دارند بی‌اعتبار می‌کند. راه درست: در «ساخت جفت‌کلید» یک شناسهٔ تازه " +
                        "(مثلاً sup-202612) بسازید تا ردیف تازه اضافه شود و ردیف قبلی بماند. " +
                        "اگر مطمئنید هیچ لایسنس فعالی با کلید قبلی وجود ندارد، تیک «جایگزینی ردیف موجود» را بزنید.");
                }
            }

            var action = existing is null ? "افزوده شد" : "به‌روزرسانی شد";
            var entryJson = new System.Text.Json.Nodes.JsonObject
            {
                ["keyId"] = info.KeyId,
                ["algorithm"] = "RS256",
                ["file"] = fileName,
                ["sha256"] = info.Fingerprint,
                ["note"] = "کلید تولیدی — کلید خصوصی بیرون از مخزن",
                ["debugOnly"] = false
            };

            if (existing is null) keys.Add(entryJson);
            else
            {
                var index = keys.IndexOf(existing);
                keys[index] = entryJson;
            }

            var backup = manifestPath + $".bak-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(manifestPath, backup, overwrite: false);

            await File.WriteAllTextAsync(manifestPath,
                json.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
                ct);

            if (!File.Exists(targetPem) || !string.Equals(await File.ReadAllTextAsync(targetPem, ct), await File.ReadAllTextAsync(publicPath, ct), StringComparison.Ordinal))
                File.Copy(publicPath, targetPem, overwrite: true);

            await audit.AppendAsync(actor.UserName, "SIGNER_KEY_REGISTERED", "Setting", SettingKey,
                JsonSerializer.Serialize(new { info.KeyId, info.Fingerprint, KeysFolder = keysFolder, Action = action }),
                actor.Id, ct);

            return (true, $"ردیف کلید «{info.KeyId}» در manifest.json {action} و فایل {fileName} کنار آن کپی شد. " +
                          $"نسخهٔ پشتیبان: {Path.GetFileName(backup)}. اکنون بیلد Release بگیرید و نصب مشتری را به‌روزرسانی کنید.");
        }
        catch (Exception ex)
        {
            return (false, $"ثبت در manifest ناموفق بود: {ex.Message}");
        }
    }

    /// <summary>قطع اتصال از کلید ثبت‌شده (فایل‌ها پاک نمی‌شوند).</summary>
    public async Task DisconnectAsync(SupportUser actor, CancellationToken ct = default)
    {
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == SettingKey, ct);
        if (row is null) return;
        db.Settings.Remove(row);
        await db.SaveChangesAsync(ct);
        options.KeyId = "";
        options.PrivateKeyPath = "";
        options.PublicKeyPath = "";
        await audit.AppendAsync(actor.UserName, "SIGNER_KEY_DISCONNECTED", "Setting", SettingKey, null, actor.Id, ct);
    }

    private static void Restrict(string path, bool isPrivate)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // NTFS: دسترسی به کاربر جاری محدود می‌شود (بدون نیاز به ابزار خارجی).
                var info = new FileInfo(path);
                var security = info.GetAccessControl();
                security.SetAccessRuleProtection(true, false);
                var user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
                security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(user,
                    System.Security.AccessControl.FileSystemRights.FullControl,
                    System.Security.AccessControl.AccessControlType.Allow));
                if (!isPrivate)
                {
                    security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule("Users",
                        System.Security.AccessControl.FileSystemRights.Read,
                        System.Security.AccessControl.AccessControlType.Allow));
                }
                info.SetAccessControl(security);
            }
            else
            {
                File.SetUnixFileMode(path, isPrivate
                    ? UnixFileMode.UserRead | UnixFileMode.UserWrite
                    : UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            }
        }
        catch
        {
            // محدودسازی دسترسی «بهترین تلاش» است؛ شکستش نباید ساخت کلید را خراب کند.
        }
    }
}
