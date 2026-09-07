using Db = Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers.Warehousing;

// =====================================================================
// اطلاعات پایه ماژول انبارداری: گروه کالا (درختی)، ویژگی‌ها، انبارها
// =====================================================================

/// <summary>گروه‌های کالا — نمایش و مدیریت درختی.</summary>
[Route("api/inv/categories")]
public class InvCategoriesController : RbacControllerBase
{
    private readonly IWarehousingService _svc;

    public InvCategoriesController(Db.AppDbContext db, IWarehousingService svc) : base(db) => _svc = svc;

    /// <summary>درخت گروه‌های کالا (هر گره شامل زیرگروه‌هایش).</summary>
    [HttpGet("tree")]
    public async Task<ActionResult<List<InvCategory>>> Tree([FromQuery] bool activeOnly = false)
        => Ok(await _svc.GetCategoryTreeAsync(activeOnly));

    /// <summary>فهرست تخت گروه‌ها (برای کمبوها و انتخاب والد).</summary>
    [HttpGet]
    public async Task<ActionResult<List<InvCategory>>> Flat([FromQuery] bool activeOnly = false)
        => Ok(await _svc.GetCategoriesFlatAsync(activeOnly));

    /// <summary>ایجاد یا ویرایش گروه کالا.</summary>
    [HttpPost]
    public async Task<ActionResult<InvCategory>> Save([FromBody] InvCategory dto)
    {
        if (await ForbiddenUnlessAsync("Products", dto.Id == 0 ? "Create" : "Update") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.SaveCategoryAsync(dto));
    }

    /// <summary>جابه‌جایی گروه در درخت (تغییر والد / ترتیب).</summary>
    [HttpPost("move")]
    public async Task<IActionResult> Move([FromBody] InvCategoryMove cmd)
    {
        if (await ForbiddenUnlessAsync("Products", "Update") is ObjectResult forbidden) return forbidden;
        await _svc.MoveCategoryAsync(cmd);
        return Ok(new { ok = true });
    }

    /// <summary>حذف گروه کالا.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync("Products", "Delete") is ObjectResult forbidden) return forbidden;
        await _svc.DeleteCategoryAsync(id);
        return Ok(new { ok = true });
    }
}

/// <summary>ویژگی‌های کالا (رنگ، سایز، ولتاژ، …).</summary>
[Route("api/inv/attributes")]
public class InvAttributesController : RbacControllerBase
{
    private readonly IWarehousingService _svc;

    public InvAttributesController(Db.AppDbContext db, IWarehousingService svc) : base(db) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<List<InvAttribute>>> GetAll(
        [FromQuery] bool activeOnly = false, [FromQuery] int? categoryId = null)
        => Ok(await _svc.GetAttributesAsync(activeOnly, categoryId));

    [HttpPost]
    public async Task<ActionResult<InvAttribute>> Save([FromBody] InvAttribute dto)
    {
        if (await ForbiddenUnlessAsync("Products", dto.Id == 0 ? "Create" : "Update") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.SaveAttributeAsync(dto));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync("Products", "Delete") is ObjectResult forbidden) return forbidden;
        await _svc.DeleteAttributeAsync(id);
        return Ok(new { ok = true });
    }
}

/// <summary>انبارها — با ماهیت، انباردار و اجازه منفی شدن موجودی.</summary>
[Route("api/inv/warehouses")]
public class InvWarehousesController : RbacControllerBase
{
    private readonly IWarehousingService _svc;

    public InvWarehousesController(Db.AppDbContext db, IWarehousingService svc) : base(db) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<List<InvWarehouse>>> GetAll([FromQuery] bool activeOnly = false)
        => Ok(await _svc.GetWarehousesAsync(activeOnly));

    [HttpGet("lookups")]
    public async Task<ActionResult<List<LookupItem>>> Lookups([FromQuery] bool activeOnly = true)
    {
        var list = await _svc.GetWarehousesAsync(activeOnly);
        return Ok(list.Select(w => new LookupItem { Id = w.Id, Name = w.Name }).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<InvWarehouse>> Save([FromBody] InvWarehouse dto)
    {
        if (await ForbiddenUnlessAsync("Warehouses", dto.Id == 0 ? "Create" : "Update") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.SaveWarehouseAsync(dto));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync("Warehouses", "Delete") is ObjectResult forbidden) return forbidden;
        await _svc.DeleteWarehouseAsync(id);
        return Ok(new { ok = true });
    }
}
