using System.Globalization;
using System.Text;
using Inventory.Shared.Dtos;

namespace Inventory.Api.Services.ItAssets;

/// <summary>Lookup-token rules; only the SMS title may be shortened with an explicit ellipsis.</summary>
public static class WorkOrderSmsPolicy
{
    private static readonly string[] Months =
    {
        "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
        "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"
    };

    public static (string Year, string Serial)? NumberTokens(string? number)
    {
        var parts = number?.Split('/');
        if (parts is not { Length: 3 } || parts[0] != "WO" || parts[1].Length != 4 ||
            !KavenegarSmsValidation.NumericToken(parts[1]) || !KavenegarSmsValidation.NumericToken(parts[2]))
            return null;
        return (parts[1], parts[2]);
    }

    // Preserve seconds and fractions if another module creates a sub-minute deadline.
    // A series' registration also summarizes its cadence and occurrence count in the
    // same no-space token: the five available provider tokens are otherwise all in use.
    public static string? DeadlineToken(DateTime dueAt, int recurrence = 0, int seriesCount = 0)
    {
        try
        {
            var pc = new PersianCalendar();
            // token20 is reserved for the title and token10 for the company. token3
            // carries the no-space date and HH:mm together; the gateway escapes the colon.
            var text = string.Format(CultureInfo.InvariantCulture, "{0}{1}{2}ساعت{3:00}:{4:00}",
                pc.GetDayOfMonth(dueAt), Months[pc.GetMonth(dueAt) - 1], pc.GetYear(dueAt), dueAt.Hour, dueAt.Minute);
            var fractionalTicks = dueAt.Ticks % TimeSpan.TicksPerSecond;
            if (dueAt.Second != 0 || fractionalTicks != 0)
            {
                text += "و" + dueAt.Second.ToString("00", CultureInfo.InvariantCulture);
                if (fractionalTicks != 0)
                    text += "ممیز" + fractionalTicks.ToString("0000000", CultureInfo.InvariantCulture).TrimEnd('0');
                text += "ثانیه";
            }
            var cadence = recurrence switch { 1 => "روزانه", 2 => "هفتگی", 3 => "ماهانه", _ => null };
            if (seriesCount > 1 && cadence is not null)
                text += "تکرار" + cadence + seriesCount.ToString(CultureInfo.InvariantCulture) + "نوبت";
            var chars = text.Select(c => c is >= '0' and <= '9' ? (char)('۰' + c - '0') : c).ToArray();
            var token = new string(chars);
            return KavenegarSmsValidation.WorkOrderDeadlineToken(token) ? token : null;
        }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    // The user approved showing an ellipsis in SMS when the title cannot fit. Only the
    // outgoing token is shortened: WorkOrder.Title stays intact. The provider also caps
    // token20 at eight spaces, so reaching either limit ends the displayed title with '...'.
    // Preserve whole text elements (e.g. surrogate pairs and combining marks). Other invalid
    // characters are never silently replaced; those events are reported as InvalidTitle.
    public static string? TitleToken(string? title)
    {
        if (string.IsNullOrWhiteSpace(title) || !KavenegarSmsValidation.Token20Characters(title)) return null;
        if (title.Length <= 100 && title.Count(c => c == ' ') <= 8)
            return KavenegarSmsValidation.Token20(title) ? title : null;

        var prefix = new StringBuilder();
        var spaces = 0;
        var elements = StringInfo.GetTextElementEnumerator(title);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            var newSpaces = element.Count(c => c == ' ');
            if (prefix.Length + element.Length > 97 || spaces + newSpaces > 8) break;
            prefix.Append(element);
            spaces += newSpaces;
        }
        var shortened = prefix.ToString().TrimEnd(' ');
        if (shortened.Length == 0) return null;
        var result = shortened + "...";
        return KavenegarSmsValidation.Token20(result) ? result : null;
    }

    public static string? TemplateName(WorkOrderSmsEventType kind, KavenegarSmsOptions options) => kind switch
    {
        WorkOrderSmsEventType.Registration => options.WorkOrderRegistrationTemplate,
        WorkOrderSmsEventType.Referral => options.WorkOrderReferralTemplate,
        WorkOrderSmsEventType.Reply => options.WorkOrderReplyTemplate,
        _ => null
    };

    // A new sub-order is also a registration; only subsequent additions are referrals.
    // Refuse an event/log mismatch instead of choosing a different or older template.
    public static bool MatchesLog(WorkOrderSmsEventType kind, string? action) => kind switch
    {
        WorkOrderSmsEventType.Registration => action == "Created",
        WorkOrderSmsEventType.Referral => action is "Edited" or "AssigneesAdded",
        WorkOrderSmsEventType.Reply => action == "Reply",
        _ => false
    };

    public static bool RequiresDeadline(WorkOrderSmsEventType kind) =>
        kind is WorkOrderSmsEventType.Registration or WorkOrderSmsEventType.Referral;

    public static WorkOrderSmsStatus InitialStatus(string? mobile, bool recipientHasAccess, bool moduleEnabled,
        bool gatewayEnabled, bool configured, string? companyName, string? number, string? title, DateTime dueAt, WorkOrderSmsEventType kind) =>
        KavenegarSmsValidation.NormalizeMobile(mobile) is null ? WorkOrderSmsStatus.InvalidMobile :
        !recipientHasAccess ? WorkOrderSmsStatus.NoCompanyAccess :
        !moduleEnabled ? WorkOrderSmsStatus.ModuleDisabled :
        !gatewayEnabled ? WorkOrderSmsStatus.Disabled :
        !configured ? WorkOrderSmsStatus.NotConfigured :
        !KavenegarSmsValidation.CompanyToken(companyName) ? WorkOrderSmsStatus.InvalidCompanyName :
        NumberTokens(number) is null ? WorkOrderSmsStatus.InvalidOrderNumber :
        TitleToken(title) is null ? WorkOrderSmsStatus.InvalidTitle :
        RequiresDeadline(kind) && DeadlineToken(dueAt) is null ? WorkOrderSmsStatus.InvalidDeadline :
        WorkOrderSmsStatus.Pending;

    public static WorkOrderSmsStatus Result(KavenegarSmsSendState state) => state switch
    {
        KavenegarSmsSendState.Accepted => WorkOrderSmsStatus.Accepted,
        KavenegarSmsSendState.Delivered => WorkOrderSmsStatus.Delivered,
        KavenegarSmsSendState.Disabled => WorkOrderSmsStatus.Disabled,
        KavenegarSmsSendState.NotConfigured => WorkOrderSmsStatus.NotConfigured,
        KavenegarSmsSendState.InvalidRequest or KavenegarSmsSendState.Failed => WorkOrderSmsStatus.Failed,
        _ => WorkOrderSmsStatus.Unknown
    };
}
