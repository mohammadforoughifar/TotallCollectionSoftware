using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Api.Infrastructure;
using Inventory.Api.Services;
using Inventory.Api.Services.Catalog;
using Microsoft.AspNetCore.Authorization;
using Inventory.Shared.Dtos;

namespace Inventory.Api.Controllers;

/// <summary>آمار داشبورد مداربسته (همچنین از طریق SignalR در /hubs/dashboard به‌صورت بلادرنگ پخش می‌شود).</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly Hubs.HardwareMonitor _hw;
    private readonly IInventoryService _inventory;

    public DashboardController(AppDbContext db, Hubs.HardwareMonitor hw, IInventoryService inventory)
    {
        _db = db;
        _hw = hw;
        _inventory = inventory;
    }

    // ================== داشبورد مالی/عملیاتی (کلاینت: AdminHome) ==================
    // این مسیرها پیش‌تر وجود نداشتند و صفحهٔ داشبورد با 404 مواجه می‌شد.

    /// <summary>
    /// خلاصهٔ عملیاتی فروشگاه — تعداد کالا/انبار/طرف حساب، ارزش موجودی و فروش/خرید امروز و ماه.
    /// (GET api/dashboard)
    /// </summary>
    [HttpGet]
    [RequirePermission("Dashboards", "Financial")]
    public async Task<IActionResult> Summary()
        => Ok(await _inventory.GetDashboardAsync());

    /// <summary>آخرین اسناد خرید/فروش برای جدول «فعالیت‌های اخیر» داشبورد.</summary>
    [HttpGet("recent")]
    [RequirePermission("Dashboards", "Financial")]
    public async Task<IActionResult> Recent([FromQuery] int count = 8)
        => Ok(await _inventory.GetRecentAsync(Math.Clamp(count, 1, 50)));

    /// <summary>داشبورد مدیریتی: فروش/سود دوره‌ای، بدهکاران سررسیدشده و چک‌های امروز.</summary>
    [HttpGet("admin")]
    [RequirePermission("Dashboards", "Financial")]
    public async Task<IActionResult> Admin()
        => Ok(await _inventory.GetAdminDashboardAsync());

    /// <summary>پاس کردن چک از روی داشبورد مدیریتی.</summary>
    [HttpPost("cheques/{chequeId:int}/clear")]
    [RequirePermission("Dashboards", "Financial")]
    public async Task<IActionResult> ClearCheque(int chequeId)
    {
        await _inventory.ClearChequeAsync(chequeId);
        return Ok(new { ok = true });
    }

    /// <summary>پرداخت یک قسط از روی داشبورد مدیریتی.</summary>
    [HttpPost("installments/{installmentId:int}/pay")]
    [RequirePermission("Dashboards", "Financial")]
    public async Task<IActionResult> PayInstallment(int installmentId)
    {
        await _inventory.PayInstallmentAsync(installmentId);
        return Ok(new { ok = true });
    }

    /// <summary>تسویهٔ نسیه/اعتبار مشتری از روی داشبورد مدیریتی.</summary>
    [HttpPost("credits/{transactionId:int}/settle")]
    [RequirePermission("Dashboards", "Financial")]
    public async Task<IActionResult> SettleCredit(int transactionId, [FromQuery] decimal amount)
    {
        await _inventory.SettleCreditAsync(transactionId, amount);
        return Ok(new { ok = true });
    }

[HttpGet("stats")]
[RequirePermission("Dashboards", "Management")]
public async Task<IActionResult> Stats()
{
    var data = await DashboardBroadcaster.BuildAsync(_db);
    return Ok(data);
}

/// <summary>آمار سخت‌افزار (آنلاین/آفلاین از آخرین پویش).</summary>
[HttpGet("hardware-stats")]
[RequirePermission("Dashboards", "Hardware")]
public async Task<IActionResult> HardwareStats()
{
    var data = await _hw.BuildStatsAsync();
    return Ok(data);
}

    /// <summary>اجرای فوری یک دور پینگ همه‌ی دستگاه‌ها.</summary>
    [HttpPost("hardware-check")]
    [RequirePermission("Dashboards", "Hardware")]
    public async Task<IActionResult> HardwareCheck()
    {
        _ = _hw.SweepAsync();
        return Accepted(new { message = "بررسی آغاز شد — نتیجه از طریق SignalR پخش می‌شود." });
    }
}
