using System.Text.Json;
using Inventory.Api.Controllers.Office;
using Inventory.Api.Data;
using Inventory.Api.Services.Office.Email;
using Inventory.Shared.Dtos;
using MailKit.Net.Smtp;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmailDeliveryTests;

public static class ServerTests
{
    private static int checks;
    private static void Check(bool ok, string description)
    { if (!ok) throw new InvalidOperationException("FAIL: " + description); Console.WriteLine("PASS: " + description); checks++; }
    private static async Task<T> Throws<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T error) { return error; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    public static async Task RunAsync()
    {
        await ServiceAndHistory();
        await Failures();
        await RegistrationWarnings();
        await LegacySystemAdapter();
        await Coordination();
        await Concurrency();
        await Protocol();
        await Controllers();
        await SqlServerTranslation();
        Console.WriteLine($"ALL {checks} EMAIL SERVER CHECKS PASSED (synthetic only; SQL Server commands intercepted, not executed).");
    }
    private static async Task ServiceAndHistory()
    {
        await using var f = new ServerFixture(); await f.Initialize();
        Check(await f.Db.EmailSendAttempts.CountAsync() == 0, "production additive SQLite schema runs twice without inventing legacy outcomes");
        var dto = ServerFixture.Compose();
        f.Smtp.Before = async () =>
        {
            var pending = await f.Store.FindAsync(dto.RequestId);
            Check(pending?.Status == EmailDeliveryStatus.Sending && pending.SentId > 0, "durable Sending + sent reference precede actual SMTP invocation");
            Check(await f.Db.OtoEmailAttachments.CountAsync() == 1 && Directory.GetFiles(f.Root, "*", SearchOption.AllDirectories).Any(p => p.EndsWith("test.pdf")), "attachment bytes and metadata persist before SMTP");
        };
        var result = await f.Email.SendAsync(dto, 1, false, new[] { ("test.pdf", "application/pdf", "%PDF-test"u8.ToArray()) });
        f.Smtp.Before = null;
        Check(result.SmtpAccepted && result.ResultPersisted && result.AcceptedAtUtc.HasValue, "actual compose returns observed SMTP acceptance with durable timestamp");
        Check(f.Smtp.MessageId == $"{dto.RequestId:N}@example.invalid" && f.Smtp.Attachments == 1, "request-stable Message-Id and complete attachment reach transport");
        var replay = await f.Email.SendAsync(dto, 1, false, new[] { ("test.pdf", "application/pdf", "%PDF-test"u8.ToArray()) });
        Check(replay.Replayed && replay.RequestId == dto.RequestId && f.Smtp.Calls == 1 && await f.Db.OtoSentEmails.CountAsync() == 1, "same-ID same-payload replay never prepares or sends a second email");
        dto.Body += "changed";
        var conflict = await f.Email.SendAsync(dto, 1, false, new[] { ("test.pdf", "application/pdf", "%PDF-test"u8.ToArray()) });
        Check(conflict.Code == "REQUEST_ID_CONFLICT" && !conflict.SmtpAccepted && f.Smtp.Calls == 1, "same-ID changed body is a conflict, not a new send");
        await f.Email.AddDeliveryWarningAsync(conflict, "notify", "TEST_WARNING", "must not corrupt original");
        Check(!(await f.Store.FindAsync(dto.RequestId))!.EventsJson.Contains("TEST_WARNING"), "conflict response cannot overwrite another attempt via warning callback");
        dto.Body = "synthetic body only";
        var changedFile = await f.Email.SendAsync(dto, 1, false, new[] { ("test.pdf", "application/pdf", "different bytes"u8.ToArray()) });
        Check(changedFile.Code == "REQUEST_ID_CONFLICT" && f.Smtp.Calls == 1, "attachment content contributes to the idempotency fingerprint");
        await Throws<EmailPreparationException>(() => f.Email.SendAsync(ServerFixture.Compose(12), 1, true, Array.Empty<(string, string, byte[])>()));
        Check(f.Smtp.Calls == 1, "official email admin cannot send from another user's private account");
        await f.Email.SendAsync(ServerFixture.Compose(11), 1, false, Array.Empty<(string, string, byte[])>());
        await f.Email.SendAsync(ServerFixture.Compose(12), 2, false, Array.Empty<(string, string, byte[])>());
        var officialOther = await f.Email.SendAsync(ServerFixture.Compose(13), 1, true, Array.Empty<(string, string, byte[])>());
        Check(await f.Store.GetStatusAsync(officialOther.RequestId, 1, false) == null && await f.Store.GetStatusAsync(officialOther.RequestId, 1, true) != null, "current official-account authorization scopes tracking reads");
        Check(await f.Store.GetStatusAsync(dto.RequestId, 2, false) == null, "knowing a tracking GUID does not grant account access");
        var personal = await f.Store.GetHistoryAsync(1, true, true, null, null, null, null, 1, 15);
        Check(personal.Items.Count == 1 && personal.Items[0].EmailAccountId == 11, "personal-only history excludes official and other-user accounts");
        var all = await f.Store.GetHistoryAsync(1, true, false, null, null, null, null, 1, 2);
        Check(all.TotalCount == 3 && all.Items.Count == 2, "history count and database pagination retain access scope");
        var huge = await f.Store.GetHistoryAsync(1, true, false, null, null, null, null, int.MaxValue, int.MaxValue);
        Check(huge.PageSize == 100 && huge.Items.Count == 0, "untrusted page/size is bounded without integer overflow");
        f.Db.OtoSentEmails.Add(new() { EmailId = 10, UId = "old@example.invalid", Subject = "legacy", Body = "old", ToDisplay = "test@example.invalid" });
        await f.Db.SaveChangesAsync();
        var sent = await f.Email.GetSentAsync(1, 10, null, false, false);
        Check(sent.Items.Single(x => x.Subject == "legacy").DeliveryStatus == null && sent.Items.Single(x => x.Id == result.SentId).DeliveryStatus == EmailDeliveryStatus.Accepted, "sent list distinguishes actual ledger acceptance from old/IMAP records");
        var detail = await f.Email.GetMessageAsync("Sent", result.SentId!.Value, 1, true, false);
        Check(detail.DeliveryRequestId == dto.RequestId && detail.DeliveryStatus == EmailDeliveryStatus.Accepted, "sent detail exposes the matching durable tracking ID");
        f.Db.OtoEmailFolders.Add(new() { UserId = 1, Title = "synthetic", TypeEmail = 1, IsFolder = true }); await f.Db.SaveChangesAsync();
        var folder = await f.Db.OtoEmailFolders.FirstAsync();
        await f.Email.ArchiveAsync(new() { Box = "Sent", Id = result.SentId.Value, FolderId = folder.EmailFolderId }, 1, false);
        var archive = await f.Email.GetArchiveAsync(1, 10, null, false, false);
        Check(archive.Items.Single().DeliveryStatus == EmailDeliveryStatus.Accepted, "archive hydration preserves send status without touching read state");
        Check(!JsonSerializer.Serialize(await f.Store.GetStatusAsync(dto.RequestId, 1, false)).Contains("synthetic-password") &&
            !JsonSerializer.Serialize(all).Contains("synthetic body only"), "ledger payload has no password, body, or attachment data");
    }
    private static async Task Failures()
    {
        await using var f = new ServerFixture(); await f.Initialize();
        foreach (var (to, cc) in new[] { ("invalid", ""), ("valid@example.invalid, bad", ""), ("valid@example.invalid", "bad") })
        {
            var dto = ServerFixture.Compose(); dto.To = to; dto.Cc = cc;
            var result = await f.Email.SendAsync(dto, 1, true, Array.Empty<(string, string, byte[])>());
            Check(result.Status == EmailDeliveryStatus.Failed && result.Code == "INVALID_RECIPIENT" && result.CanRetry && f.Smtp.Calls == 0, "invalid To/Cc aborts whole message instead of silently dropping recipient");
        }
        var oversized = await f.Email.SendAsync(ServerFixture.Compose(), 1, true, new[] { (new string('x', 256), "text/plain", new byte[1]) });
        Check(oversized.Code == "ATTACHMENT_LIMIT" && f.Smtp.Calls == 0, "attachment name/size validation fails before SMTP");
        f.Print.Throw = true;
        var pdf = await f.Email.SendLetterEmailAsync(100, ServerFixture.Registration(), 1, "synthetic");
        Check(pdf.Code == "LETTER_PDF_ERROR" && pdf.CanRetry && f.Smtp.Calls == 0 && !pdf.Message.Contains("private"), "signed-PDF exception is durable safe failure, not a partial email");
        Check((await f.Store.FindAsync(pdf.RequestId))!.GuardKey == null, "definite pre-SMTP failure releases only its own letter guard");
        f.Print.Throw = false; f.Print.Empty = true;
        var empty = await f.Email.SendLetterEmailAsync(100, ServerFixture.Registration(), 1, "synthetic");
        Check(empty.Code == "LETTER_PDF_EMPTY" && f.Smtp.Calls == 0 && f.Print.RequiresSignatures, "empty signed PDF is rejected and print signature enforcement remains enabled");
        f.Print.Empty = false;
        f.Db.AppAttachments.Add(new() { Module = "OutgoingLetters", RefId = 100, FileName = "missing.pdf", ContentType = "application/pdf", FilePath = "uploads/synthetic-file-that-does-not-exist.pdf" });
        await f.Db.SaveChangesAsync();
        var missing = await f.Email.SendLetterEmailAsync(100, ServerFixture.Registration(), 1, "synthetic");
        Check(missing.Code == "LETTER_ATTACHMENT_UNAVAILABLE" && missing.CanRetry && f.Smtp.Calls == 0, "missing letter attachment stops transmission and records actionable reason");
        f.AttachmentFailure.Fail = true;
        var metadata = await f.Email.SendAsync(ServerFixture.Compose(), 1, true, new[] { ("test.txt", "text/plain", "test"u8.ToArray()) });
        Check(metadata.Status == EmailDeliveryStatus.Failed && metadata.ResultPersisted && f.Smtp.Calls == 0, "attachment metadata DB failure remains durably recorded despite failed ChangeTracker entries");
        Check(!f.Db.ChangeTracker.Entries<OtoEmailAttachment>().Any(e => e.State == EntityState.Added), "failed attachment entries are detached rather than poisoning subsequent ledger saves");
        f.AttachmentFailure.Fail = false;
        f.Smtp.Outcome = EmailDeliveryStatus.Failed;
        var rejected = await f.Email.SendAsync(ServerFixture.Compose(), 1, true, Array.Empty<(string, string, byte[])>());
        Check(rejected.CanRetry && rejected.Status == EmailDeliveryStatus.Failed, "definite rejection permits only an explicit new attempt");
        var request = ServerFixture.Registration(); f.Smtp.Outcome = EmailDeliveryStatus.Unknown;
        var uncertain = await f.Email.SendLetterEmailAsync(101, request, 1, "synthetic");
        var previousCalls = f.Smtp.Calls;
        var blocked = await f.Email.SendLetterEmailAsync(101, ServerFixture.Registration(), 1, "synthetic");
        Check(uncertain.Status == EmailDeliveryStatus.Unknown && !uncertain.CanRetry && blocked.DuplicateSuppressed && blocked.RequestId == request.EmailRequestId && f.Smtp.Calls == previousCalls, "unknown letter submission retains guard and returns the existing tracking ID");
        await using var disk = new ServerFixture(); await disk.Initialize();
        Directory.CreateDirectory(Path.Combine(disk.Root, "wwwroot/uploads/office"));
        await File.WriteAllTextAsync(Path.Combine(disk.Root, "wwwroot/uploads/office/email"), "synthetic blocking file");
        var diskError = await disk.Email.SendAsync(ServerFixture.Compose(), 1, true, new[] { ("test.txt", "text/plain", new byte[1]) });
        Check(diskError.CanRetry && disk.Smtp.Calls == 0, "local attachment storage error cannot become an incomplete successful email");
    }
    private static async Task RegistrationWarnings()
    {
        await using var f = new ServerFixture(); await f.Initialize();
        f.Bank.Fail = true; f.Notify.Fail = true; f.Notify.FailBroadcast = true;
        var dto = ServerFixture.Registration();
        var result = await f.Letters.DabirkhaneRegisterAsync(100, dto, 1, "synthetic");
        Check(result.RegistrationSaved == true && result.Email?.Status == EmailDeliveryStatus.AcceptedWithWarnings && result.Warning != null, "registration, SMTP acceptance and post-commit warnings are separate outcomes");
        Check(result.Email!.Events.Any(e => e.Code == "CONTACTS_SAVE_WARNING") && result.Email.Events.Any(e => e.Code == "NOTIFICATION_WARNING") && result.Email.Events.Any(e => e.Code == "BROADCAST_WARNING"), "contact, notification and broadcast faults have durable named events");
        Check((await f.Store.GetStatusAsync(dto.EmailRequestId, 1, true))!.Status == EmailDeliveryStatus.AcceptedWithWarnings && f.Smtp.Calls == 1, "post-send warning writes never undo or repeat SMTP acceptance");
        var replay = await f.Letters.DabirkhaneRegisterAsync(100, dto, 1, "synthetic");
        Check(replay.Email!.Replayed && f.Smtp.Calls == 1 && f.Bank.RememberCalls == 1 && f.Notify.Calls == 1, "registration replay avoids repeat SMTP, contacts, and creator notification");
        var oldNote = (await f.Db.OutgoingLetters.AsNoTracking().SingleAsync(l => l.Id == 100)).DabirkhaneNote;
        dto.Note = "changed payload using an existing key";
        var conflict = await f.Letters.DabirkhaneRegisterAsync(100, dto, 1, "synthetic");
        Check(conflict.Email!.Code == "REQUEST_ID_CONFLICT" &&
            (await f.Db.OutgoingLetters.AsNoTracking().SingleAsync(l => l.Id == 100)).DabirkhaneNote == oldNote,
            "registration detects same-key conflict before overwriting committed registration fields");
        dto.Note = null;
        var fresh = ServerFixture.Registration();
        var suppressed = await f.Letters.DabirkhaneRegisterAsync(100, fresh, 1, "synthetic");
        Check(suppressed.Email!.DuplicateSuppressed && suppressed.Email.RequestId == dto.EmailRequestId && f.Smtp.Calls == 1, "fresh request key alone does not resend an already accepted letter");
        fresh.ResendOfRequestId = dto.EmailRequestId;
        var explicitResend = await f.Letters.DabirkhaneRegisterAsync(100, fresh, 1, "synthetic");
        Check(explicitResend.Email!.SmtpAccepted && !explicitResend.Email.Replayed && f.Smtp.Calls == 2, "explicit resend names exact predecessor and creates a separate attempt");
        Check((await f.Store.FindAsync(fresh.EmailRequestId))!.PreviousRequestId == dto.EmailRequestId && (await f.Store.FindAsync(dto.EmailRequestId))!.GuardKey == null, "resend links history and atomically transfers the letter guard");
        f.Print.Throw = true;
        var failed = await f.Letters.DabirkhaneRegisterAsync(102, ServerFixture.Registration(), 1, "synthetic");
        Check(failed.RegistrationSaved == true && failed.Email!.Status == EmailDeliveryStatus.Failed && (await f.Db.OutgoingLetters.AsNoTracking().SingleAsync(l => l.Id == 102)).DabirkhaneSabt, "PDF failure does not falsely roll back an already-saved secretariat registration");
        var privateSender = ServerFixture.Registration(); privateSender.EmailAccountId = 11;
        await Throws<EmailPreparationException>(() => f.Letters.DabirkhaneRegisterAsync(101, privateSender, 1, "synthetic"));
        Check(!(await f.Db.OutgoingLetters.AsNoTracking().SingleAsync(l => l.Id == 101)).DabirkhaneSabt, "personal-account selection is rejected before registration and never substituted as an official sender");
    }
    private static async Task LegacySystemAdapter()
    {
        await using var f = new ServerFixture(); await f.Initialize();
        var dto = ServerFixture.Compose(); dto.RequestId = Guid.Empty;
        var sentId = await f.Email.SendSystemEmailAsync(dto, 1, true, Array.Empty<(string, string, byte[])>());
        Check(sentId > 0 && dto.RequestId != Guid.Empty && (await f.Store.FindAsync(dto.RequestId))!.Status == EmailDeliveryStatus.Accepted,
            "legacy integer-id system callers still get a real accepted sent ID and durable history");
        Check(await f.Email.SendSystemEmailAsync(dto, 1, true, Array.Empty<(string, string, byte[])>()) == sentId && f.Smtp.Calls == 1,
            "system adapter honors an already supplied request UUID without repeating SMTP");
        f.Smtp.Outcome = EmailDeliveryStatus.Unknown;
        var exception = await Throws<InvalidOperationException>(() => f.Email.SendSystemEmailAsync(ServerFixture.Compose(), 1, true, Array.Empty<(string, string, byte[])>()));
        Check(exception.Message.Contains("کد پیگیری") && !exception.Message.Contains("synthetic-password"),
            "legacy callers cannot mistake Unknown delivery for a successful numeric send ID");
    }

    private static async Task Coordination()
    {
        await using var f = new ServerFixture(); await f.Initialize();
        var unavailable = new FaultStore(f.Store) { FailClaim = true };
        var c = new EmailDispatchCoordinator(unavailable, f.Smtp, NullLogger<EmailDispatchCoordinator>.Instance);
        var response = await c.SendAsync(ServerFixture.Identity(), ServerFixture.Prepared);
        Check(response.Status == EmailDeliveryStatus.Unknown && !response.ResultPersisted && f.Smtp.Calls == 0, "unavailable claim fails closed but does not falsely classify an inaccessible earlier request as failed");
        var before = new FaultStore(f.Store) { FailSaveAt = 1 };
        c = new(before, f.Smtp, NullLogger<EmailDispatchCoordinator>.Instance);
        var preflight = await c.SendAsync(ServerFixture.Identity(), ServerFixture.Prepared);
        Check(preflight.CanRetry && f.Smtp.Calls == 0, "failure persisting Sending prevents any SMTP call");
        var after = new FaultStore(f.Store) { FailSaveAt = 2 };
        c = new(after, f.Smtp, NullLogger<EmailDispatchCoordinator>.Instance);
        var identity = ServerFixture.Identity(guard: new string('b', 64));
        var accepted = await c.SendAsync(identity, ServerFixture.Prepared);
        Check(accepted.SmtpAccepted && !accepted.ResultPersisted && accepted.Events.Any(e => e.Code == "RESULT_SAVE_WARNING"), "final-save fault preserves observed acceptance as warning, never Failed");
        var durable = await f.Store.FindAsync(identity.RequestId);
        Check(durable!.Status == EmailDeliveryStatus.Sending && durable.GuardKey != null, "failed final save leaves a durable in-flight guard instead of enabling blind retry");
        var replay = await c.SendAsync(identity, ServerFixture.Prepared);
        Check(replay.Replayed && f.Smtp.Calls == 1, "same-ID replay after final-save failure never resends");
        var stale = DateTime.UtcNow.AddMinutes(-11);
        await f.Db.EmailSendAttempts.Where(a => a.RequestId == identity.RequestId).ExecuteUpdateAsync(u => u.SetProperty(a => a.UpdatedAtUtc, stale));
        var status = await f.Store.GetStatusAsync(identity.RequestId, 1, true);
        Check(status!.Status == EmailDeliveryStatus.Unknown && status.Code == "INTERRUPTED_RESULT_UNKNOWN", "stale pending attempt projects Unknown without inventing a failure");
        var history = await f.Store.GetHistoryAsync(1, true, false, null, null, null, EmailDeliveryStatus.Unknown, 1, 15);
        Check(history.Items.Any(i => i.RequestId == identity.RequestId), "Unknown history filter includes stale pending rows");
        var denied = await f.Store.ClaimAsync(ServerFixture.Identity(guard: identity.GuardKey, previous: identity.RequestId));
        Check(!denied.IsNew && denied.Suppressed, "unknown predecessor requires explicit duplicate-risk confirmation");
        var next = await f.Store.ClaimAsync(ServerFixture.Identity(guard: identity.GuardKey, previous: identity.RequestId, risk: true));
        Check(next.IsNew && next.Attempt.PreviousRequestId == identity.RequestId, "confirmed unknown resend is linked and separately claimed");
        durable = (await f.Store.FindAsync(identity.RequestId))!; durable.Status = EmailDeliveryStatus.Accepted; durable.AcceptedAtUtc = DateTime.UtcNow;
        await f.Store.SaveAsync(durable);
        Check((await f.Store.FindAsync(identity.RequestId))!.GuardKey == null && (await f.Store.FindAsync(next.Attempt.RequestId))!.GuardKey != null, "late completion never resurrects a released predecessor guard");
        var fresh = await f.Store.ClaimAsync(ServerFixture.Identity(guard: new string('c', 64)));
        var blocked = await f.Store.ClaimAsync(ServerFixture.Identity(guard: new string('c', 64), previous: fresh.Attempt.RequestId, risk: true));
        Check(!blocked.IsNew, "risk confirmation cannot bypass a fresh in-flight attempt");
        var snapshot1 = (await f.Store.FindAsync(fresh.Attempt.RequestId))!;
        var snapshot2 = (await f.Store.FindAsync(fresh.Attempt.RequestId))!;
        snapshot1.Status = EmailDeliveryStatus.Accepted; await f.Store.SaveAsync(snapshot1);
        await Throws<InvalidOperationException>(() => f.Store.SaveAsync(snapshot2));
        Check((await f.Store.FindAsync(fresh.Attempt.RequestId))!.Status == EmailDeliveryStatus.Accepted, "optimistic ledger updates prevent stale warning writes from regressing acceptance");
        f.Smtp.Throw = true;
        c = new(f.Store, f.Smtp, NullLogger<EmailDispatchCoordinator>.Instance);
        var unexpected = await c.SendAsync(ServerFixture.Identity(), ServerFixture.Prepared);
        Check(unexpected.Status == EmailDeliveryStatus.Unknown && !unexpected.CanRetry, "unclassified exception after SMTP invocation is conservative Unknown");
        f.Smtp.Throw = false;
        var oldWorker = ServerFixture.Identity(guard: new string('e', 64));
        EmailDispatchClaim? newer = null; var callsBefore = f.Smtp.Calls;
        var abortedOldWorker = await c.SendAsync(oldWorker, async attempt =>
        {
            var interruptedAt = DateTime.UtcNow.AddMinutes(-11);
            await f.Db.EmailSendAttempts.Where(a => a.Id == attempt.Id).ExecuteUpdateAsync(u => u.SetProperty(a => a.UpdatedAtUtc, interruptedAt));
            attempt.UpdatedAtUtc = interruptedAt;
            newer = await f.Store.ClaimAsync(ServerFixture.Identity(guard: oldWorker.GuardKey, previous: oldWorker.RequestId, risk: true));
            return await ServerFixture.Prepared(attempt);
        });
        Check(abortedOldWorker.Status == EmailDeliveryStatus.Failed && f.Smtp.Calls == callsBefore && newer!.IsNew,
            "stalled preparation cannot cross SMTP boundary after its guard was explicitly transferred");
        Check((await f.Store.FindAsync(newer!.Attempt.RequestId))!.GuardKey == oldWorker.GuardKey,
            "late old-worker failure cannot release the new attempt's guard");
    }
    private static async Task Concurrency()
    {
        await using var f = new ServerFixture(); await f.Initialize();
        var same = ServerFixture.Identity();
        var transport = new FakeSmtp { Before = () => Task.Delay(35) };
        async Task<EmailDeliveryResultDto> Run(EmailDispatchIdentity identity)
        {
            await using var db = new AppDbContext(f.Options);
            return await new EmailDispatchCoordinator(new EmailDispatchStore(db), transport, NullLogger<EmailDispatchCoordinator>.Instance).SendAsync(identity, ServerFixture.Prepared);
        }
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(() => Run(same))));
        Check(transport.Calls == 1 && await f.Db.EmailSendAttempts.CountAsync(a => a.RequestId == same.RequestId) == 1, "five concurrent independent contexts with same UUID produce one SMTP submission");
        var replay = await Run(same);
        Check(replay.Replayed && transport.Calls == 1, "post-race same-key recovery returns the existing result");
        var guard = new string('d', 64);
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(() => Run(ServerFixture.Identity(guard: guard)))));
        Check(transport.Calls == 2 && await f.Db.EmailSendAttempts.CountAsync(a => a.GuardKey == guard) == 1, "cross-request letter guard remains unique across independent contexts");
    }
    private static async Task Protocol()
    {
        foreach (var mode in new[] { "accept", "auth", "recipient", "reject", "unknown", "quit" })
        {
            await using var server = new SmtpFixture(mode);
            var account = ServerFixture.Account(); account.SmtpPort = server.Port;
            using var message = ServerFixture.Message();
            var result = await new EmailSmtpTransport().SendAsync(account, message);
            var expected = mode is "auth" or "recipient" or "reject" ? EmailDeliveryStatus.Failed : mode == "unknown" ? EmailDeliveryStatus.Unknown : EmailDeliveryStatus.Accepted;
            Check(result.Status == expected, $"real MailKit against loopback {mode}: {result.Status}");
            Check(!result.Message.Contains("private") && !result.Message.Contains("secret") && (mode is not ("auth" or "recipient") || server.Submissions == 0), $"loopback {mode}: safe diagnostic and no DATA before pre-send rejection");
        }
        await using (var server = new SmtpFixture("accept"))
        {
            var account = ServerFixture.Account(); account.SmtpPort = server.Port;
            using var message = ServerFixture.Message();
            var cleanup = System.Reflection.DispatchProxy.Create<MailKit.Net.Smtp.ISmtpClient, FaultyCleanup>();
            var result = await new EmailSmtpTransport(() => cleanup).SendAsync(account, message);
            Check(result.Status == EmailDeliveryStatus.AcceptedWithWarnings && result.AcceptedAtUtc.HasValue &&
                result.Warnings.Any(w => w.Code == "SMTP_QUIT_WARNING") && result.Warnings.Any(w => w.Code == "SMTP_DISPOSE_WARNING"),
                "injected QUIT/disposal exceptions after real loopback acceptance are warnings, never send failure");
        }
        await using var tlsServer = new SmtpFixture("accept");
        var secure = ServerFixture.Account(); secure.SmtpPort = tlsServer.Port; secure.SmtpSsl = true;
        using var secureMessage = ServerFixture.Message();
        var tls = await new EmailSmtpTransport().SendAsync(secure, secureMessage);
        Check(tls.Status == EmailDeliveryStatus.Failed && tls.Code == "SMTP_TLS_ERROR" && tlsServer.Submissions == 0, "secure SMTP requires advertised STARTTLS and never downgrades");
        await using (var service = new ServerFixture())
        {
            await service.Initialize();
            await using var probe = new SmtpFixture("accept");
            var tested = await service.Email.TestAccountAsync(new() { EmailAddress = "sender@example.invalid", Password = "synthetic-only", Smtp = "127.0.0.1", SmtpPort = probe.Port, SmtpSsl = false });
            Check(tested.SmtpOk && probe.Submissions == 0, "account connection test authenticates without sending any DATA");
            await using var secureProbe = new SmtpFixture("accept");
            var secureTest = await service.Email.TestAccountAsync(new() { EmailAddress = "sender@example.invalid", Password = "synthetic-only", Smtp = "127.0.0.1", SmtpPort = secureProbe.Port, SmtpSsl = true });
            Check(!secureTest.SmtpOk && secureProbe.Submissions == 0 && secureTest.Message.Contains("امن"),
                "account testing enforces the same required STARTTLS policy as actual sending");
        }
        var rejection = new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, (SmtpStatusCode)451, "private provider diagnostic");
        Check(EmailSmtpTransport.Classify(rejection, "send").Status == EmailDeliveryStatus.Failed, "explicit temporary 4xx rejection is known failure, not acceptance");
        Check(EmailSmtpTransport.Classify(new IOException("secret"), "send").Status == EmailDeliveryStatus.Unknown, "socket failure during submission is ambiguous and not retryable automatically");
    }
    private static async Task Controllers()
    {
        await using var f = new ServerFixture(); await f.Initialize();
        var controller = new EmailController(f.Db, f.Email, NullLogger<EmailController>.Instance);
        var noKey = await controller.Send(10, "test@example.invalid", null, "test", "body", Guid.Empty, new());
        Check(noKey is BadRequestObjectResult && f.Smtp.Calls == 0, "controller explicitly rejects an old client without request UUID");
        var id = Guid.NewGuid();
        var success = await controller.Send(10, "test@example.invalid", null, "test", "body", id, new());
        Check(success is OkObjectResult { Value: EmailDeliveryResultDto { SmtpAccepted: true } }, "send endpoint returns structured result instead of an unconditional success message");
        var tooLarge = new FormFile(Stream.Null, 0, 21 * 1024 * 1024, "files", "too-large.txt");
        var rejected = await controller.Send(10, "test@example.invalid", null, "test", "body", Guid.NewGuid(), new() { tooLarge });
        Check(rejected is BadRequestObjectResult && f.Smtp.Calls == 1, "multipart upload limit aborts request instead of dropping file");
        controller.Permissions.Clear();
        Check(await controller.DeliveryStatus(id) is ObjectResult { StatusCode: 403 }, "tracking endpoint checks module/secretariat permission");
        controller.Permissions.Add("Email.Read"); controller.UserId = 2;
        Check(await controller.DeliveryStatus(id) is NotFoundObjectResult, "tracking endpoint hides another account's request");
        controller.UserId = 1;
        Check(await controller.DeliveryHistory(null, null, null, null, personal: true) is ObjectResult { StatusCode: 403 }, "personal-history endpoint retains personal permission gate");
    }
    private static async Task SqlServerTranslation()
    {
        var network = new NoSqlServerNetwork(); var commands = new CaptureSqlServerCommand();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=127.0.0.1,1;Database=SyntheticOnly;User Id=synthetic;Password=synthetic;TrustServerCertificate=true")
            .AddInterceptors(network, commands).Options;
        await using var db = new AppDbContext(options);
        var store = new EmailDispatchStore(db);
        var row = new EmailSendAttempt { Id = 1, UpdatedAtUtc = DateTime.UtcNow, StartedAtUtc = DateTime.UtcNow, RequestId = Guid.NewGuid(), Status = EmailDeliveryStatus.Sending, GuardKey = new string('a', 64) };
        await store.SaveAsync(row);
        Check(commands.Commands.Count == 1 && commands.Commands[0].Contains("UPDATE") && commands.Commands[0].Contains("GuardKey") && commands.Commands[0].Contains("UpdatedAtUtc"), "actual isolated update translates under SQL Server provider with guard/version conditions");
        var ddl = db.Database.GenerateCreateScript();
        Check(ddl.Contains("UX_EmailSendAttempt_Request") && ddl.Contains("UX_EmailSendAttempt_Guard") && ddl.Contains("WHERE [GuardKey] IS NOT NULL"), "SQL Server model emits unique request and filtered nullable guard indexes");
        await EmailDeliverySchemaV1.EnsureAsync(db);
        Check(commands.Commands.Last().Contains("OBJECT_ID") && network.Suppressed > 0, "SQL Server bootstrap command is provider-compatible and intercepted: no SQL Server network connection opened");
    }
}
