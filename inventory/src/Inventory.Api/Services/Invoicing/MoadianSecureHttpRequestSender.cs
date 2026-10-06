using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaxCollectData.Library.Abstraction;
using TaxCollectData.Library.Dto;

namespace Inventory.Api.Services.Invoicing;

/// <summary>
/// جایگزین امن <c>IHttpRequestSender</c> خودِ SDK برای تست اتصال.
/// فرستندهٔ پیش‌فرض SDK اعتبارسنجی گواهی TLS را کامل غیرفعال می‌کند
/// (<c>ServerCertificateCustomValidationCallback =&gt; true</c>) و در
/// <c>ServicePointManager</c> هم callback می‌گذارد؛ این کلاس هیچ‌کدام را انجام نمی‌دهد
/// و علاوه بر آن، آدرس درخواست را به همان origin پیکربندی‌شده محدود می‌کند.
/// </summary>
internal sealed class MoadianSecureHttpRequestSender : IHttpRequestSender, IDisposable
{
    /// <summary>مثل SDK: پاسخِ غیرقابل‌تفسیر به‌عنوان خطای گذرا (408) گزارش می‌شود.</summary>
    private const int UnparsableResponseStatus = 408;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _client;
    private readonly Uri _baseUri;

    public MoadianSecureHttpRequestSender(string baseUrl, TimeSpan timeout)
    {
        var normalized = baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
        _baseUri = new Uri(normalized, UriKind.Absolute);
        _client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All
        })
        {
            BaseAddress = _baseUri,
            Timeout = timeout
        };
    }

    public async Task<HttpResponse<T?>> SendPostRequestAsync<T>(
        string url,
        string requestBody,
        Dictionary<string, string> headers)
    {
        var target = ResolveTarget(url);
        using var request = new HttpRequestMessage(HttpMethod.Post, target)
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        foreach (var header in headers)
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseContentRead).ConfigureAwait(false);
        try
        {
            var body = await response.Content.ReadFromJsonAsync<T?>(JsonOptions).ConfigureAwait(false);
            return new HttpResponse<T?>(body, (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or HttpRequestException or IOException)
        {
            // متن پاسخ هرگز لاگ/برگردانده نمی‌شود؛ فقط وضعیت قابل تفسیر نبودن.
            return new HttpResponse<T?>(UnparsableResponseStatus);
        }
    }

    private string ResolveTarget(string url)
    {
        // Path.Combine داخل SDK روی ویندوز از \ استفاده می‌کند؛ آدرس باید با / ساخته شود.
        var candidate = (url ?? "").Replace('\\', '/');
        if (!Uri.TryCreate(candidate, UriKind.RelativeOrAbsolute, out var parsed))
            throw new InvalidOperationException("آدرس سرویس نامعتبر است.");

        var target = parsed.IsAbsoluteUri ? parsed : new Uri(_baseUri, candidate);
        if (!target.IsAbsoluteUri || target.Scheme != _baseUri.Scheme || !target.Authority.Equals(_baseUri.Authority, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("آدرس درخواست خارج از میزبان سامانهٔ پیکربندی‌شده است.");
        return target.AbsoluteUri;
    }

    public void Dispose() => _client.Dispose();
}
