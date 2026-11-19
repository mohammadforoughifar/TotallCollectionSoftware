using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Inventory.Licensing.Crypto;

/// <summary>یک کلید عمومی در حلقهٔ کلیدها.</summary>
public sealed class PublicKeyEntry
{
    public required string KeyId { get; init; }

    /// <summary>RS256 یا ES256.</summary>
    public required string Algorithm { get; init; }

    /// <summary>کلید عمومی در قالب PEM (SubjectPublicKeyInfo).</summary>
    public required string PublicKeyPem { get; init; }

    /// <summary>اثر انگشت SHA-256 کلید DER (hex) — برای تشخیص جایگزینی/خرابی فایل.</summary>
    public string? Sha256 { get; init; }
}

/// <summary>ردیف manifest.json.</summary>
internal sealed class PublicKeyManifest
{
    [JsonPropertyName("keys")]
    public List<PublicKeyManifestEntry> Keys { get; set; } = new();
}

internal sealed class PublicKeyManifestEntry
{
    [JsonPropertyName("keyId")] public string KeyId { get; set; } = "";
    [JsonPropertyName("algorithm")] public string Algorithm { get; set; } = "RS256";
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("sha256")] public string? Sha256 { get; set; }

    /// <summary>
    /// کلیدِ توسعه — فقط در بیلد <c>Debug</c> بارگذاری می‌شود.
    ///
    /// <para>
    /// <b>چرا لازم شد؟</b> تا پیش از این، کلید عمومی <c>dev-2026a</c> در هر بیلدی — از جمله
    /// نسخه‌ای که به مشتری تحویل داده می‌شود — وجود داشت. یعنی هر کسی که کلید خصوصیِ
    /// توسعه را داشت (روی ماشین توسعه‌دهنده، بیرون از مخزن) می‌توانست برای **هر** نصب
    /// تولیدی، لایسنس معتبر بسازد. حالا بیلد Release این ردیف را حتی نمی‌بیند.
    /// </para>
    /// </summary>
    [JsonPropertyName("debugOnly")] public bool DebugOnly { get; set; }
    [JsonPropertyName("note")] public string? Note { get; set; }
}

/// <summary>
/// حلقهٔ کلیدهای عمومی امضا — نقطهٔ مرکزی اعتماد.
///
/// <para>
/// کلیدها به‌صورت <b>EmbeddedResource</b> داخل همین اسمبلی و از طریق
/// <c>Keys/manifest.json</c> بارگذاری می‌شوند. چرا embedded و نه فایل کنار exe؟
/// چون در انتشار Single-File و در استقرار IIS، فایل‌های جانبی ممکن است جابه‌جا یا
/// عمداً عوض شوند؛ کلید عمومیِ داخل اسمبلی با هر بار ساخت تازه از نو توزیع می‌شود و
/// قابل تعویض محلی نیست. همین نکته جلوی «لایسنس‌سازی با کلید جعلی» را می‌گیرد.
/// </para>
///
/// <para>
/// پشتیبانی از چند کلید از طریق <c>keyId</c> برای چرخش کلید است: کلید تازه اضافه
/// می‌شود، کلید قدیمی حذف نمی‌شود تا لایسنس‌های صادرشدهٔ قبلی معتبر بمانند.
/// </para>
/// </summary>
public sealed class PublicKeyRing
{
    private readonly Dictionary<string, PublicKeyEntry> _keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _loadErrors = new();

    // کلیدهای جاسازی‌شده در اسمبلی — تنها مرجع اعتماد برای سنجش «مجوز اعتماد».
    // عمداً از _keys جدا است: کلید نمایندگی نباید بتواند کلید نمایندگی دیگر بسازد.
    private readonly Dictionary<string, PublicKeyEntry> _embedded = new(StringComparer.OrdinalIgnoreCase);

    // شناسهٔ کلیدهایی که از فایل مجوز اعتماد آمده‌اند (نه از manifest).
    private readonly List<string> _delegated = new();

    /// <summary>شناسهٔ کلیدهای قابل استفاده.</summary>
    public IReadOnlyCollection<string> KeyIds => _keys.Keys.ToList();

    /// <summary>خطاهای بارگذاری (کلیدی که اثر انگشتش مطابق manifest نبود، رد می‌شود).</summary>
    public IReadOnlyList<string> LoadErrors => _loadErrors;

    /// <summary>
    /// کلیدهای بارگذاری‌شده با متن PEM — برای ابزار تشخیص (<c>LicenseTool doctor</c>) که باید
    /// اثر انگشت هر کلید را با کلید امضا مقایسه کند. فقط‌خواندنی؛ کلید عمومی راز نیست.
    /// </summary>
    public IReadOnlyList<PublicKeyEntry> Entries => _keys.Values.ToList();

    /// <summary>
    /// شناسهٔ کلیدهایی که از «مجوز اعتماد» نصب مشتری آمده‌اند (نه از manifest داخل بیلد).
    /// برای نمایش در «وضعیت لایسنس» و عیب‌یابی پشتیبانی.
    /// </summary>
    public IReadOnlyList<string> DelegatedKeyIds => _delegated.ToList();

    /// <summary>کلید ریشهٔ جاسازی‌شده با این شناسه (یا null).</summary>
    public PublicKeyEntry? EmbeddedKey(string keyId) =>
        string.IsNullOrWhiteSpace(keyId) ? null
        : _embedded.TryGetValue(keyId.Trim(), out var entry) ? entry : null;

    /// <summary>ساخت حلقه از ردیف‌های آماده (برای تست و برای ابزار صدور).</summary>
    public static PublicKeyRing FromEntries(IEnumerable<PublicKeyEntry> entries)
    {
        var ring = new PublicKeyRing();
        foreach (var e in entries)
        {
            if (string.IsNullOrWhiteSpace(e.KeyId)) continue;
            ring._keys[e.KeyId] = e;
            ring._embedded[e.KeyId] = e;
        }
        return ring;
    }

    /// <summary>
    /// آیا کلیدی با این پرچم در <b>همین بیلد</b> قابل بارگذاری است؟
    ///
    /// <para>
    /// جدا شد تا تست بتواند بدون ساختن حلقهٔ کامل، قاعده را بسنجد: در بیلد Debug همهٔ
    /// کلیدها مجازند؛ در Release فقط کلیدهایی که <c>debugOnly=false</c> دارند.
    /// (بازبینی چهارم، مورد ۵: بیلد تولیدی نباید کلید توسعه را حتی بشناسد.)
    /// </para>
    /// </summary>
    public static bool IsKeyAllowedInBuild(bool debugOnly)
    {
#if DEBUG
        return true;
#else
        return !debugOnly;
#endif
    }

    /// <summary>ساخت حلقه از منابع Embedded اسمبلی جاری.</summary>
    public static PublicKeyRing FromEmbedded()
    {
        var ring = new PublicKeyRing();
        var assembly = typeof(PublicKeyRing).Assembly;

        var manifestName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("Keys.manifest.json", StringComparison.OrdinalIgnoreCase));
        if (manifestName is null)
        {
            ring._loadErrors.Add("manifest.json کلیدهای عمومی پیدا نشد.");
            return ring;
        }

        PublicKeyManifest? manifest;
        try
        {
            using var stream = assembly.GetManifestResourceStream(manifestName)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            manifest = JsonSerializer.Deserialize<PublicKeyManifest>(reader.ReadToEnd(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            ring._loadErrors.Add($"خواندن manifest کلیدها ناموفق بود: {ex.Message}");
            return ring;
        }

        if (manifest is null) { ring._loadErrors.Add("manifest کلیدها خالی است."); return ring; }

        foreach (var entry in manifest.Keys)
        {
            if (string.IsNullOrWhiteSpace(entry.KeyId) || string.IsNullOrWhiteSpace(entry.File))
            {
                ring._loadErrors.Add("ردیف کلید بدون keyId یا file در manifest نادیده گرفته شد.");
                continue;
            }

            // ⚠️ بیلد Release کلید توسعه را بارگذاری نمی‌کند (بازبینی چهارم، مورد ۵).
            // شرط عمداً روی «DEBUG نبودن» است، نه روی نام فایل: اگر روزی کلید توسعهٔ
            // دومی اضافه شود، همین یک شرط آن را هم می‌بندد.
            if (!IsKeyAllowedInBuild(entry.DebugOnly))
            {
                ring._loadErrors.Add(
                    $"کلید «{entry.KeyId}» فقط مخصوص Debug است و در بیلد Release بارگذاری نشد.");
                continue;
            }

            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("Keys." + entry.File, StringComparison.OrdinalIgnoreCase));
            if (resourceName is null)
            {
                ring._loadErrors.Add($"فایل کلید «{entry.File}» (keyId={entry.KeyId}) داخل اسمبلی پیدا نشد.");
                continue;
            }

            string pem;
            using (var stream = assembly.GetManifestResourceStream(resourceName)!)
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                pem = reader.ReadToEnd();

            // بررسی اثر انگشت: اگر فایل کلید عوض شده باشد، کلید رد می‌شود.
            if (!string.IsNullOrWhiteSpace(entry.Sha256))
            {
                var actual = Fingerprint(pem, entry.Algorithm);
                if (actual is null || !actual.StartsWith(entry.Sha256!, StringComparison.OrdinalIgnoreCase))
                {
                    ring._loadErrors.Add($"اثر انگشت کلید «{entry.KeyId}» با manifest نمی‌خواند؛ کلید نادیده گرفته شد.");
                    continue;
                }
            }

            var loaded = new PublicKeyEntry
            {
                KeyId = entry.KeyId,
                Algorithm = entry.Algorithm,
                PublicKeyPem = pem,
                Sha256 = entry.Sha256
            };
            ring._keys[entry.KeyId] = loaded;
            ring._embedded[entry.KeyId] = loaded;
        }

        if (ring._keys.Count == 0)
            ring._loadErrors.Add("هیچ کلید عمومی معتبری بارگذاری نشد؛ همهٔ فایل‌های لایسنس نامعتبر خواهند بود.");

        return ring;
    }

    /// <summary>
    /// ساخت حلقه از manifest داخل اسمبلی <b>به‌همراه</b> کلیدهای مجازشده با «مجوز اعتماد»
    /// در پوشهٔ نصب مشتری.
    /// </summary>
    /// <param name="trustedKeysFolder">
    /// پوشهٔ <c>*.trust.json</c> (پیش‌فرض در API: <c>…\License\trusted-keys</c>).
    /// خالی/ناموجود ⇒ فقط کلیدهای جاسازی‌شده.
    /// </param>
    /// <param name="installationId">شناسهٔ همین نصب؛ مجوز اعتمادِ نصب دیگر پذیرفته نمی‌شود.</param>
    public static PublicKeyRing FromEmbeddedAndTrusted(string? trustedKeysFolder, string? installationId,
        DateTime? nowUtc = null)
    {
        var ring = FromEmbedded();
        ring.LoadTrustedKeys(trustedKeysFolder, installationId, nowUtc);
        return ring;
    }

    /// <summary>
    /// خواندن و پذیرش مجوزهای اعتمادِ یک پوشه. برای هر فایل، همهٔ شرط‌ها سنجیده می‌شود و
    /// دلیل رد شدن در <see cref="LoadErrors"/> می‌نشیند تا پشتیبانی بتواند بگوید «چرا».
    /// </summary>
    /// <returns>تعداد کلیدهای پذیرفته‌شده.</returns>
    public int LoadTrustedKeys(string? trustedKeysFolder, string? installationId, DateTime? nowUtc = null)
    {
        if (string.IsNullOrWhiteSpace(trustedKeysFolder) || !Directory.Exists(trustedKeysFolder))
        {
            // نبودن پوشه خطا نیست: نصب‌های بدون کلید نمایندگی باید مثل قبل کار کنند.
            return 0;
        }

        var now = (nowUtc ?? DateTime.UtcNow).ToUniversalTime();
        var revoked = ReadRevocations(trustedKeysFolder);
        var accepted = 0;

        foreach (var file in Directory.EnumerateFiles(trustedKeysFolder, "*.trust.json"))
        {
            var name = Path.GetFileName(file);
            try
            {
                var doc = TrustCertificate.Parse(File.ReadAllText(file));
                if (doc is null)
                {
                    _loadErrors.Add($"مجوز اعتماد «{name}» خوانده نشد (JSON نامعتبر).");
                    continue;
                }

                if (!TrustCertificate.TryVerify(doc, EmbeddedKey, installationId, now,
                        out var key, out var error) || key is null)
                {
                    _loadErrors.Add($"مجوز اعتماد «{name}» پذیرفته نشد: {error}");
                    continue;
                }

                // کلید نمایندگی نباید کلید جاسازی‌شده را جعل کند (مثلاً کلید ریشه را بازنویسی کند).
                if (_embedded.ContainsKey(key.KeyId))
                {
                    _loadErrors.Add($"مجوز اعتماد «{name}» کلید «{key.KeyId}» را تعریف می‌کند که " +
                                    "همین حالا داخل بیلد جاسازی شده است؛ نادیده گرفته شد.");
                    continue;
                }

                if (revoked.Contains(key.KeyId) || revoked.Contains(key.Sha256 ?? "")
                    || revoked.Contains(name.ToLowerInvariant()))
                {
                    _loadErrors.Add($"کلید «{key.KeyId}» در فهرست ابطال این نصب است؛ پذیرفته نشد.");
                    continue;
                }

                if (_keys.ContainsKey(key.KeyId))
                {
                    _loadErrors.Add($"کلید «{key.KeyId}» تکراری است؛ نسخهٔ دوم نادیده گرفته شد.");
                    continue;
                }

                _keys[key.KeyId] = key;
                _delegated.Add(key.KeyId);
                accepted++;
            }
            catch (Exception ex)
            {
                _loadErrors.Add($"خواندن مجوز اعتماد «{name}» با خطا مواجه شد: {ex.Message}");
            }
        }

        return accepted;
    }

    /// <summary>فهرست ابطال: فایل <c>revoked.txt</c>، هر خط یک keyId یا اثر انگشت یا نام فایل.</summary>
    private static HashSet<string> ReadRevocations(string folder)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(folder, "revoked.txt");
        if (!File.Exists(path)) return set;

        foreach (var line in File.ReadAllLines(path))
        {
            var text = line.Trim();
            if (text.Length == 0 || text.StartsWith('#')) continue;
            set.Add(text);
        }
        return set;
    }

    /// <summary>اثر انگشت hex کلید عمومی (روی DER).</summary>
    public static string? Fingerprint(string publicKeyPem, string algorithm)
    {
        try
        {
            var der = KeyImport.DerFromPem(publicKeyPem);
            return Convert.ToHexString(SHA256.HashData(der)).ToLowerInvariant();
        }
        catch { return null; }
    }

    /// <summary>بررسی امضا. خطا به فارسی و بدون استثنا برگردانده می‌شود.</summary>
    public bool Verify(string? keyId, byte[] payload, byte[] signature, string? algorithm, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(keyId)) { error = "فایل لایسنس شناسهٔ کلید (keyId) ندارد."; return false; }
        if (!_keys.TryGetValue(keyId!, out var key))
        {
            // پیام عمداً «خودتشخیص» است: بدون این جزئیات، کاربر فقط می‌فهمد «کار نمی‌کند» و
            // نمی‌داند مشکل در فایل لایسنس است یا در نسخهٔ نصب‌شده (کلید در بیلد نیست).
            var known = _keys.Count == 0
                ? "هیچ کلیدی در این بیلد بارگذاری نشد"
                : "کلیدهای شناخته‌شدهٔ این بیلد: " + string.Join("، ", _keys.Keys);

            var details = _loadErrors.Count == 0
                ? ""
                : " | یادداشت بارگذاری: " + string.Join(" ", _loadErrors.Take(3));

            error = $"کلید امضای «{keyId}» روی این نصب شناخته‌شده نیست. {known}{details}. " +
                    "اگر شناسهٔ کلید شما در فهرست بالا نیست، یعنی نسخهٔ نصب‌شده از بیلدی است که کلید شما " +
                    "در آن جاسازی نشده است؛ با بیلد Release تازه جایگزین کنید و همان فایل را دوباره بارگذاری کنید.";
            return false;
        }

        var alg = string.IsNullOrWhiteSpace(algorithm) ? key.Algorithm : algorithm!;
        try
        {
            return KeyVerify.Verify(key.PublicKeyPem, alg, payload, signature, out error);
        }
        catch (Exception ex)
        {
            error = $"بررسی امضا با خطا مواجه شد: {ex.Message}";
            return false;
        }
    }
}
