using Db = Inventory.Api.Data;
using Inventory.Api.Services.Invoicing;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers.Invoicing;

// =====================================================================
// چاپگر مالی / حرارتی
//   api/fiscal-printer/setting          تنظیمات چاپگر
//   api/fiscal-printer/render/{id}      پیش‌نمایش رسید (بدون چاپ)
//   api/fiscal-printer/print/{id}       ارسال به چاپگر/فایل
// =====================================================================

/// <summary>چاپگر مالی/حرارتی.</summary>
[Route("api/fiscal-printer")]
public class FiscalPrinterController : RbacControllerBase
{
    private readonly IFiscalPrinterService _svc;
    public FiscalPrinterController(Db.AppDbContext db, IFiscalPrinterService svc) : base(db) => _svc = svc;

    [HttpGet("setting")]
    public async Task<ActionResult<FiscalPrinterSetting>> GetSetting() => Ok(await _svc.GetSettingAsync());

    [HttpPost("setting")]
    public async Task<ActionResult<FiscalPrinterSetting>> SaveSetting([FromBody] FiscalPrinterSetting dto)
    {
        if (await ForbiddenUnlessAsync("FiscalPrinter", "Update") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.SaveSettingAsync(dto));
    }

    [HttpGet("render/{invoiceId:int}")]
    public async Task<ActionResult<FiscalPrintResult>> Render(int invoiceId)
        => Ok(await _svc.RenderAsync(invoiceId));

    [HttpPost("print/{invoiceId:int}")]
    public async Task<ActionResult<FiscalPrintResult>> Print(int invoiceId)
    {
        if (await ForbiddenUnlessAsync("FiscalPrinter", "Print") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.PrintAsync(invoiceId));
    }
}
