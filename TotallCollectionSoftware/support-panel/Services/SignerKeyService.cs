using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
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

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{1,39}$")]
    private static partial Regex KeyIdPattern();

    /// <summary>پوشهٔ پیشنهادی کلیدها: بیرون از مخزن، کنار پوشهٔ کاری پنل.</summary>
    public string SuggestedFolder()
    {
        // بیرون از پوشهٔ برنامه/مخزن: آپدیت پنل نباید کلیدها را جابه‌جا یا پاک کند.
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Directory.GetParent(baseDir)?.FullName ?? baseDir;
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
        CancellationToken ct = default)
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
