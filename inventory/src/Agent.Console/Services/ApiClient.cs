using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using InventoryAgent.Models;

namespace InventoryAgent.Services;

/// <summary>کلاینت HTTP ارتباط با API مدیریت سخت‌افزار (Inventory.Api).</summary>
public sealed class ApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json;

    public string BaseUrl { get; }

    public ApiClient(string baseUrl, bool insecure = false, int timeoutSeconds = 30)
    {
        BaseUrl = baseUrl.Trim().TrimEnd('/');
        HttpMessageHandler handler = new HttpClientHandler();
        if (insecure)
        {
            var h = (HttpClientHandler)handler;
            h.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        }
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("InventoryAgent/1.0");
        _json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    public void UseToken(string token)
    {
        if (!string.IsNullOrWhiteSpace(token))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
    }

    /// <summary>ورود با نام کاربری/رمز و گرفتن توکن — POST api/auth/login</summary>
    public async Task<(bool ok, string message)> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.PostAsJsonAsync($"{BaseUrl}/api/auth/login",
                new { username, password }, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return (false, $"Login failed ({(int)resp.StatusCode}): {ExtractMessage(body)}");
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("token", out var t) ||
                    doc.RootElement.TryGetProperty("Token", out t))
                {
                    var token = t.GetString() ?? "";
                    if (token.Length > 0)
                    {
                        UseToken(token);
                        return (true, "ورود موفق.");
                    }
                }
            }
            catch { }
            return (false, "پاسخ لاگین نامعتبر است.");
        }
        catch (Exception ex)
        {
            return (false, $"خطای اتصال در لاگین: {ex.Message}");
        }
    }

    /// <summary>ارسال گزارش سخت‌افزار — POST api/SystemInfo (با ۳ تلاش و backoff).</summary>
    public async Task<(bool ok, string message)> SendReportAsync(AgentReport report, CancellationToken ct = default)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var resp = await _http.PostAsJsonAsync(
                    $"{BaseUrl}/api/SystemInfo", report, AgentJson.Options, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (resp.IsSuccessStatusCode)
                    return (true, DescribeReportResponse(body));
                // خطای 4xx را تکرار نکن — مشکل از داده است نه شبکه
                if ((int)resp.StatusCode is >= 400 and < 500)
                    return (false, $"سرور قبول نکرد ({(int)resp.StatusCode}): {ExtractMessage(body)}");
                last = new Exception($"HTTP {(int)resp.StatusCode}: {ExtractMessage(body)}");
            }
            catch (Exception ex)
            {
                last = ex;
            }
            if (attempt < 3)
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
        }
        return (false, $"ارسال ناموفق بعد از ۳ تلاش: {last?.Message}");
    }

    /// <summary>دریافت دستورهای در انتظار — GET api/SystemInfo/agent-commands?agentId=</summary>
    public async Task<List<RemoteCommand>> GetPendingCommandsAsync(string agentId, CancellationToken ct = default)
    {
        try
        {
            var url = $"{BaseUrl}/api/SystemInfo/agent-commands?agentId={Uri.EscapeDataString(agentId)}";
            var list = await _http.GetFromJsonAsync<List<RemoteCommand>>(url, _json, ct);
            return list ?? new List<RemoteCommand>();
        }
        catch
        {
            return new List<RemoteCommand>();
        }
    }

    /// <summary>گزارش نتیجه‌ی اجرای دستور — POST api/SystemInfo/commands/{id}/result</summary>
    public async Task AckCommandAsync(int cmdId, bool ok, string message, CancellationToken ct = default)
    {
        try
        {
            await _http.PostAsJsonAsync($"{BaseUrl}/api/SystemInfo/commands/{cmdId}/result",
                new { ok, message }, ct);
        }
        catch { }
    }

    private static string ExtractMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "بدون جزئیات";
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("message", out var m) || root.TryGetProperty("Message", out m))
                    return m.GetString() ?? body;
                if (root.TryGetProperty("title", out var t))
                    return t.GetString() ?? body;
            }
        }
        catch { }
        return body.Length > 300 ? body[..300] : body;
    }

    private static string DescribeReportResponse(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            string P(string camel, string pascal)
            {
                if (root.TryGetProperty(camel, out var v)) return v.ToString();
                if (root.TryGetProperty(pascal, out v)) return v.ToString();
                return "";
            }
            var id = P("id", "Id");
            if (root.TryGetProperty("firstRegistration", out var fr) && fr.ValueKind == JsonValueKind.True)
                return $"ثبت اول موفق (id={id}).";
            if (root.TryGetProperty("unchanged", out var un) && un.ValueKind == JsonValueKind.True)
                return $"بدون تغییر — فقط زمان به‌روز شد (id={id}).";
            var pending = P("pendingChanges", "PendingChanges");
            var msg = P("message", "Message");
            if (msg.Length == 0) msg = "تغییرات در انتظار تایید ثبت شد.";
            return string.IsNullOrEmpty(pending) ? $"{msg} (id={id})" : $"{msg} ({pending} تغییر، id={id})";
        }
        catch
        {
            return "ارسال موفق.";
        }
    }

    public void Dispose() => _http.Dispose();
}
