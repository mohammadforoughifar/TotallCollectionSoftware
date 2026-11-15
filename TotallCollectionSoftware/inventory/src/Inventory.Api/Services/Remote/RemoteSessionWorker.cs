namespace Inventory.Api.Services.Remote;

public sealed class RemoteSessionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RemoteSessionWorker> _log;
    public RemoteSessionWorker(IServiceScopeFactory scopeFactory, ILogger<RemoteSessionWorker> log)
    { _scopeFactory = scopeFactory; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<IRemoteSessionService>();
                await svc.ExpireAndCleanupAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _log.LogWarning(ex, "RemoteSessionWorker tick failed"); }
        }
    }
}
