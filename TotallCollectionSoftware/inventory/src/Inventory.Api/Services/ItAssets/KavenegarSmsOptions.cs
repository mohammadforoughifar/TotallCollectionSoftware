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

    // Independent of the existing work-order and profile SMS services; default is off.
    public bool SdkSendEnabled { get; set; }
}
