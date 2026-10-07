using Inventory.Api.Controllers;
using Db = Inventory.Api.Data;
using Inventory.Api.Services.Invoicing;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers.Invoicing;

/// <summary>
/// صورتحساب‌های مودیان به تفکیک خدمات‌دهنده:
///   GET  api/moadian/provider-invoices          فهرست با فیلتر وضعیت/جستجو (providerId الزامی)
///   POST api/moadian/provider-invoices/{id}/send      ارسال رسمی به سامانه مودیان
///   POST api/moadian/provider-invoices/{id}/inquiry   استعلام رسمی وضعیت از سامانه مودیان
/// </summary>
[Route("api/moadian/provider-invoices")]
public sealed class MoadianProviderInvoicesController : RbacControllerBase
{
    private readonly IMoadianProviderInvoiceService _invoices;
    private readonly IMoadianSubmissionService _submission;

    public MoadianProviderInvoicesController(
        Db.AppDbContext db,
        IMoadianProviderInvoiceService invoices,
        IMoadianSubmissionService submission) : base(db)
    {
        _invoices = invoices;
        _submission = submission;
    }

    [HttpGet]
    public async Task<ActionResult> GetInvoices(
        [FromQuery] int providerId,
        [FromQuery] string? search,
        [FromQuery] MoadianInvoiceStatus? status,
        [FromQuery] int skip = 0,
        [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync("Moadian", "Read") is ObjectResult forbidden) return forbidden;
        if (providerId <= 0)
            return BadRequest(new { message = "خدمات‌دهنده را انتخاب کنید." });
        Response.Headers.CacheControl = "no-store";
        return Ok(await Paging.ResultAsync(p => _invoices.GetByProviderAsync(providerId, search, status, p), skip, take));
    }

    [HttpPost("{id:int}/send")]
    public async Task<ActionResult<MoadianSubmissionResultDto>> Send(int id)
    {
        if (await ForbiddenUnlessAsync("Moadian", "Send") is ObjectResult forbidden) return forbidden;
        try
        {
            return Ok(await _submission.SendAsync(id, MyUsername));
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (DbUpdateException) { return Conflict(new { message = "به‌روزرسانی وضعیت در پایگاه‌داده ناموفق بود." }); }
    }

    [HttpPost("{id:int}/inquiry")]
    public async Task<ActionResult<MoadianSubmissionResultDto>> Inquiry(int id)
    {
        if (await ForbiddenUnlessAsync("Moadian", "Read") is ObjectResult forbidden) return forbidden;
        try
        {
            return Ok(await _submission.InquiryAsync(id, MyUsername));
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (DbUpdateException) { return Conflict(new { message = "به‌روزرسانی وضعیت در پایگاه‌داده ناموفق بود." }); }
    }
}
