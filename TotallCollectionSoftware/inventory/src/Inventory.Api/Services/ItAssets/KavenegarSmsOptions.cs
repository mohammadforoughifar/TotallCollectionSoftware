namespace Inventory.Api.Services.ItAssets;

/// <summary>
/// Shared Kavenegar settings, as read from appsettings.json. These are the fallback:
/// once an ApiKey is saved in the AppSettings table it replaces the key, sender and
/// send switch together (see KavenegarSmsGatewayFactory), so the configuration survives
/// a publish. Never put a real ApiKey in source control or send one to the browser.
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

    // Preserve the configured timeout and template names without modifying shared IOptions.Value.
    internal KavenegarSmsOptions WithAccount(string apiKey, string sender, bool enabled)
    {
        var copy = (KavenegarSmsOptions)MemberwiseClone();
        copy.ApiKey = apiKey;
        copy.Sender = sender;
        copy.SdkSendEnabled = enabled;
        return copy;
    }
}
