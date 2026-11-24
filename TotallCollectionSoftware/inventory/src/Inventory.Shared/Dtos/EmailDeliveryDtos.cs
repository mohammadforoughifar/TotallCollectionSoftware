namespace Inventory.Shared.Dtos;

/// <summary>Accepted means SMTP accepted the message, NOT that the recipient read/received it.</summary>
public enum EmailDeliveryStatus
{
    Unknown = 0,
    Preparing = 1,
    Sending = 2,
    Accepted = 3,
    AcceptedWithWarnings = 4,
    Failed = 5
}

public sealed class EmailDeliveryEventDto
{
    public DateTime AtUtc { get; set; }
    public string Stage { get; set; } = "";
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
}

public class EmailDeliveryResultDto
{
    public Guid RequestId { get; set; }
    public long? AttemptId { get; set; }
    public int? SentId { get; set; }
    public int? LetterSourceId { get; set; }
    public EmailDeliveryStatus Status { get; set; } = EmailDeliveryStatus.Unknown;
    public string Stage { get; set; } = "";
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public string MessageId { get; set; } = "";
    public DateTime? AcceptedAtUtc { get; set; }
    public bool ResultPersisted { get; set; } = true;
    public bool Replayed { get; set; }
    public bool DuplicateSuppressed { get; set; }
    public List<EmailDeliveryEventDto> Events { get; set; } = new();

    public bool SmtpAccepted => Status is EmailDeliveryStatus.Accepted or EmailDeliveryStatus.AcceptedWithWarnings;
    public bool IsPending => Status is EmailDeliveryStatus.Preparing or EmailDeliveryStatus.Sending;
    public bool CanRetry => Status == EmailDeliveryStatus.Failed && ResultPersisted;
}

public sealed class EmailDeliveryAttemptDto : EmailDeliveryResultDto
{
    public int EmailAccountId { get; set; }
    public int RequestedByUserId { get; set; }
    public string AccountAddress { get; set; } = "";
    public string Recipients { get; set; } = "";
    public string Subject { get; set; } = "";
    public DateTime StartedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public Guid? PreviousRequestId { get; set; }
    public int? SmtpStatusCode { get; set; }
    // Only the exception TYPE is exposed. Never an exception dump, credential, or message body.
    public string? TechnicalType { get; set; }
}

public sealed class DabirkhaneRegisterResultDto
{
    /// <summary>null = the HTTP result was lost; do not infer registration or delivery.</summary>
    public bool? RegistrationSaved { get; set; }
    public EmailDeliveryResultDto? Email { get; set; }
    public string? Warning { get; set; }
}

public static class EmailDeliveryText
{
    public static string Status(EmailDeliveryStatus? status) => status switch
    {
        EmailDeliveryStatus.Preparing => "در حال آماده‌سازی",
        EmailDeliveryStatus.Sending => "در حال ارسال",
        EmailDeliveryStatus.Accepted => "پذیرفته‌شده توسط سرور ایمیل",
        EmailDeliveryStatus.AcceptedWithWarnings => "پذیرفته‌شده با هشدار تکمیلی",
        EmailDeliveryStatus.Failed => "ناموفق — ارسال انجام نشد",
        EmailDeliveryStatus.Unknown => "نتیجه نامشخص — نیازمند بررسی",
        _ => "سابقهٔ قدیمی / دریافت‌شده از سرور"
    };

    public static string Stage(string? stage) => stage switch
    {
        "prepare" => "آماده‌سازی و پیوست‌ها",
        "connect" => "اتصال به سرور ایمیل",
        "authenticate" => "احراز هویت حساب",
        "send" => "تحویل پیام به SMTP",
        "disconnect" => "بستن اتصال پس از ارسال",
        "persist" => "ثبت نتیجه در سامانه",
        "notify" => "اعلان پس از ثبت",
        "contacts" => "بانک ایمیل مخاطبین",
        "request" => "ثبت درخواست",
        _ => "بررسی نتیجه"
    };

    public static string Tone(EmailDeliveryStatus? status) => status switch
    {
        EmailDeliveryStatus.Accepted => "success",
        EmailDeliveryStatus.Failed => "danger",
        EmailDeliveryStatus.AcceptedWithWarnings or EmailDeliveryStatus.Unknown => "warning",
        _ => "info"
    };

    public static EmailDeliveryResultDto Uncertain(Guid requestId) => new()
    {
        RequestId = requestId,
        Status = EmailDeliveryStatus.Unknown,
        ResultPersisted = false,
        Code = "HTTP_RESULT_UNKNOWN",
        Stage = "request",
        Message = "نتیجهٔ درخواست از سرور دریافت نشد؛ این پیام به معنی ارسال‌نشدن نیست. با همین کد پیگیری وضعیت را بررسی کنید و بدون بررسی، ارسال جدید انجام ندهید."
    };
}
