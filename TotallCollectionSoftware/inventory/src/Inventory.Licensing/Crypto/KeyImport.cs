using System.Security.Cryptography;
using System.Text;

namespace Inventory.Licensing.Crypto;

/// <summary>کمکی‌های تبدیل PEM ↔ DER (بدون وابستگی به کتابخانهٔ جانبی).</summary>
public static class KeyImport
{
    /// <summary>استخراج بایت‌های DER از یک PEM (هر برچسبی: PUBLIC KEY / PRIVATE KEY / RSA PRIVATE KEY).</summary>
    public static byte[] DerFromPem(string pem)
    {
        if (string.IsNullOrWhiteSpace(pem)) throw new FormatException("محتوای PEM خالی است.");

        var start = pem.IndexOf("-----BEGIN ", StringComparison.Ordinal);
        if (start < 0) throw new FormatException("سرآغاز PEM پیدا نشد.");

        var headerEnd = pem.IndexOf("-----", start + 11, StringComparison.Ordinal);
        if (headerEnd < 0) throw new FormatException("سرآغاز PEM ناقص است.");

        var end = pem.IndexOf("-----END ", headerEnd, StringComparison.Ordinal);
        if (end < 0) throw new FormatException("پایان PEM پیدا نشد.");

        var base64 = pem.Substring(headerEnd + 5, end - headerEnd - 5);
        var cleaned = new StringBuilder(base64.Length);
        foreach (var ch in base64)
            if (!char.IsWhiteSpace(ch)) cleaned.Append(ch);

        return Convert.FromBase64String(cleaned.ToString());
    }

    /// <summary>بارگذاری کلید عمومی RSA از PEM.</summary>
    public static RSA RsaFromPem(string pem)
    {
        var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(DerFromPem(pem), out _);
        return rsa;
    }

    /// <summary>بارگذاری کلید عمومی ECDSA از PEM.</summary>
    public static ECDsa EcdsaFromPem(string pem)
    {
        var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(DerFromPem(pem), out _);
        return ecdsa;
    }

    /// <summary>بارگذاری کلید <b>خصوصی</b> RSA از PEM — فقط برای ابزار صدور و تست.</summary>
    public static RSA RsaPrivateFromPem(string pem)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return rsa;
    }

    /// <summary>بارگذاری کلید <b>خصوصی</b> ECDSA از PEM — فقط برای ابزار صدور و تست.</summary>
    public static ECDsa EcdsaPrivateFromPem(string pem)
    {
        var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(pem);
        return ecdsa;
    }
}

/// <summary>بررسی امضای دیجیتال (فقط کلید عمومی).</summary>
public static class KeyVerify
{
    /// <summary>
    /// بررسی امضا. <paramref name="algorithm"/> فقط RS256 یا ES256 پذیرفته می‌شود؛
    /// هر الگوریتم دیگری رد می‌شود تا از حملهٔ «الگوریتم none» جلوگیری شود.
    /// </summary>
    public static bool Verify(string publicKeyPem, string? algorithm, byte[] payload, byte[] signature, out string error)
    {
        error = "";
        if (payload.Length == 0) { error = "محتوای امضاشده خالی است."; return false; }
        if (signature.Length == 0) { error = "امضای فایل خالی است."; return false; }

        switch ((algorithm ?? "").Trim().ToUpperInvariant())
        {
            case Model.LicenseFormat.AlgorithmRsaSha256:
            {
                var rsa = KeyImport.RsaFromPem(publicKeyPem);
                // تنظیم صریح الگوریتم هش و padding — پارامتر خالی در .NET به معنای «خودت حدس بزن» است
                var ok = rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                if (!ok) error = "امضای فایل لایسنس معتبر نیست (محتوای فایل تغییر کرده یا کلید اشتباه است).";
                return ok;
            }
            case Model.LicenseFormat.AlgorithmEcdsaP256Sha256:
            {
                var ecdsa = KeyImport.EcdsaFromPem(publicKeyPem);

                // ⚠️ دو قالب امضای ECDSA در دنیای واقعی وجود دارد و هر دو را می‌پذیریم:
                //   • DER (X9.62 / RFC 3279) — خروجی openssl و اکثر ابزارهای بیرونی.
                //     برای P-256 حدود ۷۰ تا ۷۲ بایت است.
                //   • P1363 (r‖s با طول ثابت) — پیش‌فرض .NET و قالب JWS.
                //     برای P-256 دقیقاً ۶۴ بایت است.
                // عدم پذیرش DER یعنی هر ابزار امضای خارج از .NET (مثل openssl یا
                // LicenseTool آینده که ممکن است از کتابخانهٔ دیگری استفاده کند) بلااستفاده
                // می‌شد — و این دقیقاً همان چیزی است که تست سازگاری نشان داد.
                var ok = ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256,
                    DSASignatureFormat.Rfc3279DerSequence);

                if (!ok && signature.Length == 64)
                    ok = ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256,
                        DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

                if (!ok) error = "امضای فایل لایسنس معتبر نیست (محتوای فایل تغییر کرده یا کلید اشتباه است).";
                return ok;
            }
            default:
                error = $"الگوریتم امضای «{algorithm}» پشتیبانی نمی‌شود.";
                return false;
        }
    }
}

/// <summary>
/// ساخت امضا — فقط ابزار صدور لایسنس و تست‌ها از آن استفاده می‌کنند.
/// کلید خصوصی هرگز داخل سورس برنامه نیست و از مسیر بیرونی خوانده می‌شود.
/// </summary>
public static class KeySign
{
    /// <summary>امضای بایت‌های payload با کلید خصوصی PEM.</summary>
    public static byte[] Sign(byte[] payload, string privateKeyPem, string algorithm)
    {
        switch ((algorithm ?? "").Trim().ToUpperInvariant())
        {
            case Model.LicenseFormat.AlgorithmRsaSha256:
            {
                using var rsa = KeyImport.RsaPrivateFromPem(privateKeyPem);
                return rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
            case Model.LicenseFormat.AlgorithmEcdsaP256Sha256:
            {
                using var ecdsa = KeyImport.EcdsaPrivateFromPem(privateKeyPem);
                // خروجی در قالب DER/X9.62 — همان قالبی که openssl تولید می‌کند، تا ابزار
                // صدور آینده بتواند مستقل از .NET باشد. (بررسی، هر دو قالب را می‌پذیرد.)
                return ecdsa.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            }
            default:
                throw new ArgumentException($"الگوریتم امضای «{algorithm}» پشتیبانی نمی‌شود.", nameof(algorithm));
        }
    }

    /// <summary>کلید عمومی متناظر با یک کلید خصوصی (برای ساخت manifest و تست).</summary>
    public static string PublicKeyPemOf(string privateKeyPem, string algorithm)
    {
        if (string.Equals(algorithm, Model.LicenseFormat.AlgorithmEcdsaP256Sha256, StringComparison.OrdinalIgnoreCase))
        {
            using var ecdsa = KeyImport.EcdsaPrivateFromPem(privateKeyPem);
            return ecdsa.ExportSubjectPublicKeyInfoPem();
        }

        using var rsa = KeyImport.RsaPrivateFromPem(privateKeyPem);
        return rsa.ExportSubjectPublicKeyInfoPem();
    }
}
