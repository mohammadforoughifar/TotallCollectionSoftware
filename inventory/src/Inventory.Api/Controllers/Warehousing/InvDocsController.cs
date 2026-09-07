using Db = Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.Accounting;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers.Warehousing;

/// <summary>انواع رسید و حواله — ماهیت افزایشی / کاهشی / خنثی.</summary>
[Route("api/inv/doc-types")]
public class InvDocTypesController : RbacControllerBase
{
    private readonly IWarehousingService _svc;

    public InvDocTypesController(Db.AppDbContext db, IWarehousingService svc) : base(db) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<List<InvDocType>>> GetAll(
        [FromQuery] bool activeOnly = false, [FromQuery] StockNature? nature = null)
        => Ok(await _svc.GetDocTypesAsync(activeOnly, nature));

    [HttpPost]
    public async Task<ActionResult<InvDocType>> Save([FromBody] InvDocType dto)
    {
        if (await ForbiddenUnlessAsync("InvDocTypes", dto.Id == 0 ? "Create" : "Update") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.SaveDocTypeAsync(dto));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync("InvDocTypes", "Delete") is ObjectResult forbidden) return forbidden;
        await _svc.DeleteDocTypeAsync(id);
        return Ok(new { ok = true });
    }
}

/// <summary>اسناد انبار — رسید، حواله و انتقال بین انبار.</summary>
[Route("api/inv/docs")]
public class InvDocsController : RbacControllerBase
{
    private readonly IWarehousingService _svc;
    private readonly IAccountingService _acc;

    public InvDocsController(Db.AppDbContext db, IWarehousingService svc, IAccountingService acc) : base(db)
    {
        _svc = svc;
        _acc = acc;
    }

    /// <summary>فهرست اسناد با فیلتر ماهیت، نوع، انبار، وضعیت و بازه تاریخ.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<InvDoc>>> GetAll(
        [FromQuery] StockNature? nature = null,
        [FromQuery] int? docTypeId = null,
        [FromQuery] int? warehouseId = null,
        [FromQuery] InvDocStatus? status = null,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 15)
        => Ok(await _svc.GetDocsAsync(nature, docTypeId, warehouseId, status, search, from, to, page, pageSize));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<InvDoc>> Get(int id)
    {
        var doc = await _svc.GetDocAsync(id);
        return doc is null ? NotFound(new { message = "سند یافت نشد." }) : Ok(doc);
    }

    /// <summary>ثبت یا ویرایش سند (پیش‌نویس).</summary>
    [HttpPost]
    public async Task<ActionResult<InvDoc>> Save([FromBody] InvDoc dto)
    {
        if (await ForbiddenUnlessAsync("InvDocs", dto.Id == 0 ? "Create" : "Update") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.SaveDocAsync(dto, MyUsername));
    }

    /// <summary>قطعی‌سازی سند — اثرگذاری بر موجودی و کاردکس.</summary>
    [HttpPost("{id:int}/confirm")]
    public async Task<ActionResult<InvDoc>> Confirm(int id)
    {
        if (await ForbiddenUnlessAsync("InvDocs", "Confirm") is ObjectResult forbidden) return forbidden;
        var confirmed = await _svc.ConfirmDocAsync(id, MyUsername);

        // صدور خودکار سند حسابداری (در صورت وجود قاعده‌ی فعال برای این نوع سند)
        await _acc.PostInventoryDocAsync(id, MyUsername);
        return Ok(confirmed);
    }

    /// <summary>برگشت سند قطعی به پیش‌نویس.</summary>
    [HttpPost("{id:int}/unconfirm")]
    public async Task<ActionResult<InvDoc>> Unconfirm(int id)
    {
        if (await ForbiddenUnlessAsync("InvDocs", "Confirm") is ObjectResult forbidden) return forbidden;
        var draft = await _svc.UnconfirmDocAsync(id, MyUsername);

        // حذف سند حسابداری خودکارِ متناظر
        await _acc.UnpostInventoryDocAsync(id);
        return Ok(draft);
    }

    /// <summary>ابطال سند.</summary>
    [HttpPost("{id:int}/cancel")]
    public async Task<ActionResult<InvDoc>> Cancel(int id)
    {
        if (await ForbiddenUnlessAsync("InvDocs", "Cancel") is ObjectResult forbidden) return forbidden;
        var cancelled = await _svc.CancelDocAsync(id, MyUsername);
        await _acc.UnpostInventoryDocAsync(id);
        return Ok(cancelled);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync("InvDocs", "Delete") is ObjectResult forbidden) return forbidden;
        await _acc.UnpostInventoryDocAsync(id);
        await _svc.DeleteDocAsync(id);
        return Ok(new { ok = true });
    }
}

/// <summary>گزارش‌های انبارداری: کاردکس کالا و موجودی ریالی.</summary>
[Route("api/inv/reports")]
public class InvReportsController : RbacControllerBase
{
    private readonly IWarehousingService _svc;

    public InvReportsController(Db.AppDbContext db, IWarehousingService svc) : base(db) => _svc = svc;

    /// <summary>کاردکس مقداری و ریالی یک کالا.</summary>
    [HttpGet("kardex")]
    public async Task<ActionResult<InvKardexResult>> Kardex(
        [FromQuery] int productId,
        [FromQuery] int? warehouseId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        if (await ForbiddenUnlessAsync("ReportPages", "Kardex") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.GetKardexAsync(productId, warehouseId, from, to));
    }

    /// <summary>موجودی مقداری و ریالی انبارها.</summary>
    [HttpGet("stock")]
    public async Task<ActionResult<PagedResult<InvStockRow>>> Stock(
        [FromQuery] int? warehouseId = null,
        [FromQuery] int? categoryId = null,
        [FromQuery] string? search = null,
        [FromQuery] bool below = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 15)
        => Ok(await _svc.GetStockAsync(warehouseId, categoryId, search, below, page, pageSize));
}
