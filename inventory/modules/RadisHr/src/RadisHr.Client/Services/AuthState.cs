using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;
using RadisHr.Shared.Contracts;

namespace RadisHr.Client.Services;

/// <summary>وضعیت احراز هویت سمت مرورگر — توکن در localStorage نگهداری می‌شود</summary>
public class AuthState
{
    private const string StorageKey = "radisHrSession";
    private readonly HttpClient _http;
    private readonly IJSRuntime _js;

    public AuthState(HttpClient http, IJSRuntime js)
    {
        _http = http;
        _js = js;
    }

    public LoginResponse? Session { get; private set; }
    public bool IsAuthenticated => Session != null && Session.ExpiresAt > DateTime.UtcNow;
    public string UserKey => Session?.UserKey ?? "";
    public string DisplayName => Session?.DisplayName ?? "";
    public string RoleTitle => Session?.RoleTitle ?? "";
    public string[] Pages => Session?.Pages ?? Array.Empty<string>();
    public string Home => Session?.Home ?? "dashboard";

    public event Action? Changed;

    public bool Can(string page) =>
        Session != null && (UserKey == "ceo" || UserKey == "hr" || Pages.Contains(page) || page == "notices");

    public async Task InitializeAsync()
    {
        try
        {
            // SSO: نشست برنامه جامع روی همان Origin قرار دارد. RADIS-HR همان توکن JWT
            // را استفاده می‌کند و دیگر صفحه ورود یا حساب کاربری جداگانه ندارد.
            var inventoryJson = await _js.InvokeAsync<string?>("localStorage.getItem", "authSession");
            if (!string.IsNullOrWhiteSpace(inventoryJson))
            {
                using var doc = JsonDocument.Parse(inventoryJson);
                var root = doc.RootElement;
                var token = Text(root, "Token", "token");
                var role = Text(root, "Role", "role");
                var display = Text(root, "DisplayName", "displayName");
                var permissions = Strings(root, "Permissions", "permissions");

                if (!string.IsNullOrWhiteSpace(token)
                    && (role == "Admin" || permissions.Contains("RadisHr.Access", StringComparer.OrdinalIgnoreCase)))
                {
                    var pages = new[] { "dashboard", "employees", "employeeEntry", "attendance", "payroll",
                        "statutoryRules", "organizationStructure", "organizationSettings", "hse", "notices",
                        "finance", "accounting", "productionDaily" };
                    var userKey = role == "Admin" ? "ceo" : "hr";
                    var session = new LoginResponse(token, userKey,
                        string.IsNullOrWhiteSpace(display) ? Text(root, "Username", "username") : display,
                        role == "Admin" ? "مدیر سامانه" : "منابع انسانی", pages, "dashboard",
                        false, 0, TokenExpiry(token));
                    if (session.ExpiresAt > DateTime.UtcNow) Apply(session);
                    return;
                }
            }

            // نشست مستقل قدیمی عمداً پاک می‌شود تا ورود در کل مجموعه واقعاً یکپارچه باشد.
            await _js.InvokeVoidAsync("radisInterop.removeItem", StorageKey);
        }
        catch { /* نشست نامعتبر نادیده گرفته می‌شود */ }
    }

    private static string Text(JsonElement root, string pascal, string camel)
        => root.TryGetProperty(pascal, out var p) || root.TryGetProperty(camel, out p) ? p.GetString() ?? "" : "";

    private static string[] Strings(JsonElement root, string pascal, string camel)
    {
        if (!(root.TryGetProperty(pascal, out var p) || root.TryGetProperty(camel, out p)) || p.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        return p.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToArray();
    }

    private static DateTime TokenExpiry(string token)
    {
        try
        {
            var part = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
            part = part.PadRight(part.Length + ((4 - part.Length % 4) % 4), '=');
            using var payload = JsonDocument.Parse(Convert.FromBase64String(part));
            if (payload.RootElement.TryGetProperty("exp", out var exp))
                return DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64()).UtcDateTime;
        }
        catch { }
        return DateTime.UtcNow.AddHours(12);
    }

    public async Task<(bool Ok, string Message)> LoginAsync(string userKey, string password)
    {
        var response = await _http.PostAsJsonAsync("api/auth/login", new LoginRequest(userKey, password));
        if (!response.IsSuccessStatusCode)
        {
            var error = await SafeMessageAsync(response);
            return (false, error);
        }

        var session = await response.Content.ReadFromJsonAsync<LoginResponse>();
        if (session == null) return (false, "پاسخ نامعتبر از سرور.");

        Apply(session);
        await _js.InvokeVoidAsync("radisInterop.setItem", StorageKey,
            JsonSerializer.Serialize(session, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return (true, "ورود موفق.");
    }

    public async Task LogoutAsync()
    {
        Session = null;
        _http.DefaultRequestHeaders.Authorization = null;
        await _js.InvokeVoidAsync("radisInterop.removeItem", StorageKey);
        await _js.InvokeVoidAsync("localStorage.removeItem", "authSession");
        Changed?.Invoke();
    }

    private void Apply(LoginResponse session)
    {
        Session = session;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        Changed?.Invoke();
    }

    private static async Task<string> SafeMessageAsync(HttpResponseMessage response)
    {
        // ۴۰۵ یعنی درخواست به سرور فایل استاتیک رسیده و نه به سرویس REST؛
        // معمولاً وقتی رخ می‌دهد که پروژهٔ RadisHr.Client جداگانه اجرا شده باشد.
        if (response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
            return "سرویس REST در دسترس نیست. برنامه را از پروژهٔ RadisHr.Api اجرا کنید "
                 + "(dotnet run --project src/RadisHr.Api) نه از RadisHr.Client.";

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return "نشانی سرویس ورود یافت نشد. از اجرای پروژهٔ RadisHr.Api مطمئن شوید.";

        try
        {
            var message = await response.Content.ReadFromJsonAsync<ApiMessage>();
            return message?.Message ?? "خطا در ورود.";
        }
        catch { return "خطا در ارتباط با سرور."; }
    }
}
