using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using MimeKit;

namespace Inventory.Api.Services.Office.Email;

public sealed record PreparedEmail(OtoEmail Account, MimeMessage Message, int SentId);
public sealed class EmailPreparationException(string code, string safeMessage) : Exception(safeMessage)
{
    public string Code { get; } = code;
}

/// <summary>
/// Claims once, persists all preparation before SMTP, then records the observed outcome.
/// The database and remote SMTP cannot form an atomic transaction: ambiguous outcomes stay Unknown.
/// </summary>
public sealed class EmailDispatchCoordinator(IEmailDispatchStore store, IEmailSmtpTransport transport,
    ILogger<EmailDispatchCoordinator> log)
{
    public async Task<EmailDeliveryResultDto> SendAsync(EmailDispatchIdentity identity, Func<EmailSendAttempt, Task<PreparedEmail>> prepare)
    {
        EmailDispatchClaim claim;
        try { claim = await store.ClaimAsync(identity); }
        catch (EmailRequestConflictException ex)
        {
            return new() { RequestId = identity.RequestId, Status = EmailDeliveryStatus.Unknown, Stage = "request",
                Code = "REQUEST_ID_CONFLICT", Message = ex.Message, ResultPersisted = false };
        }
        catch (Exception ex)
        {
            // No exception message/body/credentials are logged or reflected to the client.
            log.LogError("Email {RequestId}: durable claim unavailable ({ErrorType}); SMTP was not invoked.", identity.RequestId, ex.GetType().Name);
            return new() { RequestId = identity.RequestId, Status = EmailDeliveryStatus.Unknown, Stage = "request",
                Code = "DELIVERY_LOG_UNAVAILABLE", ResultPersisted = false,
                Message = "ثبت یا بازیابی پایدار کد پیگیری انجام نشد؛ این فراخوانی با SMTP تماس نگرفت، اما نتیجهٔ درخواست احتمالی قبلی معلوم نیست. پس از رفع مشکل، همین کد پیگیری را بررسی کنید." };
        }
        if (!claim.IsNew)
        {
            var existing = EmailDispatchStore.ToDto(claim.Attempt);
            existing.Replayed = true; existing.DuplicateSuppressed = claim.Suppressed;
            if (claim.Suppressed)
                existing.Message = "برای همین نامه، حساب و گیرنده یک تلاش قبلی وجود دارد؛ هیچ ایمیل جدیدی ارسال نشد. " + existing.Message;
            return existing;
        }

        var attempt = claim.Attempt;
        PreparedEmail? prepared = null;
        var smtpInvoked = false;
        try
        {
            attempt.Stage = "prepare";
            prepared = await prepare(attempt);
            attempt.SentId = prepared.SentId;
            attempt.Status = EmailDeliveryStatus.Sending;
            attempt.Stage = "send"; attempt.Code = "SMTP_IN_PROGRESS";
            attempt.Message = "آماده‌سازی کامل شد؛ نتیجهٔ تحویل به سرور ایمیل هنوز مشخص نیست.";
            EmailDispatchStore.AddEvent(attempt, "prepare", "PREPARATION_COMPLETE", "بدنه و همهٔ پیوست‌ها پیش از تماس با SMTP آماده و ذخیره شدند.");
            await store.SaveAsync(attempt); // Fail closed: no SMTP without a durable in-flight record.
            smtpInvoked = true;
            var outcome = await transport.SendAsync(prepared.Account, prepared.Message);
            attempt.Status = outcome.Status; attempt.Stage = outcome.Stage; attempt.Code = outcome.Code;
            attempt.Message = outcome.Message; attempt.TechnicalType = outcome.TechnicalType;
            attempt.SmtpStatusCode = outcome.SmtpStatusCode; attempt.AcceptedAtUtc = outcome.AcceptedAtUtc;
            attempt.CompletedAtUtc = DateTime.UtcNow;
            EmailDispatchStore.AddEvent(attempt, outcome.Stage, outcome.Code, outcome.Message);
            foreach (var warning in outcome.Warnings)
                EmailDispatchStore.AddEvent(attempt, warning.Stage, warning.Code, warning.Message);
            return await FinishAsync(attempt);
        }
        catch (Exception ex)
        {
            // An unexpected transport exception is conservative: it must never authorize a blind retry.
            attempt.Status = smtpInvoked ? EmailDeliveryStatus.Unknown : EmailDeliveryStatus.Failed;
            attempt.Stage = smtpInvoked ? "send" : "prepare";
            attempt.Code = smtpInvoked ? "SMTP_RESULT_UNKNOWN" : (ex as EmailPreparationException)?.Code ?? "PREPARATION_ERROR";
            attempt.Message = smtpInvoked
                ? "نتیجهٔ ارسال قابل تأیید نیست؛ قبل از هر تلاش جدید، وضعیت همین کد پیگیری را بررسی کنید."
                : ex is EmailPreparationException safe ? safe.Message
                : "آماده‌سازی یا ذخیرهٔ درخواست/پیوست‌ها انجام نشد؛ SMTP فراخوانی نشده است. کد پیگیری را برای بررسی در اختیار پشتیبانی قرار دهید.";
            attempt.TechnicalType = ex.GetType().Name; attempt.CompletedAtUtc = DateTime.UtcNow;
            EmailDispatchStore.AddEvent(attempt, attempt.Stage, attempt.Code, attempt.Message);
            log.LogWarning("Email {RequestId}: {Stage}/{Code} ({ErrorType}).", attempt.RequestId, attempt.Stage, attempt.Code, ex.GetType().Name);
            return await FinishAsync(attempt);
        }
        finally
        {
            try { prepared?.Message.Dispose(); } catch { /* cleanup cannot change observed SMTP outcome */ }
        }
    }

    private async Task<EmailDeliveryResultDto> FinishAsync(EmailSendAttempt attempt)
    {
        try
        {
            await store.SaveAsync(attempt);
            return EmailDispatchStore.ToDto(attempt);
        }
        catch (Exception ex)
        {
            // The in-flight record remains locked on persistence failure. Never resend to compensate.
            log.LogError("Email {RequestId}: result persistence failed ({ErrorType}); observed status {Status}, MessageId {MessageId}.",
                attempt.RequestId, ex.GetType().Name, attempt.Status, attempt.MessageId);
            if (attempt.Status is EmailDeliveryStatus.Accepted or EmailDeliveryStatus.AcceptedWithWarnings)
            {
                attempt.Status = EmailDeliveryStatus.AcceptedWithWarnings;
                attempt.Message = "سرور ایمیل پیام را پذیرفته است، اما ثبت نتیجه در سامانه با خطا مواجه شد. ارسال را تکرار نکنید؛ کد پیگیری را به پشتیبانی بدهید.";
            }
            EmailDispatchStore.AddEvent(attempt, "persist", "RESULT_SAVE_WARNING", "ثبت نتیجه در سامانه تأیید نشد؛ سابقه ممکن است موقتاً نتیجهٔ نامشخص نشان دهد.");
            var result = EmailDispatchStore.ToDto(attempt);
            result.ResultPersisted = false;
            return result;
        }
    }

    public async Task<EmailDeliveryResultDto> AddWarningAsync(EmailDeliveryResultDto result, string stage, string code, string safeMessage)
    {
        result.Events.Add(new() { AtUtc = DateTime.UtcNow, Stage = stage, Code = code, Message = safeMessage });
        if (result.SmtpAccepted) result.Status = EmailDeliveryStatus.AcceptedWithWarnings;
        if (!result.AttemptId.HasValue) return result;
        try
        {
            var attempt = await store.FindAsync(result.RequestId);
            if (attempt == null || attempt.Id != result.AttemptId.Value) return result;
            // Also repairs a failed final save when a later non-SMTP write becomes available.
            if (result.SmtpAccepted)
            {
                attempt.Status = result.Status;
                attempt.AcceptedAtUtc = result.AcceptedAtUtc;
                attempt.Message = result.Message;
                attempt.Code = "SMTP_ACCEPTED";
                attempt.CompletedAtUtc ??= DateTime.UtcNow;
            }
            EmailDispatchStore.AddEvent(attempt, stage, code, safeMessage);
            await store.SaveAsync(attempt);
        }
        catch (Exception ex)
        {
            result.ResultPersisted = false;
            log.LogWarning("Email {RequestId}: warning {Code} could not be persisted ({ErrorType}).", result.RequestId, code, ex.GetType().Name);
        }
        return result;
    }
}
