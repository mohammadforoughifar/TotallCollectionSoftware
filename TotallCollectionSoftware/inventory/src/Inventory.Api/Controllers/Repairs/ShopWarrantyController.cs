using Inventory.Api.Infrastructure;
using Inventory.Api.Services.Repairs;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[Route("api/shop-warranties")]
[ModulePermission("Repairs")]
public class ShopWarrantyController : ApiControllerBase
{
    private readonly ShopWarrantyService _service;
    public ShopWarrantyController(ShopWarrantyService service) => _service = service;
    private string? Actor => User.Identity?.Name ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    [HttpGet]
    public async Task<ActionResult<List<ShopWarrantyDto>>> GetAll() => Ok(await _service.ListAsync());

    [HttpGet("lookups")]
    public async Task<ActionResult<ShopWarrantyLookupsDto>> Lookups() => Ok(await _service.LookupsAsync());

    [HttpGet("report")]
    public async Task<ActionResult<List<ShopWarrantyDto>>> Report([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(await _service.ListAsync(from, to, finalizedOnly: true));

    [HttpPost]
    public async Task<ActionResult<ShopWarrantyDto>> Save([FromBody] ShopWarrantyDto dto)
        => Ok(await _service.SaveDraftAsync(dto, Actor));

    [HttpPost("{id:int}/finalize")]
    public async Task<ActionResult<ShopWarrantyDto>> FinalizeWarranty(int id) => Ok(await _service.FinalizeAsync(id, Actor));
}
