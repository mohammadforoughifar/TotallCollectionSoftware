using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.JSInterop;

namespace Inventory.Client.Services;

/// <summary>کلاینت HTTP ماژول منابع انسانی بن‌سازه — مسیرهای /radis-hr/api</summary>
public sealed class BonHrApi
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IApiClient _api;
    private readonly HttpClient _http;
    private readonly IAuthState _auth;
    private readonly ApiOptions _opts;

    public BonHrApi(IApiClient api, HttpClient http, IAuthState auth, ApiOptions opts)
    {
        _api = api;
        _http = http;
        _auth = auth;
        _opts = opts;
    }

    private static string P(string path) => "radis-hr/" + path.TrimStart('/');

    public async Task<List<T>> ListAsync<T>(string path)
    {
        try { return await _api.GetAsync<List<T>>(P(path)) ?? new(); }
        catch { return new(); }
    }

    public async Task<T?> GetAsync<T>(string path)
    {
        try { return await _api.GetAsync<T>(P(path)); }
        catch { return default; }
    }

    public async Task<T?> PostAsync<T>(string path, object? body = null)
    {
        try { return await _api.PostAsync<T>(P(path), body); }
        catch { return default; }
    }

    public async Task<T?> PutAsync<T>(string path, object? body = null)
    {
        try { return await _api.PutAsync<T>(P(path), body); }
        catch { return default; }
    }

    public async Task<bool> DeleteAsync(string path)
    {
        try { await _api.DeleteAsync(P(path)); return true; }
        catch { return false; }
    }

    public async Task<bool> SendAsync(HttpMethod method, string path, object? body = null)
    {
        try
        {
            if (method == HttpMethod.Post) { await _api.PostAsync<object>(P(path), body); return true; }
            if (method == HttpMethod.Put) { await _api.PutAsync<object>(P(path), body); return true; }
            if (method == HttpMethod.Delete) { await _api.DeleteAsync(P(path)); return true; }
            return false;
        }
        catch { return false; }
    }

    public Task<T> PostFileAsync<T>(string path, Stream fileStream, string fileName)
        => _api.PostFileAsync<T>(P(path), fileStream, fileName);

    public async Task<T?> PostFormAsync<T>(string path, MultipartFormDataContent content)
    {
        var url = $"{_opts.BaseUrl.TrimEnd('/')}/{P(path)}";
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        if (!string.IsNullOrEmpty(_auth.Token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _auth.Token);
        var resp = await _http.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) return default;
        if (string.IsNullOrWhiteSpace(text)) return default;
        return JsonSerializer.Deserialize<T>(text, JsonOpts);
    }

    public async Task DownloadCsvAsync(IJSRuntime js, string fileName, string csv)
    {
        var bytes = System.Text.Encoding.UTF8.GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes(csv)).ToArray();
        await js.InvokeVoidAsync("saveAsFile", fileName, Convert.ToBase64String(bytes));
    }
}

public static class BonHrNav
{
    public static readonly (string Href, string Icon, string Label)[] Items =
    {
        ("bon-hr", "bi-speedometer2", "داشبورد"),
        ("bon-hr/employees", "bi-people", "پرسنل"),
        ("bon-hr/employee-entry", "bi-person-plus", "ورود اطلاعات"),
        ("bon-hr/attendance", "bi-clock-history", "حضور و غیاب"),
        ("bon-hr/payroll", "bi-cash-stack", "حقوق و دستمزد"),
        ("bon-hr/statutory-rules", "bi-journal-text", "الزامات سالانه"),
        ("bon-hr/organization-structure", "bi-diagram-3", "واحدها و ماتریس"),
        ("bon-hr/organization-settings", "bi-calendar3", "تقویم و ساعات"),
        ("bon-hr/hse", "bi-shield-plus", "ایمنی و حوادث"),
        ("bon-hr/finance", "bi-wallet2", "مالی"),
        ("bon-hr/accounting", "bi-calculator", "حسابداری حقوق"),
        ("bon-hr/production-daily", "bi-clipboard-data", "گزارش روزانه"),
        ("bon-hr/notices", "bi-bell", "اطلاعیه‌ها"),
    };
}
