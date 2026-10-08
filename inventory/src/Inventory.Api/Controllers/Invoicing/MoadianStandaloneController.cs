using Inventory.Api.Controllers;
using Db = Inventory.Api.Data;
using Inventory.Api.Services.Invoicing;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers.Invoicing;

/// <summary>
/// «سال مالی مودیان» و ثبت صورتحساب مستقل از ERP.
/// ارسال به سامانه همچنان غیرفعال است؛ این بخش فقط پیش‌نویس با شمارهٔ سند سالانه می‌سازد.
/// </summary>
[Route("api/moadian/standalone")]
public sealed class MoadianStandaloneController : RbacControllerBase
{
    private readonly IMoadianFiscalYearService _years;
    private readonly IMoadianStandaloneInvoiceService _invoices;

    public MoadianStandaloneController(
        Db.AppDbContext db,
        IMoadianFiscalYearService years,
        IMoadianStandaloneInvoiceService invoices) : base(db)
        => (_years, _invoices) = (years, invoices);

    // ---------- سال مالی ----------

    [HttpGet("fiscal-years")]
    public async Task<ActionResult<List<MoadianFiscalYearDto>>> GetYears([FromQuery] int? providerId)
    {
        if (await ForbiddenUnlessFormAsync("Moadian", new[] { "Read", "Create" }, "FiscalYears", "InvoiceNew") is ObjectResult forbidden) return forbidden;
        return Ok(await _years.GetYearsAsync(providerId));
    }

    [HttpGet("fiscal-years/{id:int}")]
    public async Task<ActionResult<MoadianFiscalYearDetailDto>> GetYear(int id)
    {
        if (await ForbiddenUnlessFormAsync("Moadian", new[] { "Read", "Create" }, "FiscalYears", "InvoiceNew") is ObjectResult forbidden) return forbidden;
        return await ReadAsync(() => _years.GetYearAsync(id));
    }

    [HttpPost("fiscal-years")]
    public async Task<ActionResult<MoadianFiscalYearDetailDto>> SaveYear([FromBody] MoadianFiscalYearRequest request)
    {
        if (await ForbiddenUnlessFormAsync("Moadian", "Create", "FiscalYears") is ObjectResult forbidden) return forbidden;
        return await WriteAsync(() => _years.SaveYearAsync(request));
    }

    [HttpPost("fiscal-years/{id:int}/close")]
    public async Task<ActionResult<MoadianFiscalYearDetailDto>> CloseYear(int id)
    {
        if (await ForbiddenUnlessFormAsync("Moadian", "Update", "FiscalYears") is ObjectResult forbidden) return forbidden;
        return await WriteAsync(() => _years.SetYearClosedAsync(id, true));
    }

    [HttpPost("fiscal-years/{id:int}/reopen")]
    public async Task<ActionResult<MoadianFiscalYearDetailDto>> ReopenYear(int id)
    {
        if (await ForbiddenUnlessFormAsync("Moadian", "Update", "FiscalYears") is ObjectResult forbidden) return forbidden;
        return await WriteAsync(() => _years.SetYearClosedAsync(id, false));
    }

    [HttpDelete("fiscal-years/{id:int}")]
    public async Task<IActionResult> DeleteYear(int id)
    {
        if (await ForbiddenUnlessFormAsync("Moadian", "Delete", "FiscalYears") is ObjectResult forbidden) return forbidden;
        try
        {
            await _years.DeleteYearAsync(id);
            return NoContent();
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (DbUpdateException ex)
        {
            var detail = DbError(ex);
            Console.WriteLine($"[Moadian] خطای پایگاه‌داده در حذف سال مالی: {detail}");
            return Conflict(new { message = $"حذف سال مالی با خطای پایگاه‌داده مواجه شد. جزئیات: {detail}" });
        }
    }

    [HttpPost("fiscal-periods/{id:int}/close")]
    public async Task<ActionResult<MoadianFiscalPeriodDto>> ClosePeriod(int id)
    {
        if (await ForbiddenUnlessFormAsync("Moadian", "Update", "FiscalYears") is ObjectResult forbidden) return forbidden;
        return await WriteAsync(() => _years.SetPeriodClosedAsync(id, true));
    }

    [HttpPost("fiscal-periods/{id:int}/reopen")]
    public async Task<ActionResult<MoadianFiscalPeriodDto>> ReopenPeriod(int id)
    {
        if (await ForbiddenUnlessFormAsync("Moadian", "Update", "FiscalYears") is ObjectResult forbidden) return forbidden;
        return await WriteAsync(() => _years.SetPeriodClosedAsync(id, false));
    }

    // ---------- ثبت صورتحساب ----------

    [HttpGet("next-number")]
    public async Task<ActionResult<MoadianNextNumberDto>> GetNextNumber(
        [FromQuery] int fiscalYearId = 0,
        [FromQuery] DateTime? date = null,
        [FromQuery] int? providerId = null)
    {
        if (await ForbiddenUnlessFormAsync("Moadian", "Read", "InvoiceNew") is ObjectResult forbidden) return forbidden;
        Response.Headers.CacheControl = "no-store";
        return Ok(await _years.GetNextNumberAsync(fiscalYearId, date, periodId: null, providerId: providerId));
    }

    [HttpPost("invoices")]
    public async Task<ActionResult<MoadianInvoice>> Create([FromBody] MoadianStandaloneInvoiceRequest request)
    {
        if (await ForbiddenUnlessFormAsync("Moadian", "Create", "InvoiceNew") is ObjectResult forbidden) return forbidden;
        return await WriteAsync(() => _invoices.CreateAsync(request, MyUsername));
    }

    private async Task<ActionResult<T>> WriteAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return Ok(await operation());
        }
        catch (MoadianBuyerValidationException ex)
        {
            // اعتبارسنجی محلی خریدار شکست خورد — ورودی‌ها نامعتبرند؛ به SDK/سامانه نرسیده است.
            return StatusCode(422, new { field = ex.Field, message = ex.Message });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (DbUpdateException ex)
        {
            var detail = DbError(ex);
            Console.WriteLine($"[Moadian] خطای پایگاه‌داده در ثبت/به‌روزرسانی: {detail}");
            return Conflict(new { message = $"ثبت در پایگاه‌داده ناموفق بود؛ ورودی‌ها یا شماره‌ها را بررسی کنید. جزئیات خطای پایگاه‌داده: {detail}" });
        }
    }

    private async Task<ActionResult<T>> ReadAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return Ok(await operation());
        }
        catch (InvalidOperationException ex) { return NotFound(new { message = ex.Message }); }
    }

    /// <summary>
    /// جزئیات خطای پایگاه‌داده (نام محدودیت یکتایی/FK، ستون نامعتبر، تراکشن داده و…)
    /// برای عیب‌یابی سریع؛ در پاسخ‌های 409 همراه پیام اصلی ارسال می‌شود.
    /// </summary>
    private static string DbError(Exception ex)
    {
        var inner = ex.InnerException?.Message ?? ex.Message;
        inner = inner.Replace("\r", " ").Replace("\n", " ").Trim();
        return inner.Length <= 500 ? inner : inner[..500] + "…";
    }
}
