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

    /// <summary>گردش کالا (کاردکس) با فیلتر انبار و بازه تاریخ — صفحه‌بندی‌شده.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<KardexRow>>> Get(
        [FromQuery] int productId, [FromQuery] int? warehouseId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        => Ok(await _service.GetKardexPagedAsync(productId, warehouseId, from, to, page, pageSize));
}

/// <summary>گزارش نقطه سفارش.</summary>
[Route("api/reorder")]
public class ReorderController : ApiControllerBase
{
    private readonly IInventoryService _service;

    public ReorderController(IInventoryService service) => _service = service;

    /// <summary>کالاهایی که موجودی‌شان به نقطه سفارش رسیده است — صفحه‌بندی‌شده.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<ReorderItem>>> Get(
        [FromQuery] int? warehouseId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        => Ok(await _service.GetReorderPagedAsync(warehouseId, page, pageSize));
}
