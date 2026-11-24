using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace Inventory.Api.Services.Office.Email;

public partial class EmailService
{
    private readonly IEmailDispatchStore _dispatchStore;
    private readonly EmailDispatchCoordinator _dispatch;

    public Task<PagedResult<EmailDeliveryAttemptDto>> GetDeliveryHistoryAsync(int userId, bool isDabirkhaneAdmin, bool personalOnly,
        int? accountId = null, int? letterId = null, int? sentId = null, EmailDeliveryStatus? status = null, int page = 1, int pageSize = 15) =>
        _dispatchStore.GetHistoryAsync(userId, isDabirkhaneAdmin, personalOnly, accountId, letterId, sentId, status, page, pageSize);

    public Task<EmailDeliveryAttemptDto?> GetDeliveryStatusAsync(Guid requestId, int userId, bool isDabirkhaneAdmin, bool personalOnly = false) =>
        _dispatchStore.GetStatusAsync(requestId, userId, isDabirkhaneAdmin, personalOnly);

    public Task<EmailDeliveryResultDto> AddDeliveryWarningAsync(EmailDeliveryResultDto result, string stage, string code, string safeMessage) =>
        _dispatch.AddWarningAsync(result, stage, code, safeMessage);

    public async Task<EmailDeliveryResultDto> SendAsync(EmailComposeDto dto, int userId, bool isDabirkhaneAdmin,
        (string Name, string ContentType, byte[] Data)[] attachments)
    {
        RequireRequestId(dto.RequestId);
        var acc = await AccessibleAccountAsync(dto.EmailAccountId, userId, isDabirkhaneAdmin)
            ?? throw new EmailPreparationException("ACCOUNT_UNAVAILABLE", "حساب ایمیل فرستنده پیدا نشد یا در دسترس شما نیست.");
        attachments ??= Array.Empty<(string, string, byte[])>();
        var identity = new EmailDispatchIdentity(dto.RequestId,
            Fingerprint(new { Kind = "compose", userId, dto.EmailAccountId, dto.To, dto.Cc, dto.Subject, dto.Body,
                Files = attachments.Select(a => new { a.Name, a.ContentType, Hash = Convert.ToHexString(SHA256.HashData(a.Data)) }) }),
            userId, acc.EmailId, acc.EmailAddress, dto.To, dto.Subject, MessageId(dto.RequestId, acc));
        return await _dispatch.SendAsync(identity, attempt => PrepareMessageAsync(acc, StrictAddresses(dto.To, true),
            StrictAddresses(dto.Cc, false), dto.Subject?.Trim() ?? "", dto.Body ?? "", attachments, attempt));
    }

    public async Task<int> SendSystemEmailAsync(EmailComposeDto dto, int userId, bool isDabirkhaneAdmin,
        (string Name, string ContentType, byte[] Data)[] attachments)
    {
        // Preserve the three pre-existing system callers without fabricating success from a
        // failed/ambiguous structured result. A new invocation is a new system operation;
        // these legacy HTTP interfaces do not carry a stable client request key.
        if (dto.RequestId == Guid.Empty) dto.RequestId = Guid.NewGuid();
        var result = await SendAsync(dto, userId, isDabirkhaneAdmin, attachments);
        if (result.SmtpAccepted && result.SentId.HasValue) return result.SentId.Value;
        throw new InvalidOperationException($"{result.Message} — کد نتیجه: {result.Code} — کد پیگیری: {result.RequestId}. قبل از تکرار عملیات، گزارش ارسال را بررسی کنید.");
    }

    public async Task<EmailDeliveryResultDto> SendLetterEmailAsync(int letterId, DabirkhaneRegisterDto dto, int userId, string userName)
    {
        RequireRequestId(dto.EmailRequestId);
        if (dto.EmailAccountId is not > 0)
            throw new EmailPreparationException("ACCOUNT_REQUIRED", "حساب رسمی فرستنده را صریحاً انتخاب کنید؛ حساب ایمیل به‌صورت حدسی انتخاب نمی‌شود.");
        var acc = await _db.OtoEmails.FirstOrDefaultAsync(a => a.EmailId == dto.EmailAccountId && a.IsDabirkhane)
            ?? throw new EmailPreparationException("ACCOUNT_UNAVAILABLE", "حساب رسمی دبیرخانه پیدا نشد؛ ایمیل شخصی برای ارسال این نامه مجاز نیست.");
        var identity = new EmailDispatchIdentity(dto.EmailRequestId,
            LetterRequestFingerprint(letterId, dto, userId),
            userId, acc.EmailId, acc.EmailAddress, dto.DestEmail ?? "", $"نامهٔ دبیرخانه {letterId}",
            MessageId(dto.EmailRequestId, acc), letterId,
            Fingerprint(new { Kind = "letter-guard", letterId, AccountId = acc.EmailId, Recipients = RecipientKey(dto.DestEmail) }),
            dto.ResendOfRequestId, dto.AcceptDuplicateRisk);
        var result = await _dispatch.SendAsync(identity, async attempt =>
        {
            var to = StrictAddresses(dto.DestEmail, true);
            ValidateAccount(acc);
            var letter = await _db.OutgoingLetters.AsNoTracking().Include(l => l.Source)
                .FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete)
                ?? throw new EmailPreparationException("LETTER_NOT_FOUND", "نامه پیدا نشد؛ هیچ ایمیلی ارسال نشده است.");
            if (string.IsNullOrWhiteSpace(letter.SadereNumber))
                throw new EmailPreparationException("LETTER_UNSIGNED", "فقط نامهٔ امضاشده و شماره‌دار قابل ارسال است؛ ایمیلی ارسال نشده است.");
            var signers = await _db.OutgoingLetterSigners.AsNoTracking().Include(s => s.User)
                .Where(s => s.SourceId == letterId && !s.IsDelete).OrderBy(s => s.Order).ToListAsync();
            static string Name(User? u) => u == null ? "" : string.IsNullOrWhiteSpace((u.FirstName ?? "") + (u.LastName ?? ""))
                ? u.Username ?? "" : ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim();
            static string Html(string? text) => System.Net.WebUtility.HtmlEncode(text ?? "");
            var body = new StringBuilder(letter.Text ?? "");
            body.Append("<hr/><div style=\"font-family:Tahoma;font-size:12px;direction:rtl\">");
            body.Append($"<p><b>شماره صادره:</b> {Html(letter.SadereNumber)} — <b>تاریخ صدور:</b> {Html(PersianDate.ToShortDateTime(letter.DateSadere ?? letter.DateSabt))}</p>");
            body.Append($"<p><b>گیرنده:</b> {Html(letter.ReceiverOrganization)} {Html(letter.ReceiverName)}</p>");
            if (signers.Count > 0)
                body.Append("<p><b>امضا کنندگان:</b> " + string.Join("، ", signers.Select(s => Html(Name(s.User)) + (s.IsSigned ? " ✓" : ""))) + "</p>");
            body.Append("<p>این نامه از طریق سامانه اتوماسیون اداری و درگاه ایمیل دبیرخانه ارسال شده است.</p></div>");

            var files = new List<(string, string, byte[])>();
            byte[]? pdf;
            try { pdf = await _print.GeneratePdfAsync(letterId, "A4"); }
            catch (Exception ex)
            {
                _log.LogWarning("Email {RequestId}: signed PDF preparation failed ({ErrorType}); SMTP was not invoked.", attempt.RequestId, ex.GetType().Name);
                throw new EmailPreparationException("LETTER_PDF_ERROR", "PDF رسمی نامه با سربرگ و امضا ساخته نشد؛ برای جلوگیری از ارسال ناقص، ایمیل ارسال نشده است. چاپ نامه و سربرگ شرکت را بررسی کنید.");
            }
            if (pdf is not { Length: > 0 })
                throw new EmailPreparationException("LETTER_PDF_EMPTY", "PDF رسمی نامه خالی است؛ ایمیل بدون سند رسمی ارسال نمی‌شود.");
            files.Add(($"نامه-{letter.SadereNumber.Replace('/', '-')}.pdf", "application/pdf", pdf));
            var letterFiles = await _db.AppAttachments.AsNoTracking().Where(a => a.Module == "OutgoingLetters" && a.RefId == letterId).ToListAsync();
            foreach (var a in letterFiles)
            {
                var bytes = a.Data is { Length: > 0 } ? a.Data : ReadDiskFile(a.FilePath);
                if (bytes is not { Length: > 0 })
                    throw new EmailPreparationException("LETTER_ATTACHMENT_UNAVAILABLE", "یکی از پیوست‌های نامه قابل خواندن نیست؛ برای جلوگیری از ارسال ناقص، هیچ ایمیلی ارسال نشده است.");
                files.Add((a.FileName, a.ContentType, bytes));
            }
            var subject = EmailDispatchStore.Limit($"نامه شماره {letter.SadereNumber} — {letter.Title}", 500);
            return await PrepareMessageAsync(acc, to, new(), subject, body.ToString(), files.ToArray(), attempt);
        });

        // A replay must not execute post-send work again. Failures here are warnings, never send failures.
        if (result.SmtpAccepted && !result.Replayed)
        {
            try
            {
                var letter = await _db.OutgoingLetters.AsNoTracking().Where(l => l.Id == letterId)
                    .Select(l => new { l.ReceiverOrganization, l.ReceiverName }).FirstOrDefaultAsync();
                foreach (var address in StrictAddresses(dto.DestEmail, true).Select(a => a.Address).Distinct(StringComparer.OrdinalIgnoreCase))
                    await _bank.RememberAsync(address, letter?.ReceiverOrganization, letter?.ReceiverName, userId, userName, markUsed: true);
            }
            catch (Exception ex)
            {
                _log.LogWarning("Email {RequestId}: address-book update failed ({ErrorType}); SMTP acceptance is preserved.", result.RequestId, ex.GetType().Name);
                result = await _dispatch.AddWarningAsync(result, "contacts", "CONTACTS_SAVE_WARNING", "پیام توسط سرور ایمیل پذیرفته شد، اما به‌روزرسانی بانک مخاطبین انجام نشد؛ ارسال را تکرار نکنید.");
            }
        }
        return result;
    }

    private async Task<PreparedEmail> PrepareMessageAsync(OtoEmail acc, List<MailboxAddress> to, List<MailboxAddress> cc,
        string subject, string body, (string Name, string ContentType, byte[] Data)[] attachments, EmailSendAttempt attempt)
    {
        ValidateAccount(acc);
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 500)
            throw new EmailPreparationException("INVALID_SUBJECT", "موضوع ایمیل الزامی است و باید حداکثر ۵۰۰ نویسه باشد؛ ایمیلی ارسال نشده است.");
        var recipients = string.Join("، ", to.Select(a => a.Address));
        if (recipients.Length > 1000 || string.Join(",", cc.Select(a => a.Address)).Length > 1000)
            throw new EmailPreparationException("TOO_MANY_RECIPIENTS", "فهرست گیرندگان بیش از حد طولانی است؛ ایمیلی ارسال نشده است.");
        if (attachments.Length > 100)
            throw new EmailPreparationException("ATTACHMENT_LIMIT", "حداکثر ۱۰۰ پیوست در هر ایمیل مجاز است؛ ایمیلی ارسال نشده است.");
        long total = 0;
        foreach (var a in attachments)
        {
            total += a.Data.LongLength;
            if (a.Data.Length == 0 || a.Data.LongLength > 20 * 1024 * 1024 || total > 100 * 1024 * 1024 || string.IsNullOrWhiteSpace(a.Name) || a.Name.Length > 255)
                throw new EmailPreparationException("ATTACHMENT_LIMIT", "پیوست‌ها باید غیرخالی باشند: حداکثر ۲۰ مگابایت برای هر فایل، ۱۰۰ مگابایت مجموع و ۲۵۵ نویسه برای نام فایل. ایمیلی ارسال نشده است.");
        }
        var message = new MimeMessage { Subject = subject, MessageId = attempt.MessageId };
        int? sentId = null;
        try
        {
            message.From.Add(new MailboxAddress(string.IsNullOrWhiteSpace(acc.DisplayName) ? acc.EmailAddress : acc.DisplayName, acc.EmailAddress));
            message.To.AddRange(to); message.Cc.AddRange(cc);
            var builder = new BodyBuilder { HtmlBody = body };
            foreach (var a in attachments)
            {
                var type = ContentType.TryParse(MimeToExtensionType(a.Name, a.ContentType), out var parsed)
                    ? parsed : new ContentType("application", "octet-stream");
                builder.Attachments.Add(a.Name, a.Data, type);
            }
            message.Body = builder.ToMessageBody();
            var sent = new OtoSentEmail
            {
                EmailId = acc.EmailId, UId = attempt.MessageId, ToDisplay = recipients, Subject = subject,
                Date = IranNow(), Body = body, IsAttachment = attachments.Length > 0, LetterSourceId = attempt.LetterSourceId
            };
            _db.OtoSentEmails.Add(sent);
            await _db.SaveChangesAsync();
            sentId = sent.SentId; attempt.SentId = sent.SentId;
            attempt.Subject = subject; attempt.Recipients = recipients; attempt.Stage = "prepare";
            await _dispatchStore.SaveAsync(attempt);
            foreach (var a in attachments)
            {
                var dir = Path.Combine(AttachmentRoot, $"Sent_{sent.SentId}");
                Directory.CreateDirectory(dir);
                var saved = $"{Guid.NewGuid():N}_{EmailDispatchStore.Limit(SafeName(a.Name), 160)}";
                await File.WriteAllBytesAsync(Path.Combine(dir, saved), a.Data);
                _db.OtoEmailAttachments.Add(new OtoEmailAttachment
                {
                    EmailId = sent.SentId, UId = attempt.MessageId, Type = "Sent",
                    AttachmentRealName = a.Name, AttachmentSavedName = saved,
                    FilePath = $"{AttachmentFolder}/Sent_{sent.SentId}/{saved}"
                });
            }
            await _db.SaveChangesAsync();
            return new PreparedEmail(acc, message, sent.SentId);
        }
        catch
        {
            message.Dispose();
            foreach (var entry in _db.ChangeTracker.Entries<OtoEmailAttachment>()
                .Where(e => e.State == EntityState.Added && sentId.HasValue && e.Entity.EmailId == sentId.Value && e.Entity.Type == "Sent").ToArray())
                entry.State = EntityState.Detached;
            foreach (var entry in _db.ChangeTracker.Entries<OtoSentEmail>().Where(e => e.State == EntityState.Added).ToArray())
                entry.State = EntityState.Detached;
            if (sentId.HasValue) DeleteAttachmentFiles("Sent", sentId.Value);
            throw;
        }
    }

    internal static string LetterRequestFingerprint(int letterId, DabirkhaneRegisterDto dto, int userId) =>
        Fingerprint(new { Kind = "letter", userId, letterId, dto.EmailAccountId, Dest = (dto.DestEmail ?? "").Trim(),
            dto.SendMethod, dto.DestRegNumber, dto.Note, dto.DelivererName, dto.TrackingCode, dto.DestFax });

    private static void RequireRequestId(Guid id)
    {
        if (id == Guid.Empty) throw new EmailPreparationException("REQUEST_ID_REQUIRED", "شناسهٔ پیگیری ارسال موجود نیست؛ صفحه را کامل بازخوانی کنید. برای جلوگیری از ارسال تکراری، درخواست قدیمی ارسال نمی‌شود.");
    }
    private static void ValidateAccount(OtoEmail account)
    {
        if (!account.IsActive) throw new EmailPreparationException("ACCOUNT_INACTIVE", "حساب ایمیل غیرفعال است؛ ایمیلی ارسال نشده است.");
        if (string.IsNullOrWhiteSpace(account.Smtp) || account.SmtpPort is < 1 or > 65535)
            throw new EmailPreparationException("SMTP_NOT_CONFIGURED", "سرور یا پورت SMTP تنظیم نشده است؛ حساب را از بخش ایمیل سازمانی بررسی کنید. ایمیلی ارسال نشده است.");
        if (!MailboxAddress.TryParse(account.EmailAddress, out var address) || !address.Address.Contains('@'))
            throw new EmailPreparationException("INVALID_SENDER", "آدرس حساب فرستنده معتبر نیست؛ ایمیلی ارسال نشده است.");
    }
    private static List<MailboxAddress> StrictAddresses(string? raw, bool required)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (required) throw new EmailPreparationException("INVALID_RECIPIENT", "حداقل یک گیرندهٔ معتبر لازم است؛ ایمیلی ارسال نشده است.");
            return new();
        }
        var list = new List<MailboxAddress>();
        foreach (var part in raw.Split(new[] { ',', '،', ';', '؛' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!MailboxAddress.TryParse(part.Trim(), out var address) || !address.Address.Contains('@'))
                throw new EmailPreparationException("INVALID_RECIPIENT", "یکی از آدرس‌های گیرنده یا رونوشت نامعتبر است؛ هیچ گیرنده‌ای بی‌صدا حذف نمی‌شود و ایمیلی ارسال نشده است.");
            list.Add(address);
        }
        if (required && list.Count == 0) throw new EmailPreparationException("INVALID_RECIPIENT", "گیرندهٔ معتبر وارد کنید؛ ایمیلی ارسال نشده است.");
        return list;
    }
    private static string Fingerprint(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static string MessageId(Guid requestId, OtoEmail account)
    {
        var domain = MailboxAddress.TryParse(account.EmailAddress, out var address) && address.Address.Contains('@')
            ? address.Address[(address.Address.LastIndexOf('@') + 1)..].ToLowerInvariant() : "automation.local";
        return $"{requestId:N}@{domain}";
    }
    private static string RecipientKey(string? value)
    {
        try
        {
            return string.Join(",", StrictAddresses(value, true).Select(a =>
            {
                var at = a.Address.LastIndexOf('@');
                return a.Address[..at] + "@" + a.Address[(at + 1)..].ToLowerInvariant();
            }).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal));
        }
        catch (EmailPreparationException) { return (value ?? "").Trim(); }
    }

    private async Task FillDeliveryStatusAsync(IEnumerable<EmailMessageListItemDto> items)
    {
        var sent = items.Where(i => i.Box == "Sent").ToList();
        if (sent.Count == 0) return;
        var ids = sent.Select(i => i.Id).ToList();
        var rows = await _db.EmailSendAttempts.AsNoTracking().Where(a => a.SentId.HasValue && ids.Contains(a.SentId.Value))
            .OrderByDescending(a => a.Id).ToListAsync();
        var bySent = rows.GroupBy(a => a.SentId!.Value).ToDictionary(g => g.Key, g => g.First());
        foreach (var row in sent)
            if (bySent.TryGetValue(row.Id, out var attempt))
            {
                row.DeliveryStatus = EmailDispatchStore.EffectiveStatus(attempt);
                row.DeliveryRequestId = attempt.RequestId;
            }
    }
}
