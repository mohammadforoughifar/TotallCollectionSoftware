using Inventory.Shared;
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

    // Why an event did not reach the recipient, in words. The compact Text label stays
    // unchanged; this is the explanatory line shown under it, so a bare number such as
    // 13 is never the only thing the owner can see.
    public static string Detail(WorkOrderSmsStatus status) => status switch
    {
        WorkOrderSmsStatus.Pending => "در صف ارسال",
        WorkOrderSmsStatus.Sending => "در حال ارسال",
        WorkOrderSmsStatus.Accepted => "پذیرفته‌شده توسط درگاه؛ تحویل قطعی نیست",
        WorkOrderSmsStatus.Delivered => "تحویل‌شده",
        WorkOrderSmsStatus.Failed => "درگاه پیامک، ارسال را ناموفق اعلام کرد",
        WorkOrderSmsStatus.Unknown => "نتیجه نامشخص؛ پیش از ارسال دوباره، وضعیت را در کاوه‌نگار بررسی کنید",
        WorkOrderSmsStatus.InvalidMobile => "شمارهٔ موبایل این گیرنده در پروفایلش ثبت نشده یا معتبر نیست؛ ارسال نشد",
        // این وضعیت دو علت کاملاً متفاوت دارد و نوشتن فقط «غیرفعال است» گمراه‌کننده بود:
        // سوئیچ واقعاً خاموش باشد، یا سرور نتوانسته باشد تنظیمات پیامک را بخواند. هر دو
        // اینجا نوشته می‌شوند تا مدیر بداند کجا را نگاه کند.
        WorkOrderSmsStatus.Disabled => "ارسال انجام نشد چون در لحظهٔ ارسال، تنظیمات پیامک «خاموش» دیده شد: "
            + "یا سوئیچ «تنظیمات ← سامانهٔ پیامکی» خاموش است، یا سرور نتوانست تنظیمات را از دیتابیس بخواند",
        WorkOrderSmsStatus.NotConfigured => "کلید API، خط فرستنده یا نام الگوی پیامک کامل نیست؛ «تنظیمات ← سامانهٔ پیامکی» را بررسی کنید",
        WorkOrderSmsStatus.Cancelled => "اطلاعات گیرنده یا دستور کار پیش از ارسال تغییر کرده بود؛ ارسال نشد",
        WorkOrderSmsStatus.ModuleDisabled => "مجوز پیامک «دستور کار» برای این شرکت غیرفعال بود یا لغو شد؛ ارسال نشد",
        WorkOrderSmsStatus.InvalidCompanyName => "نام شرکت با محدودیت الگوی پیامک سازگار نیست؛ ارسال نشد",
        WorkOrderSmsStatus.InvalidOrderNumber => "شمارهٔ دستور کار با الگوی پیامک سازگار نیست؛ ارسال نشد",
        WorkOrderSmsStatus.NoCompanyAccess => "گیرنده به این شرکت دسترسی ندارد؛ ارسال نشد",
        WorkOrderSmsStatus.InvalidDeadline => "مهلت دستور کار با الگوی پیامک سازگار نیست؛ ارسال نشد",
        WorkOrderSmsStatus.InvalidTitle => "عنوان دستور کار با الگوی پیامک سازگار نیست؛ ارسال نشد",
        _ => "وضعیت نامشخص"
    };

    // A returned provider code explains the outcome better than the generic status, so it
    // wins when present. Successful and in-flight events need no explanation.
    public static string? FailureReason(WorkOrderSmsEventDto sms)
    {
        if (IsSuccess(sms.Status) || sms.Status is WorkOrderSmsStatus.Pending or WorkOrderSmsStatus.Sending)
            return null;
        return ErrorCode(sms) is int code ? KavenegarCodeText.Describe(code) : Detail(sms.Status);
    }
}
