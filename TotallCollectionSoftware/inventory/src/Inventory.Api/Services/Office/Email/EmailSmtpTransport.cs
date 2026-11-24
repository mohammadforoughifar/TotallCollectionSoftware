using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Inventory.Api.Services.Office.Email;

public sealed class EmailSmtpOutcome
{
    public EmailDeliveryStatus Status { get; set; }
    public string Stage { get; set; } = "send";
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public string? TechnicalType { get; set; }
    public int? SmtpStatusCode { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public List<EmailDeliveryEventDto> Warnings { get; } = new();
}

public interface IEmailSmtpTransport
{
    Task<EmailSmtpOutcome> SendAsync(OtoEmail account, MimeMessage message);
}

/// <summary>One SMTP transaction. There is deliberately no automatic SMTP retry.</summary>
public sealed class EmailSmtpTransport : IEmailSmtpTransport
{
    private readonly Func<ISmtpClient> createClient;
    public EmailSmtpTransport() : this(() => new SmtpClient { Timeout = 30000 }) { }
    // A test-only seam for cleanup faults that some MailKit servers internally swallow.
    internal EmailSmtpTransport(Func<ISmtpClient> createClient) => this.createClient = createClient;
    public async Task<EmailSmtpOutcome> SendAsync(OtoEmail account, MimeMessage message)
    {
        var client = createClient();
        var stage = "connect";
        EmailSmtpOutcome result;
        try
        {
            // A configured secure connection must not silently downgrade to cleartext.
            var options = !account.SmtpSsl ? SecureSocketOptions.None
                : account.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
            await client.ConnectAsync(account.Smtp, account.SmtpPort, options);
            stage = "authenticate";
            await client.AuthenticateAsync(account.EmailAddress, account.Password);
            stage = "send";
            await client.SendAsync(message);
            // This is the irreversible boundary. QUIT, persistence and notifications cannot undo acceptance.
            result = new()
            {
                Status = EmailDeliveryStatus.Accepted, Code = "SMTP_ACCEPTED", Stage = "send",
                AcceptedAtUtc = DateTime.UtcNow,
                Message = "سرور ایمیل پیام را پذیرفته است؛ این وضعیت به معنی تحویل قطعی به صندوق گیرنده یا خوانده‌شدن نیست."
            };
        }
        catch (Exception ex)
        {
            result = Classify(ex, stage);
        }
        try
        {
            if (client.IsConnected) await client.DisconnectAsync(true);
        }
        catch (Exception)
        {
            result.Warnings.Add(new() { AtUtc = DateTime.UtcNow, Stage = "disconnect", Code = "SMTP_QUIT_WARNING",
                Message = "بستن اتصال ایمیل با خطا مواجه شد؛ نتیجهٔ مرحلهٔ ارسال تغییر نکرده است." });
        }
        finally
        {
            try { client.Dispose(); }
            catch (Exception)
            {
                result.Warnings.Add(new() { AtUtc = DateTime.UtcNow, Stage = "disconnect", Code = "SMTP_DISPOSE_WARNING",
                    Message = "پاک‌سازی اتصال با هشدار پایان یافت؛ این هشدار به معنی ارسال‌نشدن پیام نیست." });
            }
        }
        if (result.Status == EmailDeliveryStatus.Accepted && result.Warnings.Count > 0)
            result.Status = EmailDeliveryStatus.AcceptedWithWarnings;
        return result;
    }

    public static EmailSmtpOutcome Classify(Exception error, string stage)
    {
        var smtp = error as SmtpCommandException;
        var rejected = smtp != null && (int)smtp.StatusCode is >= 400 and < 600
            && smtp.ErrorCode is SmtpErrorCode.SenderNotAccepted or SmtpErrorCode.RecipientNotAccepted or SmtpErrorCode.MessageNotAccepted;
        var uncertain = stage == "send" && !rejected;
        var tls = error is SslHandshakeException or System.Security.Authentication.AuthenticationException
            || (stage == "connect" && error is NotSupportedException);
        var auth = stage == "authenticate";
        return new()
        {
            Status = uncertain ? EmailDeliveryStatus.Unknown : EmailDeliveryStatus.Failed,
            Stage = stage,
            Code = uncertain ? "SMTP_RESULT_UNKNOWN" : rejected ? "SMTP_REJECTED" : tls ? "SMTP_TLS_ERROR" : auth ? "SMTP_AUTH_ERROR" : "SMTP_CONNECT_ERROR",
            Message = uncertain
                ? "ارتباط هنگام تحویل پیام قطع یا نامعتبر شد؛ پذیرش پیام قابل تأیید نیست. قبل از ارسال جدید، وضعیت را در سرویس ایمیل یا نزد گیرنده بررسی کنید."
                : rejected ? "سرور ایمیل فرستنده، گیرنده یا پیام را رد کرد؛ این تلاش پذیرفته نشد. کد SMTP و تنظیمات حساب/گیرنده را بررسی کنید."
                : tls ? "اتصال امن ایمیل برقرار نشد؛ نام سرور، پورت، گواهی و تنظیمات TLS را بررسی کنید. پیام ارسال نشده است."
                : auth ? "احراز هویت حساب ایمیل انجام نشد؛ تنظیمات حساب و رمز برنامه را بررسی کنید. پیام ارسال نشده است."
                : "اتصال به سرور ایمیل برقرار نشد؛ آدرس سرور، پورت، شبکه و تنظیمات اتصال را بررسی کنید. پیام ارسال نشده است.",
            TechnicalType = error.GetType().Name,
            SmtpStatusCode = smtp == null ? null : (int)smtp.StatusCode
        };
    }
}
