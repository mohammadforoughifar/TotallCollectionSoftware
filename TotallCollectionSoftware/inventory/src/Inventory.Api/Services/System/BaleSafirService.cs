using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Inventory.Api.Services;

/// <summary>
/// نتیجهٔ یک تلاش ارسال از طریق «سفیر» (سرویس ارسال پیام بله به شماره تلفن).
/// </summary>
public enum BaleSendStatus
{
    /// <summary>پیام با موفقیت ارسال شد.</summary>
    Sent,
    /// <summary>شماره تلفن نامعتبر است (قالب ایرانی پشتیبانی نمی‌شود).</summary>
    InvalidPhone,
    /// <summary>این شماره اکانت بله ندارد — یعنی کاربر در بله ثبت‌نشده است.</summary>
    NotBaleUser,
    /// <summary>سقف تعداد مخاطبین بازو پر شده است.</summary>
    ContactLimitReached,
    /// <summary>اعتبار مالی سازمان کافی نیست.</summary>
    PaymentRequired,
    /// <summary>محدودیت نرخ ارسال (باید بعداً دوباره تلاش شود).</summary>
    RateLimited,
    /// <summary>خطای داخلی سرور یا ورودی نامعتبر.</summary>
    ServerError,
    /// <summary>ارتباط شبکه برقرار نشد.</summary>
    NetworkError
}

/// <summary>نتیجهٔ ارسال یک پیام به یک شماره از طریق سفیر.</summary>
public record BaleSendResult(BaleSendStatus Status, string? MessageId = null, string? Error = null)
{
    public bool Ok => Status == BaleSendStatus.Sent;

    /// <summary>آیا این شماره اکانت بله دارد؟ (یعنی کاربر واقعاً در بله هست)</summary>
    public bool IsBaleUser => Status == BaleSendStatus.Sent;

    /// <summary>آیا خطا موقتی است و ارزش تلاش دوباره دارد؟</summary>
    public bool Transient => Status is BaleSendStatus.RateLimited or BaleSendStatus.ServerError or BaleSendStatus.NetworkError;

    /// <summary>متن فارسی قابل‌نمایش برای مدیر.</summary>
    public string Describe() => Status.Describe();
}

/// <summary>توصیف متنی وضعیت‌های ارسال — برای پیام‌های قابل‌نمایش به مدیر.</summary>
public static class BaleSendStatusText
{
    public static string Describe(this BaleSendStatus status) => status switch
    {
        BaleSendStatus.Sent => "ارسال شد",
        BaleSendStatus.InvalidPhone => "شمارهٔ نامعتبر",
        BaleSendStatus.NotBaleUser => "اکانت بله ندارد",
        BaleSendStatus.ContactLimitReached => "سقف مخاطبین بازو پر است",
        BaleSendStatus.PaymentRequired => "اعتبار مالی کافی نیست",
        BaleSendStatus.RateLimited => "محدودیت نرخ ارسال",
        BaleSendStatus.ServerError => "خطای سرور",
        _ => "ارتباط شبکه برقرار نشد"
    };
}

/// <summary>
/// ================== سرویس «سفیر» بله — ارسال مستقیم به شماره تلفن ==================
///  • سرویس رسمی سفیر (https://safir.bale.ai/api/v3/send_message) پیام را مستقیماً به شمارهٔ
///    مقصد می‌فرستد و نیازی به اینکه کاربر قبلاً ربات را استارت کرده باشد ندارد.
///  • پاسخ: { "message_id": "...", "error_data": [ {phone_number, code, description} ] }
///  • کدهای خطای مهم: ۸=شماره نامعتبر، ۱۷=کاربر اکانت بله ندارد، ۳=سقف نرخ،
///    ۲۰=اعتبار مالی ناکافی، ۲۱=سقف مخاطبین بازو.
///  • احراز هویت با هدر <c>api-access-key</c> و شناسهٔ عددی بازو در بدنهٔ درخواست.
///  • با <c>request_id</c> از ارسال تکراری جلوگیری می‌شود (idempotency).
/// </summary>
public interface IBaleSafirService
{
    /// <summary>آیا تنظیمات لازم (کلید دسترسی و شناسهٔ بازو) برای ارسال موجود است؟</summary>
    Task<bool> IsConfiguredAsync();

    /// <summary>
    /// ارسال یک پیام متنی به شمارهٔ تلفن مقصد.
    /// شماره باید به قالب <c>98…</c> نرمال شود (اجباری — قالب سفیر <c>09…</c> را نمی‌پذیرد).
    /// </summary>
    /// <param name="phone">شمارهٔ مقصد (هر قالبی که در سامانه پذیرفته است).</param>
    /// <param name="text">متن پیام.</param>
    /// <param name="requestId">شناسهٔ یکتا برای جلوگیری از ارسال تکراری.</param>
    Task<BaleSendResult> SendTextAsync(string phone, string text, string requestId);
}

public class BaleSafirService : IBaleSafirService
{
    private const string SendUrl = "https://safir.bale.ai/api/v3/send_message";

    private readonly IHttpClientFactory _httpFactory;
    private readonly Func<Task<(string? key, int botId)>> _credentials;
    private readonly ILogger<BaleSafirService> _log;

    public BaleSafirService(IHttpClientFactory httpFactory, Func<Task<(string? key, int botId)>> credentials, ILogger<BaleSafirService> log)
    {
        _httpFactory = httpFactory;
        _credentials = credentials;
        _log = log;
    }

    public async Task<bool> IsConfiguredAsync()
    {
        var (key, botId) = await _credentials();
        return !string.IsNullOrWhiteSpace(key) && botId > 0;
    }

    public async Task<BaleSendResult> SendTextAsync(string phone, string text, string requestId)
    {
        var normalized = NormalizePhone(phone);
        if (normalized == null)
            return new BaleSendResult(BaleSendStatus.InvalidPhone, Error: "شمارهٔ تلفن به قالب معتبر ایرانی نرمال نشد.");

        var (key, botId) = await _credentials();
        if (string.IsNullOrWhiteSpace(key))
            return new BaleSendResult(BaleSendStatus.ServerError, Error: "کلید دسترسی سفیر (Api Access Key) در تنظیمات وارد نشده است.");
        if (botId <= 0)
            return new BaleSendResult(BaleSendStatus.ServerError, Error: "شناسهٔ بازو (Bot Id) در تنظیمات وارد نشده است.");

        var payload = new
        {
            request_id = requestId,
            bot_id = botId,
            phone_number = normalized,
            message_data = new { message = new { text = text } }
        };

        var http = _httpFactory.CreateClient("messenger");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, SendUrl);
            request.Headers.TryAddWithoutValidation("api-access-key", key);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return new BaleSendResult(BaleSendStatus.ServerError,
                    Error: $"سفیر پاسخ {(int)response.StatusCode} داد: {Truncate(body, 200)}");

            return ParseResponse(body);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "ارسال سفیر به {Phone} ناموفق بود", normalized);
            return new BaleSendResult(BaleSendStatus.NetworkError, Error: "ارتباط با سرویس سفیر برقرار نشد: " + ex.Message);
        }
    }

    /// <summary>تجزیهٔ پاسخ سفیر — پیام موفق یا فهرست خطاهای مربوط به شماره.</summary>
    internal static BaleSendResult ParseResponse(string body)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body); }
        catch { return new BaleSendResult(BaleSendStatus.ServerError, Error: "پاسخ نامعتبر از سرویس سفیر."); }

        using (doc)
        {
            var root = doc.RootElement;

            // پاسخ موفق: message_id دارد و error_data خالی/ناموجود است
            var messageId = root.TryGetProperty("message_id", out var mid) && mid.ValueKind == JsonValueKind.String
                ? mid.GetString() : null;

            if (root.TryGetProperty("error_data", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            {
                var first = errors[0];
                var code = first.TryGetProperty("code", out var c) && c.TryGetInt32(out var ci) ? ci : 0;
                var description = first.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;

                var status = code switch
                {
                    8 => BaleSendStatus.InvalidPhone,
                    17 => BaleSendStatus.NotBaleUser,
                    3 => BaleSendStatus.RateLimited,
                    20 => BaleSendStatus.PaymentRequired,
                    21 => BaleSendStatus.ContactLimitReached,
                    4 => BaleSendStatus.ServerError,
                    _ => BaleSendStatus.ServerError
                };
                var message = description ?? status.Describe();
                return new BaleSendResult(status, Error: $"[{code}] {message}");
            }

            if (!string.IsNullOrWhiteSpace(messageId))
                return new BaleSendResult(BaleSendStatus.Sent, messageId);

            // پاسخ بدون پیام و بدون خطای ساختاریافته
            return new BaleSendResult(BaleSendStatus.ServerError, Error: "پاسخ ناشناخته از سرویس سفیر: " + Truncate(body, 200));
        }
    }

    /// <summary>
    /// نرمال‌سازی شمارهٔ ایرانی به قالب <c>98XXXXXXXXXX</c> که سفیر می‌پذیرد.
    /// ورودی‌های پذیرفته‌شده: 09xxxxxxxxx، +989xxxxxxxxx، 989xxxxxxxxx، 00989xxxxxxxxx
    /// و شماره‌های دارای فاصله/خط تیره. اگر شماره موبایل ایرانی نباشد null برمی‌گردد.
    /// </summary>
    public static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;

        // تبدیل ارقام فارسی/عربی به لاتین
        var digits = new StringBuilder();
        foreach (var ch in phone)
        {
            var d = ToLatinDigit(ch);
            if (d >= 0) digits.Append((char)('0' + d));
            else if (ch == '+') digits.Append('+');
        }

        var s = digits.ToString().Trim();
        if (s.StartsWith("+", StringComparison.Ordinal)) s = s[1..];

        // حذف صفرهای ابتداییِ پیش‌شمارهٔ بین‌المللی (0098… → 98…)
        if (s.StartsWith("0098", StringComparison.Ordinal)) s = s[2..];
        else if (s.StartsWith("00", StringComparison.Ordinal)) return null;

        string national;
        if (s.StartsWith("98", StringComparison.Ordinal) && s.Length == 12) national = "0" + s[2..];
        else if (s.StartsWith("0", StringComparison.Ordinal) && s.Length == 11) national = s;
        else if (s.StartsWith("9", StringComparison.Ordinal) && s.Length == 10) national = "0" + s;
        else return null;

        // فقط موبایل‌های ایران: 09x و ۱۰ رقم پس از صفر
        if (national.Length != 11 || !national.StartsWith("09", StringComparison.Ordinal)) return null;
        if (national[2] is < '0' or > '9') return null;

        return "98" + national[1..];
    }

    /// <summary>رقم لاتین متناظر با رقم فارسی/عربی؛ اگر رقم نبود -1.</summary>
    private static int ToLatinDigit(char ch) => ch switch
    {
        >= '0' and <= '9' => ch - '0',
        '۰' => 0, '۱' => 1, '۲' => 2, '۳' => 3, '۴' => 4,
        '۵' => 5, '۶' => 6, '۷' => 7, '۸' => 8, '۹' => 9,
        '٠' => 0, '١' => 1, '٢' => 2, '٣' => 3, '٤' => 4,
        '٥' => 5, '٦' => 6, '٧' => 7, '٨' => 8, '٩' => 9,
        _ => -1
    };

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = s.Trim();
        return s.Length <= max ? s : s[..max] + "…";
    }
}
