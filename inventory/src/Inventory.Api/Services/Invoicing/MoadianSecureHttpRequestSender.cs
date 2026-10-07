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

    /// <summary>
    /// تشخیصی آخرین فراخوانی: نام عملیات، وضعیت HTTP و خطای خود سامانه (code/detail).
    /// فقط وقتی سامانه خطا برگردانده ثبت می‌شود؛ پاسخ‌های موفق (که شامل توکن هستند) هرگز ذخیره نمی‌شوند.
    /// </summary>
    public sealed record CallDiagnostics(string Operation, int StatusCode, string? ErrorCode, string? ErrorDetail);

    private CallDiagnostics? _lastCall;

    public CallDiagnostics? LastCall => _lastCall;

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
        var rawBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        CaptureDiagnostics(new Uri(target), (int)response.StatusCode, rawBody);
        try
        {
            var body = rawBody.Length == 0 ? default(T?) : JsonSerializer.Deserialize<T>(rawBody, JsonOptions);
            return new HttpResponse<T?>(body, (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or FormatException or OverflowException or ArgumentException)
        {
            // متن پاسخ هرگز لاگ/برگردانده نمی‌شود؛ فقط وضعیت قابل تفسیر نبودن.
            return new HttpResponse<T?>(UnparsableResponseStatus);
        }
    }

    /// <summary>فقط خطاهای سامانه را ثبت می‌کند (code/detail از errors[0]) — رازی (توکن) در آن نیست.</summary>
    private void CaptureDiagnostics(Uri target, int status, string rawBody)
    {
        if (status is >= 200 and < 300)
        {
            _lastCall = null;
            return;
        }

        string? code = null;
        string? detail = null;
        try
        {
            if (rawBody.Length > 0)
            {
                using var doc = JsonDocument.Parse(rawBody);
                if (doc.RootElement.TryGetProperty("errors", out var errors)
                    && errors.ValueKind == JsonValueKind.Array
                    && errors.GetArrayLength() > 0
                    && errors[0].ValueKind == JsonValueKind.Object)
                {
                    code = ReadStringProp(errors[0], "errorCode") ?? ReadStringProp(errors[0], "ErrorCode");
                    detail = ReadStringProp(errors[0], "detail") ?? ReadStringProp(errors[0], "Detail");
                }
            }
        }
        catch (JsonException)
        {
            // پاسخ غیر-JSON؛ فقط وضعیت HTTP ثبت می‌شود.
        }

        _lastCall = new CallDiagnostics(PathOnly(target), status, code, detail);
    }

    private static string? ReadStringProp(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;

    private static string PathOnly(Uri target)
    {
        var path = target.AbsolutePath.TrimStart('/');
        return path.Length == 0 ? "(root)" : path;
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
