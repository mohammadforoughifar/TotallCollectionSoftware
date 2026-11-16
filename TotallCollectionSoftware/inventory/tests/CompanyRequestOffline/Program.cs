using System.Net;
using System.Text;
using Inventory.Client.Services;
using Microsoft.JSInterop;

var checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
    Console.WriteLine("PASS: " + message);
}

var fakeHttp = new FakeHandler();
var js = new FakeJs();
var api = new ApiClient(new HttpClient(fakeHttp), new ApiOptions { BaseUrl = "https://example.test" }, new FakeAuth(), js);
var first = await api.PostAsync<Response>("api/example", new { value = "normal" });
Check(first?.Ok == true && fakeHttp.Company == "1" && fakeHttp.Method == HttpMethod.Post && js.Calls == 1,
    "ordinary requests retain the global active-company header");
Check(fakeHttp.Authorization == "Bearer fake-auth" && fakeHttp.Body?.Contains("normal") == true,
    "ordinary authentication and JSON body are preserved");

var second = await api.PostForCompanyAsync<Response>("api/servers/alerts/1/work-order", 2, new { value = "selected" });
Check(second?.Ok == true && fakeHttp.Company == "2" && js.Calls == 1,
    "the form's company replaces the global company only for this request");
Check(fakeHttp.Authorization == "Bearer fake-auth" && fakeHttp.Body?.Contains("selected") == true,
    "the company-scoped request preserves authentication and body");

await api.GetAsync<Response>("api/example");
Check(fakeHttp.Company == "1" && js.Calls == 2,
    "a later request still uses the unchanged global selection");

var before = fakeHttp.Calls;
foreach (var invalid in new[] { 0, -1 })
{
    var rejected = false;
    try { await api.PostForCompanyAsync<Response>("api/example", invalid); }
    catch (ArgumentOutOfRangeException) { rejected = true; }
    Check(rejected && fakeHttp.Calls == before, "an invalid explicit company makes no HTTP request");
}
Console.WriteLine($"Company request transport: {checks} offline checks passed.");

sealed class Response { public bool Ok { get; set; } }
sealed class FakeAuth : IAuthState { public string? Token => "fake-auth"; }
sealed class FakeJs : IJSRuntime
{
    public int Calls;
    public ValueTask<T> InvokeAsync<T>(string identifier, params object?[]? args)
    {
        if (identifier != "localStorage.getItem" || args?[0]?.ToString() != "activeCompanyId")
            throw new InvalidOperationException("unexpected JS call");
        Calls++;
        return ValueTask.FromResult((T)(object)"1");
    }
}
sealed class FakeHandler : HttpMessageHandler
{
    public int Calls;
    public string? Company, Authorization, Body;
    public HttpMethod? Method;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++;
        Company = request.Headers.TryGetValues("X-Company-Id", out var values) ? string.Join(",", values) : null;
        Authorization = request.Headers.Authorization?.ToString();
        Method = request.Method;
        Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json") };
    }
}
