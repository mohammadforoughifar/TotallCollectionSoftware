using System.ComponentModel.DataAnnotations;
using Inventory.Shared.Dtos;

namespace Inventory.Api.Data;

/// <summary>
/// One provider request per business log and normalized phone. Company and permission
/// revision are captured with the operation; the actual mobile is retained only in
/// the pending/in-flight event and is never returned to the browser.
/// </summary>
public sealed class WorkOrderSmsNotification
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int WorkOrderId { get; set; }
    public int EventLogId { get; set; }
    public int RecipientUserId { get; set; }
    [MaxLength(64)] public string RecipientKey { get; set; } = "";
    public WorkOrderSmsEventType EventType { get; set; }
    public WorkOrderSmsStatus Status { get; set; }
    public long? PermissionRevision { get; set; }
    [MaxLength(20)] public string? Recipient { get; set; }
    public long? ProviderMessageId { get; set; }
    public int? ProviderCode { get; set; }
    public int? ProviderMessageStatus { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? AttemptStartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
