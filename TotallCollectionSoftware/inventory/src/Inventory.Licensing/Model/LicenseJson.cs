using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Inventory.Licensing.Model;

/// <summary>
/// تنظیمات واحد (دیکشنری) JSON لایسنس.
///
/// نکتهٔ مهم دربارهٔ امضا: امضا روی <b>بایت‌های خام payload</b> زده می‌شود، نه روی یک
/// نمایش دوباره‌سریال‌شده. بنابراین لازم نیست نگران تفاوت‌های جزئی سریال‌سازی (ترتیب
/// کلیدها، فاصله‌ها، ارقام اعشار) بین ابزار صدور و برنامهٔ مشتری باشیم؛ هر تغییری در
/// payload امضا را می‌شکند — که همان چیزی است که می‌خواهیم.
/// </summary>
public static class LicenseJson
{
    /// <summary>برای خواندن: نام‌ها نام‌حساس نیستند، مقادیر ناشناخته نادیده گرفته می‌شوند.</summary>
    public static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    /// <summary>
    /// برای نوشتن (ابزار صدور): فشرده، بدون BOM، بدون کاراکتر گریز اضافی.
    /// خروجی این متد همان بایت‌هایی است که امضا می‌شود.
    /// </summary>
    public static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    /// <summary>سریال‌سازی سند به بایت امضانشدنی — منبع واحد «payload».</summary>
    public static byte[] ToPayloadBytes(LicenseDocument document)
        => JsonSerializer.SerializeToUtf8Bytes(document, WriteOptions);

    /// <summary>خواندن سند از بایت‌های payload (پس از تأیید امضا).</summary>
    public static LicenseDocument? ReadDocument(ReadOnlySpan<byte> payload)
        => JsonSerializer.Deserialize<LicenseDocument>(payload, ReadOptions);

    /// <summary>خواندن پوشش امضا از متن فایل.</summary>
    public static SignedLicenseDocument? ReadEnvelope(string fileText)
        => JsonSerializer.Deserialize<SignedLicenseDocument>(fileText, ReadOptions);
}
