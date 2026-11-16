using Inventory.Shared.Dtos;

namespace Inventory.Client.Pages;

/// <summary>Compact work-order SMS results, consistent with the repair details view.</summary>
internal static class WorkOrderSmsAppearance
{
    public static bool IsSuccess(WorkOrderSmsStatus status) =>
        status is WorkOrderSmsStatus.Accepted or WorkOrderSmsStatus.Delivered;

    public static bool IsFailure(WorkOrderSmsStatus status) => status is
        WorkOrderSmsStatus.Failed or WorkOrderSmsStatus.InvalidMobile or WorkOrderSmsStatus.Disabled or
        WorkOrderSmsStatus.NotConfigured or WorkOrderSmsStatus.Cancelled or WorkOrderSmsStatus.ModuleDisabled or
        WorkOrderSmsStatus.InvalidCompanyName or WorkOrderSmsStatus.InvalidOrderNumber or
        WorkOrderSmsStatus.NoCompanyAccess or WorkOrderSmsStatus.InvalidDeadline or WorkOrderSmsStatus.InvalidTitle;

    // Accepted means the provider accepted the request, not that the message was delivered.
    // Keep the two descriptions distinct for the tooltip and screen readers.
    public static string Text(WorkOrderSmsStatus status) => status switch
    {
        WorkOrderSmsStatus.Accepted => "پذیرفته‌شده در درگاه؛ تحویل قطعی نیست",
        WorkOrderSmsStatus.Delivered => "تحویل‌شده",
        WorkOrderSmsStatus.Pending => "در صف ارسال",
        WorkOrderSmsStatus.Sending => "در حال ارسال",
        _ when IsFailure(status) => "ناموفق",
        _ => "نامشخص"
    };

    // Do not display a successful API code as a failure. A message-specific failure code
    // is already selected by the server when the provider itself returned HTTP/API 200.
    public static int? ErrorCode(WorkOrderSmsEventDto sms) =>
        IsFailure(sms.Status) && sms.FailureCode is int code && code != 200 ? code : null;
}
