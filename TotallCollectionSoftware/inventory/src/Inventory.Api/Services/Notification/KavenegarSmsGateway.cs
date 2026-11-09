using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Inventory.Api.Services.ItAssets;
using Kavenegar;
using Kavenegar.Exceptions;
using Microsoft.Extensions.Options;

namespace Inventory.Api.Services;

/// <summary>The result of one SMS request, not an automatic retry instruction.</summary>
public enum KavenegarSmsSendState
{
    Disabled,
    NotConfigured,
    InvalidRequest,
    Accepted,
    Delivered,
    Failed,
    Unknown
}

/// <summary>
/// Accepted means the request was accepted by Kavenegar, not that the SMS reached the recipient.
/// Unknown means the caller must check the provider before considering another send.
/// </summary>
public sealed record KavenegarSmsSendResult(
    KavenegarSmsSendState State,
    long? MessageId = null,
    int? MessageStatus = null,
    int? ProviderCode = null);

/// <summary>Base server-side SDK gateway. No module or HTTP endpoint calls it yet.</summary>
public interface IKavenegarSmsGateway
{
    bool IsEnabled { get; }
    bool IsConfigured { get; }
    Task<KavenegarSmsSendResult> SendAsync(string recipient, string message);
}

public sealed class KavenegarSmsGateway : IKavenegarSmsGateway
{
    private readonly KavenegarSmsOptions _options;
    private readonly Func<HttpMessageHandler> _handlerFactory;

    public KavenegarSmsGateway(IOptions<KavenegarSmsOptions> options)
        : this(options, () => SharedTransport.Handler)
    {
    }

    // Tests supply an in-memory handler, so they never contact the SMS provider.
    internal KavenegarSmsGateway(IOptions<KavenegarSmsOptions> options, Func<HttpMessageHandler> handlerFactory)
    {
        _options = options.Value;
        _handlerFactory = handlerFactory;
    }

    public bool IsEnabled => _options.SdkSendEnabled;
    // Presence only; this does not verify the key, sender ownership or network connectivity.
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey)
                                && !string.IsNullOrWhiteSpace(_options.Sender);

    public async Task<KavenegarSmsSendResult> SendAsync(string recipient, string message)
    {
        if (!IsEnabled)
            return new(KavenegarSmsSendState.Disabled);
        if (!IsConfigured)
            return new(KavenegarSmsSendState.NotConfigured);
        if (string.IsNullOrWhiteSpace(recipient) || string.IsNullOrWhiteSpace(message)
            || recipient.IndexOfAny(new[] { ',', ';', '\r', '\n' }) >= 0)
            return new(KavenegarSmsSendState.InvalidRequest);

        // Kavenegar requires the key in the URL path. Do not use IHttpClientFactory's default
        // request-URI logging, allow redirects, log exceptions or return their raw messages.
        using var capture = new ReturnStatusHandler(_handlerFactory());
        using var http = new HttpClient(capture, disposeHandler: false)
        {
            Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 5, 120))
        };

        try
        {
            var key = Uri.EscapeDataString(_options.ApiKey.Trim());
            var api = new KavenegarApi(key, http);
            var sent = await api.SendAsync(_options.Sender.Trim(), recipient.Trim(), message).ConfigureAwait(false);

            // SDK 2.0.1 can return entries without checking return.status on an HTTP 200 response.
            // Require both a successful provider return and an identifiable message result.
            if (capture.ProviderCode != 200)
                return FromProviderCode(capture.ProviderCode);
            if (sent is null || sent.Messageid <= 0)
                return new(KavenegarSmsSendState.Unknown, ProviderCode: 200);

            var state = sent.Status switch
            {
                1 or 2 or 4 or 5 => KavenegarSmsSendState.Accepted,
                10 => KavenegarSmsSendState.Delivered,
                6 or 11 or 13 or 14 => KavenegarSmsSendState.Failed,
                _ => KavenegarSmsSendState.Unknown
            };
            return new(state, sent.Messageid, sent.Status, 200);
        }
        catch (ApiException ex)
        {
            return FromProviderCode(capture.ProviderCode ?? (int)ex.Code);
        }
        catch (Exception)
        {
            // Timeouts, malformed/empty SDK responses and transport errors may happen after the
            // provider received the request. Never retry automatically or leak exception text.
            return FromProviderCode(capture.ProviderCode);
        }
    }

    private static KavenegarSmsSendResult FromProviderCode(int? code)
    {
        // 408/409 can indicate an unresolved server/timeout outcome. Kavenegar's 501 is a
        // documented restriction to the account owner's test number, not an accepted request.
        var rejected = code is >= 400 and < 500 and not (408 or 409) or 501;
        return new(rejected ? KavenegarSmsSendState.Failed : KavenegarSmsSendState.Unknown,
            ProviderCode: code);
    }

    // A process-wide connection pool, with redirects disabled so a key-bearing URL cannot be
    // forwarded to another host. This nested class is lazy, allowing fully offline handler tests.
    private static class SharedTransport
    {
        internal static readonly SocketsHttpHandler Handler = new()
        {
            AllowAutoRedirect = false,
            // On .NET 8 this also prevents the per-request diagnostics handler from
            // publishing the key-bearing request URI to Activity/DiagnosticSource listeners.
            ActivityHeadersPropagator = null,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            UseCookies = false
        };
    }

    /// <summary>
    /// Captures only return.status while leaving the response content for the official SDK.
    /// The inner pooled handler is not owned by this per-request wrapper.
    /// </summary>
    private sealed class ReturnStatusHandler : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _inner;
        private HttpRequestMessage? _request;
        private HttpResponseMessage? _response;
        public int? ProviderCode { get; private set; }

        public ReturnStatusHandler(HttpMessageHandler inner)
        {
            _inner = new HttpMessageInvoker(inner, disposeHandler: false);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _request = request;
            var response = await _inner.SendAsync(request, cancellationToken).ConfigureAwait(false);
            _response = response;
            if (response.Content is not null)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                ProviderCode = ReadProviderCode(body);
            }
            return response;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // SDK 2.0.1 does not dispose these messages itself.
                _response?.Dispose();
                _request?.Dispose();
                _inner.Dispose();
            }
            base.Dispose(disposing);
        }

        private static int? ReadProviderCode(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (!TryGetProperty(doc.RootElement, "return", out var returned)
                    || !TryGetProperty(returned, "status", out var status))
                    return null;
                if (status.ValueKind == JsonValueKind.Number && status.TryGetInt32(out var number))
                    return number;
                if (status.ValueKind == JsonValueKind.String
                    && int.TryParse(status.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                    return number;
            }
            catch (JsonException)
            {
                // Unknown provider response; no raw content is retained or logged.
            }
            return null;
        }

        private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
                foreach (var property in element.EnumerateObject())
                    if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
            value = default;
            return false;
        }
    }
}
