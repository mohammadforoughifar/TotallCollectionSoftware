using Db = Inventory.Api.Data;
using Inventory.Api.Services.Invoicing;
using Inventory.Shared.Dtos;
using Inventory.Shared;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers.Invoicing;

// =====================================================================
// سامانه مودیان — فاکتور الکترونیکی
//   api/moadian/setting            تنظیمات اتصال
//   api/moadian/periods            دوره‌های مالیاتی
//   api/moadian/invoices           فاکتورهای الکترونیکی (ثبت/ویرایش/حذف)
//   api/moadian/invoices/...       صف/ارسال/برگشت/ابطال/payload/چاپ
//   api/moadian/dashboard          نمای کلی
//   api/moadian/logs               لاگ ارسال
//   api/moadian/cpc                شناسه‌های کالا/خدمت
// =====================================================================

/// <summary>سامانه مودیان (فاکتور الکترونیکی).</summary>
[Route("api/moadian")]
public class MoadianController : RbacControllerBase
{
    private readonly IMoadianService _svc;
    public MoadianController(Db.AppDbContext db, IMoadianService svc) : base(db) => _svc = svc;

    // ---------------------- تنظیمات ----------------------
    [HttpGet("setting")]
    public async Task<ActionResult<MoadianSetting>> GetSetting() => Ok(await _svc.GetSettingAsync());

    [HttpPost("setting")]
    public async Task<ActionResult<MoadianSetting>> SaveSetting([FromBody] MoadianSetting dto)
    {
        if (await ForbiddenUnlessAsync("Moadian", "Update") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.SaveSettingAsync(dto));
    }

    // ---------------------- دوره‌ها ----------------------
    [HttpGet("periods")]
    public async Task<ActionResult<List<MoadianFiscalPeriod>>> GetPeriods() => Ok(await _svc.GetPeriodsAsync());

    [HttpPost("periods")]
    public async Task<ActionResult<MoadianFiscalPeriod>> SavePeriod([FromBody] MoadianFiscalPeriod dto)
    {
        if (await ForbiddenUnlessAsync("Moadian", "Create") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.SavePeriodAsync(dto));
    }

    [HttpDelete("periods/{id:int}")]
    public async Task<IActionResult> DeletePeriod(int id)
    {
        if (await ForbiddenUnlessAsync("Moadian", "Delete") is ObjectResult forbidden) return forbidden;
        await _svc.DeletePeriodAsync(id);
        return Ok(new { ok = true });
    }

    // ---------------------- فاکتورها ----------------------
    [HttpGet("invoices")]
    public async Task<ActionResult<List<MoadianInvoice>>> GetInvoices(
        [FromQuery] int? periodId, [FromQuery] MoadianInvoiceStatus? status, [FromQuery] string? search)
        => Ok(await _svc.GetInvoicesAsync(periodId, status, search));

    [HttpGet("invoices/{id:int}")]
    public async Task<ActionResult<MoadianInvoice>> GetInvoice(int id) => Ok(await _svc.GetInvoiceAsync(id));

    [HttpPost("invoices")]
    public async Task<ActionResult<MoadianInvoice>> CreateManual([FromBody] MoadianInvoiceRequest req)
    {
        if (await ForbiddenUnlessAsync("Moadian", "Create") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.CreateManualAsync(req, MyUsername));
    }

    [HttpPost("invoices/from-fac/{facInvoiceId:int}")]
    public async Task<ActionResult<MoadianInvoice>> CreateFromFac(int facInvoiceId)
    {
        if (await ForbiddenUnlessAsync("Moadian", "Create") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.CreateFromFacInvoiceAsync(facInvoiceId, MyUsername));
    }

    [HttpDelete("invoices/{id:int}")]
    public async Task<IActionResult> DeleteInvoice(int id)
    {
        if (await ForbiddenUnlessAsync("Moadian", "Delete") is ObjectResult forbidden) return forbidden;
        await _svc.DeleteInvoiceAsync(id);
        return Ok(new { ok = true });
    }

    // ---------------------- صف ارسال ----------------------
    [HttpPost("invoices/{id:int}/enqueue")]
    public async Task<ActionResult<MoadianInvoice>> Enqueue(int id)
    {
        if (await ForbiddenUnlessAsync("Moadian", "Send") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.EnqueueAsync(id, MyUsername));
    }

    [HttpPost("invoices/{id:int}/cancel")]
    public async Task<ActionResult<MoadianInvoice>> Cancel(int id)
    {
        if (await ForbiddenUnlessAsync("Moadian", "Send") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.CancelAsync(id, MyUsername));
    }

    [HttpPost("invoices/{id:int}/retry")]
    public async Task<ActionResult<MoadianInvoice>> Retry(int id)
    {
        if (await ForbiddenUnlessAsync("Moadian", "Send") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.RetryAsync(id, MyUsername));
    }

    [HttpPost("invoices/{id:int}/send")]
    public async Task<ActionResult<object>> SendNow(int id)
    {
        if (await ForbiddenUnlessAsync("Moadian", "Send") is ObjectResult forbidden) return forbidden;
        await _svc.EnqueueAsync(id, MyUsername);
        var sent = await _svc.SendPendingAsync();
        return Ok(new { sent, invoice = await _svc.GetInvoiceAsync(id) });
    }

    [HttpPost("send-pending")]
    public async Task<ActionResult<object>> SendPending()
    {
        if (await ForbiddenUnlessAsync("Moadian", "Send") is ObjectResult forbidden) return forbidden;
        return Ok(new { sent = await _svc.SendPendingAsync() });
    }

    [HttpGet("invoices/{id:int}/payload")]
    public async Task<ActionResult<MoadianPayloadResult>> Payload(int id)
        => Ok(await _svc.BuildPayloadAsync(id));

    // ---------------------- لاگ / CPC / داشبورد ----------------------
    [HttpGet("logs")]
    public async Task<ActionResult<List<MoadianLog>>> GetLogs([FromQuery] int? invoiceId)
        => Ok(await _svc.GetLogsAsync(invoiceId));

    [HttpGet("cpc")]
    public async Task<ActionResult<List<MoadianCpc>>> GetCpc([FromQuery] string? search)
        => Ok(await _svc.GetCpcAsync(search));

    [HttpPost("cpc")]
    public async Task<ActionResult<MoadianCpc>> SaveCpc([FromBody] MoadianCpc dto)
    {
        if (await ForbiddenUnlessAsync("Moadian", "Update") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.SaveCpcAsync(dto));
    }

    [HttpDelete("cpc/{id:int}")]
    public async Task<IActionResult> DeleteCpc(int id)
    {
        if (await ForbiddenUnlessAsync("Moadian", "Delete") is ObjectResult forbidden) return forbidden;
        await _svc.DeleteCpcAsync(id);
        return Ok(new { ok = true });
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<MoadianDashboard>> GetDashboard() => Ok(await _svc.GetDashboardAsync());
}
