using System.Text.Json;
using Inventory.Licensing.Crypto;
using Inventory.Licensing.Model;

namespace LicenseSigner.Services;

/// <summary>نتیجهٔ بازبینی یک فایل لایسنس.</summary>
public sealed record LicenseInspection(bool Ok, string? Error, string ItemsText, DateTime? ValidUntil,
    string? Serial, string? InstallationId, string? CustomerName);

/// <summary>
/// بازبینی فایل لایسنس با <b>همان کد مشتری</b> (<see cref="LicenseVerifier"/>) و کلید عمومی
/// پیکربندی‌شده. هیچ داده‌ای پیش از تأیید امضا خوانده نمی‌شود (قاعدهٔ خودِ Verifier).
/// </summary>
public sealed class LicenseInspector(SignerOptions options, ILogger<LicenseInspector> log)
{
    private PublicKeyRing? _ring;

    private PublicKeyRing? Ring()
    {
        if (_ring is not null) return _ring;
        if (string.IsNullOrWhiteSpace(options.PublicKeyPath) || !File.Exists(options.PublicKeyPath))
        {
            log.LogWarning("کلید عمومی برای بازبینی پیدا نشد؛ بازبینی انجام نمی‌شود.");
            return null;
        }

        var pem = File.ReadAllText(options.PublicKeyPath);
        _ring = PublicKeyRing.FromEntries(new[]
        {
            new PublicKeyEntry
            {
                KeyId = string.IsNullOrWhiteSpace(options.KeyId) ? "panel" : options.KeyId,
                Algorithm = LicenseFormat.AlgorithmRsaSha256,
                PublicKeyPem = pem
            }
        });
        return _ring;
    }

    public LicenseInspection Inspect(string? fileText)
    {
        var ring = Ring();
        if (ring is null)
            return new LicenseInspection(false, "کلید عمومی پیکربندی نشده است (Signer:PublicKeyPath).", "", null, null, null, null);

        var result = LicenseVerifier.Verify(fileText ?? "", ring);
        if (!result.Ok || result.Document is null)
            return new LicenseInspection(false, "امضا/ساختار نامعتبر: " + result.ErrorText, "", null, null, null, null);

        var document = result.Document;
        var items = document.Items.Select(item =>
        {
            var scope = string.Join(", ", item.Scope ?? new List<string>());
            var until = item.Term.ValidUntil is { } v ? JalaliDate.Short(v) : "—";
            return $"• {item.Id}: {scope} | {item.Term.Type} تا {until} | کاربر: {item.Users.Mode} {item.Users.Max}";
        });

        return new LicenseInspection(true, null, string.Join(Environment.NewLine, items),
            document.Items.Select(i => i.Term.ValidUntil).Where(d => d is not null).Max(),
            document.Serial, document.InstallationId, document.CustomerName);
    }

    /// <summary>بازبینی و تطابق installationId — برای بازبینی خودکار پس از امضا.</summary>
    public (bool SignatureOk, bool InstallationOk, LicenseInspection Inspection) VerifyFor(string? fileText, string expectedInstallationId)
    {
        var inspection = Inspect(fileText);
        var installationOk = inspection.Ok &&
                             string.Equals(inspection.InstallationId, expectedInstallationId, StringComparison.OrdinalIgnoreCase);
        return (inspection.Ok, installationOk, inspection);
    }

    public static string? SerialFromFile(string fileText)
    {
        try
        {
            var envelope = LicenseJson.ReadEnvelope(fileText);
            if (envelope is null) return null;
            var payload = Convert.FromBase64String(envelope.Payload);
            return LicenseJson.ReadDocument(payload)?.Serial;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string? ReadNote(string fileText)
    {
        try
        {
            using var doc = JsonDocument.Parse(fileText);
            return doc.RootElement.TryGetProperty("version", out _) ? "envelope-ok" : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
