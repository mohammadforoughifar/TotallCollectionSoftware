using System.Net;
using System.Net.Http;
using System.Text;
using Inventory.Api.Services;
using Inventory.Api.Services.ItAssets;
using Microsoft.Extensions.Options;

var assertions = 0;
void Check(bool condition, string message)
{
    assertions++;
    if (!condition) throw new InvalidOperationException(message);
}

KavenegarSmsOptions Config(bool enabled = true) => new()
{
    ApiKey = "fake-key/with?reserved",
    Sender = "1000000000",
    TimeoutSeconds = 20,
    SdkSendEnabled = enabled
};

KavenegarSmsGateway Create(KavenegarSmsOptions options, FakeHandler fake) =>
    new(Options.Create(options), () => fake);

static string Response(int? providerStatus = 200, int? messageStatus = 1, long messageId = 123)
{
    var returned = providerStatus.HasValue ? $"\"return\":{{\"status\":{providerStatus.Value}}}," : "";
    var entries = messageStatus.HasValue
        ? $"[{{\"messageid\":{messageId},\"status\":{messageStatus.Value}}}]"
        : "[]";
    return $"{{{returned}\"entries\":{entries}}}";
}

var fake = new FakeHandler();
var gateway = Create(Config(enabled: false), fake);
var result = await gateway.SendAsync("09120000000", "تست");
Check(result.State == KavenegarSmsSendState.Disabled && fake.Count == 0, "Disabled gateway must not send.");
Check(!gateway.IsEnabled && gateway.IsConfigured, "Enabled and configured flags are separate.");

var options = Config();
options.ApiKey = "";
gateway = Create(options, fake);
result = await gateway.SendAsync("09120000000", "تست");
Check(result.State == KavenegarSmsSendState.NotConfigured && fake.Count == 0, "Missing key must not send.");
options = Config();
options.Sender = " ";
gateway = Create(options, fake);
result = await gateway.SendAsync("09120000000", "تست");
Check(result.State == KavenegarSmsSendState.NotConfigured && fake.Count == 0, "Missing sender must not send.");

gateway = Create(Config(), fake);
foreach (var (recipient, message) in new[]
{
    ("", "متن"), ("09120000000", " "), ("09120000000,09120000001", "متن"),
    ("09120000000;09120000001", "متن"), ("09120000000\n09120000001", "متن")
})
{
    result = await gateway.SendAsync(recipient, message);
    Check(result.State == KavenegarSmsSendState.InvalidRequest && fake.Count == 0,
        "Invalid or multiple recipients must not send.");
}

fake.Body = Response();
result = await gateway.SendAsync(" 09120000000 ", "سلام");
Check(result.State == KavenegarSmsSendState.Accepted, "Queued SMS is accepted, not delivered.");
Check(result.MessageId == 123 && result.MessageStatus == 1 && result.ProviderCode == 200,
    "Provider tracking details must be preserved.");
Check(fake.Count == 1 && fake.Method == HttpMethod.Post, "A send must make exactly one POST.");
Check(fake.Uri is { Scheme: "https", Host: "api.kavenegar.com" }
      && fake.Uri.AbsolutePath.Contains("fake-key%2Fwith%3Freserved", StringComparison.OrdinalIgnoreCase),
    "The key must stay escaped in the expected HTTPS URL path.");
Check(fake.RequestBody?.Contains("receptor=09120000000") == true, "Recipient must be trimmed in the SDK request.");
Check(fake.RequestBody?.Contains("sender=1000000000") == true
      && fake.RequestBody.Contains("message="), "Sender and message must be passed to the SDK.");
Check(!result.ToString().Contains("fake-key", StringComparison.Ordinal), "Results must not expose the key.");

fake.Body = Response(messageStatus: 10);
result = await gateway.SendAsync("09120000000", "متن");
Check(result.State == KavenegarSmsSendState.Delivered, "Status 10 represents provider-confirmed delivery.");
foreach (var status in new[] { 6, 11, 13, 14 })
{
    fake.Body = Response(messageStatus: status);
    result = await gateway.SendAsync("09120000000", "متن");
    Check(result.State == KavenegarSmsSendState.Failed, "Undelivered or blocked messages must not be called accepted.");
}
fake.Body = "{\"Return\":{\"Status\":\"200\"},\"entries\":[{\"messageid\":123,\"status\":1}]}";
result = await gateway.SendAsync("09120000000", "متن");
Check(result.State == KavenegarSmsSendState.Accepted, "Provider code supports casing and string numbers.");
fake.Body = Response(messageStatus: 100);
result = await gateway.SendAsync("09120000000", "متن");
Check(result.State == KavenegarSmsSendState.Unknown, "An invalid message ID is not proof of rejection.");
fake.Body = Response(messageId: 0);
result = await gateway.SendAsync("09120000000", "متن");
Check(result.State == KavenegarSmsSendState.Unknown, "A missing message ID cannot be tracked.");

fake.Body = Response(providerStatus: 403, messageStatus: 1);
result = await gateway.SendAsync("09120000000", "متن");
Check(result.State == KavenegarSmsSendState.Failed && result.ProviderCode == 403,
    "An HTTP 200 must not hide a provider-level rejection.");
fake.Body = Response(providerStatus: 409, messageStatus: 1);
result = await gateway.SendAsync("09120000000", "متن");
Check(result.State == KavenegarSmsSendState.Unknown, "Provider busy is not definite rejection.");
fake.Body = Response(providerStatus: null, messageStatus: 1);
result = await gateway.SendAsync("09120000000", "متن");
Check(result.State == KavenegarSmsSendState.Unknown, "Missing provider status cannot be accepted.");
fake.Body = Response(providerStatus: 200, messageStatus: null);
result = await gateway.SendAsync("09120000000", "متن");
Check(result.State == KavenegarSmsSendState.Unknown, "An empty result must be unknown.");
fake.Body = "not json";
result = await gateway.SendAsync("09120000000", "متن");
Check(result.State == KavenegarSmsSendState.Unknown, "Malformed response must be unknown.");

fake.StatusCode = HttpStatusCode.BadRequest;
fake.Body = Response(providerStatus: 403, messageStatus: null);
result = await gateway.SendAsync("09120000000", "متن");
Check(result.State == KavenegarSmsSendState.Failed && result.ProviderCode == 403,
    "A provider API error must preserve its code.");
fake.StatusCode = HttpStatusCode.InternalServerError;
fake.Body = Response(providerStatus: 500, messageStatus: null);
result = await gateway.SendAsync("09120000000", "متن");
Check(result.State == KavenegarSmsSendState.Unknown, "Server errors have unknown delivery state.");
fake.StatusCode = HttpStatusCode.OK;
fake.ThrowNetworkError = true;
var beforeFailure = fake.Count;
result = await gateway.SendAsync("09120000000", "متن");
Check(result.State == KavenegarSmsSendState.Unknown && fake.Count == beforeFailure + 1,
    "Network errors must not trigger an automatic retry.");
Check(!result.ToString().Contains("fake-key", StringComparison.Ordinal)
      && !result.ToString().Contains("09120000000", StringComparison.Ordinal),
    "Raw exception details must not reach the result.");
fake.ThrowNetworkError = false;
fake.ThrowTimeout = true;
var beforeTimeout = fake.Count;
result = await gateway.SendAsync("09120000000", "متن");
Check(result.State == KavenegarSmsSendState.Unknown && fake.Count == beforeTimeout + 1,
    "A timeout has unknown delivery state and must not trigger an automatic retry.");

Console.WriteLine($"Kavenegar SDK gateway: {assertions} offline assertions passed.");

sealed class FakeHandler : HttpMessageHandler
{
    public int Count { get; private set; }
    public HttpMethod? Method { get; private set; }
    public Uri? Uri { get; private set; }
    public string? RequestBody { get; private set; }
    public string Body { get; set; } = "{}";
    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
    public bool ThrowNetworkError { get; set; }
    public bool ThrowTimeout { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Count++;
        Method = request.Method;
        Uri = request.RequestUri;
        RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        if (ThrowNetworkError)
            throw new HttpRequestException("fake-key/with?reserved 09120000000");
        if (ThrowTimeout)
            throw new OperationCanceledException("fake-key/with?reserved 09120000000");
        return new HttpResponseMessage(StatusCode)
        {
            Content = new StringContent(Body, Encoding.UTF8, "application/json")
        };
    }
}
