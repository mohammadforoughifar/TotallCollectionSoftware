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

    /// <summary>شناسهٔ کلیدهای قابل استفاده.</summary>
    public IReadOnlyCollection<string> KeyIds => _keys.Keys.ToList();

    /// <summary>خطاهای بارگذاری (کلیدی که اثر انگشتش مطابق manifest نبود، رد می‌شود).</summary>
    public IReadOnlyList<string> LoadErrors => _loadErrors;

    /// <summary>ساخت حلقه از ردیف‌های آماده (برای تست و برای ابزار صدور).</summary>
    public static PublicKeyRing FromEntries(IEnumerable<PublicKeyEntry> entries)
    {
        var ring = new PublicKeyRing();
        foreach (var e in entries)
        {
            if (string.IsNullOrWhiteSpace(e.KeyId)) continue;
            ring._keys[e.KeyId] = e;
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

            ring._keys[entry.KeyId] = new PublicKeyEntry
            {
                KeyId = entry.KeyId,
                Algorithm = entry.Algorithm,
                PublicKeyPem = pem,
                Sha256 = entry.Sha256
            };
        }

        if (ring._keys.Count == 0)
            ring._loadErrors.Add("هیچ کلید عمومی معتبری بارگذاری نشد؛ همهٔ فایل‌های لایسنس نامعتبر خواهند بود.");

        return ring;
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
            error = $"کلید امضای «{keyId}» روی این نصب شناخته‌شده نیست. فایل باید با یکی از کلیدهای معتبر صادر شده باشد.";
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
