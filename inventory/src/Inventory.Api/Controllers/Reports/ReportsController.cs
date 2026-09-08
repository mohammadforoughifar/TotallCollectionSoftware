using Inventory.Api.Services;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>گزارش کاردکس کالا.</summary>
[Route("api/kardex")]
public class KardexController : ApiControllerBase
{
    private readonly IInventoryService _service;

    public KardexController(IInventoryService service) => _service = service;

    /// <summary>گردش کالا (کاردکس) با فیلتر انبار و بازه تاریخ.</summary>
    [HttpGet]
    public async Task<ActionResult<List<KardexRow>>> Get(
        [FromQuery] int productId, [FromQuery] int? warehouseId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(await _service.GetKardexAsync(productId, warehouseId, from, to));
}

/// <summary>گزارش نقطه سفارش.</summary>
[Route("api/reorder")]
public class ReorderController : ApiControllerBase
{
    private readonly IInventoryService _service;

    public ReorderController(IInventoryService service) => _service = service;

    /// <summary>کالاهایی که موجودی‌شان به نقطه سفارش رسیده است.</summary>
    [HttpGet]
    public async Task<ActionResult<List<ReorderItem>>> Get([FromQuery] int? warehouseId)
        => Ok(await _service.GetReorderAsync(warehouseId));
}
