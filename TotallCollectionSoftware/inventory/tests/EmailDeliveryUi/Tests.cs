using System.Net;
using Inventory.Client.Pages.Office;
using Inventory.Client.Pages.Office.Letters.Outgoing;
using Inventory.Client.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Probe = OutgoingLetterAvatarTests.Probe;

namespace EmailDeliveryUiTests;
public static class Tests
{
    public static async Task Run()
    {
        var checks = 0;
        void Check(bool ok, string description) { if (!ok) throw new Exception("FAIL: " + description); Console.WriteLine("PASS: " + description); checks++; }
        var f = new Fixture(); var services = new ServiceCollection(); var capture = new Capture();
        f.Register(services, true); services.AddSingleton<IComponentActivator>(capture);
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        Task<HtmlRootComponent> Render<T>(Dictionary<string, object?>? parameters = null) where T : IComponent => renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters ?? new())));
        Task<string> Html(HtmlRootComponent root) => renderer.Dispatcher.InvokeAsync(() => WebUtility.HtmlDecode(root.ToHtmlString()));
        Task Change(ComponentBase page, Action change) => renderer.Dispatcher.InvokeAsync(() => { change(); Probe.Refresh(page); });
        Task Act(ComponentBase page, string method, params object?[] args) => renderer.Dispatcher.InvokeAsync(async () => { if (Probe.Call(page, method, args) is Task task) await task; Probe.Refresh(page); });
        var root = await Render<Email>(); var page = capture.Last<Email>();
        Check(f.SendCalls == 0 && !Probe.Get<bool>(page, "loading"), "actual email page initializes without sending");
        await Act(page, "OpenCompose");
        await Change(page, () => { Probe.Set(page, "cpTo", "receiver@example.invalid"); Probe.Set(page, "cpSubject", "synthetic subject"); });
        f.Mode = "failed"; var firstId = Probe.Get<Guid>(page, "cpRequestId");
        await Act(page, "SubmitCompose");
        Check(Probe.Get<EmailDeliveryResultDto>(page, "cpResult").Status == EmailDeliveryStatus.Failed && Probe.Get<bool>(page, "composeOpen"), "definite failure stays in compose form with structured status");
        var html = await Html(root);
        Check(html.Contains("SMTP_AUTH_ERROR") && html.Contains("آماده‌سازی تلاش جدید") && html.Contains(firstId.ToString()), "error stage/code, tracking ID and explicit new-attempt action are visible");
        await Act(page, "SubmitCompose");
        Check(f.SendCalls == 1, "double-click/repeated submit is ignored while an outcome is awaiting review");
        await Act(page, "StartNewComposeAttempt", false);
        Check(Probe.Get<Guid>(page, "cpRequestId") != firstId && Probe.Get<EmailDeliveryResultDto?>(page, "cpResult") == null, "known-failed attempt is retried only with explicit new UUID");
        f.Mode = "warning"; await Act(page, "SubmitCompose");
        var accepted = Probe.Get<EmailDeliveryResultDto>(page, "cpResult");
        Check(accepted.SmtpAccepted && (await Html(root)).Contains("CONTACTS_SAVE_WARNING"), "post-send warning is shown without turning acceptance into failure");
        await Act(page, "CloseCompose"); await Act(page, "OpenCompose");
        Check(Probe.Get<EmailDeliveryResultDto>(page, "cpResult").RequestId == accepted.RequestId && f.SendCalls == 2, "closing/reopening compose preserves the result instead of silently generating a resend");
        f.Attempts[accepted.RequestId].Status = EmailDeliveryStatus.Sending;
        await Act(page, "CheckComposeDelivery");
        Check(Probe.Get<EmailDeliveryResultDto>(page, "cpResult").SmtpAccepted && f.SendCalls == 2, "stale durable status cannot erase SMTP acceptance observed by the form");
        await Act(page, "StartNewComposeAttempt", false);
        f.Mode = "http-loss"; await Act(page, "SubmitCompose"); var lostId = Probe.Get<Guid>(page, "cpRequestId");
        Check(Probe.Get<EmailDeliveryResultDto>(page, "cpResult").Status == EmailDeliveryStatus.Unknown && f.Js.Values.Values.Any(s => s.Contains(lostId.ToString())), "lost response remains Unknown and pending key is remembered before leaving page");
        f.RejectReplay = true; await Act(page, "ReplayCompose");
        Check(Probe.Get<EmailDeliveryResultDto>(page, "cpResult").RequestId == lostId && !Probe.Get<EmailDeliveryResultDto>(page, "cpResult").ResultPersisted, "auth rejection of a recovery request does not erase the earlier uncertain submission");
        f.RejectReplay = false; await Act(page, "ReplayCompose");
        Check(f.Keys.TakeLast(3).All(k => k == lostId) && Probe.Get<EmailDeliveryResultDto>(page, "cpResult").Replayed, "manual safe replay freezes UUID and payload across lost responses");
        await Act(page, "StartNewComposeAttempt", false); await Act(page, "SubmitCompose");
        var recoveryId = Probe.Get<Guid>(page, "cpRequestId"); var sendsBeforeReload = f.SendCalls;
        f.StatusFails = true;
        var recoveredRoot = await Render<Email>(); var recovered = capture.Last<Email>();
        Check(Probe.Get<EmailDeliveryResultDto>(recovered, "cpResult").RequestId == recoveryId && (await Html(recoveredRoot)).Contains("email-recovery-banner") && f.SendCalls == sendsBeforeReload, "page recreation restores tracking without silently resending or needing message-body storage");
        f.StatusFails = false; await Act(recovered, "CheckComposeDelivery");
        Check(Probe.Get<EmailDeliveryResultDto>(recovered, "cpResult").SmtpAccepted && f.SendCalls == sendsBeforeReload, "read-only recovery resolves to server acceptance without sending");
        await Act(page, "CheckComposeDelivery"); await Act(page, "StartNewComposeAttempt", false);
        await Change(page, () => Probe.Set(page, "_pendingBrowser", new List<IBrowserFile> { new UnreadableFile() }));
        var beforeFile = f.SendCalls; await Act(page, "SubmitCompose");
        Check(f.SendCalls == beforeFile && Probe.Get<string?>(page, "cpAttachmentError") != null && (await Html(root)).Contains("compose-attachment-error"), "unreadable selected browser file aborts the whole send instead of disappearing silently");
        await Act(page, "ClearPendingAttachments");
        Check(Probe.Get<string?>(page, "cpAttachmentError") == null, "user can explicitly clear bad files before a new submit");
        await Act(page, "SetTab", "sent"); html = await Html(root);
        Check(html.Contains("سابقهٔ قدیمی / دریافت‌شده از سرور") && html.Contains("ناموفق — ارسال انجام نشد") && html.Contains("delivery-status-label"), "sent-list badges distinguish legacy rows, failed preparation and accepted submissions");
        var beforeSync = f.SyncCalls; var beforeRead = f.ReadWrites;
        await Act(page, "SetTab", "delivery");
        Check(f.HistoryCalls > 0 && f.SyncCalls == beforeSync && f.ReadWrites == beforeRead && f.SendCalls == beforeFile, "history tab only reads ledger: no IMAP sync, marking read, or send");
        var history = capture.Last<EmailDeliveryHistory>();
        var statusReads = f.StatusCalls;
        await Change(history, () => Probe.Set(history, "lookupText", "not-a-key")); await Act(history, "LookupAsync");
        Check(f.StatusCalls == statusReads && Probe.Get<string?>(history, "lookupError") != null, "manual tracking input validates UUID without making a request");
        await Change(history, () => Probe.Set(history, "lookupText", recoveryId.ToString())); await Act(history, "LookupAsync");
        Check(Probe.Get<EmailDeliveryResultDto>(history, "lookupResult").RequestId == recoveryId && f.SendCalls == beforeFile,
            "manual tracking lookup retrieves the same authorized result without a send");
        f.StatusFails = true; await Act(history, "LookupAsync");
        Check(Probe.Get<EmailDeliveryResultDto>(history, "lookupResult").SmtpAccepted && Probe.Get<string?>(history, "lookupError") != null,
            "tracking lookup failure keeps observed acceptance and shows a separate read error");
        f.StatusFails = false;
        await Change(history, () => Probe.Set(history, "selectedStatus", (int)EmailDeliveryStatus.Failed)); await Act(history, "FilterChanged");
        Check((await Html(root)).Contains("SMTP_AUTH_ERROR"), "status filter renders durable reason/event data");
        f.StatusFails = true; await Act(history, "ReloadAsync");
        Check((await Html(root)).Contains("گزارش در دسترس نیست") && f.SendCalls == beforeFile, "history outage is an informational error, not automatic resend"); f.StatusFails = false;
        f.Navigation.SetUri("office/personal-email");
        var personalRoot = await Render<Email>(); var personal = capture.Last<Email>();
        await Act(personal, "SetTab", "delivery");
        Check(f.LastPersonal && f.LastAccount == 11, "shared personal page passes personal-only access scope to history");
        f.Auth.Personal = false;
        var deniedRoot = await Render<Email>();
        Check((await Html(deniedRoot)).Contains("دسترسی به ایمیل شخصی ندارید"), "personal-page permission gate remains intact");
        f.Auth.Personal = true; f.Navigation.SetUri("office/email");

        var freshFrom = new EmailDeliveryResultDto { RequestId = Guid.NewGuid(), AttemptId = 150, Status = EmailDeliveryStatus.Accepted, ResultPersisted = true };
        await Change(page, () => { Probe.Set(page, "cpResult", freshFrom); Probe.Set(page, "cpTo", "old@example.invalid"); Probe.Set(page, "cpCc", "old-cc@example.invalid"); Probe.Set(page, "cpSubject", "old subject"); Probe.Set(page, "cpBody", "old body"); Probe.Set(page, "_pendingBrowser", new List<IBrowserFile> { new UnreadableFile() }); });
        Check((await Html(root)).Contains("compose-fresh"), "durably completed outcome offers a distinct blank-message action, not only resend");
        var beforeFresh = f.SendCalls; await Act(page, "StartFreshCompose");
        Check(Probe.Get<EmailDeliveryResultDto?>(page, "cpResult") == null && Probe.Get<Guid>(page, "cpRequestId") != freshFrom.RequestId
            && new[] { "cpTo", "cpCc", "cpSubject", "cpBody" }.All(p => Probe.Get<string>(page, p) == "")
            && Probe.Get<List<IBrowserFile>>(page, "_pendingBrowser").Count == 0 && f.SendCalls == beforeFresh,
            "genuinely new message clears old recipients/body/files and generates a new key without sending");
        var unresolved = EmailDeliveryText.Uncertain(Guid.NewGuid());
        await Change(page, () => Probe.Set(page, "cpResult", unresolved)); await Act(page, "StartFreshCompose");
        Check(Probe.Get<EmailDeliveryResultDto>(page, "cpResult").RequestId == unresolved.RequestId && !(await Html(root)).Contains("compose-fresh"),
            "blank-message action cannot silently discard unresolved recovery evidence");
        await Change(page, () => { Probe.Set(page, "cpResult", null); Probe.Set(page, "cpTo", "receiver@example.invalid"); Probe.Set(page, "cpSubject", "truncated file test"); Probe.Set(page, "_pendingBrowser", new List<IBrowserFile> { new TruncatedFile() }); });
        await Act(page, "SubmitCompose");
        Check(f.SendCalls == beforeFresh && Probe.Get<string?>(page, "cpAttachmentError") != null,
            "browser stream shorter than declared file size aborts the entire send before HTTP");

        var letterRoot = await Render<OutgoingDabirkhane>(); var secretariat = capture.Last<OutgoingDabirkhane>();
        var letter = f.Letters.Outgoing[0]; await Act(secretariat, "OpenRegister", letter);
        Check(Probe.Get<int>(secretariat, "regEmailAccountId") == 0, "secretariat does not guess an official sender even with exactly one account");
        await Change(secretariat, () => { Probe.Set(secretariat, "regMethod", "ایمیل"); Probe.Set(secretariat, "regDestEmail", "receiver@example.invalid"); });
        await Act(secretariat, "OnMethodChanged"); await Act(secretariat, "SubmitRegister");
        Check(f.RegistrationCalls == 0, "registration submit is blocked until official sender is explicitly selected");
        await Change(secretariat, () => Probe.Set(secretariat, "regEmailAccountId", 10));
        f.Mode = "failed"; await Act(secretariat, "SubmitRegister");
        var registration = Probe.Get<DabirkhaneRegisterResultDto>(secretariat, "regResult"); html = await Html(letterRoot);
        Check(registration.RegistrationSaved == true && registration.Email!.Status == EmailDeliveryStatus.Failed && html.Contains("ثبت دبیرخانه ذخیره شد") && html.Contains("ناموفق — ارسال انجام نشد"), "secretariat visibly separates saved registration from failed email");
        var registrationId = f.LastRegistration!.EmailRequestId;
        await Act(secretariat, "SubmitRegister");
        Check(f.RegistrationCalls == 1, "secretariat double-submit is blocked while reviewing a result");
        await Act(secretariat, "StartNewRegistrationAttempt", false);
        Check(Probe.Get<Guid>(secretariat, "regRequestId") != registrationId, "new letter attempt receives a fresh UUID only after explicit action");
        f.Mode = "http-loss"; await Act(secretariat, "SubmitRegister"); var registrationRecovery = f.LastRegistration!.EmailRequestId;
        Check(Probe.Get<DabirkhaneRegisterResultDto>(secretariat, "regResult").RegistrationSaved == null, "HTTP loss never presents registration as definitely saved or rolled back");
        await Act(secretariat, "ReplayRegistration");
        Check(f.LastRegistration!.EmailRequestId == registrationRecovery && Probe.Get<DabirkhaneRegisterResultDto>(secretariat, "regResult").Email!.Replayed, "secretariat replay reuses immutable request UUID and payload");
        await Act(secretariat, "OpenEmailHistory", letter.LetterId);
        Check(f.LastLetter == letter.LetterId && (await Html(letterRoot)).Contains("email-delivery-history-modal"), "per-letter history passes letter scope and opens an independent read-only report");
        var prior = Probe.Get<DabirkhaneRegisterResultDto>(secretariat, "regResult").Email!;
        await Act(secretariat, "StartNewRegistrationAttempt", false);
        f.SuppressWith = prior.RequestId; await Act(secretariat, "SubmitRegister");
        Check(Probe.Get<DabirkhaneRegisterResultDto>(secretariat, "regResult").Email!.RequestId == prior.RequestId, "suppressed attempt follows existing tracking key rather than the discarded fresh key");
        f.SuppressWith = null;

        var unknown = new EmailDeliveryResultDto { RequestId = Guid.NewGuid(), Status = EmailDeliveryStatus.Unknown, AttemptId = 25, Message = "synthetic unknown" };
        var outcomeRoot = await Render<EmailDeliveryResult>(new() { ["Result"] = unknown, ["AllowNewAttempt"] = true, ["OnNewAttempt"] = EventCallback.Factory.Create<bool>(new object(), (bool _) => { }) });
        var outcome = capture.Last<EmailDeliveryResult>(); html = await Html(outcomeRoot);
        Check(html.Contains("delivery-explicit-resend") && html.Contains("disabled") && !html.Contains("delivery-retry"), "Unknown has no ordinary retry and explicit resend is gated by duplicate-risk confirmation");
        await Change(outcome, () => Probe.Set(outcome, "confirmed", true));
        Check(!(await Html(outcomeRoot)).Contains("disabled"), "explicit duplicate-risk confirmation enables only preparation of a new attempt");
        Check(f.Letters.IncomingDetailReads == 0 && f.Letters.OutgoingReadWrites == 0, "new history and recovery actions preserve letter read-state behavior");
        Console.WriteLine($"ALL {checks} EMAIL/SECRETARIAT RAZOR CHECKS PASSED.");
    }
    private sealed class Capture : IComponentActivator
    {
        private readonly List<IComponent> components = new();
        public T Last<T>() where T : IComponent => components.OfType<T>().Last();
        public IComponent CreateInstance(Type type) { var instance = (IComponent)Activator.CreateInstance(type)!; components.Add(instance); return instance; }
    }
    private sealed class TruncatedFile : IBrowserFile
    {
        public string Name => "truncated.txt"; public DateTimeOffset LastModified => DateTimeOffset.UtcNow; public long Size => 5; public string ContentType => "text/plain";
        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) => new MemoryStream(new byte[] { 1, 2 });
    }
    private sealed class UnreadableFile : IBrowserFile
    {
        public string Name => "unreadable.txt"; public DateTimeOffset LastModified => DateTimeOffset.UtcNow; public long Size => 3; public string ContentType => "text/plain";
        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) => throw new IOException("synthetic browser file loss");
    }
}
