using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LicenseSigner.Data;

namespace LicenseSigner.Services;

/// <summary>درخواست تجزیه‌شده — خروجی مشترک همهٔ مسیرهای ورود.</summary>
public sealed class ParsedRequest
{
    public int RequestVersion { get; set; } = 2;
    public RequestType Type { get; set; } = RequestType.NewActivation;
    public string InstallationId { get; set; } = "";
    public string? CustomerName { get; set; }
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public string? CustomerNote { get; set; }
    public List<string> InterestedModules { get; } = new();
    public List<string> HintIssues { get; } = new();
    public List<string> Warnings { get; } = new();
    public string? Deployment { get; set; }
    public string? MachineName { get; set; }
    public string? EngineVersion { get; set; }
    public string? EngineSchema { get; set; }
    public string? BuildDate { get; set; }
    public DateTime? RequestedAtUtc { get; set; }
    public string RawJson { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public int Users { get; set; }
    public string? CurrentLicenseSerial { get; set; }
    public DateTime? CurrentLicenseValidUntil { get; set; }
    public bool Ok => Warnings.All(w => !w.StartsWith("⛔", StringComparison.Ordinal));
}

/// <summary>
/// رابط ورود درخواست از منابع دیگر (IMAP/endpoint آنلاین). در این فاز
/// <bc>عمداً پیاده‌سازی نشده</b> و فقط قرارداد آن وجود دارد.
/// </summary>
public interface IRequestIngest
{
    string Name { get; }
    Task<IReadOnlyList<ParsedRequest>> PullAsync(CancellationToken ct = default);
}

/// <summary>
/// تجزیهٔ ورودی غیرقابل‌اعتماد: JSON نسخهٔ ۲ (و نسخهٔ ۱ برای سازگاری)، متن خام، یا .eml.
/// قواعد: سقف حجم، اعتبارسنجی ساختار، حذف کد ماژول ناشناخته، هیچ اجرای محتوا.
/// </summary>
public static class RequestParser
{
    public const int MaxJsonBytes = 256 * 1024;
    public const int MaxNameLength = 100;
    public const int MaxPhoneLength = 30;
    public const int MaxEmailLength = 150;
    public const int MaxNoteLength = 500;

    private static readonly JsonDocumentOptions DocOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 32
    };

    public static ParsedRequest ParseJson(string json)
    {
        var result = new ParsedRequest { RawJson = json ?? "" };
        result.Sha256 = Sha256(result.RawJson);

        if (string.IsNullOrWhiteSpace(json))
        {
            result.Warnings.Add("⛔ متن درخواست خالی است.");
            return result;
        }

        if (Encoding.UTF8.GetByteCount(json) > MaxJsonBytes)
        {
            result.Warnings.Add("⛔ حجم درخواست بیش از سقف مجاز (۲۵۶ کیلوبایت) است.");
            return result;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, DocOptions);
        }
        catch (JsonException)
        {
            result.Warnings.Add("⛔ ساختار JSON درخواست معتبر نیست.");
            return result;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                result.Warnings.Add("⛔ ریشهٔ درخواست باید یک شیء JSON باشد.");
                return result;
            }

            result.RequestVersion = GetInt(root, "requestVersion") ?? 1;
            result.InstallationId = Clean(GetString(root, "installationId"), 64) ?? "";
            result.CustomerName = Clean(GetString(root, "customerName"), MaxNameLength);
            result.ContactName = Clean(GetString(root, "contactName"), MaxNameLength);
            result.ContactPhone = Clean(GetString(root, "contactPhone"), MaxPhoneLength);
            result.ContactEmail = Clean(GetString(root, "contactEmail"), MaxEmailLength);
            result.CustomerNote = Clean(GetString(root, "customerNote"), MaxNoteLength);
            result.MachineName = Clean(GetString(root, "machineName"), 100);
            result.EngineVersion = Clean(GetString(root, "productVersion") ?? GetString(root, "engineVersion"), 40);
            result.EngineSchema = Clean(GetString(root, "engineSchema"), 40);
            result.BuildDate = Clean(GetString(root, "buildDate"), 40);
            result.Deployment = Clean(GetString(root, "deployment"), 20);
            result.Users = GetInt(root, "users") ?? 0;

            if (root.TryGetProperty("requestedAt", out var requestedAt) && requestedAt.ValueKind == JsonValueKind.String
                && DateTime.TryParse(requestedAt.GetString(), out var parsedAt))
                result.RequestedAtUtc = parsedAt.ToUniversalTime();

            result.Type = ParseType(GetString(root, "requestType"));

            if (root.TryGetProperty("currentLicense", out var current) && current.ValueKind == JsonValueKind.Object)
            {
                result.CurrentLicenseSerial = Clean(GetString(current, "serial"), 40);
                if (DateTime.TryParse(GetString(current, "validUntil"), out var until))
                    result.CurrentLicenseValidUntil = until;
            }

            var known = Inventory.Licensing.Modules.ModuleCodes.All.Select(m => m.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var property in new[] { "interestedModules", "modules" })
            {
                if (!root.TryGetProperty(property, out var modules) || modules.ValueKind != JsonValueKind.Array) continue;
                foreach (var item in modules.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;
                    var code = Clean(item.GetString(), 40);
                    if (string.IsNullOrWhiteSpace(code)) continue;
                    if (!known.Contains(code))
                    {
                        var warn = $"کد ماژول ناشناخته کنار گذاشته شد: {code}";
                        if (!result.Warnings.Contains(warn)) result.Warnings.Add(warn);
                        continue;
                    }

                    if (!result.InterestedModules.Contains(code, StringComparer.OrdinalIgnoreCase))
                        result.InterestedModules.Add(code.ToLowerInvariant());
                }
            }

            if (root.TryGetProperty("hintIssues", out var hints) && hints.ValueKind == JsonValueKind.Array)
            {
                foreach (var hint in hints.EnumerateArray())
                {
                    if (hint.ValueKind != JsonValueKind.String) continue;
                    var text = Clean(hint.GetString(), 200);
                    if (!string.IsNullOrWhiteSpace(text)) result.HintIssues.Add(text!);
                }
            }
        }

        if (result.InstallationId.Length < 8)
            result.Warnings.Add("⛔ شناسهٔ نصب (installationId) معتبر نیست.");

        if (!string.IsNullOrEmpty(result.CustomerNote) && ContainsMarkup(result.CustomerNote))
            result.Warnings.Add("متن درخواست شامل تگ‌های HTML/اسکریپت است؛ فقط به‌صورت متن نمایش داده می‌شود (اجرا نمی‌شود).");

        return result;
    }

    /// <summary>ورودی غیر JSON: اگر شبیه درخواست نسخهٔ ۲ نبود، هشدار و پذیرش به‌صورت خام.</summary>
    public static ParsedRequest ParseText(string text)
    {
        var trimmed = (text ?? "").Trim();
        if (trimmed.StartsWith('{'))
        {
            var parsed = ParseJson(trimmed);
            if (parsed.Warnings.All(w => !w.StartsWith("⛔", StringComparison.Ordinal))) return parsed;
        }

        var result = new ParsedRequest { RawJson = text ?? "" };
        result.Sha256 = Sha256(result.RawJson);
        result.Warnings.Add("⛔ محتوای ورودی به‌عنوان درخواست نسخهٔ ۲ خوانده نشد؛ ثبت دستی لازم است.");
        return result;
    }

    /// <summary>استخراج بدنه/پیوست‌های .eml — فقط پیوست JSON درخواست پذیرفته می‌شود.</summary>
    public static (ParsedRequest? Parsed, List<(string FileName, byte[] Bytes)> Attachments, List<string> Warnings)
        ParseEml(byte[] bytes, string fileName)
    {
        var warnings = new List<string>();
        var attachments = new List<(string, byte[])>();
        if (bytes.Length > 2 * 1024 * 1024)
        {
            warnings.Add("⛔ حجم فایل .eml بیش از سقف مجاز (۲ مگابایت) است.");
            return (null, attachments, warnings);
        }

        var text = Encoding.UTF8.GetString(bytes);
        var jsonBody = ExtractJsonObject(text);
        if (jsonBody is not null)
        {
            var parsed = ParseJson(jsonBody);
            parsed.Warnings.InsertRange(0, warnings);
            attachments.Add((fileName, bytes));
            return (parsed, attachments, warnings);
        }

        warnings.Add("⛔ در فایل .eml درخواست JSON پیدا نشد (پیوست یا بدنهٔ JSON لازم است).");
        return (null, attachments, warnings);
    }

    private static string? ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        while (start >= 0)
        {
            var depth = 0;
            for (var i = start; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        var candidate = text.Substring(start, i - start + 1);
                        try
                        {
                            using var doc = JsonDocument.Parse(candidate, DocOptions);
                            if (doc.RootElement.TryGetProperty("installationId", out _)) return candidate;
                        }
                        catch (JsonException)
                        {
                            // ادامه بده؛ شاید شیء بعدی معتبر باشد
                        }

                        break;
                    }
                }
            }

            start = text.IndexOf('{', start + 1);
        }

        return null;
    }

    public static string Sha256(string text) => Sha256(Encoding.UTF8.GetBytes(text ?? ""));

    public static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static bool ContainsMarkup(string text) =>
        text.Contains('<') || text.Contains("script", StringComparison.OrdinalIgnoreCase)
                              || text.Contains("javascript:", StringComparison.OrdinalIgnoreCase);

    private static RequestType ParseType(string? value) => (value ?? "").Trim().ToLowerInvariant() switch
    {
        "newactivation" or "new" or "فعال‌سازی" => RequestType.NewActivation,
        "renewal" or "تمدید" => RequestType.Renewal,
        "addmodule" or "افزودن" => RequestType.AddModule,
        "quotapack" or "سهمیه" => RequestType.QuotaPack,
        "rehost" or "جابه‌جایی" => RequestType.Rehost,
        "trial" or "آزمایشی" => RequestType.Trial,
        "bridge" or "پل" => RequestType.Bridge,
        _ => RequestType.Other
    };
}
