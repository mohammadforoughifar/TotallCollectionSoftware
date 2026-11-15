using System.Net;
using System.Net.Mail;
using System.Text;
using LicenseSigner.Data;
using Microsoft.EntityFrameworkCore;

namespace LicenseSigner.Services;

/// <summary>نتیجهٔ تحویل.</summary>
public sealed record DeliveryResult(bool Ok, string Message, string? FilePath = null);

/// <summary>
/// تحویل لایسنس: پیش‌نویس <c>.eml</c> با پیوست، ارسال SMTP (اختیاری) و ثبت تحویل دستی.
/// رمز SMTP فقط از متغیر محیطی خوانده می‌شود.
/// </summary>
public sealed class DeliveryService(SupportDbContext db, SignerOptions options, SupportSettingsService settings,
    DraftService drafts, AuditChainService audit, RequestService requests, ILogger<DeliveryService> log)
{
    public string OutboxFolder
    {
        get
        {
            var folder = Path.Combine(options.EffectiveWorkFolder(), "outbox");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    public (byte[]? Bytes, string? Error) ReadLicenseFile(IssuedLicense license)
    {
        var path = drafts.ResolveLicensePath(license);
        if (path is null || !File.Exists(path)) return (null, "فایل لایسنس روی دیسک نیست (پوشهٔ کاری را بررسی کنید).");
        try
        {
            return (File.ReadAllBytes(path), null);
        }
        catch (IOException ex)
        {
            return (null, "خواندن فایل لایسنس ناموفق بود: " + ex.Message);
        }
    }

    /// <summary>ساخت پیش‌نویس ایمیل با پیوست .lic — چیزی ارسال نمی‌شود.</summary>
    public async Task<DeliveryResult> CreateEmlDraftAsync(int licenseId, SupportUser actor, CancellationToken ct = default)
    {
        var license = await db.IssuedLicenses.Include(l => l.Request).ThenInclude(r => r!.Customer)
            .FirstOrDefaultAsync(l => l.Id == licenseId, ct);
        if (license is null) return new DeliveryResult(false, "لایسنس پیدا نشد.");

        var (bytes, error) = ReadLicenseFile(license);
        if (bytes is null) return new DeliveryResult(false, error ?? "فایل لایسنس خوانده نشد.");

        var template = await settings.GetTemplateAsync(ct);
        var recipient = license.Request?.Customer?.Email ?? license.Request?.ContactEmail ?? "";
        var subject = Apply(template.Subject, license);
        var body = Apply(template.Body, license);

        var stamp = JalaliDate.TehranNow().ToString("yyyyMMddHHmmss");
        var fileName = $"{Safe(license.Serial)}-{stamp}.eml";
        var path = Path.Combine(OutboxFolder, fileName);

        // .eml خودمان می‌سازیم: .NET 8 متد WriteToAsync برای MailMessage ندارد و
        // وابستگی بیرونی هم نمی‌خواهیم. ساختار: multipart/mixed با پیوست base64.
        var boundary = "==LICPANEL-" + Guid.NewGuid().ToString("N") + "==";
        var builder = new StringBuilder();
        builder.Append("From: پشتیبانی لایسنس <support@localhost>\r\n");
        if (!string.IsNullOrWhiteSpace(recipient)) builder.Append($"To: {recipient}\r\n");
        builder.Append($"Subject: =?UTF-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(subject))}?=\r\n");
        builder.Append($"Date: {DateTime.UtcNow:R}\r\n");
        builder.Append("MIME-Version: 1.0\r\n");
        builder.Append($"Content-Type: multipart/mixed; boundary=\"{boundary}\"\r\n\r\n");
        builder.Append($"--{boundary}\r\n");
        builder.Append("Content-Type: text/plain; charset=utf-8\r\n");
        builder.Append("Content-Transfer-Encoding: base64\r\n\r\n");
        builder.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(body), Base64FormattingOptions.InsertLineBreaks)).Append("\r\n");
        builder.Append($"--{boundary}\r\n");
        builder.Append($"Content-Type: application/json; name=\"{license.FileName}\"\r\n");
        builder.Append($"Content-Disposition: attachment; filename=\"{license.FileName}\"\r\n");
        builder.Append("Content-Transfer-Encoding: base64\r\n\r\n");
        builder.Append(Convert.ToBase64String(bytes, Base64FormattingOptions.InsertLineBreaks)).Append("\r\n");
        builder.Append($"--{boundary}--\r\n");

        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(false), ct);

        db.Deliveries.Add(new Delivery
        {
            IssuedLicenseId = license.Id,
            Channel = DeliveryChannel.EmlDraft,
            At = DateTime.UtcNow,
            Recipient = string.IsNullOrWhiteSpace(recipient) ? null : recipient,
            Subject = subject,
            Note = fileName,
            Success = true,
            ByUserId = actor.Id,
            ByName = actor.DisplayName
        });

        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(actor.UserName, "DELIVER_EML", "IssuedLicense", license.Id.ToString(),
            null, actor.Id, ct);

        log.LogInformation("پیش‌نویس ایمیل برای لایسنس {Serial} ساخته شد", license.Serial);
        return new DeliveryResult(true, "پیش‌نویس ایمیل ساخته شد", path);
    }

    /// <summary>ارسال SMTP — فقط اگر در تنظیمات کامل باشد و رمز در env موجود باشد.</summary>
    public async Task<DeliveryResult> SendSmtpAsync(int licenseId, string recipient, SupportUser actor,
        CancellationToken ct = default)
    {
        var license = await db.IssuedLicenses.FirstOrDefaultAsync(l => l.Id == licenseId, ct);
        if (license is null) return new DeliveryResult(false, "لایسنس پیدا نشد.");

        var smtp = await settings.GetSmtpAsync(ct);
        if (!smtp.IsConfigured) return new DeliveryResult(false, "SMTP تنظیم نشده است؛ از «پیش‌نویس ایمیل» یا «دانلود» استفاده کنید.");
        if (string.IsNullOrWhiteSpace(recipient)) return new DeliveryResult(false, "گیرنده را وارد کنید.");

        var (bytes, error) = ReadLicenseFile(license);
        if (bytes is null) return new DeliveryResult(false, error ?? "فایل لایسنس خوانده نشد.");

        var password = Environment.GetEnvironmentVariable(options.SmtpPasswordEnvironmentVariable);
        var template = await settings.GetTemplateAsync(ct);

        try
        {
            using var client = new SmtpClient(smtp.Host, smtp.Port)
            {
                EnableSsl = smtp.UseStartTls,
                Timeout = Math.Clamp(smtp.TimeoutSeconds, 5, 120) * 1000
            };
            if (!string.IsNullOrWhiteSpace(smtp.User))
                client.Credentials = new NetworkCredential(smtp.User, password ?? "");

            using var message = new MailMessage
            {
                From = new MailAddress(smtp.From, smtp.FromName),
                Subject = Apply(template.Subject, license),
                Body = Apply(template.Body, license),
                SubjectEncoding = Encoding.UTF8,
                BodyEncoding = Encoding.UTF8
            };
            message.To.Add(recipient);
            message.Attachments.Add(new Attachment(new MemoryStream(bytes), license.FileName, "application/json"));

            await client.SendMailAsync(message, ct);

            db.Deliveries.Add(new Delivery
            {
                IssuedLicenseId = license.Id,
                Channel = DeliveryChannel.EmailSmtp,
                At = DateTime.UtcNow,
                Recipient = recipient,
                Subject = message.Subject,
                Success = true,
                ByUserId = actor.Id,
                ByName = actor.DisplayName
            });
            await MarkDeliveredAsync(license.RequestId, ct);
            await db.SaveChangesAsync(ct);
            await audit.AppendAsync(actor.UserName, "DELIVER_SMTP", "IssuedLicense", license.Id.ToString(),
                null, actor.Id, ct);
            return new DeliveryResult(true, "ایمیل ارسال شد.");
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or IOException)
        {
            db.Deliveries.Add(new Delivery
            {
                IssuedLicenseId = license.Id,
                Channel = DeliveryChannel.EmailSmtp,
                At = DateTime.UtcNow,
                Recipient = recipient,
                Success = false,
                Error = ex.Message.Length <= 300 ? ex.Message : ex.Message[..300],
                ByUserId = actor.Id,
                ByName = actor.DisplayName
            });
            await db.SaveChangesAsync(ct);
            log.LogWarning("ارسال SMTP ناموفق بود: {Message}", ex.Message);
            return new DeliveryResult(false, "ارسال ایمیل ناموفق بود: " + ex.Message);
        }
    }

    /// <summary>ثبت تحویل دستی/دانلود — بدون ارسال هیچ چیزی.</summary>
    public async Task<(bool Ok, string? Error)> RecordManualAsync(int licenseId, DeliveryChannel channel,
        string? recipient, string? note, SupportUser actor, CancellationToken ct = default)
    {
        var license = await db.IssuedLicenses.FirstOrDefaultAsync(l => l.Id == licenseId, ct);
        if (license is null) return (false, "لایسنس پیدا نشد.");

        db.Deliveries.Add(new Delivery
        {
            IssuedLicenseId = license.Id,
            Channel = channel,
            At = DateTime.UtcNow,
            Recipient = string.IsNullOrWhiteSpace(recipient) ? null : Clamp(recipient!, 200),
            Note = string.IsNullOrWhiteSpace(note) ? null : Clamp(note!, 300),
            Success = true,
            ByUserId = actor.Id,
            ByName = actor.DisplayName
        });

        await MarkDeliveredAsync(license.RequestId, ct);
        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(actor.UserName, "DELIVER", "IssuedLicense", license.Id.ToString(),
            channel.ToString(), actor.Id, ct);
        return (true, null);
    }

    private async Task MarkDeliveredAsync(int requestId, CancellationToken ct)
    {
        var request = await db.Requests.FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null || request.Status is not (RequestStatus.Issued or RequestStatus.Delivered)) return;

        var from = request.Status;
        request.Status = RequestStatus.Delivered;
        request.UpdatedAt = DateTime.UtcNow;
        await requests.AddEventAsync(requestId, "STATUS", from.ToString(), RequestStatus.Delivered.ToString(),
            "لایسنس تحویل داده شد.", null, ct);
        await audit.AppendAsync("system", "STATUS", "LicenseRequest", requestId.ToString(),
            $"{{\"from\":\"{from}\",\"to\":\"Delivered\"}}", null, ct);
    }

    private static string Apply(string template, IssuedLicense license) => template
        .Replace("{{Serial}}", license.Serial, StringComparison.Ordinal)
        .Replace("{{InstallationId}}", license.InstallationId, StringComparison.Ordinal)
        .Replace("{{CustomerName}}", license.Request?.Customer?.Name ?? "", StringComparison.Ordinal)
        .Replace("{{RequestNo}}", license.Request?.RequestNo ?? "", StringComparison.Ordinal);

    private static string Safe(string value) =>
        new string(value.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());

    private static string Clamp(string value, int max) => value.Length <= max ? value : value[..max];
}
