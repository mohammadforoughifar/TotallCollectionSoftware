using Inventory.Api.Infrastructure;
using Inventory.Api.Services.Repairs;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>طرح تعویض و اعتبار مشتری در فروشگاه.</summary>
[Route("api/shop-trade-ins")]
[ModulePermission("Repairs")]
public class ShopTradeInController : ApiControllerBase
{
    private readonly ShopTradeInService _service;
    public ShopTradeInController(ShopTradeInService service) => _service = service;
    private string? Actor => User.Identity?.Name ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    [HttpGet]
    public async Task<ActionResult<List<ShopTradeInDto>>> GetAll() => Ok(await _service.ListAsync());

    [HttpGet("customers")]
    public async Task<ActionResult<List<LookupItem>>> Customers() => Ok(await _service.CustomersAsync());

    [HttpGet("target-sales/{partyId:int}")]
    [RequirePermission("Orders", "Read")]
    public async Task<ActionResult<List<ShopTradeInTargetSaleDto>>> TargetSales(int partyId) => Ok(await _service.TargetSalesAsync(partyId));

    [HttpPost]
    public async Task<ActionResult<ShopTradeInDto>> Save([FromBody] ShopTradeInDto dto)
        => Ok(await _service.SaveDraftAsync(dto, Actor));

    [HttpPost("{id:int}/finalize")]
    public async Task<ActionResult<ShopTradeInDto>> FinalizeTradeIn(int id, [FromBody] ShopTradeInFinalizationCommand cmd)
        => Ok(await _service.FinalizeAsync(id, cmd.Date, Actor));

    [HttpPost("{id:int}/use")]
    [RequirePermission("Orders", "Update")]
    public async Task<IActionResult> Use(int id, [FromBody] ShopTradeInUseCommand cmd)
    {
        await _service.UseAsync(id, cmd, Actor);
        return Ok(new { ok = true });
    }
}
