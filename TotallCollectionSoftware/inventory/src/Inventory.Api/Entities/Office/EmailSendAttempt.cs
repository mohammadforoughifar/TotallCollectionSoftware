using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// Durable send ledger. No password, body, raw server response or attachment content lives here.
/// Scalar references deliberately retain history without introducing cascade-delete paths into legacy tables.
/// Every read must join the current account-access scope.
/// </summary>
[Table("Oto_EmailSendAttempts")]
[Index(nameof(RequestId), IsUnique = true, Name = "UX_EmailSendAttempt_Request")]
[Index(nameof(GuardKey), IsUnique = true, Name = "UX_EmailSendAttempt_Guard")]
[Index(nameof(EmailAccountId), nameof(StartedAtUtc), Name = "IX_EmailSendAttempt_Account_Date")]
[Index(nameof(LetterSourceId), nameof(StartedAtUtc), Name = "IX_EmailSendAttempt_Letter_Date")]
[Index(nameof(SentId), Name = "IX_EmailSendAttempt_Sent")]
public sealed class EmailSendAttempt
{
    [Key] public long Id { get; set; }
    public Guid RequestId { get; set; }
    [MaxLength(64)] public string RequestHash { get; set; } = "";
    // Non-null holds a per-letter/account/recipient guard across requests and application instances.
    // Only a definite pre-acceptance failure, or a separate explicitly-confirmed resend, releases it.
    [MaxLength(64)] public string? GuardKey { get; set; }
    public Guid? PreviousRequestId { get; set; }
    public int RequestedByUserId { get; set; }
    public int EmailAccountId { get; set; }
    public int? LetterSourceId { get; set; }
    public int? SentId { get; set; }
    [MaxLength(250)] public string AccountAddress { get; set; } = "";
    [MaxLength(1000)] public string Recipients { get; set; } = "";
    [MaxLength(500)] public string Subject { get; set; } = "";
    public EmailDeliveryStatus Status { get; set; } = EmailDeliveryStatus.Preparing;
    [MaxLength(40)] public string Stage { get; set; } = "request";
    [MaxLength(64)] public string Code { get; set; } = "PREPARING";
    [MaxLength(1000)] public string Message { get; set; } = "درخواست ثبت شد؛ هنوز ارسال نشده است.";
    [MaxLength(200)] public string? TechnicalType { get; set; }
    public int? SmtpStatusCode { get; set; }
    [MaxLength(300)] public string MessageId { get; set; } = "";
    public DateTime StartedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string EventsJson { get; set; } = "[]";
}
