using Microsoft.AspNetCore.SignalR;

namespace Inventory.Api.Hubs;

/// <summary>هاب بلادرنگ داشبورد — هر تغییری در سیستم‌ها/دوربین‌ها/NVRها به همه‌ی صفحات داشبورد پخش می‌شود.</summary>
public class DashboardHub : Hub
{
    private readonly IServiceProvider _sp;
    private readonly HardwareMonitor _monitor;
    public DashboardHub(IServiceProvider sp, HardwareMonitor monitor)
    {
        _sp = sp;
        _monitor = monitor;
    }

    public override async Task OnConnectedAsync()
    {
        // ارسال آمار فعلی به تازه‌واصل‌شده‌ها
        try
        {
            using var scope = _sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
            await Clients.Caller.SendAsync("ReceiveStats", await DashboardBroadcaster.BuildAsync(db));
            await Clients.Caller.SendAsync("ReceiveHardwareStats", await _monitor.BuildStatsAsync());
        }
        catch { }
        await base.OnConnectedAsync();
    }

    /// <summary>درخواست یک دور بررسی فوری پینگ همه‌ی دستگاه‌ها از سمت داشبورد.</summary>
    public Task RequestHardwareCheck() => _monitor.SweepAsync();
}
