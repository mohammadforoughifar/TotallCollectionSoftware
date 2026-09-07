using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Inventory.Client.Services.RadisHr;

/// <summary>
/// API منابع انسانی در Host اصلی. توکن در هر درخواست از نشست جاری Inventory خوانده
/// می‌شود (حتی پس از خروج/ورود با کاربر دیگر). پاسخ 401 همان نشست را پایان می‌دهد.
/// </summary>
public sealed class RadisHrApiClient(HttpClient http, ApiOptions options, IAuthState auth, IToastService toasts)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private HttpRequestMessage Request(HttpMethod method, string path, HttpContent? content = null)
    {
        if (!auth.IsLoggedIn || string.IsNullOrWhiteSpace(auth.Token))
            throw new ApiException("برای استفاده از منابع انسانی ابتدا وارد سامانه شوید.");
        path = path.TrimStart('/');
        if (!path.StartsWith("api/", StringComparison.Ordinal))
            throw new ArgumentException("مسیر سرویس منابع انسانی باید با api/ شروع شود.", nameof(path));

        var baseUri = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        var request = new HttpRequestMessage(method, new Uri(baseUri, "api/hr/" + path[4..])) { Content = content };
        if (!string.IsNullOrWhiteSpace(auth.Token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return request;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await auth.SignOutAsync();
            throw new ApiException("نشست شما منقضی شده است. دوباره وارد سامانه شوید.");
        }
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new ApiException("شما به این بخش از منابع انسانی دسترسی ندارید.");
        if (!response.IsSuccessStatusCode)
            throw new ApiException(await MessageAsync(response));
    }

    private async Task<T?> ReadAsync<T>(HttpMethod method, string path, HttpContent? content = null)
    {
        try
        {
            using var request = Request(method, path, content);
            using var response = await http.SendAsync(request);
            await EnsureSuccessAsync(response);
            var text = await response.Content.ReadAsStringAsync();
            return string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<T>(text, JsonOptions);
        }
        catch (Exception ex)
        {
            toasts.Error(ex.Message);
            return default;
        }
    }

    public Task<T?> GetAsync<T>(string path) => ReadAsync<T>(HttpMethod.Get, path);
    public async Task<List<T>> GetListAsync<T>(string path) => await GetAsync<List<T>>(path) ?? new();
    public Task<T?> PostAsync<T>(string path, object? body = null) =>
        ReadAsync<T>(HttpMethod.Post, path, body == null ? null : JsonContent.Create(body, options: JsonOptions));
    public Task<T?> PutAsync<T>(string path, object body) =>
        ReadAsync<T>(HttpMethod.Put, path, JsonContent.Create(body, options: JsonOptions));
    public Task<T?> PostFormAsync<T>(string path, MultipartFormDataContent content) =>
        ReadAsync<T>(HttpMethod.Post, path, content);
    public Task<bool> DeleteAsync(string path) => SendAsync(HttpMethod.Delete, path);

    public async Task<bool> SendAsync(HttpMethod method, string path, object? body = null)
    {
        try
        {
            using var request = Request(method, path, body == null ? null : JsonContent.Create(body, options: JsonOptions));
            using var response = await http.SendAsync(request);
            await EnsureSuccessAsync(response);
            var message = await MessageAsync(response);
            if (!string.IsNullOrWhiteSpace(message)) toasts.Success(message);
            return true;
        }
        catch (Exception ex)
        {
            toasts.Error(ex.Message);
            return false;
        }
    }

    // Downloads use the Authorization header, never a token in the URL or an unauthenticated <a>.
    public async Task<(byte[] Data, string FileName, string ContentType)?> GetFileAsync(string path)
    {
        try
        {
            using var request = Request(HttpMethod.Get, path);
            using var response = await http.SendAsync(request);
            await EnsureSuccessAsync(response);
            var disposition = response.Content.Headers.ContentDisposition;
            return (await response.Content.ReadAsByteArrayAsync(),
                (disposition?.FileNameStar ?? disposition?.FileName ?? "attachment").Trim('"'),
                response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream");
        }
        catch (Exception ex)
        {
            toasts.Error(ex.Message);
            return null;
        }
    }

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        var fallback = response.IsSuccessStatusCode ? "" : $"خطای سرویس منابع انسانی ({(int)response.StatusCode})";
        try
        {
            var text = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in new[] { "message", "title" })
                    if (document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                        return value.GetString() ?? fallback;
            }
        }
        catch (JsonException) { }
        return fallback;
    }
}
