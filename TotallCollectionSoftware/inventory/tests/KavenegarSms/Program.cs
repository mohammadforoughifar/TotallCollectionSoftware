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

// Lookup must not require a sender: it uses an approved pattern, token and token10.
fake.ThrowTimeout = false;
fake.Body = Response();
var lookupOptions = Config(); lookupOptions.Sender = "";
gateway = Create(lookupOptions, fake);
var companyName = "نام فارسی & + = درصد%20";
var lookupBefore = fake.Count;
result = await gateway.SendLookupAsync("09120000000", "0001", companyName, "RepairAccept");
Check(result.State == KavenegarSmsSendState.Accepted && gateway.IsLookupConfigured && !gateway.IsConfigured,
    "Lookup is accepted without an ordinary sender, not claimed delivered.");
Check(fake.Count == lookupBefore + 1 && fake.Uri?.AbsolutePath.EndsWith("/verify/lookup.json") == true,
    "Repair lookup posts once to verify/lookup, not sms/send.");
var fields = (fake.RequestBody ?? "").Split('&', StringSplitOptions.RemoveEmptyEntries)
    .Select(part => part.Split('=', 2)).ToDictionary(pair => pair[0],
        pair => Uri.UnescapeDataString((pair.Length > 1 ? pair[1] : "").Replace("+", " ")));
Check(fields.Count == 8 && fields["receptor"] == "09120000000" && fields["token"] == "0001" &&
    fields["token10"] == companyName && fields["template"] == "RepairAccept",
    "Form decode preserves Persian, literal percent, ampersand, plus, equals and exactly one company value.");
Check(!fields.ContainsKey("sender") && !fields.ContainsKey("message"), "Lookup does not pass ordinary SMS fields.");
lookupBefore = fake.Count;
foreach (var (recipient, token, name, template) in new[] {
    ("09120000000,09120000001", "0001", "نام", "RepairAccept"),
    ("09120000000", "RP-0001", "نام", "RepairAccept"),
    ("09120000000", "0001", new string('ا', 101), "RepairAccept"),
    ("09120000000", "0001", "یک دو سه چهار پنج شش هفت", "RepairAccept"),
    ("09120000000", "0001", "نام_شرکت", "RepairAccept"),
    ("09120000000", "0001", "نام\nشرکت", "RepairAccept"),
    ("09120000000", "0001", "نام", "Repair_Accept") })
{
    result = await gateway.SendLookupAsync(recipient, token, name, template);
    Check(result.State == KavenegarSmsSendState.InvalidRequest && fake.Count == lookupBefore,
        "Invalid lookup input must not reach the provider.");
}
fake.Body = Response(providerStatus: 427);
result = await gateway.SendLookupAsync("09120000000", "0001", "فروشگاه", "RepairReady");
Check(result.State == KavenegarSmsSendState.Failed && result.ProviderCode == 427,
    "Lookup preserves a definite provider rejection.");
fake.ThrowNetworkError = true;
lookupBefore = fake.Count;
result = await gateway.SendLookupAsync("09120000000", "0001", "فروشگاه", "RepairReady");
Check(result.State == KavenegarSmsSendState.Unknown && fake.Count == lookupBefore + 1,
    "Uncertain lookup never retries automatically.");
fake.ThrowNetworkError = false;
lookupOptions.SdkSendEnabled = false;
gateway = Create(lookupOptions, fake);
lookupBefore = fake.Count;
result = await gateway.SendLookupAsync("09120000000", "0001", "فروشگاه", "RepairReady");
Check(result.State == KavenegarSmsSendState.Disabled && fake.Count == lookupBefore,
    "Default-off gateway prevents a lookup.");

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
