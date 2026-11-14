using LicenseSigner.Data;

namespace LicenseSigner.Services;

/// <summary>یک گذار مجاز در چرخهٔ وضعیت درخواست.</summary>
public sealed record Transition(RequestStatus From, RequestStatus To, SupportRole[] Roles, string Title,
    bool RequiresIdentity = false);

/// <summary>نتیجهٔ بررسی مجاز بودن گذار/امضا.</summary>
public sealed record WorkflowCheck(bool Ok, string? Error);

/// <summary>
/// جدول گذارها در یک جا. قاعده‌های ثابت:
/// «صادرشده» فقط از مسیر امضا، «تحویل‌شده» فقط از مسیر تحویل،
/// گذار به «تأییدشده» فقط با IdentityCheck ثبت‌شده.
/// </summary>
public static class Workflow
{
    public static readonly Transition[] Transitions =
    {
        new(RequestStatus.New, RequestStatus.Reviewing, new[] { SupportRole.Operator, SupportRole.Admin }, "شروع بررسی"),
        new(RequestStatus.New, RequestStatus.Rejected, new[] { SupportRole.Admin }, "رد درخواست"),
        new(RequestStatus.New, RequestStatus.Cancelled, new[] { SupportRole.Admin }, "لغو درخواست"),
        new(RequestStatus.Reviewing, RequestStatus.NeedsInfo, new[] { SupportRole.Operator, SupportRole.Admin }, "نیاز به اطلاعات"),
        new(RequestStatus.Reviewing, RequestStatus.Approved, new[] { SupportRole.Admin, SupportRole.Signer }, "تأیید درخواست", true),
        new(RequestStatus.Reviewing, RequestStatus.Rejected, new[] { SupportRole.Admin }, "رد درخواست"),
        new(RequestStatus.Reviewing, RequestStatus.Cancelled, new[] { SupportRole.Admin }, "لغو درخواست"),
        new(RequestStatus.NeedsInfo, RequestStatus.Reviewing, new[] { SupportRole.Operator, SupportRole.Admin }, "بازگشت به بررسی"),
        new(RequestStatus.NeedsInfo, RequestStatus.Cancelled, new[] { SupportRole.Admin }, "لغو درخواست"),
        new(RequestStatus.Approved, RequestStatus.Reviewing, new[] { SupportRole.Admin }, "بازگشت به بررسی"),
        new(RequestStatus.Approved, RequestStatus.Cancelled, new[] { SupportRole.Admin }, "لغو درخواست"),
        new(RequestStatus.Issued, RequestStatus.Delivered, new[] { SupportRole.Operator, SupportRole.Admin }, "ثبت تحویل"),
        new(RequestStatus.Issued, RequestStatus.Closed, new[] { SupportRole.Admin }, "بستن پرونده"),
        new(RequestStatus.Delivered, RequestStatus.Closed, new[] { SupportRole.Operator, SupportRole.Admin }, "بستن پرونده")
    };

    /// <summary>گذاره‌های مجاز از یک وضعیت برای یک نقش.</summary>
    public static IEnumerable<Transition> AllowedFrom(RequestStatus from, SupportRole role) =>
        Transitions.Where(t => t.From == from && t.Roles.Contains(role));

    public static WorkflowCheck CanTransition(RequestStatus from, RequestStatus to, SupportRole role, bool hasIdentity)
    {
        if (to == RequestStatus.Issued)
            return new WorkflowCheck(false, "وضعیت «صادرشده» فقط با انجام امضا ثبت می‌شود، نه با تغییر دستی وضعیت.");

        if (from == to)
            return new WorkflowCheck(false, $"وضعیت فعلی «{Ui.Status(from)}» است؛ تغییر به همان وضعیت معنا ندارد.");

        var transition = Transitions.FirstOrDefault(t => t.From == from && t.To == to);
        if (transition is null)
            return new WorkflowCheck(false, $"گذار از «{Ui.Status(from)}» به «{Ui.Status(to)}» مجاز نیست.");

        if (!transition.Roles.Contains(role))
            return new WorkflowCheck(false, $"نقش شما ({Ui.Role(role)}) اجازهٔ این گذار را ندارد.");

        if (transition.RequiresIdentity && !hasIdentity)
            return new WorkflowCheck(false,
                "پیش از تأیید درخواست، باید «تأیید هویت» (چه کسی، با چه روشی، چه زمانی) ثبت شود.");

        return new WorkflowCheck(true, null);
    }

    /// <summary>پیش‌شرط امضا: وضعیت تأییدشده + نقش امضاکننده + تأیید هویت.</summary>
    public static WorkflowCheck CheckSign(RequestStatus status, SupportRole role, bool hasIdentity)
    {
        if (status != RequestStatus.Approved)
            return new WorkflowCheck(false,
                $"امضا فقط از وضعیت «تأییدشده» ممکن است؛ وضعیت فعلی: «{Ui.Status(status)}».");

        if (role is not (SupportRole.Signer or SupportRole.Admin))
            return new WorkflowCheck(false, "نقش شما اجازهٔ امضا ندارد (نقش لازم: امضاکننده یا مدیر).");

        if (!hasIdentity)
            return new WorkflowCheck(false, "بدون تأیید هویت امضا انجام نمی‌شود.");

        return new WorkflowCheck(true, null);
    }

    /// <summary>وضعیت‌های بسته که ویرایش پیش‌نویس/تأیید هویت نمی‌پذیرند.</summary>
    public static bool IsClosed(RequestStatus status) =>
        status is RequestStatus.Rejected or RequestStatus.Cancelled or RequestStatus.Duplicate
            or RequestStatus.Closed or RequestStatus.Delivered or RequestStatus.Issued;
}
