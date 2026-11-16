namespace Inventory.Api.Services.ItAssets;

/// <summary>
/// Shared Kavenegar settings. Keep ApiKey in a server-side secret store or the
/// KavenegarSms__ApiKey environment variable, never in source control or the browser.
/// </summary>
public sealed class KavenegarSmsOptions
{
    public string ApiKey { get; set; } = "";
    public string Sender { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 20;

    // Names must match the approved lookup templates on the server's Kavenegar account.
    public string RepairAcceptTemplate { get; set; } = "RepairAccept";
    public string RepairReadyTemplate { get; set; } = "RepairReady";
    public string RepairDeliveredTemplate { get; set; } = "RepairDelivered";

    // The user reported approval of the three templates. These are the proposed names;
    // the server can override them if the account uses different names. SDK and
    // company/module permission remain independent, default-off controls.
    public string WorkOrderRegistrationTemplate { get; set; } = "WorkOrderRegistration";
    public string WorkOrderReferralTemplate { get; set; } = "WorkOrderReferral";
    public string WorkOrderReplyTemplate { get; set; } = "WorkOrderReply";

    // Shared switch for integrated SDK senders. The independent profile OTP is unchanged.
    public bool SdkSendEnabled { get; set; }
}
