using System.Text.Json;
using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Office.Email;

public sealed record EmailDispatchIdentity(Guid RequestId, string RequestHash, int UserId, int AccountId,
    string AccountAddress, string Recipients, string Subject, string MessageId, int? LetterId = null,
    string? GuardKey = null, Guid? ResendOf = null, bool AcceptDuplicateRisk = false);
public sealed record EmailDispatchClaim(EmailSendAttempt Attempt, bool IsNew, bool Suppressed = false);
public sealed class EmailRequestConflictException : Exception
{
    public EmailRequestConflictException() : base("این کد پیگیری قبلاً برای درخواست دیگری استفاده شده است؛ هیچ ارسال جدیدی انجام نشد. وضعیت درخواست قبلی را بررسی کنید.") { }
}

public interface IEmailDispatchStore
{
    Task<EmailDispatchClaim> ClaimAsync(EmailDispatchIdentity identity);
    Task SaveAsync(EmailSendAttempt attempt);
    Task<EmailSendAttempt?> FindAsync(Guid requestId);
    Task<EmailDeliveryAttemptDto?> GetStatusAsync(Guid requestId, int userId, bool isDabirkhaneAdmin, bool personalOnly = false);
    Task<PagedResult<EmailDeliveryAttemptDto>> GetHistoryAsync(int userId, bool isDabirkhaneAdmin, bool personalOnly,
        int? accountId, int? letterId, int? sentId, EmailDeliveryStatus? status, int page, int pageSize);
}

/// <summary>Database uniqueness, not a process-local lock, owns the right to call SMTP.</summary>
public sealed class EmailDispatchStore(AppDbContext db) : IEmailDispatchStore
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    public async Task<EmailDispatchClaim> ClaimAsync(EmailDispatchIdentity request)
    {
        if (request.RequestId == Guid.Empty) throw new EmailRequestConflictException();
        // SQL Server's retry strategy requires explicitly-created transactions to be inside ExecuteAsync.
        // A retry after an uncertain COMMIT finds the same RequestId and NEVER resends SMTP.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            foreach (var entry in db.ChangeTracker.Entries<EmailSendAttempt>().Where(e => e.State == EntityState.Added).ToArray())
                entry.State = EntityState.Detached;
            await using var tx = await db.Database.BeginTransactionAsync();
            EmailSendAttempt? added = null;
            try
            {
                var same = await FindAsync(request.RequestId);
                if (same != null) return Replay(same, request);
                var blocker = request.GuardKey == null ? null : await db.EmailSendAttempts.AsNoTracking()
                    .SingleOrDefaultAsync(a => a.GuardKey == request.GuardKey);
                if (blocker != null)
                {
                    var effective = EffectiveStatus(blocker);
                    var accepted = effective is EmailDeliveryStatus.Accepted or EmailDeliveryStatus.AcceptedWithWarnings;
                    var confirmedUnknown = effective == EmailDeliveryStatus.Unknown && request.AcceptDuplicateRisk;
                    if (request.ResendOf != blocker.RequestId || !(accepted || confirmedUnknown))
                        return new(blocker, false, true);

                    // Explicit resend releases only the exact observed predecessor, atomically with the new claim.
                    // A fresh in-flight send can never be bypassed by a confirmation flag.
                    var changed = await db.EmailSendAttempts.Where(a => a.Id == blocker.Id && a.GuardKey == request.GuardKey
                            && a.Status == blocker.Status && a.UpdatedAtUtc == blocker.UpdatedAtUtc)
                        .ExecuteUpdateAsync(s => s.SetProperty(a => a.GuardKey, (string?)null));
                    if (changed != 1) throw new DbUpdateException("Delivery guard changed concurrently.");
                }
                var now = DateTime.UtcNow;
                added = new EmailSendAttempt
                {
                    RequestId = request.RequestId, RequestHash = request.RequestHash, GuardKey = request.GuardKey,
                    PreviousRequestId = blocker?.RequestId, RequestedByUserId = request.UserId,
                    EmailAccountId = request.AccountId, AccountAddress = Limit(request.AccountAddress, 250),
                    LetterSourceId = request.LetterId, Recipients = Limit(request.Recipients, 1000),
                    Subject = Limit(request.Subject, 500), MessageId = request.MessageId,
                    StartedAtUtc = now, UpdatedAtUtc = now
                };
                AddEvent(added, "request", "PREPARING", "درخواست ثبت شد؛ هنوز پیام به سرور ایمیل تحویل نشده است.");
                if (blocker != null)
                    AddEvent(added, "request", "EXPLICIT_RESEND", "تلاش جدید با تأیید صریح کاربر و ارتباط با درخواست قبلی ایجاد شد؛ نتیجهٔ قبلی بازنویسی نشده است.");
                db.EmailSendAttempts.Add(added);
                await db.SaveChangesAsync();
                await tx.CommitAsync();
                db.Entry(added).State = EntityState.Detached;
                return new EmailDispatchClaim(added, true);
            }
            catch (DbUpdateException)
            {
                await tx.RollbackAsync();
                if (added != null) db.Entry(added).State = EntityState.Detached;
                var same = await FindAsync(request.RequestId);
                if (same != null) return Replay(same, request);
                var winner = request.GuardKey == null ? null : await db.EmailSendAttempts.AsNoTracking()
                    .SingleOrDefaultAsync(a => a.GuardKey == request.GuardKey);
                if (winner != null) return new EmailDispatchClaim(winner, false, true);
                // Do not misclassify an unrelated DB failure as a duplicate. Fail closed before SMTP.
                throw;
            }
        });
    }

    private static EmailDispatchClaim Replay(EmailSendAttempt existing, EmailDispatchIdentity request)
    {
        if (existing.RequestedByUserId != request.UserId || existing.EmailAccountId != request.AccountId
            || existing.RequestHash != request.RequestHash || existing.LetterSourceId != request.LetterId)
            throw new EmailRequestConflictException();
        return new(existing, false);
    }

    public Task<EmailSendAttempt?> FindAsync(Guid requestId) =>
        db.EmailSendAttempts.AsNoTracking().SingleOrDefaultAsync(a => a.RequestId == requestId);

    public async Task SaveAsync(EmailSendAttempt attempt)
    {
        var expectedUpdate = attempt.UpdatedAtUtc;
        var now = DateTime.UtcNow;
        var failed = attempt.Status == EmailDeliveryStatus.Failed;
        // Isolated UPDATE: an invalid/unsaved attachment in the request's ChangeTracker must not
        // prevent recording its failure. Preserve any guard released by an explicit later attempt.
        var startingSmtp = attempt.Status == EmailDeliveryStatus.Sending;
        var changed = await db.EmailSendAttempts.Where(a => a.Id == attempt.Id && a.UpdatedAtUtc == expectedUpdate
            && (!startingSmtp || attempt.GuardKey == null || a.GuardKey == attempt.GuardKey)).ExecuteUpdateAsync(s => s
            .SetProperty(a => a.Status, attempt.Status).SetProperty(a => a.Stage, attempt.Stage)
            .SetProperty(a => a.Code, attempt.Code).SetProperty(a => a.Message, attempt.Message)
            .SetProperty(a => a.SentId, attempt.SentId).SetProperty(a => a.TechnicalType, attempt.TechnicalType)
            .SetProperty(a => a.Subject, attempt.Subject).SetProperty(a => a.Recipients, attempt.Recipients)
            .SetProperty(a => a.SmtpStatusCode, attempt.SmtpStatusCode)
            .SetProperty(a => a.UpdatedAtUtc, now).SetProperty(a => a.AcceptedAtUtc, attempt.AcceptedAtUtc)
            .SetProperty(a => a.CompletedAtUtc, attempt.CompletedAtUtc).SetProperty(a => a.EventsJson, attempt.EventsJson)
            .SetProperty(a => a.GuardKey, a => failed ? null : a.GuardKey));
        if (changed != 1) throw new InvalidOperationException("Delivery attempt disappeared before its result could be saved.");
        attempt.UpdatedAtUtc = now;
        if (failed) attempt.GuardKey = null;
    }

    private IQueryable<EmailSendAttempt> Accessible(int userId, bool isDabirkhaneAdmin, bool personalOnly)
    {
        var accountIds = db.OtoEmails.AsNoTracking().Where(a => personalOnly
            ? a.UserId == userId && !a.IsDabirkhane
            : a.UserId == userId || (isDabirkhaneAdmin && a.IsDabirkhane)).Select(a => a.EmailId);
        return db.EmailSendAttempts.AsNoTracking().Where(a => accountIds.Contains(a.EmailAccountId));
    }

    public async Task<EmailDeliveryAttemptDto?> GetStatusAsync(Guid requestId, int userId, bool isDabirkhaneAdmin, bool personalOnly = false)
    {
        var row = await Accessible(userId, isDabirkhaneAdmin, personalOnly).SingleOrDefaultAsync(a => a.RequestId == requestId);
        return row == null ? null : ToDto(row);
    }

    public async Task<PagedResult<EmailDeliveryAttemptDto>> GetHistoryAsync(int userId, bool isDabirkhaneAdmin, bool personalOnly,
        int? accountId, int? letterId, int? sentId, EmailDeliveryStatus? status, int page, int pageSize)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var q = Accessible(userId, isDabirkhaneAdmin, personalOnly);
        if (accountId.HasValue) q = q.Where(a => a.EmailAccountId == accountId.Value);
        if (letterId.HasValue) q = q.Where(a => a.LetterSourceId == letterId.Value);
        if (sentId.HasValue) q = q.Where(a => a.SentId == sentId.Value);
        var cutoff = DateTime.UtcNow - StaleAfter;
        if (status == EmailDeliveryStatus.Unknown)
            q = q.Where(a => a.Status == EmailDeliveryStatus.Unknown ||
                ((a.Status == EmailDeliveryStatus.Preparing || a.Status == EmailDeliveryStatus.Sending) && a.UpdatedAtUtc < cutoff));
        else if (status is EmailDeliveryStatus.Preparing or EmailDeliveryStatus.Sending)
            q = q.Where(a => a.Status == status && a.UpdatedAtUtc >= cutoff);
        else if (status.HasValue) q = q.Where(a => a.Status == status);
        var total = await q.CountAsync();
        // Bound offset without integer overflow for untrusted query-string page numbers.
        var offset = (int)Math.Min((long)(page - 1) * pageSize, int.MaxValue);
        var rows = await q.OrderByDescending(a => a.StartedAtUtc).ThenByDescending(a => a.Id)
            .Skip(offset).Take(pageSize).ToListAsync();
        return new PagedResult<EmailDeliveryAttemptDto> { Items = rows.Select(ToDto).ToList(), TotalCount = total, Page = page, PageSize = pageSize };
    }

    public static EmailDeliveryStatus EffectiveStatus(EmailSendAttempt a) =>
        a.Status is EmailDeliveryStatus.Preparing or EmailDeliveryStatus.Sending && a.UpdatedAtUtc < DateTime.UtcNow - StaleAfter
            ? EmailDeliveryStatus.Unknown : a.Status;

    public static EmailDeliveryAttemptDto ToDto(EmailSendAttempt a)
    {
        var status = EffectiveStatus(a);
        var stale = status != a.Status;
        return new()
        {
            AttemptId = a.Id, RequestId = a.RequestId, SentId = a.SentId, LetterSourceId = a.LetterSourceId,
            Status = status, Stage = a.Stage, Code = stale ? "INTERRUPTED_RESULT_UNKNOWN" : a.Code,
            Message = stale ? "نتیجهٔ این تلاش پس از وقفه ثبت نشده است. ناموفق‌بودن یا تحویل به گیرنده قابل تأیید نیست؛ قبل از هر ارسال جدید بررسی کنید." : a.Message,
            MessageId = a.MessageId, AcceptedAtUtc = Utc(a.AcceptedAtUtc), Events = Events(a),
            EmailAccountId = a.EmailAccountId, RequestedByUserId = a.RequestedByUserId,
            AccountAddress = a.AccountAddress, Recipients = a.Recipients, Subject = a.Subject,
            StartedAtUtc = DateTime.SpecifyKind(a.StartedAtUtc, DateTimeKind.Utc),
            UpdatedAtUtc = DateTime.SpecifyKind(a.UpdatedAtUtc, DateTimeKind.Utc), CompletedAtUtc = Utc(a.CompletedAtUtc),
            PreviousRequestId = a.PreviousRequestId, SmtpStatusCode = a.SmtpStatusCode, TechnicalType = a.TechnicalType
        };
    }

    public static List<EmailDeliveryEventDto> Events(EmailSendAttempt a)
    {
        try { return JsonSerializer.Deserialize<List<EmailDeliveryEventDto>>(a.EventsJson) ?? new(); }
        catch (JsonException) { return new(); }
    }
    public static void AddEvent(EmailSendAttempt a, string stage, string code, string safeMessage)
    {
        var events = Events(a);
        events.Add(new() { AtUtc = DateTime.UtcNow, Stage = stage, Code = code, Message = Limit(safeMessage, 700) });
        a.EventsJson = JsonSerializer.Serialize(events.TakeLast(16));
    }
    public static string Limit(string? value, int max) => (value ?? "")[..Math.Min((value ?? "").Length, max)];
    private static DateTime? Utc(DateTime? value) => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
}
