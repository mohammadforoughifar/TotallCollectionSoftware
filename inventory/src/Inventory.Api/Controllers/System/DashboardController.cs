using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventory.Api.Data;
using Inventory.Api.Hubs;

namespace Inventory.Api.Controllers;

/// <summary>آمار داشبورد مداربسته (همچنین از طریق SignalR در /hubs/dashboard به‌صورت بلادرنگ پخش می‌شود).</summary>
[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly Hubs.HardwareMonitor _hw;
    public DashboardController(AppDbContext db, Hubs.HardwareMonitor hw)
    {
        _db = db;
        _hw = hw;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats() => Ok(await DashboardBroadcaster.BuildAsync(_db));

    /// <summary>آمار سخت‌افزار (آنلاین/آفلاین از آخرین پویش).</summary>
    [HttpGet("hardware-stats")]
    public async Task<IActionResult> HardwareStats() => Ok(await _hw.BuildStatsAsync());

    /// <summary>اجرای فوری یک دور پینگ همه‌ی دستگاه‌ها.</summary>
    [HttpPost("hardware-check")]
    public async Task<IActionResult> HardwareCheck()
    {
        _ = _hw.SweepAsync();
        return Accepted(new { message = "بررسی آغاز شد — نتیجه از طریق SignalR پخش می‌شود." });
    }
}
