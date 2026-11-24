using System.Net;
using System.Text;
using System.Text.Json;
using Inventory.Client.Services;
using Inventory.Shared.Dtos;

namespace EmailDeliveryUiTests;
public static class ClientTests
{
    public static async Task Run()
    {
        var checks = 0;
        void Check(bool ok, string description) { if (!ok) throw new Exception("FAIL: " + description); Console.WriteLine("PASS: " + description); checks++; }
        var transport = new ResponseHandler(); using var http = new HttpClient(transport);
        var auth = new TestAuth(); var js = new MemoryJs(); var opts = new ApiOptions { BaseUrl = "https://fixture.invalid" };
        var api = new ApiClient(http, opts, auth, js); var email = new EmailClientService(api, http, opts, auth);
        var outgoing = new OutgoingLetterService(api, http, auth);
        var id = Guid.NewGuid();
        Task<EmailDeliveryResultDto> Send() => email.SendAsync(10, "receiver@example.invalid", null, "test", "body", new() { new("test.txt", "text/plain", "synthetic attachment"u8.ToArray()) }, id);
        transport.Respond = _ => Task.FromResult(Json(new EmailDeliveryResultDto { RequestId = id, Status = EmailDeliveryStatus.Accepted, AttemptId = 5 }));
        var accepted = await Send();
        Check(accepted.SmtpAccepted && transport.Body!.Contains(id.ToString()) && transport.Body.Contains("synthetic attachment"), "actual email client transmits tracking UUID and selected attachment bytes");
        Check(transport.Authorization == "Bearer synthetic-token" && transport.Calls == 1, "multipart send preserves auth and performs exactly one HTTP request");
        transport.Respond = _ => Task.FromResult(Json(new { requestId = id, requestRejected = true, message = "safe validation", code = "ACCOUNT_UNAVAILABLE" }, HttpStatusCode.BadRequest));
        try { await Send(); throw new Exception("Expected rejection"); }
        catch (ApiException ex) { Check(ex.RequestRejected && ex.StatusCode == 400 && ex.Message == "safe validation", "validation with requestId is not misparsed as an Unknown delivery result"); }
        foreach (var response in new[] { new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"message\":\"old success\"}") },
            new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>private gateway fault</html>") },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") } })
        {
            transport.Respond = _ => Task.FromResult(response);
            var result = await Send();
            Check(result.RequestId == id && result.Status == EmailDeliveryStatus.Unknown && !result.ResultPersisted && !result.Message.Contains("private"), "malformed/legacy/lost HTTP outcome stays Unknown with original UUID");
        }
        transport.Respond = _ => throw new HttpRequestException("synthetic private network detail");
        var before = transport.Calls; var lost = await Send();
        Check(lost.Status == EmailDeliveryStatus.Unknown && transport.Calls == before + 1, "network failure does not infer send failure or automatically retry");
        transport.Respond = _ => throw new TaskCanceledException("synthetic timeout");
        Check((await Send()).Status == EmailDeliveryStatus.Unknown, "HTTP timeout retains uncertainty");
        transport.Respond = _ => Task.FromResult(Json(EmailDeliveryText.Uncertain(id), HttpStatusCode.ServiceUnavailable));
        Check((await Send()).Code == "HTTP_RESULT_UNKNOWN", "structured 503 outcome retains tracking payload");
        transport.Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("not JSON") });
        try { await Send(); throw new Exception("Expected forbidden"); }
        catch (ApiException ex) { Check(ex.RequestRejected && ex.StatusCode == 403, "explicit authorization rejection is differentiated from ambiguous transport error"); }
        before = transport.Calls;
        try { await email.SendAsync(10, "to@example.invalid", null, "test", "body", new(), Guid.Empty); throw new Exception("Expected missing key"); }
        catch (ApiException ex) { Check(ex.RequestRejected && transport.Calls == before, "client refuses missing request UUID before network"); }
        var registration = new DabirkhaneRegisterDto { EmailRequestId = id, EmailAccountId = 10, SendMethod = "ایمیل", SendByEmail = true };
        transport.Respond = _ => Task.FromResult(Json(new DabirkhaneRegisterResultDto { RegistrationSaved = true, Email = new() { RequestId = id, Status = EmailDeliveryStatus.Failed } }));
        var saved = await outgoing.DabirkhaneRegisterAsync(100, registration);
        Check(saved.RegistrationSaved == true && saved.Email!.Status == EmailDeliveryStatus.Failed, "outgoing client preserves registration-success/email-failure partial outcome");
        Check(transport.Company == "42", "registration still uses centralized active-company header");
        transport.Respond = _ => throw new HttpRequestException("private network fault");
        var uncertain = await outgoing.DabirkhaneRegisterAsync(100, registration);
        Check(uncertain.RegistrationSaved == null && uncertain.Email!.RequestId == id && !uncertain.Email.ResultPersisted, "lost registration response does not claim either rollback or email failure");
        transport.Respond = _ => Task.FromResult(Json(new { message = "safe validation", requestRejected = true }, HttpStatusCode.BadRequest));
        try { await outgoing.DabirkhaneRegisterAsync(100, registration); throw new Exception("Expected rejection"); }
        catch (ApiException ex) { Check(ex.RequestRejected && ex.StatusCode == 400, "ApiClient retains explicit pre-processing rejection for registration forms"); }
        var pending = EmailDeliveryText.Uncertain(id);
        await EmailRequestMemory.RememberAsync(js, 1, "organizational", pending);
        Check((await EmailRequestMemory.RestoreAsync(js, 1, "organizational"))!.RequestId == id && await EmailRequestMemory.RestoreAsync(js, 2, "organizational") == null, "session recovery UUID is isolated by login user");
        pending.Status = EmailDeliveryStatus.AcceptedWithWarnings;
        await EmailRequestMemory.RememberAsync(js, 1, "organizational", pending);
        Check((await EmailRequestMemory.RestoreAsync(js, 1, "organizational"))!.SmtpAccepted, "unpersisted observed acceptance survives browser recovery hint");
        pending.ResultPersisted = true; await EmailRequestMemory.RememberAsync(js, 1, "organizational", pending);
        Check(await EmailRequestMemory.RestoreAsync(js, 1, "organizational") == null, "terminal durable outcome clears pending browser hint without touching server history");
        js.Fail = true;
        Check(!await EmailRequestMemory.RememberAsync(js, 1, "organizational", pending), "disabled browser storage is reported, not a send failure");
        Console.WriteLine($"ALL {checks} EMAIL CLIENT/RECOVERY CHECKS PASSED.");
    }
    private static HttpResponseMessage Json(object value, HttpStatusCode code = HttpStatusCode.OK) => new(code)
    { Content = new StringContent(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json") };
    private sealed class ResponseHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> Respond { get; set; } = _ => throw new InvalidOperationException("no synthetic response configured");
        public int Calls; public string? Body, Authorization, Company;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Authorization = request.Headers.Authorization?.ToString(); Company = request.Headers.TryGetValues("X-Company-Id", out var values) ? values.Single() : null;
            return await Respond(request);
        }
    }
}
