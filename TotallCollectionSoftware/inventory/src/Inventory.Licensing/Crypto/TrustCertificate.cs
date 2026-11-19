using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Inventory.Licensing.Crypto;

/// <summary>
/// مجوز اعتماد (Trust Certificate) — سند JSON امضاشده با <b>کلید ریشه</b> که می‌گوید:
/// «کلید <c>keyId</c> با اثر انگشت <c>sha256</c>، برای نصب <c>installationId</c>،
/// از این تاریخ تا آن تاریخ، کلید امضای مجاز است.»
///
/// <para>
/// <b>چرا لازم شد؟</b> تا امروز هر مشتری باید با همان کلیدی لایسنس می‌گرفت که داخل بیلد
/// جاسازی شده بود؛ نتیجه: برای هر مشتری/هر دانلود سورس، دست‌کاری کلید در سورس و انتشار
/// بیلد تازه. با این سند، فقط <b>کلید ریشه</b> باید داخل بیلد باشد؛ کلید هر مشتری در
/// پنل پشتیبان ساخته می‌شود و همین سند را همراه لایسنس می‌گیرد.
/// </para>
///
/// <para>
/// <b>مدل اعتماد:</b> امضای سند فقط با کلیدهایی سنجیده می‌شود که در حلقهٔ <b>جاسازی‌شده</b>
/// (manifest داخل اسمبلی) هستند؛ یعنی زنجیرهٔ اعتماد یک‌سطحی است و کلید نمایندگی
/// نمی‌تواند کلید نمایندگی دیگر بسازد.
/// </para>
///
/// <para>
/// <b>قالب سند:</b> <c>kind=license-trust</c>, <c>version=1</c>. امضا روی متن
/// <see cref="CanonicalPayload"/> زده می‌شود (RS256 / PKCS#1 v1.5 / SHA-256) و
/// <c>publicKeyPem</c> از طریق اثر انگشتش داخل همان متن پوشش داده می‌شود؛ پس تغییر
/// یک بایت از کلید یا هر فیلد دیگر، امضا را می‌شکند.
/// </para>
/// </summary>
public sealed class TrustCertificateDocument
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = TrustCertificate.Kind;
    [JsonPropertyName("version")] public int Version { get; set; } = TrustCertificate.Version;
    [JsonPropertyName("keyId")] public string KeyId { get; set; } = "";
    [JsonPropertyName("algorithm")] public string Algorithm { get; set; } = "RS256";

    /// <summary>اثر انگشت SHA-256 کلید عمومی (hex، روی DER SubjectPublicKeyInfo).</summary>
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";

    [JsonPropertyName("publicKeyPem")] public string PublicKeyPem { get; set; } = "";
    [JsonPropertyName("installationId")] public string InstallationId { get; set; } = "";
    [JsonPropertyName("issuedAtUtc")] public string IssuedAtUtc { get; set; } = "";
    [JsonPropertyName("validUntilUtc")] public string? ValidUntilUtc { get; set; }

    /// <summary>شناسهٔ کلید ریفی که این سند را امضا کرده است.</summary>
    [JsonPropertyName("issuedBy")] public string IssuedBy { get; set; } = "";

    [JsonPropertyName("note")] public string? Note { get; set; }

    /// <summary>امضای base64 روی <see cref="TrustCertificate.CanonicalPayload"/>.</summary>
    [JsonPropertyName("signature")] public string Signature { get; set; } = "";
}

/// <summary>ساخت/سنجش مجوز اعتماد — بدون وابستگی به پنل یا API.</summary>
public static class TrustCertificate
{
    public const string Kind = "license-trust";
    public const int Version = 1;
    public const string Algorithm = "RS256";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// متنِ کانونیکِ امضاشده. ترتیب و قالب فیلدها <b>قرارداد</b> است؛ تغییرش همهٔ
    /// سندهای صادرشده را باطل می‌کند. بدون فاصله، بدون تاریخ محلی، خط‌به‌خط.
    /// </summary>
    public static string CanonicalPayload(TrustCertificateDocument d)
    {
        var alg = string.IsNullOrWhiteSpace(d.Algorithm) ? Algorithm : d.Algorithm.Trim().ToUpperInvariant();
        return string.Join("\n", new[]
        {
            $"{Kind}|{d.Version}",
            $"keyId={d.KeyId.Trim()}",
            $"algorithm={alg}",
            $"sha256={d.Sha256.Trim().ToLowerInvariant()}",
            $"installationId={d.InstallationId.Trim()}",
            $"issuedAtUtc={NormalizeUtc(d.IssuedAtUtc)}",
            $"validUntilUtc={NormalizeUtc(d.ValidUntilUtc)}",
            $"issuedBy={d.IssuedBy.Trim()}"
        });
    }

    /// <summary>
    /// ساخت و امضای سند. <paramref name="rootPrivateKeyPem"/> می‌تواند
    /// <c>PRIVATE KEY</c> (PKCS#8)، <c>RSA PRIVATE KEY</c> (PKCS#1) یا
    /// <c>ENCRYPTED PRIVATE KEY</c> با <paramref name="rootPassword"/> باشد.
    /// </summary>
    public static TrustCertificateDocument Sign(
        string keyId,
        string delegatedPublicKeyPem,
        string installationId,
        string rootKeyId,
        string rootPrivateKeyPem,
        DateTime issuedAtUtc,
        DateTime? validUntilUtc = null,
        string? rootPassword = null,
        string? note = null)
    {
        if (string.IsNullOrWhiteSpace(keyId)) throw new ArgumentException("شناسهٔ کلید نمایندگی لازم است.", nameof(keyId));
        if (string.IsNullOrWhiteSpace(delegatedPublicKeyPem)) throw new ArgumentException("کلید عمومی نمایندگی لازم است.", nameof(delegatedPublicKeyPem));
        if (string.IsNullOrWhiteSpace(installationId)) throw new ArgumentException("شناسهٔ نصب لازم است.", nameof(installationId));
        if (string.IsNullOrWhiteSpace(rootKeyId)) throw new ArgumentException("شناسهٔ کلید ریشه لازم است.", nameof(rootKeyId));

        var fingerprint = PublicKeyRing.Fingerprint(delegatedPublicKeyPem, Algorithm)
                          ?? throw new FormatException("اثر انگشت کلید عمومی نمایندگی محاسبه نشد (PEM نامعتبر است).");

        var doc = new TrustCertificateDocument
        {
            Kind = Kind,
            Version = Version,
            KeyId = keyId.Trim(),
            Algorithm = Algorithm,
            Sha256 = fingerprint,
            PublicKeyPem = delegatedPublicKeyPem.Trim(),
            InstallationId = installationId.Trim(),
            IssuedAtUtc = ToUtcString(issuedAtUtc),
            ValidUntilUtc = validUntilUtc is null ? null : ToUtcString(validUntilUtc.Value),
            IssuedBy = rootKeyId.Trim(),
            Note = note
        };

        var payload = Encoding.UTF8.GetBytes(CanonicalPayload(doc));
        using var rsa = LoadPrivateKey(rootPrivateKeyPem, rootPassword);
        doc.Signature = Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1));
        return doc;
    }

    /// <summary>
    /// سنجش سند. <paramref name="rootLookup"/> فقط باید کلیدهای <b>جاسازی‌شده</b> را
    /// برگرداند (نه کلیدهای نمایندگی)؛ امضای ریشه محل اعتماد است و نباید زنجیره شود.
    /// </summary>
    public static bool TryVerify(
        TrustCertificateDocument doc,
        Func<string, PublicKeyEntry?> rootLookup,
        string? expectedInstallationId,
        DateTime nowUtc,
        out PublicKeyEntry? delegatedKey,
        out string error)
    {
        delegatedKey = null;
        error = "";

        if (doc.Kind != Kind) { error = "این فایل مجوز اعتماد نیست (kind نامناسب)."; return false; }
        if (doc.Version != Version) { error = $"نسخهٔ مجوز اعتماد پشتیبانی نمی‌شود ({doc.Version})."; return false; }
        if (string.IsNullOrWhiteSpace(doc.KeyId)) { error = "مجوز اعتماد شناسهٔ کلید (keyId) ندارد."; return false; }
        if (!string.Equals(doc.Algorithm, Algorithm, StringComparison.OrdinalIgnoreCase))
        { error = $"الگوریتم مجوز اعتماد پشتیبانی نمی‌شود ({doc.Algorithm})."; return false; }
        if (string.IsNullOrWhiteSpace(doc.PublicKeyPem)) { error = "مجوز اعتماد کلید عمومی ندارد."; return false; }
        if (string.IsNullOrWhiteSpace(doc.Signature)) { error = "مجوز اعتماد امضا ندارد."; return false; }

        // ۱) کلید عمومی واقعاً همان است که اثر انگشتش ادعا شده؟
        var actual = PublicKeyRing.Fingerprint(doc.PublicKeyPem, doc.Algorithm);
        if (actual is null || !string.Equals(actual, doc.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            error = $"اثر انگشت کلید «{doc.KeyId}» با محتوای کلید نمی‌خواند؛ سند دست‌کاری شده است.";
            return false;
        }

        // ۲) امضا با کلید ریشه
        var root = rootLookup(doc.IssuedBy);
        if (root is null)
        {
            error = $"مجوز اعتماد را کلید ریشهٔ «{doc.IssuedBy}» امضا کرده که در این نصب شناخته‌شده نیست.";
            return false;
        }

        try
        {
            var payload = Encoding.UTF8.GetBytes(CanonicalPayload(doc));
            using var rsa = KeyImport.RsaFromPem(root.PublicKeyPem);
            if (!rsa.VerifyData(payload, Convert.FromBase64String(doc.Signature), HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1))
            {
                error = $"امضای مجوز اعتماد «{doc.KeyId}» با کلید ریشهٔ «{doc.IssuedBy}» نمی‌خواند.";
                return false;
            }
        }
        catch (Exception ex)
        {
            error = $"سنجش امضای مجوز اعتماد با خطا مواجه شد: {ex.Message}";
            return false;
        }

        // ۳) قید نصب: کلید برای نصب دیگری صادر شده باشد، بی‌فایده است.
        if (string.IsNullOrWhiteSpace(expectedInstallationId))
        {
            error = "شناسهٔ نصب این سیستم مشخص نشد؛ مجوز اعتماد سنجیده نشد.";
            return false;
        }
        if (!string.Equals(doc.InstallationId.Trim(), expectedInstallationId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            error = $"مجوز اعتماد برای نصب دیگری صادر شده است ({doc.InstallationId} ≠ {expectedInstallationId}).";
            return false;
        }

        // ۴) پنجرهٔ اعتبار
        if (!TryParseUtc(doc.IssuedAtUtc, out var issuedAt))
        {
            error = "تاریخ صدور مجوز اعتماد خوانده نشد.";
            return false;
        }
        if (issuedAt > nowUtc.AddDays(1))
        {
            error = $"تاریخ صدور مجوز اعتماد در آینده است ({issuedAt:yyyy-MM-dd}؛ ساعت سیستم را بررسی کنید).";
            return false;
        }
        if (!string.IsNullOrWhiteSpace(doc.ValidUntilUtc))
        {
            if (!TryParseUtc(doc.ValidUntilUtc!, out var validUntil))
            {
                error = "تاریخ پایان اعتبار مجوز اعتماد خوانده نشد.";
                return false;
            }
            if (validUntil < nowUtc)
            {
                error = $"مجوز اعتماد کلید «{doc.KeyId}» در {validUntil:yyyy-MM-dd} منقضی شده است.";
                return false;
            }
        }

        delegatedKey = new PublicKeyEntry
        {
            KeyId = doc.KeyId.Trim(),
            Algorithm = Algorithm,
            PublicKeyPem = doc.PublicKeyPem.Trim(),
            Sha256 = actual
        };
        return true;
    }

    public static TrustCertificateDocument? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<TrustCertificateDocument>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    public static string ToJson(TrustCertificateDocument doc) => JsonSerializer.Serialize(doc, JsonOpts);

    /// <summary>نام استاندارد فایل مجوز اعتماد در پوشهٔ نصب مشتری.</summary>
    public static string FileNameFor(string keyId) =>
        (string.IsNullOrWhiteSpace(keyId) ? "trust" : keyId.Trim().ToLowerInvariant()) + ".trust.json";

    // ---------- کمکی‌ها ----------

    private static string NormalizeUtc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        return TryParseUtc(value, out var dt)
            ? dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
            : value.Trim();
    }

    private static string ToUtcString(DateTime value) =>
        (value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc))
        .ToString("yyyy-MM-ddTHH:mm:ssZ");

    private static bool TryParseUtc(string? value, out DateTime utc)
    {
        utc = default;

        // اندیس‌های غیر UTC هم پذیرفته می‌شوند، ولی همیشه به UTC تبدیل می‌شوند.
        if (DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var dto))
        {
            utc = dto.UtcDateTime;
            return true;
        }
        return false;
    }

    /// <summary>بارگذاری کلید خصوصی (ساده یا رمزدار).</summary>
    internal static RSA LoadPrivateKey(string privateKeyPem, string? password)
    {
        var rsa = RSA.Create();
        var der = KeyImport.DerFromPem(privateKeyPem);
        var isEncrypted = privateKeyPem.Contains("ENCRYPTED PRIVATE KEY", StringComparison.OrdinalIgnoreCase);

        if (isEncrypted)
        {
            if (string.IsNullOrEmpty(password))
                throw new InvalidOperationException(
                    "کلید خصوصی رمزدار است؛ گذرواژهٔ کلید لازم است.");
            rsa.ImportEncryptedPkcs8PrivateKey(Encoding.UTF8.GetBytes(password), der, out _);
        }
        else if (privateKeyPem.Contains("BEGIN RSA PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
        {
            rsa.ImportRSAPrivateKey(der, out _);
        }
        else
        {
            rsa.ImportPkcs8PrivateKey(der, out _);
        }

        return rsa;
    }
}
