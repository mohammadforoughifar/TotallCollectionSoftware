namespace Inventory.Shared.Dtos;

// Registration is used for every new order, including a new sub-order. Referral is
// used only when someone is added to an existing order. Persist these numeric values.
public enum WorkOrderSmsEventType { Registration = 1, Reply = 2, Referral = 3 }

// Accepted is a provider acceptance, not a delivery receipt. Unknown is terminal.
public enum WorkOrderSmsStatus
{
    Pending = 1, Sending = 2, Accepted = 3, Delivered = 4, Failed = 5, Unknown = 6,
    InvalidMobile = 7, Disabled = 8, NotConfigured = 9, Cancelled = 10,
    ModuleDisabled = 11, InvalidCompanyName = 12, InvalidOrderNumber = 13,
    NoCompanyAccess = 14, InvalidDeadline = 15, InvalidTitle = 16, SettingsUnavailable = 17
}

/// <summary>Owner-only work-order SMS history; never exposes mobiles or credentials.</summary>
public sealed class WorkOrderSmsEventDto
{
    public int Id { get; set; }
    public string RecipientName { get; set; } = "";
    public WorkOrderSmsEventType EventType { get; set; }
    public WorkOrderSmsStatus Status { get; set; }
    public int? FailureCode { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Legacy response shape retained for older clients; it no longer initiates a send.</summary>
public class WorkOrderSmsResult
{
    public List<WorkOrderSmsRecipientResult> Recipients { get; set; } = new();
}

public class WorkOrderSmsRecipientResult
{
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    // accepted | failed | skipped | unknown. Never expose mobile numbers or the API key.
    public string State { get; set; } = "";
    public string Message { get; set; } = "";
}
