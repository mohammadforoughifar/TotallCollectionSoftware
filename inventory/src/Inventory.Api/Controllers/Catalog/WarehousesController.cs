using Inventory.Api.Services;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>مدیریت انبارها.</summary>
[Route("api/warehouses")]
public class WarehousesController : ApiControllerBase
{
    private readonly IInventoryService _service;

    public WarehousesController(IInventoryService service) => _service = service;

    /// <summary>فهرست انبارها.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<Warehouse>>> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await _service.GetWarehousesPagedAsync(page, pageSize));

    /// <summary>ایجاد یا ویرایش انبار.</summary>
    [HttpPost]
    public async Task<ActionResult<Warehouse>> Save([FromBody] Warehouse warehouse)
        => Ok(await _service.SaveWarehouseAsync(warehouse));

    /// <summary>حذف انبار.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _service.DeleteWarehouseAsync(id);
        return Ok(new { ok = true });
    }
}
