namespace Inventory.Api.Services.Invoicing;

/// <summary>
/// Deliberate no-op. Automatic Moadian submission is not permitted until the
/// current official protocol, credentials, and authorization have been verified.
/// Draft creation is handled synchronously by the sales workflows instead.
/// </summary>
public sealed class MoadianAutoSender : BackgroundService
{
    private readonly ILogger<MoadianAutoSender> _log;

    public MoadianAutoSender(ILogger<MoadianAutoSender> log) => _log = log;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogWarning("Moadian auto-sender is intentionally disabled; no invoices will be submitted.");
        return Task.CompletedTask;
    }
}
