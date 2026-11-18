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

    public static RepairSmsStatus InitialStatus(string? mobile, bool moduleEnabled, bool? gatewayEnabled,
        bool lookupConfigured, string? companyName, string? number,
        RepairSmsEventType kind = RepairSmsEventType.Registration, string? customerName = null) =>
        NormalizeMobile(mobile) is null ? RepairSmsStatus.InvalidMobile :
        !moduleEnabled ? RepairSmsStatus.ModuleDisabled :
        gatewayEnabled is null ? RepairSmsStatus.SettingsUnavailable :
        gatewayEnabled == false ? RepairSmsStatus.Disabled :
        !lookupConfigured ? RepairSmsStatus.NotConfigured :
        !KavenegarSmsValidation.CompanyToken(companyName) ? RepairSmsStatus.InvalidCompanyName :
        NumberToken(number) is null ? RepairSmsStatus.InvalidRepairNumber :
        kind == RepairSmsEventType.Delivery && !KavenegarSmsValidation.Token20(customerName)
            ? RepairSmsStatus.InvalidCustomerName : RepairSmsStatus.Pending;

    public static string? TemplateName(RepairSmsEventType kind, KavenegarSmsOptions options) => kind switch
    {
        RepairSmsEventType.Registration => options.RepairAcceptTemplate,
        RepairSmsEventType.Ready => options.RepairReadyTemplate,
        RepairSmsEventType.Delivery => options.RepairDeliveredTemplate,
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

    // Kept as a compatibility wrapper; all SDK modules share the same safe normalization.
    public static string? NormalizeMobile(string? value) => KavenegarSmsValidation.NormalizeMobile(value);

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
