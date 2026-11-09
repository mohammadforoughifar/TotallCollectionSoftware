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

    // This is deliberately independent of the existing work-order and profile SMS services.
    // The new SDK gateway stays inactive until a future integration is explicitly enabled.
    public bool SdkSendEnabled { get; set; }
}
