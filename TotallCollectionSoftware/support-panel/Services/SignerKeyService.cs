using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using LicenseSigner.Data;
using Microsoft.EntityFrameworkCore;

namespace LicenseSigner.Services;

/// <summary>کلید فعال امضا (مسیرها در جدول <see cref="Setting"/> نگه داشته می‌شوند؛ <b>رمز هرگز</b>).</summary>
public sealed record SignerKeyRecord(string KeyId, string PrivateKeyPath, string PublicKeyPath,
    string Fingerprint, DateTime CreatedAtUtc, string Folder);

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

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{1,39}$")]
    private static partial Regex KeyIdPattern();

    /// <summary>پوشهٔ پیشنهادی کلیدها: بیرون از مخزن، کنار پوشهٔ کاری پنل.</summary>
    public string SuggestedFolder()
    {
        var work = options.EffectiveWorkFolder();
        var parent = Directory.GetParent(work)?.FullName ?? work;
        return Path.Combine(parent, "license-keys");
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
