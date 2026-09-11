using Inventory.Api.Data;
using Inventory.Api.Services.Office.Outgoing;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Inventory.Api.Services.Office.Email;

// ============================================================
//  سرویس ایمیل سازمانی — ارسال با SMTP و دریافت با IMAP (MailKit)
//  • حساب‌های شخصی هر کاربر + حساب‌های رسمی دبیرخانه (IsDabirkhane)
//  • پشتیبانی Gmail / Yahoo / Outlook / وب‌میل هاست / سفارشی
//  • پیوست‌ها روی دیسک: wwwroot/فایل های ایمیل/{Inbox|Sent}_{id}/
//  • ارسال نامه صادره از دبیرخانه با پست الکترونیک (PDF نامه روی سربرگ + پیوست‌ها)
// ============================================================

public interface IEmailService
{
    // ---------- حساب‌ها ----------
    Task<List<EmailAccountDto>> GetMyAccountsAsync(int userId, bool isDabirkhaneAdmin);
    Task<List<EmailAccountDto>> GetDabirkhaneAccountsAsync();
    Task<int> SaveAccountAsync(SaveEmailAccountDto dto, int userId);
    Task DeleteAccountAsync(int emailId, int userId);
    Task<EmailTestResultDto> TestAccountAsync(SaveEmailAccountDto dto);

    // ---------- ارسال ----------
    Task<int> SendAsync(EmailComposeDto dto, int userId, bool isDabirkhaneAdmin, (string Name, string ContentType, byte[] Data)[] attachments);
    Task<int> SendLetterEmailAsync(int letterId, DabirkhaneRegisterDto dto, string userName);

    // ---------- دریافت ----------
    Task<EmailSyncResultDto> SyncAsync(int emailId, int userId, bool isDabirkhaneAdmin);
    Task<List<EmailMessageListItemDto>> GetInboxAsync(int userId, int? emailId, string? search, bool? unreadOnly, bool isDabirkhaneAdmin);
    Task<List<EmailMessageListItemDto>> GetSentAsync(int userId, int? emailId, string? search, bool isDabirkhaneAdmin);
    Task<EmailMessageDetailDto> GetMessageAsync(string box, int id, int userId, bool markRead, bool isDabirkhaneAdmin);
    Task MarkReadAsync(string box, int id, int userId, bool isDabirkhaneAdmin);
    Task<bool> ToggleNeshanAsync(string box, int id, int userId, bool isDabirkhaneAdmin);
    Task ArchiveAsync(EmailArchiveDto dto, int userId, bool isDabirkhaneAdmin);
    Task<byte[]?> ReadAttachmentAsync(int attachmentId, int userId, bool isDabirkhaneAdmin);

    // ---------- پوشه‌های بایگانی ----------
    Task<List<EmailFolderDto>> GetFoldersAsync(int userId);
    Task<int> SaveFolderAsync(SaveEmailFolderDto dto, int userId);
    Task DeleteFolderAsync(int folderId, int userId);
}

public class EmailService : IEmailService
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<EmailService> _log;
    private readonly IOutgoingLetterPrintService _print;

    /// <summary>پوشه ذخیره پیوست‌های ایمیل — مستقیم زیر wwwroot (درخواست کارفرما)</summary>
    private const string AttachmentFolder = "فایل های ایمیل";

    public EmailService(AppDbContext db, IWebHostEnvironment env, ILogger<EmailService> log, IOutgoingLetterPrintService print)
    {
        _db = db;
        _env = env;
        _log = log;
        _print = print;
    }

    // ==================== ابزارهای مشترک ====================

    private string AttachmentRoot =>
        Path.Combine(_env.ContentRootPath, "wwwroot", AttachmentFolder);

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder();
        foreach (var ch in Path.GetFileName(name ?? "file"))
            sb.Append(invalid.Contains(ch) ? '_' : ch);
        var s = sb.ToString();
        return string.IsNullOrWhiteSpace(s) ? "file" : s.Length > 100 ? s[..100] : s;
    }

    private static string MimeToExtensionType(string fileName, string contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType)) return contentType;
        var ext = (Path.GetExtension(fileName) ?? "").ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".txt" => "text/plain",
            ".zip" => "application/zip",
            _ => "application/octet-stream"
        };
    }

    /// <summary>گزینه‌های امنیتی SMTP بر اساس پورت/تنظیم کاربر</summary>
    private static SecureSocketOptions SmtpOptions(int port, bool ssl) =>
        !ssl ? SecureSocketOptions.None
        : port == 465 ? SecureSocketOptions.SslOnConnect
        : SecureSocketOptions.StartTlsWhenAvailable;

    /// <summary>تجزیه رشته گیرندگان «a@b.com، c@d.com» به لیست آدرس معتبر</summary>
    private static List<MailboxAddress> ParseAddresses(string? raw)
    {
        var list = new List<MailboxAddress>();
        if (string.IsNullOrWhiteSpace(raw)) return list;
        var parts = raw.Split(new[] { ',', '،', ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            var s = p.Trim();
            if (s.Length == 0) continue;
            if (MailboxAddress.TryParse(s, out var addr)) list.Add(addr);
        }
        return list;
    }

    private static string? ExtractAddress(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var at = raw.IndexOf('@');
        if (at < 0) return raw.Trim();
        var start = raw.LastIndexOf('<', at);
        var end = raw.IndexOf('>', at);
        if (start >= 0 && end > start) return raw[(start + 1)..end].Trim();
        return raw.Trim();
    }

    /// <summary>آیا کاربر اجازه دسترسی به این حساب را دارد؟ (مالک، یا حساب دبیرخانه با دسترسی دبیرخانه)</summary>
    private async Task<OtoEmail?> AccessibleAccountAsync(int emailId, int userId, bool isDabirkhaneAdmin)
    {
        var acc = await _db.OtoEmails.FirstOrDefaultAsync(e => e.EmailId == emailId);
        if (acc == null) return null;
        if (acc.UserId == userId) return acc;
        if (acc.IsDabirkhane && isDabirkhaneAdmin) return acc;
        return null;
    }

    // ==================== حساب‌ها ====================

    public async Task<List<EmailAccountDto>> GetMyAccountsAsync(int userId, bool isDabirkhaneAdmin)
    {
        // حساب‌های شخصی کاربر + (برای دستیار دبیرخانه) حساب‌های رسمی دبیرخانه
        var q = _db.OtoEmails.AsNoTracking()
            .Include(e => e.User)
            .Where(e => e.UserId == userId || (e.IsDabirkhane && isDabirkhaneAdmin));

        return await q.OrderBy(e => e.IsDabirkhane).ThenBy(e => e.EmailId)
            .Select(e => new EmailAccountDto
            {
                EmailId = e.EmailId,
                EmailAddress = e.EmailAddress,
                Smtp = e.Smtp,
                Imap = e.Imap,
                SmtpPort = e.SmtpPort,
                SmtpSsl = e.SmtpSsl,
                ImapPort = e.ImapPort,
                ImapSsl = e.ImapSsl,
                EmailType = e.EmailType,
                IsActive = e.IsActive,
                IsDabirkhane = e.IsDabirkhane,
                DisplayName = e.DisplayName,
                LastSync = e.LastSync,
                UserId = e.UserId,
                UserName = e.User != null
                    ? (string.IsNullOrWhiteSpace((e.User.FirstName ?? "") + (e.User.LastName ?? "")) ? e.User.Username : ((e.User.FirstName ?? "") + " " + (e.User.LastName ?? "")).Trim())
                    : "",
                IsMine = e.UserId == userId
            }).ToListAsync();
    }

    public async Task<List<EmailAccountDto>> GetDabirkhaneAccountsAsync()
    {
        return await _db.OtoEmails.AsNoTracking()
            .Where(e => e.IsDabirkhane && e.IsActive)
            .OrderBy(e => e.EmailId)
            .Select(e => new EmailAccountDto
            {
                EmailId = e.EmailId,
                EmailAddress = e.EmailAddress,
                EmailType = e.EmailType,
                DisplayName = e.DisplayName,
                IsActive = e.IsActive,
                IsDabirkhane = true
            }).ToListAsync();
    }

    public async Task<int> SaveAccountAsync(SaveEmailAccountDto dto, int userId)
    {
        if (string.IsNullOrWhiteSpace(dto.EmailAddress)) throw new Exception("آدرس ایمیل الزامی است.");
        if (string.IsNullOrWhiteSpace(dto.Password) && dto.EmailId == 0)
            throw new Exception("رمز عبور (App Password) الزامی است.");

        // هشدار مهم: رمز حساب باید App Password باشد نه رمز اصلی جیمیل/یاهو
        OtoEmail acc;
        if (dto.EmailId > 0)
        {
            acc = await _db.OtoEmails.FirstOrDefaultAsync(e => e.EmailId == dto.EmailId)
                ?? throw new Exception("حساب ایمیل پیدا نشد.");
            if (acc.UserId != userId) throw new Exception("فقط مالک حساب می‌تواند آن را ویرایش کند.");
        }
        else
        {
            // جلوگیری از ثبت تکراری آدرس برای یک کاربر
            var dup = await _db.OtoEmails.AnyAsync(e => e.UserId == userId && e.EmailAddress == dto.EmailAddress.Trim());
            if (dup) throw new Exception("این آدرس ایمیل قبلاً برای شما ثبت شده است.");
            acc = new OtoEmail { UserId = userId };
            _db.OtoEmails.Add(acc);
        }

        acc.EmailAddress = dto.EmailAddress.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Password)) acc.Password = dto.Password.Trim();
        acc.EmailType = string.IsNullOrWhiteSpace(dto.EmailType) ? "Custom" : dto.EmailType.Trim();
        acc.Smtp = dto.Smtp?.Trim() ?? "";
        acc.Imap = dto.Imap?.Trim() ?? "";
        acc.SmtpPort = dto.SmtpPort > 0 ? dto.SmtpPort : 587;
        acc.SmtpSsl = dto.SmtpSsl;
        acc.ImapPort = dto.ImapPort > 0 ? dto.ImapPort : 993;
        acc.ImapSsl = dto.ImapSsl;
        acc.DisplayName = dto.DisplayName?.Trim();
        acc.IsActive = dto.IsActive;
        acc.IsDabirkhane = dto.IsDabirkhane;
        acc.ActivationCode = Guid.NewGuid().ToString("N")[..16];

        await _db.SaveChangesAsync();
        return acc.EmailId;
    }

    public async Task DeleteAccountAsync(int emailId, int userId)
    {
        var acc = await _db.OtoEmails.FirstOrDefaultAsync(e => e.EmailId == emailId)
            ?? throw new Exception("حساب ایمیل پیدا نشد.");
        if (acc.UserId != userId) throw new Exception("فقط مالک حساب می‌تواند آن را حذف کند.");

        // پیوست‌های روی دیسک هم پاک می‌شوند
        var ids = new { inboxIds = await _db.OtoInboxEmails.Where(i => i.EmailId == emailId).Select(i => i.InboxId).ToListAsync(),
                        sentIds = await _db.OtoSentEmails.Where(s => s.EmailId == emailId).Select(s => s.SentId).ToListAsync() };
        foreach (var i in ids.inboxIds) DeleteAttachmentFiles("Inbox", i);
        foreach (var s in ids.sentIds) DeleteAttachmentFiles("Sent", s);

        // ردیف‌های پیوست (بدون FK) هم حذف می‌شوند — پیام‌ها با Cascade حذف می‌شوند
        await _db.OtoEmailAttachments
            .Where(a => (a.Type == "Inbox" && ids.inboxIds.Contains(a.EmailId)) ||
                        (a.Type == "Sent" && ids.sentIds.Contains(a.EmailId)))
            .ExecuteDeleteAsync();

        _db.OtoEmails.Remove(acc);
        await _db.SaveChangesAsync();
    }

    /// <summary>تست اتصال SMTP و IMAP — قبل از ذخیره، مقادیر پیشنهادی فرم را تست می‌کند</summary>
    public async Task<EmailTestResultDto> TestAccountAsync(SaveEmailAccountDto dto)
    {
        var result = new EmailTestResultDto();
        var smtpHost = dto.Smtp?.Trim();
        var imapHost = dto.Imap?.Trim();

        // ---------- SMTP ----------
        if (string.IsNullOrWhiteSpace(smtpHost))
        {
            result.Message = "سرور SMTP مشخص نشده است.";
            return result;
        }
        try
        {
            using var client = new SmtpClient { Timeout = 15000 };
            await client.ConnectAsync(smtpHost, dto.SmtpPort > 0 ? dto.SmtpPort : 587, SmtpOptions(dto.SmtpPort, dto.SmtpSsl));
            await client.AuthenticateAsync(dto.EmailAddress.Trim(), dto.Password);
            await client.DisconnectAsync(true);
            result.SmtpOk = true;
        }
        catch (Exception ex)
        {
            result.Message = $"SMTP: {ex.Message}";
            return result;
        }

        // ---------- IMAP (اختیاری) ----------
        if (!string.IsNullOrWhiteSpace(imapHost))
        {
            try
            {
                using var client = new ImapClient { Timeout = 15000 };
                await client.ConnectAsync(imapHost, dto.ImapPort > 0 ? dto.ImapPort : 993,
                    dto.ImapSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.None);
                await client.AuthenticateAsync(dto.EmailAddress.Trim(), dto.Password);
                await client.DisconnectAsync(true);
                result.ImapOk = true;
            }
            catch (Exception ex)
            {
                result.Message = $"SMTP سالم است — IMAP: {ex.Message}";
                return result;
            }
        }
        else
        {
            result.ImapOk = true; // دریافت تنظیم نشده — فقط ارسال
        }

        result.Message = "اتصال موفق بود ✓" +
            (dto.EmailType == "Gmail" ? " — یادآوری: برای Gmail از App Password استفاده کنید." : "");
        return result;
    }

    // ==================== ارسال ایمیل (SMTP) ====================

    public async Task<int> SendAsync(EmailComposeDto dto, int userId, bool isDabirkhaneAdmin, (string Name, string ContentType, byte[] Data)[] attachments)
    {
        var acc = await AccessibleAccountAsync(dto.EmailAccountId, userId, isDabirkhaneAdmin)
            ?? throw new Exception("حساب ایمیل فرستنده پیدا نشد یا به شما تعلق ندارد.");
        if (!acc.IsActive) throw new Exception("حساب ایمیل غیرفعال است.");

        var toList = ParseAddresses(dto.To);
        if (toList.Count == 0) throw new Exception("هیچ آدرس گیرنده معتبری وارد نشده است.");

        var sent = new OtoSentEmail
        {
            EmailId = acc.EmailId,
            UId = "",
            ToDisplay = string.Join("، ", toList.Select(t => t.Address)),
            Subject = dto.Subject?.Trim() ?? "",
            Date = DateTime.Now,
            Body = dto.Body ?? "",
            IsAttachment = false
        };
        _db.OtoSentEmails.Add(sent);
        await _db.SaveChangesAsync();

        var files = await BuildAndSendAsync(acc, toList, ParseAddresses(dto.Cc), dto.Subject?.Trim() ?? "", dto.Body ?? "",
            attachments ?? Array.Empty<(string, string, byte[])>(), sent.SentId);

        sent.UId = files.uid;
        sent.IsAttachment = files.hasAtt;
        await _db.SaveChangesAsync();

        return sent.SentId;
    }

    /// <summary>ساخت پیام Mime و ارسال با SMTP + ذخیره پیوست روی دیسک</summary>
    private async Task<(string uid, bool hasAtt)> BuildAndSendAsync(
        OtoEmail acc, List<MailboxAddress> to, List<MailboxAddress> cc,
        string subject, string htmlBody, (string Name, string ContentType, byte[] Data)[] attachments, int sentId)
    {
        var msg = new MimeMessage();
        var fromName = string.IsNullOrWhiteSpace(acc.DisplayName) ? acc.EmailAddress : acc.DisplayName;
        msg.From.Add(new MailboxAddress(fromName, acc.EmailAddress));
        msg.To.AddRange(to);
        if (cc.Count > 0) msg.Cc.AddRange(cc);
        msg.Subject = subject;
        msg.MessageId = $"<{Guid.NewGuid():N}@autmation.local>";

        var builder = new BodyBuilder { HtmlBody = htmlBody };
        foreach (var (name, ct, data) in attachments)
        {
            builder.Attachments.Add(name, data, ContentType.Parse(MimeToExtensionType(name, ct)));
        }
        msg.Body = builder.ToMessageBody();

        if (string.IsNullOrWhiteSpace(acc.Smtp))
            throw new Exception("سرور SMTP حساب ایمیل تنظیم نشده است — از بخش تنظیمات ایمیل تکمیل کنید.");

        string uid;
        using (var client = new SmtpClient { Timeout = 30000 })
        {
            await client.ConnectAsync(acc.Smtp, acc.SmtpPort, SmtpOptions(acc.SmtpPort, acc.SmtpSsl));
            await client.AuthenticateAsync(acc.EmailAddress, acc.Password);
            await client.SendAsync(msg);
            await client.DisconnectAsync(true);
        }
        uid = msg.MessageId ?? Guid.NewGuid().ToString("N");

        // ذخیره پیوست‌های ارسالی روی دیسک + جدول Oto_TBL_EmailAttachments
        foreach (var (name, ct, data) in attachments)
        {
            var dir = Path.Combine(AttachmentRoot, $"Sent_{sentId}");
            Directory.CreateDirectory(dir);
            var saved = $"{Guid.NewGuid():N}_{SafeName(name)}";
            await File.WriteAllBytesAsync(Path.Combine(dir, saved), data);
            _db.OtoEmailAttachments.Add(new OtoEmailAttachment
            {
                EmailId = sentId,
                UId = uid,
                Type = "Sent",
                AttachmentRealName = name,
                AttachmentSavedName = saved,
                FilePath = $"{AttachmentFolder}/Sent_{sentId}/{saved}"
            });
        }
        await _db.SaveChangesAsync();

        return (uid, attachments.Length > 0);
    }

    /// <summary>حذف فایل‌های پیوست یک پیام از دیسک</summary>
    private void DeleteAttachmentFiles(string box, int messageId)
    {
        var dir = Path.Combine(AttachmentRoot, $"{box}_{messageId}");
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
    }

    // ==================== ارسال نامه صادره از دبیرخانه با پست الکترونیک ====================

    /// <summary>
    /// ارسال نامه صادره امضا شده به ایمیل مقصد:
    /// متن نامه در بدنه + PDF چاپ‌شده روی سربرگ شرکت + پیوست‌های نامه
    /// </summary>
    public async Task<int> SendLetterEmailAsync(int letterId, DabirkhaneRegisterDto dto, string userName)
    {
        var destEmail = dto.DestEmail?.Trim();
        if (string.IsNullOrWhiteSpace(destEmail))
            throw new Exception("آدرس ایمیل مقصد برای ارسال نامه الزامی است.");
        var toList = ParseAddresses(destEmail);
        if (toList.Count == 0) throw new Exception("آدرس ایمیل مقصد معتبر نیست.");

        // ---------- حساب دبیرخانه (جدا از ایمیل شخصی) ----------
        var acc = dto.EmailAccountId is > 0
            ? await _db.OtoEmails.FirstOrDefaultAsync(e => e.EmailId == dto.EmailAccountId && e.IsActive)
              ?? throw new Exception("حساب ایمیل دبیرخانه انتخاب‌شده پیدا نشد یا غیرفعال است.")
            : await _db.OtoEmails.FirstOrDefaultAsync(e => e.IsDabirkhane && e.IsActive)
              ?? throw new Exception("هیچ حساب ایمیل دبیرخانه فعالی ثبت نشده است — ابتدا از «تنظیمات ایمیل» حساب دبیرخانه را اضافه کنید.");
        if (!acc.IsDabirkhane)
            throw new Exception("برای ارسال نامه باید حساب رسمی دبیرخانه (IsDabirkhane) انتخاب شود — ایمیل شخصی مجاز نیست.");

        var letter = await _db.OutgoingLetters.AsNoTracking()
            .Include(l => l.Source)
            .FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete)
            ?? throw new Exception("نامه پیدا نشد.");
        if (string.IsNullOrWhiteSpace(letter.SadereNumber))
            throw new Exception("فقط نامه‌های امضا شده (دارای شماره صادره) با ایمیل ارسال می‌شوند.");

        // ---------- بدنه ایمیل ----------
        var signers = await _db.OutgoingLetterSigners.AsNoTracking()
            .Include(s => s.User)
            .Where(s => s.SourceId == letterId && !s.IsDelete)
            .OrderBy(s => s.Order).ToListAsync();
        static string Name(Data.User? u) => u == null ? "" :
            string.IsNullOrWhiteSpace((u.FirstName ?? "") + (u.LastName ?? "")) ? u.Username ?? "" : ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim();

        var sb = new System.Text.StringBuilder();
        sb.Append(letter.Text ?? "");
        sb.Append("<hr style=\"border:none;border-top:1px solid #ccc;margin:18px 0\"/>");
        sb.Append("<div style=\"font-family:Tahoma;font-size:12px;color:#333;direction:rtl\">");
        sb.Append($"<p><b>شماره صادره:</b> {letter.SadereNumber} — <b>تاریخ صدور:</b> {PersianDate.ToShortDateTime(letter.DateSadere ?? letter.DateSabt)}</p>");
        sb.Append($"<p><b>گیرنده:</b> {letter.ReceiverOrganization}{(string.IsNullOrWhiteSpace(letter.ReceiverName) ? "" : " — " + letter.ReceiverName)}</p>");
        if (signers.Count > 0)
        {
            sb.Append("<p><b>امضا کنندگان:</b> ");
            sb.Append(string.Join("، ", signers.Select(s => Name(s.User) + (s.IsSigned ? " ✓" : ""))));
            sb.Append("</p>");
        }
        sb.Append("<p style=\"color:#777\">این نامه از طریق سامانه اتوماسیون اداری و درگاه ایمیل دبیرخانه ارسال شده است.</p>");
        sb.Append("</div>");

        // ---------- پیوست‌ها: PDF نامه + پیوست‌های نامه ----------
        var attachments = new List<(string, string, byte[])>();
        try
        {
            var pdf = await _print.GeneratePdfAsync(letterId, "A4");
            if (pdf is { Length: > 0 })
            {
                var pdfName = $"نامه-{letter.SadereNumber.Replace('/', '-')}.pdf";
                attachments.Add((pdfName, "application/pdf", pdf));
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "ساخت PDF نامه صادره {LetterId} برای ایمیل ناموفق بود", letterId);
        }

        var letterAtts = await _db.AppAttachments.AsNoTracking()
            .Where(a => a.Module == "OutgoingLetters" && a.RefId == letterId)
            .ToListAsync();
        foreach (var a in letterAtts)
        {
            var bytes = a.Data is { Length: > 0 } ? a.Data : ReadDiskFile(a.FilePath);
            if (bytes is { Length: > 0 }) attachments.Add((a.FileName, a.ContentType, bytes));
        }

        // ---------- ثبت و ارسال ----------
        var sent = new OtoSentEmail
        {
            EmailId = acc.EmailId,
            UId = "",
            ToDisplay = string.Join("، ", toList.Select(t => t.Address)),
            Subject = $"نامه شماره {letter.SadereNumber} — {letter.Title}",
            Date = DateTime.Now,
            Body = sb.ToString(),
            IsAttachment = attachments.Count > 0,
            LetterSourceId = letterId
        };
        _db.OtoSentEmails.Add(sent);
        await _db.SaveChangesAsync();

        var (uid, _) = await BuildAndSendAsync(acc, toList, new List<MailboxAddress>(),
            sent.Subject, sent.Body, attachments.ToArray(), sent.SentId);
        sent.UId = uid;
        await _db.SaveChangesAsync();
        return sent.SentId;
    }

    private byte[]? ReadDiskFile(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var clean = relativePath.Replace('\\', '/').TrimStart('/');
        if (clean.Contains("..")) return null;
        var full = Path.GetFullPath(Path.Combine(_env.ContentRootPath, "wwwroot", clean));
        var root = Path.GetFullPath(Path.Combine(_env.ContentRootPath, "wwwroot"));
        if (!full.StartsWith(root, StringComparison.Ordinal) || !File.Exists(full)) return null;
        try { return File.ReadAllBytes(full); } catch { return null; }
    }

    // ==================== دریافت ایمیل (IMAP) ====================

    public async Task<EmailSyncResultDto> SyncAsync(int emailId, int userId, bool isDabirkhaneAdmin)
    {
        var acc = await AccessibleAccountAsync(emailId, userId, isDabirkhaneAdmin)
            ?? throw new Exception("حساب ایمیل پیدا نشد یا به شما تعلق ندارد.");
        if (!acc.IsActive) throw new Exception("حساب ایمیل غیرفعال است.");
        if (string.IsNullOrWhiteSpace(acc.Imap))
            throw new Exception("سرور IMAP این حساب تنظیم نشده است — از تنظیمات ایمیل تکمیل کنید.");

        var result = new EmailSyncResultDto();
        using var client = new ImapClient { Timeout = 30000 };
        await client.ConnectAsync(acc.Imap, acc.ImapPort, acc.ImapSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.None);
        await client.AuthenticateAsync(acc.EmailAddress, acc.Password);
        await client.Inbox.OpenAsync(FolderAccess.ReadOnly);

        var total = client.Inbox.Count;
        result.TotalCount = total;

        // آخرین ۵۰ پیام بررسی می‌شود — پیام‌های تکراری با UId رد می‌شوند
        var start = Math.Max(0, total - 50);
        var existing = await _db.OtoInboxEmails.Where(i => i.EmailId == emailId).Select(i => i.UId).ToListAsync();
        var existingSet = new HashSet<string>(existing);

        for (int i = total - 1; i >= start; i--)
        {
            MimeMessage msg;
            try { msg = await client.Inbox.GetMessageAsync(i); }
            catch { continue; }

            var uid = !string.IsNullOrWhiteSpace(msg.MessageId) ? msg.MessageId : $"idx-{emailId}-{i}";
            if (existingSet.Contains(uid)) continue;

            var body = msg.HtmlBody ?? (msg.TextBody != null ? "<pre style=\"white-space:pre-wrap\">" + System.Net.WebUtility.HtmlEncode(msg.TextBody) + "</pre>" : "");

            var inbox = new OtoInboxEmail
            {
                EmailId = emailId,
                UId = uid,
                Subject = msg.Subject ?? "",
                Date = msg.Date == default ? DateTime.Now : msg.Date.LocalDateTime,
                IsRead = false,
                Body = body,
                FromAddress = ExtractAddress(msg.From.ToString()) ?? "",
                IsAttachment = msg.Attachments.Any()
            };
            _db.OtoInboxEmails.Add(inbox);
            await _db.SaveChangesAsync();

            // پیوست‌ها
            var dir = Path.Combine(AttachmentRoot, $"Inbox_{inbox.InboxId}");
            foreach (var entity in msg.Attachments)
            {
                if (entity is not MimePart part) continue;
                try
                {
                    var name = string.IsNullOrWhiteSpace(part.FileName) ? "attachment" : part.FileName;
                    using var ms = new MemoryStream();
                    await part.Content.DecodeToAsync(ms);
                    Directory.CreateDirectory(dir);
                    var saved = $"{Guid.NewGuid():N}_{SafeName(name)}";
                    await File.WriteAllBytesAsync(Path.Combine(dir, saved), ms.ToArray());
                    _db.OtoEmailAttachments.Add(new OtoEmailAttachment
                    {
                        EmailId = inbox.InboxId,
                        UId = uid,
                        Type = "Inbox",
                        AttachmentRealName = name,
                        AttachmentSavedName = saved,
                        FilePath = $"{AttachmentFolder}/Inbox_{inbox.InboxId}/{saved}"
                    });
                }
                catch (Exception ex) { _log.LogWarning(ex, "ذخیره پیوست ایمیل دریافتی ناموفق بود"); }
            }
            await _db.SaveChangesAsync();
            existingSet.Add(uid);
            result.NewCount++;
        }

        await client.DisconnectAsync(true);

        acc.LastSync = DateTime.Now;
        await _db.SaveChangesAsync();
        result.Message = result.NewCount > 0
            ? $"{result.NewCount} ایمیل جدید دریافت شد."
            : "ایمیل جدیدی نبود.";
        return result;
    }

    // ==================== لیست‌ها / جزئیات ====================

    public async Task<List<EmailMessageListItemDto>> GetInboxAsync(int userId, int? emailId, string? search, bool? unreadOnly, bool isDabirkhaneAdmin)
    {
        var myIds = await MyAccountIdsAsync(userId, isDabirkhaneAdmin);
        if (myIds.Count == 0) return new();
        var ids = emailId is > 0 && myIds.Contains(emailId.Value) ? new List<int> { emailId.Value } : myIds;

        var q = _db.OtoInboxEmails.AsNoTracking()
            .Include(i => i.Email)
            .Where(i => ids.Contains(i.EmailId));
        if (unreadOnly == true) q = q.Where(i => !i.IsRead);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(i => i.Subject.Contains(s) || i.FromAddress.Contains(s) || i.Body.Contains(s));
        }

        var rows = await q.OrderByDescending(i => i.Date).Take(200)
            .Select(i => new EmailMessageListItemDto
            {
                Id = i.InboxId,
                Box = "Inbox",
                EmailId = i.EmailId,
                AccountAddress = i.Email != null ? i.Email.EmailAddress : "",
                FromOrTo = i.FromAddress,
                Subject = i.Subject,
                Date = i.Date,
                IsRead = i.IsRead,
                IsNeshan = i.IsNeshan,
                IsAttachment = i.IsAttachment,
                IsInFolder = i.IsInFolder
            }).ToListAsync();
        await FillFolderTitlesAsync(rows);
        return rows;
    }

    public async Task<List<EmailMessageListItemDto>> GetSentAsync(int userId, int? emailId, string? search, bool isDabirkhaneAdmin)
    {
        var myIds = await MyAccountIdsAsync(userId, isDabirkhaneAdmin);
        if (myIds.Count == 0) return new();
        var ids = emailId is > 0 && myIds.Contains(emailId.Value) ? new List<int> { emailId.Value } : myIds;

        var q = _db.OtoSentEmails.AsNoTracking()
            .Include(s => s.Email)
            .Where(s => ids.Contains(s.EmailId));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x => x.Subject.Contains(s) || x.ToDisplay.Contains(s) || x.Body.Contains(s));
        }

        var rows = await q.OrderByDescending(x => x.Date).Take(200)
            .Select(x => new EmailMessageListItemDto
            {
                Id = x.SentId,
                Box = "Sent",
                EmailId = x.EmailId,
                AccountAddress = x.Email != null ? x.Email.EmailAddress : "",
                FromOrTo = x.ToDisplay,
                Subject = x.Subject,
                Date = x.Date,
                IsRead = true,
                IsNeshan = x.IsNeshan,
                IsAttachment = x.IsAttachment,
                IsInFolder = x.IsInFolder
            }).ToListAsync();
        await FillFolderTitlesAsync(rows);
        return rows;
    }

    /// <summary>حساب‌های قابل مشاهده کاربر: حساب‌های شخصی + (برای دبیرخانه) حساب‌های رسمی دبیرخانه</summary>
    private async Task<List<int>> MyAccountIdsAsync(int userId, bool isDabirkhaneAdmin)
    {
        var ids = await _db.OtoEmails.AsNoTracking()
            .Where(e => e.UserId == userId).Select(e => e.EmailId).ToListAsync();
        if (isDabirkhaneAdmin)
        {
            var dab = await _db.OtoEmails.AsNoTracking()
                .Where(e => e.IsDabirkhane).Select(e => e.EmailId).ToListAsync();
            ids.AddRange(dab);
        }
        return ids.Distinct().ToList();
    }

    private async Task FillFolderTitlesAsync(List<EmailMessageListItemDto> rows)
    {
        var folderIds = rows.Where(r => r.IsInFolder > 0).Select(r => r.IsInFolder).Distinct().ToList();
        if (folderIds.Count == 0) return;
        var titles = await _db.OtoEmailFolders.AsNoTracking()
            .Where(f => folderIds.Contains(f.EmailFolderId) && f.IsFolder)
            .ToDictionaryAsync(f => f.EmailFolderId, f => f.Title);
        foreach (var r in rows)
            if (r.IsInFolder > 0 && titles.TryGetValue(r.IsInFolder, out var t)) r.FolderTitle = t;
    }

    public async Task<EmailMessageDetailDto> GetMessageAsync(string box, int id, int userId, bool markRead, bool isDabirkhaneAdmin)
    {
        if (string.Equals(box, "Sent", StringComparison.OrdinalIgnoreCase))
        {
            var s = await _db.OtoSentEmails.AsNoTracking().Include(x => x.Email)
                .FirstOrDefaultAsync(x => x.SentId == id && (x.Email!.UserId == userId || (isDabirkhaneAdmin && x.Email!.IsDabirkhane)))
                ?? throw new Exception("ایمیل پیدا نشد.");
            return new EmailMessageDetailDto
            {
                Id = s.SentId, Box = "Sent", EmailId = s.EmailId,
                AccountAddress = s.Email?.EmailAddress ?? "",
                FromOrTo = s.ToDisplay, Subject = s.Subject, Date = s.Date,
                IsRead = true, IsNeshan = s.IsNeshan, Body = s.Body, IsInFolder = s.IsInFolder,
                Attachments = await AttachmentDtosAsync("Sent", s.SentId)
            };
        }

        var i = await _db.OtoInboxEmails.Include(x => x.Email)
            .FirstOrDefaultAsync(x => x.InboxId == id && (x.Email!.UserId == userId || (isDabirkhaneAdmin && x.Email!.IsDabirkhane)))
            ?? throw new Exception("ایمیل پیدا نشد.");
        if (markRead && !i.IsRead) { i.IsRead = true; await _db.SaveChangesAsync(); }
        return new EmailMessageDetailDto
        {
            Id = i.InboxId, Box = "Inbox", EmailId = i.EmailId,
            AccountAddress = i.Email?.EmailAddress ?? "",
            FromOrTo = i.FromAddress, Subject = i.Subject, Date = i.Date,
            IsRead = true, IsNeshan = i.IsNeshan, Body = i.Body, IsInFolder = i.IsInFolder,
            Attachments = await AttachmentDtosAsync("Inbox", i.InboxId)
        };
    }

    private async Task<List<EmailAttachmentDto>> AttachmentDtosAsync(string box, int messageId)
    {
        var list = await _db.OtoEmailAttachments.AsNoTracking()
            .Where(a => a.Type == box && a.EmailId == messageId)
            .ToListAsync();
        return list.Select(a => new EmailAttachmentDto
        {
            AttachmentId = a.AttachmentId,
            RealName = a.AttachmentRealName,
            Size = DiskFileSize(a.FilePath)
        }).ToList();
    }

    private long DiskFileSize(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return 0;
        var clean = relativePath.Replace('\\', '/').TrimStart('/');
        if (clean.Contains("..")) return 0;
        var full = Path.GetFullPath(Path.Combine(_env.ContentRootPath, "wwwroot", clean));
        var root = Path.GetFullPath(Path.Combine(_env.ContentRootPath, "wwwroot"));
        if (!full.StartsWith(root, StringComparison.Ordinal) || !File.Exists(full)) return 0;
        try { return new FileInfo(full).Length; } catch { return 0; }
    }

    public async Task MarkReadAsync(string box, int id, int userId, bool isDabirkhaneAdmin)
    {
        if (string.Equals(box, "Sent", StringComparison.OrdinalIgnoreCase)) return;
        var i = await _db.OtoInboxEmails.Include(x => x.Email).FirstOrDefaultAsync(x => x.InboxId == id && x.Email!.UserId == userId)
            ?? throw new Exception("ایمیل پیدا نشد.");
        i.IsRead = true;
        await _db.SaveChangesAsync();
    }

    public async Task<bool> ToggleNeshanAsync(string box, int id, int userId, bool isDabirkhaneAdmin)
    {
        if (string.Equals(box, "Sent", StringComparison.OrdinalIgnoreCase))
        {
            var s = await _db.OtoSentEmails.Include(x => x.Email).FirstOrDefaultAsync(x => x.SentId == id && (x.Email!.UserId == userId || (isDabirkhaneAdmin && x.Email!.IsDabirkhane)))
                ?? throw new Exception("ایمیل پیدا نشد.");
            s.IsNeshan = !s.IsNeshan;
            await _db.SaveChangesAsync();
            return s.IsNeshan;
        }
        var i = await _db.OtoInboxEmails.Include(x => x.Email).FirstOrDefaultAsync(x => x.InboxId == id && (x.Email!.UserId == userId || (isDabirkhaneAdmin && x.Email!.IsDabirkhane)))
            ?? throw new Exception("ایمیل پیدا نشد.");
        i.IsNeshan = !i.IsNeshan;
        await _db.SaveChangesAsync();
        return i.IsNeshan;
    }

    public async Task ArchiveAsync(EmailArchiveDto dto, int userId, bool isDabirkhaneAdmin)
    {
        if (dto.FolderId > 0)
        {
            _ = await _db.OtoEmailFolders.FirstOrDefaultAsync(f => f.EmailFolderId == dto.FolderId && f.UserId == userId && f.IsFolder)
                ?? throw new Exception("پوشه بایگانی پیدا نشد.");
        }
        if (string.Equals(dto.Box, "Sent", StringComparison.OrdinalIgnoreCase))
        {
            var s = await _db.OtoSentEmails.Include(x => x.Email).FirstOrDefaultAsync(x => x.SentId == dto.Id && (x.Email!.UserId == userId || (isDabirkhaneAdmin && x.Email!.IsDabirkhane)))
                ?? throw new Exception("ایمیل پیدا نشد.");
            s.IsInFolder = dto.FolderId;
        }
        else
        {
            var i = await _db.OtoInboxEmails.Include(x => x.Email).FirstOrDefaultAsync(x => x.InboxId == dto.Id && (x.Email!.UserId == userId || (isDabirkhaneAdmin && x.Email!.IsDabirkhane)))
                ?? throw new Exception("ایمیل پیدا نشد.");
            i.IsInFolder = dto.FolderId;
        }
        await _db.SaveChangesAsync();
    }

    public async Task<byte[]?> ReadAttachmentAsync(int attachmentId, int userId, bool isDabirkhaneAdmin)
    {
        var a = await _db.OtoEmailAttachments.AsNoTracking().FirstOrDefaultAsync(x => x.AttachmentId == attachmentId)
            ?? throw new Exception("پیوست پیدا نشد.");

        bool allowed;
        if (string.Equals(a.Type, "Sent", StringComparison.OrdinalIgnoreCase))
            allowed = await _db.OtoSentEmails.AnyAsync(s => s.SentId == a.EmailId && (s.Email!.UserId == userId || (isDabirkhaneAdmin && s.Email!.IsDabirkhane)));
        else
            allowed = await _db.OtoInboxEmails.AnyAsync(i => i.InboxId == a.EmailId && (i.Email!.UserId == userId || (isDabirkhaneAdmin && i.Email!.IsDabirkhane)));
        if (!allowed) throw new Exception("این پیوست متعلق به حساب شما نیست.");

        if (string.IsNullOrWhiteSpace(a.FilePath)) return null;
        var clean = a.FilePath.Replace('\\', '/').TrimStart('/');
        if (clean.Contains("..")) return null;
        var full = Path.GetFullPath(Path.Combine(_env.ContentRootPath, "wwwroot", clean));
        var root = Path.GetFullPath(Path.Combine(_env.ContentRootPath, "wwwroot"));
        if (!full.StartsWith(root, StringComparison.Ordinal) || !File.Exists(full)) return null;
        return await File.ReadAllBytesAsync(full);
    }

    // ==================== پوشه‌های بایگانی ====================

    public async Task<List<EmailFolderDto>> GetFoldersAsync(int userId) =>
        await _db.OtoEmailFolders.AsNoTracking()
            .Where(f => f.UserId == userId && f.IsFolder && !f.IsDelete)
            .OrderBy(f => f.EmailFolderId)
            .Select(f => new EmailFolderDto { EmailFolderId = f.EmailFolderId, Title = f.Title, ParentId = f.ParentId, TypeEmail = f.TypeEmail })
            .ToListAsync();

    public async Task<int> SaveFolderAsync(SaveEmailFolderDto dto, int userId)
    {
        OtoEmailFolder f;
        if (dto.EmailFolderId > 0)
        {
            f = await _db.OtoEmailFolders.FirstOrDefaultAsync(x => x.EmailFolderId == dto.EmailFolderId && x.UserId == userId && x.IsFolder)
                ?? throw new Exception("پوشه پیدا نشد.");
            f.Title = dto.Title.Trim();
        }
        else
        {
            f = new OtoEmailFolder { UserId = userId, IsFolder = true, EmailId = 0 };
            _db.OtoEmailFolders.Add(f);
            f.Title = dto.Title.Trim();
            f.ParentId = dto.ParentId;
            f.TypeEmail = dto.TypeEmail;
        }
        await _db.SaveChangesAsync();
        return f.EmailFolderId;
    }

    public async Task DeleteFolderAsync(int folderId, int userId)
    {
        var f = await _db.OtoEmailFolders.FirstOrDefaultAsync(x => x.EmailFolderId == folderId && x.UserId == userId && x.IsFolder)
            ?? throw new Exception("پوشه پیدا نشد.");
        f.IsDelete = true;
        // ایمیل‌های داخل پوشه فقط از بایگانی خارج می‌شوند
        await _db.OtoInboxEmails.Where(i => i.IsInFolder == folderId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsInFolder, 0));
        await _db.OtoSentEmails.Where(s => s.IsInFolder == folderId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsInFolder, 0));
        await _db.SaveChangesAsync();
    }
}
