using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Inventory.Client.Services;
using Inventory.Client.Services.RadisHr;
using Inventory.Shared.Dtos;
using Microsoft.JSInterop;
using RadisHr.Shared.Contracts;
using HrFa = Inventory.Client.Services.RadisHr.Fa;
using CoreLogin = Inventory.Shared.Dtos.LoginResponse;

var passed = 0;
var failed = new List<string>();
void Check(string name, bool ok)
{
    if (ok) passed++;
    else { failed.Add(name); Console.Error.WriteLine("FAIL: " + name); }
}

// The two former PagedResult<T> definitions are now one compatible wire contract.
var page = new PagedResult<int> { Items = new() { 1, 2 }, TotalCount = 41, PageSize = 20 };
Check("catalog count is available to projects", page.Total == 41);
Check("project page count", page.PageCount == 3);
page.Total = 7;
Check("project count is available to catalog", page.TotalCount == 7);
page.PageSize = 0;
Check("unpaged results", page.PageCount == 1);
page.Total = 0;
page.PageSize = 20;
Check("empty page", page.PageCount == 1);
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var catalogPage = JsonSerializer.Deserialize<PagedResult<int>>("{\"items\":[1],\"totalCount\":21}", json)!;
var projectPage = JsonSerializer.Deserialize<PagedResult<int>>("{\"items\":[1],\"total\":45,\"page\":2,\"pageSize\":20,\"sumTicks\":1234}", json)!;
Check("old catalog JSON", catalogPage.Total == 21);
Check("old project JSON", projectPage.TotalCount == 45 && projectPage.PageCount == 3 && projectPage.SumTicks == 1234);
using (var resultJson = JsonDocument.Parse(JsonSerializer.Serialize(projectPage, json)))
{
    Check("both JSON count names preserved", resultJson.RootElement.GetProperty("total").GetInt32() == 45
        && resultJson.RootElement.GetProperty("totalCount").GetInt32() == 45);
}

// These calls used to throw CultureNotFoundException under InvariantGlobalization.
Check("Persian numbers without ICU", HrFa.N(1234) == "۱٬۲۳۴");
Check("Persian percent without ICU", HrFa.Percent(12.5m) == "۱۲٫۵٪");
Check("Latin rial input", HrFa.Rial(1234) == "1,234");
Check("Persian/Arabic input", HrFa.ParseRial("۱۲٬۳٤۵") == 12345);
Check("all HR pages are native", AppNav.Items.Length == 13 && AppNav.Items.All(i => i.Route.StartsWith("/hr")));
Check("unique HR routes", AppNav.Items.Select(i => i.Route).Distinct().Count() == 13);
Check("legacy deep link", AppNav.ByRoute("radis-hr/employee-entry?id=2")?.Page == "employeeEntry");
Check("native dashboard alias", AppNav.ByRoute("hr/dashboard#stats")?.Page == "dashboard");

var js = new MemoryJs();
var auth = new AuthState(js);
var hr = new RadisHrAuthState(auth);
var toasts = new ToastService();
var handler = new RecordingHandler();
using var http = new HttpClient(handler);
// The shell historically sets a default header. It must never override the current HR request.
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "stale-token");
var api = new RadisHrApiClient(http, new ApiOptions { BaseUrl = "https://5100-example.e2b.app" }, auth, toasts);

Task SignIn(string token, string role = "Operator", params string[] permissions) => auth.SignInAsync(new CoreLogin
{
    UserId = 42, Username = "test", DisplayName = "کاربر آزمایشی", Token = token, Role = role,
    Permissions = permissions.ToList()
});

Check("anonymous HR access denied", !hr.HasAccess);
await SignIn("token-1", "Operator", "RadisHr.Access");
Check("shared HR session", hr.HasAccess && hr.UserKey == "hr" && hr.DisplayName == auth.DisplayName);
Check("no second storage session", js.Storage.Keys.SequenceEqual(new[] { "authSession" }));
handler.Respond = () => RecordingHandler.Json("[{\"id\":1}]");
var employees = await api.GetListAsync<JsonElement>("api/employees?search=Ali");
Check("native API and query", handler.LastUri == "https://5100-example.e2b.app/api/hr/employees?search=Ali");
Check("current bearer", handler.LastToken == "token-1" && employees.Count == 1);
await SignIn("token-2", "Admin");
await api.GetListAsync<JsonElement>("api/employees");
Check("same adapter uses new login", handler.LastToken == "token-2" && hr.UserKey == "ceo");

handler.Respond = () => RecordingHandler.Json("{}", HttpStatusCode.Forbidden);
Check("403 returns failure", !await api.SendAsync(HttpMethod.Post, "api/employees", new { code = "X" }));
Check("403 keeps shared session", auth.IsLoggedIn);
Check("403 is visible", toasts.Toasts.Last().Message.Contains("دسترسی"));
handler.Respond = () => RecordingHandler.Json("{}", HttpStatusCode.Unauthorized);
await api.GetAsync<JsonElement>("api/employees");
Check("401 logs out core and HR", !auth.IsLoggedIn && !hr.HasAccess && js.Storage.Count == 0);
var requests = handler.Count;
await api.GetAsync<JsonElement>("api/employees");
Check("logout cannot reuse stale default bearer", handler.Count == requests);

await SignIn("token-upload", "Operator", "RadisHr.Access");
handler.Respond = () => RecordingHandler.Json("{\"id\":7,\"uid\":\"file-uid\",\"name\":\"test.txt\",\"contentType\":\"text/plain\",\"size\":3,\"message\":\"file-uid\"}");
using (var form = new MultipartFormDataContent())
{
    form.Add(new ByteArrayContent(new byte[] { 1, 2, 3 }), "file", "test.txt");
    var upload = await api.PostFormAsync<FileUploadResponse>("api/files", form);
    Check("upload UID contract", upload?.Uid == "file-uid" && upload.Id == 7);
    Check("multipart uses native API and current bearer", handler.LastUri.EndsWith("/api/hr/files") && handler.LastToken == "token-upload");
}
handler.Respond = () =>
{
    var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) };
    response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
    response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "test.txt" };
    return response;
};
var download = await api.GetFileAsync("api/files/file-uid");
Check("authenticated download", download is { } file && file.Data.Length == 3 && file.FileName == "test.txt");
Check("no token in download URL", !handler.LastUri.Contains("token") && handler.LastToken == "token-upload");
handler.Respond = () => RecordingHandler.Json("", HttpStatusCode.NoContent);
Check("204 mutation succeeds", await api.SendAsync(HttpMethod.Delete, "api/files/file-uid"));
handler.Respond = () => RecordingHandler.Json("{}", HttpStatusCode.Unauthorized);
using (var form = new MultipartFormDataContent())
    await api.PostFormAsync<FileUploadResponse>("api/files", form);
Check("multipart 401 expires shared session", !auth.IsLoggedIn);
await SignIn("token-download", "Admin");
await api.GetFileAsync("api/files/file-uid");
Check("download 401 expires shared session", !auth.IsLoggedIn);
await SignIn("no-hr", "Operator");
Check("ordinary operator has no HR access", !hr.HasAccess);

Console.WriteLine($"Regression tests: {passed} passed, {failed.Count} failed.");
return failed.Count == 0 ? 0 : 1;

sealed class RecordingHandler : HttpMessageHandler
{
    public Func<HttpResponseMessage> Respond { get; set; } = () => Json("{}");
    public string LastUri { get; private set; } = "";
    public string? LastToken { get; private set; }
    public int Count { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Count++;
        LastUri = request.RequestUri!.ToString();
        LastToken = request.Headers.Authorization?.Parameter;
        return Task.FromResult(Respond());
    }
    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
}

sealed class MemoryJs : IJSRuntime
{
    public Dictionary<string, string> Storage { get; } = new();
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        var key = (string)args![0]!;
        switch (identifier)
        {
            case "localStorage.getItem":
                return ValueTask.FromResult((TValue)(object?)Storage.GetValueOrDefault(key)!);
            case "localStorage.setItem": Storage[key] = (string)args[1]!; break;
            case "localStorage.removeItem": Storage.Remove(key); break;
            default: throw new NotSupportedException(identifier);
        }
        return ValueTask.FromResult(default(TValue)!);
    }
}
