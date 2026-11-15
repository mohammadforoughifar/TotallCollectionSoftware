using Inventory.Shared;

namespace Inventory.Client.Pages;

/// <summary>برچسب و نشانهٔ یکسان وضعیت در فهرست و جزئیات پذیرش.</summary>
internal static class RepairStatusAppearance
{
    public static string Text(RepairStatus status) => status switch
    {
        RepairStatus.Received => "پذیرش شده",
        RepairStatus.InProgress => "در حال تعمیر",
        RepairStatus.Ready => "آماده تحویل",
        RepairStatus.Delivered => "تحویل شده",
        RepairStatus.Cancelled => "انصراف",
        _ => "نامشخص"
    };

    public static string BadgeCss(RepairStatus status) => status switch
    {
        RepairStatus.Received => "badge-soft-primary",
        RepairStatus.InProgress => "badge-soft-warning",
        RepairStatus.Ready => "repair-status-ready",
        RepairStatus.Delivered => "repair-status-delivered",
        RepairStatus.Cancelled => "badge-soft-danger",
        _ => "badge-soft-muted"
    };

    public static string Icon(RepairStatus status) => status switch
    {
        RepairStatus.Received => "bi-inbox",
        RepairStatus.InProgress => "bi-tools",
        RepairStatus.Ready => "bi-hourglass-split",
        RepairStatus.Delivered => "bi-check-circle-fill",
        RepairStatus.Cancelled => "bi-x-circle",
        _ => "bi-question-circle"
    };

    public static string IconCss(RepairStatus status) => status switch
    {
        RepairStatus.Ready => "repair-status-icon-ready",
        RepairStatus.Delivered => "repair-status-icon-delivered",
        RepairStatus.Cancelled => "repair-status-icon-cancelled",
        _ => ""
    };
}
