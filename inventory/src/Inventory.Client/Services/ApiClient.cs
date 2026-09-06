using System.Net.Http.Json;
using System.Text.Json;

namespace Inventory.Client.Services;

public class ApiOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5100";
}

public class ApiException : Exception
{
    public ApiException(string message) : base(message) { }
}

/// <summary>کلاینت HTTP برای ارتباط با API با استخراج پیام خطای فارسی.</summary>
public class ApiClient : IApiClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly ApiOptions _opts;

    private readonly IAuthState _auth;

    public ApiClient(HttpClient http, ApiOptions opts, IAuthState auth)
    {
        _http = http;
        _opts = opts;
        _auth = auth;
    }

    private void AddAuth(HttpRequestMessage req)
    {
        if (!string.IsNullOrEmpty(_auth.Token))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _auth.Token);
    }

    private string Url(string path) => $"{_opts.BaseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

    public string BuildUrl(string path) => Url(path);

    /// <summary>
    /// دریافت فایل باینری از API. نام فایل از هدر Content-Disposition خوانده می‌شود
    /// و اگر نبود از انتهای مسیر ساخته می‌شود.
    /// </summary>
    public async Task<(byte[] Data, string FileName, string ContentType)> GetFileAsync(string path)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, Url(path));
        AddAuth(req);

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req);
        }
        catch (Exception ex)
        {
            throw new Exception("ارتباط با سرور برقرار نشد: " + ex.Message);
        }

        if (!resp.IsSuccessStatusCode)
        {
            var text = await resp.Content.ReadAsStringAsync();
            var msg = $"خطای سرور ({(int)resp.StatusCode})";
            if (!string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(text);
                    if (doc.RootElement.TryGetProperty("message", out var m))
                        msg = m.GetString() ?? msg;
                }
                catch { /* پاسخ JSON نبود */ }
            }
            throw new Exception(msg);
        }

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        var ctype = resp.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

        var name = resp.Content.Headers.ContentDisposition?.FileNameStar
                   ?? resp.Content.Headers.ContentDisposition?.FileName;
        name = name?.Trim('"');
        if (string.IsNullOrWhiteSpace(name))
            name = path.Split('/').LastOrDefault()?.Split('?').FirstOrDefault() ?? "download";

        return (bytes, name, ctype);
    }

    public async Task<T> PostFileAsync<T>(string path, Stream fileStream, string fileName, string formFieldName = "file")
    {
        using var content = new MultipartFormDataContent();
        var sc = new StreamContent(fileStream);
        sc.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(sc, formFieldName, fileName);

        var fileReq = new HttpRequestMessage(HttpMethod.Post, Url(path)) { Content = content };
        AddAuth(fileReq);

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(fileReq);
        }
        catch (Exception ex)
        {
            throw new ApiException($"ارتباط با سرور برقرار نشد. ({ex.Message})");
        }

        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            string msg = "خطایی رخ داد.";
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                    msg = m.GetString() ?? msg;
                else msg = text;
            }
            catch { if (!string.IsNullOrWhiteSpace(text)) msg = text; }
            throw new ApiException(msg);
        }

        if (string.IsNullOrWhiteSpace(text)) return default!;
        return JsonSerializer.Deserialize<T>(text, JsonOpts) ?? default!;
    }

    public async Task<T> GetAsync<T>(string path) => await SendAsync<T>(HttpMethod.Get, path);

    public async Task<T> PostAsync<T>(string path, object? body = null) => await SendAsync<T>(HttpMethod.Post, path, body);

    public async Task<T> PutAsync<T>(string path, object? body = null) => await SendAsync<T>(HttpMethod.Put, path, body);

    public async Task DeleteAsync(string path) => await SendAsync<object>(HttpMethod.Delete, path);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null)
    {
        var req = new HttpRequestMessage(method, Url(path));
        AddAuth(req);
        if (body is not null)
            req.Content = JsonContent.Create(body, options: JsonOpts);

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req);
        }
        catch (Exception ex)
        {
            throw new ApiException($"ارتباط با سرور برقرار نشد. ({ex.Message})");
        }

        var text = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
            string msg;
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                    msg = m.GetString() ?? "خطایی رخ داد.";
                else
                    msg = string.IsNullOrWhiteSpace(text) ? "بدنه‌ی پاسخ خالی بود." : text;
            }
            catch
            {
                msg = string.IsNullOrWhiteSpace(text) ? "بدنه‌ی پاسخ خالی بود." : text;
            }
            // افزودن کد وضعیت و آدرس برای تشخیص آسان‌تر
            throw new ApiException($"{msg} (HTTP {(int)resp.StatusCode} — {Url(path)})");
        }

        if (string.IsNullOrWhiteSpace(text)) return default!;
        if (typeof(T) == typeof(string)) return (T)(object)text;
        return JsonSerializer.Deserialize<T>(text, JsonOpts) ?? default!;
    }
}
