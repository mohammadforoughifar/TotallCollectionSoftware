using System.ComponentModel.DataAnnotations;
using Inventory.Shared.Dtos;

namespace Inventory.Api.Data;

/// <summary>یک رویداد پیامک برای یک پذیرش؛ گیرنده در زمان رویداد ثابت می‌شود.</summary>
public sealed class RepairSmsNotification : ICompanyOwned
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public int RepairOrderId { get; set; }
    public int PartyId { get; set; }
    public RepairSmsEventType EventType { get; set; }
    public RepairSmsStatus Status { get; set; }
    [MaxLength(20)] public string? Recipient { get; set; }
    public long? ProviderMessageId { get; set; }
    public int? ProviderCode { get; set; }
    public int? ProviderMessageStatus { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? AttemptStartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
