using System.Text;
using Inventory.Api.Services.ItAssets;
using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Api.Services.Repairs;

/// <summary>Pure rules shared by the repair event producer and dispatcher.</summary>
public static class RepairSmsPolicy
{
    public static RepairSmsEventType? EventFor(bool isNew, RepairStatus previous, RepairStatus current) =>
        isNew ? RepairSmsEventType.Registration :
        previous != RepairStatus.Ready && current == RepairStatus.Ready ? RepairSmsEventType.Ready : null;

    public static RepairSmsStatus InitialStatus(string? mobile, bool moduleEnabled, bool gatewayEnabled,
        bool lookupConfigured, string? companyName, string? number) =>
        NormalizeMobile(mobile) is null ? RepairSmsStatus.InvalidMobile :
        !moduleEnabled ? RepairSmsStatus.ModuleDisabled :
        !gatewayEnabled ? RepairSmsStatus.Disabled :
        !lookupConfigured ? RepairSmsStatus.NotConfigured :
        !KavenegarSmsValidation.CompanyToken(companyName) ? RepairSmsStatus.InvalidCompanyName :
        NumberToken(number) is null ? RepairSmsStatus.InvalidRepairNumber : RepairSmsStatus.Pending;

    public static string? TemplateName(RepairSmsEventType kind, KavenegarSmsOptions options) => kind switch
    {
        RepairSmsEventType.Registration => options.RepairAcceptTemplate,
        RepairSmsEventType.Ready => options.RepairReadyTemplate,
        _ => null
    };

    // The proposed template includes literal "RP-" before %token. Refuse legacy numbers
    // that cannot be represented exactly rather than sending a different acceptance number.
    public static string? NumberToken(string? number)
    {
        if (number is null || !number.StartsWith("RP-", StringComparison.Ordinal)) return null;
        var suffix = number[3..];
        return KavenegarSmsValidation.NumericToken(suffix) ? suffix : null;
    }

    // Accept one mobile only, with Persian/Arabic digits and ordinary formatting. Never strip
    // arbitrary letters or separators that could turn a list/landline into another recipient.
    public static string? NormalizeMobile(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = new StringBuilder();
        foreach (var ch in value.Trim())
        {
            var n = char.GetNumericValue(ch);
            if (char.IsDigit(ch) && n is >= 0 and <= 9) { text.Append((char)('0' + (int)n)); continue; }
            if (ch == '+' && text.Length == 0) { text.Append(ch); continue; }
            if (char.IsWhiteSpace(ch) || ch is '-' or '(' or ')') continue;
            return null;
        }
        var raw = text.ToString();
        var plus = raw.StartsWith('+');
        var digits = plus ? raw[1..] : raw;
        if (digits.Length == 0 || digits.Any(c => c is < '0' or > '9')) return null;
        if (digits.StartsWith("0098", StringComparison.Ordinal)) digits = digits[4..];
        else if (digits.StartsWith("98", StringComparison.Ordinal) && (plus || digits.Length == 12)) digits = digits[2..];
        else if (plus)
            return digits.Length is >= 7 and <= 15 && digits[0] != '0' ? "00" + digits : null;
        else if (digits.StartsWith("00", StringComparison.Ordinal))
            return digits.Length is >= 9 and <= 17 && digits[2] != '0' ? digits : null;
        if (digits.Length == 10 && digits[0] == '9') digits = "0" + digits;
        return digits.Length == 11 && digits.StartsWith("09", StringComparison.Ordinal) ? digits : null;
    }

    public static RepairSmsStatus Result(KavenegarSmsSendState state) => state switch
    {
        KavenegarSmsSendState.Accepted => RepairSmsStatus.Accepted,
        KavenegarSmsSendState.Delivered => RepairSmsStatus.Delivered,
        KavenegarSmsSendState.Disabled => RepairSmsStatus.Disabled,
        KavenegarSmsSendState.NotConfigured => RepairSmsStatus.NotConfigured,
        KavenegarSmsSendState.InvalidRequest or KavenegarSmsSendState.Failed => RepairSmsStatus.Failed,
        _ => RepairSmsStatus.Unknown
    };
}
