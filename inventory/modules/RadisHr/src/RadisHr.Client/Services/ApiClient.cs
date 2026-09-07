using System.Net.Http.Json;
using System.Text.Json;
using RadisHr.Shared.Contracts;

namespace RadisHr.Client.Services;

/// <summary>لایهٔ دسترسی به REST API — تمام صفحه‌ها فقط از این کلاس استفاده می‌کنند</summary>
public class ApiClient
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly ToastService _toasts;

    public ApiClient(HttpClient http, ToastService toasts)
    {
        _http = http;
        _toasts = toasts;
    }

    public async Task<T?> GetAsync<T>(string url)
    {
        try
        {
            var response = await _http.GetAsync(url);
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadFromJsonAsync<T>(Options);

            _toasts.Error(await MessageAsync(response));
            return default;
        }
        catch (Exception ex)
        {
            _toasts.Error($"خطای ارتباط با سرور: {ex.Message}");
            return default;
        }
    }

    public async Task<List<T>> GetListAsync<T>(string url) =>
        await GetAsync<List<T>>(url) ?? new List<T>();

    public async Task<JsonElement?> GetJsonAsync(string url) => await GetAsync<JsonElement>(url);

    public async Task<TResult?> PostAsync<TResult>(string url, object? body = null)
    {
        try
        {
            var response = body == null
                ? await _http.PostAsync(url, null)
                : await _http.PostAsJsonAsync(url, body, Options);

            if (response.IsSuccessStatusCode)
            {
                if (response.Content.Headers.ContentLength == 0) return default;
                return await response.Content.ReadFromJsonAsync<TResult>(Options);
            }

            _toasts.Error(await MessageAsync(response));
            return default;
        }
        catch (Exception ex)
        {
            _toasts.Error($"خطای ارتباط با سرور: {ex.Message}");
            return default;
        }
    }

    public async Task<TResult?> PutAsync<TResult>(string url, object body)
    {
        try
        {
            var response = await _http.PutAsJsonAsync(url, body, Options);
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadFromJsonAsync<TResult>(Options);

            _toasts.Error(await MessageAsync(response));
            return default;
        }
        catch (Exception ex)
        {
            _toasts.Error($"خطای ارتباط با سرور: {ex.Message}");
            return default;
        }
    }

    public async Task<bool> DeleteAsync(string url)
    {
        try
        {
            var response = await _http.DeleteAsync(url);
            if (response.IsSuccessStatusCode) return true;
            _toasts.Error(await MessageAsync(response));
            return false;
        }
        catch (Exception ex)
        {
            _toasts.Error($"خطای ارتباط با سرور: {ex.Message}");
            return false;
        }
    }

    /// <summary>فراخوانی همراه با نمایش پیام موفقیت/خطا</summary>
    public async Task<bool> SendAsync(HttpMethod method, string url, object? body = null)
    {
        try
        {
            var request = new HttpRequestMessage(method, url);
            if (body != null) request.Content = JsonContent.Create(body, options: Options);
            var response = await _http.SendAsync(request);
            var message = await MessageAsync(response);

            if (response.IsSuccessStatusCode)
            {
                if (!string.IsNullOrEmpty(message)) _toasts.Success(message);
                return true;
            }

            _toasts.Error(message);
            return false;
        }
        catch (Exception ex)
        {
            _toasts.Error($"خطای ارتباط با سرور: {ex.Message}");
            return false;
        }
    }

    /// <summary>ارسال فایل به‌صورت multipart (پیوست قرارداد، مدارک HSE، فایل حضور و غیاب)</summary>
    public async Task<TResult?> PostFormAsync<TResult>(string url, MultipartFormDataContent content)
    {
        try
        {
            var response = await _http.PostAsync(url, content);
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadFromJsonAsync<TResult>(Options);

            _toasts.Error(await MessageAsync(response));
            return default;
        }
        catch (Exception ex)
        {
            _toasts.Error($"خطای ارتباط با سرور: {ex.Message}");
            return default;
        }
    }

    public HttpClient Raw => _http;

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(text))
                return response.IsSuccessStatusCode ? "" : $"خطای {(int)response.StatusCode}";

            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (document.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                    return m.GetString() ?? "";
                if (document.RootElement.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String)
                    return t.GetString() ?? "";
            }
            return response.IsSuccessStatusCode ? "" : $"خطای {(int)response.StatusCode}";
        }
        catch
        {
            return response.IsSuccessStatusCode ? "" : $"خطای {(int)response.StatusCode}";
        }
    }
}
