using System.Text;
using Inventory.Licensing.Model;

namespace Inventory.Licensing.Crypto;

/// <summary>نتیجهٔ بازکردن و بررسی یک فایل لایسنس.</summary>
public sealed class LicenseVerificationResult
{
    public bool Ok { get; init; }
    public LicenseDocument? Document { get; init; }
    public SignedLicenseDocument? Envelope { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public string ErrorText => string.Join(" ", Errors);
}

/// <summary>
/// باز کردن فایل لایسنس امضاشده.
///
/// <para>قاعدهٔ سخت: <b>هیچ داده‌ای پیش از تأیید امضا خوانده نمی‌شود.</b>
/// یعنی حتی نسخه، شماره سریال یا InstallationId داخل payload تا وقتی امضا معتبر نشده
/// استفاده نمی‌شوند؛ در غیر این صورت یک فایل دست‌کاری‌شده می‌توانست مثلاً
/// «فقط برای گزارش» خوانده شود.</para>
/// </summary>
public static class LicenseVerifier
{
    /// <summary>حداکثر حجم قابل قبول فایل لایسنس (جلوگیری از فایل غول‌آسا).</summary>
    public const int MaxFileBytes = 256 * 1024;

    /// <summary>بررسی فایل متنی (همان چیزی که کاربر آپلود می‌کند).</summary>
    public static LicenseVerificationResult Verify(string fileText, PublicKeyRing ring)
    {
        if (string.IsNullOrWhiteSpace(fileText))
            return Fail("فایل لایسنس خالی است.");

        if (Encoding.UTF8.GetByteCount(fileText) > MaxFileBytes)
            return Fail("حجم فایل لایسنس بیش از حد مجاز است.");

        SignedLicenseDocument? envelope;
        try
        {
            envelope = LicenseJson.ReadEnvelope(fileText);
        }
        catch (Exception ex)
        {
            return Fail($"ساختار فایل لایسنس خوانده نشد: {ex.Message}");
        }

        if (envelope is null) return Fail("ساختار فایل لایسنس خوانده نشد.");

        var errors = new List<string>();

        if (envelope.EnvelopeVersion != LicenseFormat.CurrentEnvelopeVersion)
            errors.Add($"نسخهٔ پوشش امضا ({envelope.EnvelopeVersion}) پشتیبانی نمی‌شود.");

        byte[] payload;
        byte[] signature;
        try
        {
            payload = Convert.FromBase64String(envelope.Payload);
        }
        catch { return Fail("محتوای امضاشده (payload) کدگذاری Base64 معتبری ندارد."); }

        try
        {
            signature = Convert.FromBase64String(envelope.Signature);
        }
        catch { return Fail("امضای فایل کدگذاری Base64 معتبری ندارد."); }

        // ۱) امضا — پیش از هر تفسیر دیگری
        if (!ring.Verify(envelope.KeyId, payload, signature, envelope.Algorithm, out var signatureError))
            return new LicenseVerificationResult { Ok = false, Envelope = envelope, Errors = new[] { signatureError } };

        // ۲) ساختار سند
        LicenseDocument? document;
        try
        {
            document = LicenseJson.ReadDocument(payload);
        }
        catch (Exception ex)
        {
            return new LicenseVerificationResult { Ok = false, Envelope = envelope, Errors = new[] { $"محتوای امضاشده ساختار معتبری ندارد: {ex.Message}" } };
        }

        if (document is null)
            return new LicenseVerificationResult { Ok = false, Envelope = envelope, Errors = new[] { "محتوای امضاشده خالی است." } };

        // ۳) کلیدِ پوشش باید همان کلید داخل سند باشد (جلوگیری از سردرگمی در چرخش کلید)
        if (!string.IsNullOrWhiteSpace(document.KeyId)
            && !string.Equals(document.KeyId, envelope.KeyId, StringComparison.OrdinalIgnoreCase))
            errors.Add("شناسهٔ کلید داخل سند با شناسهٔ کلید پوشش امضا یکی نیست.");

        // ۴) شناسهٔ سند به آیتم‌ها سرایت می‌کند تا در گزارش‌ها معلوم باشد از کدام فایل آمده‌اند
        foreach (var item in document.Items) item.Serial = document.Serial;

        return new LicenseVerificationResult
        {
            Ok = errors.Count == 0,
            Document = document,
            Envelope = envelope,
            Errors = errors
        };
    }

    private static LicenseVerificationResult Fail(string error)
        => new() { Ok = false, Errors = new[] { error } };
}
