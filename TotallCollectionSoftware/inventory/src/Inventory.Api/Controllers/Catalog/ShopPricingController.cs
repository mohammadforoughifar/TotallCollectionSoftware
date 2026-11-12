using Inventory.Api.Services;
using Inventory.Api.Services.Core;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Inventory.Api.Licensing.Infrastructure;

namespace Inventory.Api.Controllers;

/// <summary>Separate shop permissions; price history never exposes purchase cost.</summary>
[Route("api/shop-pricing")]
public class ShopPricingController : ApiControllerBase
{
    private readonly ShopPricingService _pricing;
    private readonly IEffectivePermissions _permissions;
    private string? CurrentActor => User.Identity?.Name ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    public ShopPricingController(ShopPricingService pricing, IEffectivePermissions permissions)
        { _pricing = pricing; _permissions = permissions; }
    private async Task<bool> AllowedAsync(bool write) => User.Identity?.IsAuthenticated == true &&
        (await _permissions.IsLegacyAdminAsync(User) ||
         (await _permissions.GetAsync(User)).Contains("Shop.Pricing.Manage") ||
         (!write && (await _permissions.GetAsync(User)).Contains("Shop.Pricing.View")));

    [HttpGet("policy")]
    public async Task<ActionResult<ShopPricingPolicyDto>> Policy() =>
        await AllowedAsync(false) ? Ok(await _pricing.GetPolicyAsync()) : StatusCode(403, new { message = "مجوز مشاهده قیمت‌گذاری لازم است." });

    [HttpPut("policy")]
    public async Task<ActionResult<ShopPricingPolicyDto>> SavePolicy([FromBody] ShopPricingPolicyDto dto) =>
        await AllowedAsync(true) ? Ok(await _pricing.SavePolicyAsync(dto, CurrentActor)) : StatusCode(403, new { message = "مجوز مدیریت قیمت‌گذاری لازم است." });

    [AllowInReadOnly]
    [HttpPost("preview")]
    public async Task<ActionResult<ShopPriceAdjustmentPreview>> Preview([FromBody] ShopPriceAdjustmentRequest dto) =>
        await AllowedAsync(true) ? Ok(await _pricing.PreviewAsync(dto)) : StatusCode(403, new { message = "مجوز مدیریت قیمت‌گذاری لازم است." });

    [HttpPost("apply")]
    public async Task<ActionResult<ShopPriceAdjustmentPreview>> Apply([FromBody] ShopPriceAdjustmentCommand dto) =>
        await AllowedAsync(true) ? Ok(await _pricing.ApplyAsync(dto, CurrentActor)) : StatusCode(403, new { message = "مجوز مدیریت قیمت‌گذاری لازم است." });

    [HttpGet("categories")]
    public async Task<ActionResult<List<LookupItem>>> Categories() =>
        await AllowedAsync(false) ? Ok(await _pricing.CategoriesAsync()) : StatusCode(403, new { message = "مجوز مشاهده قیمت‌گذاری لازم است." });

    [HttpGet("history")]
    public async Task<ActionResult<List<ShopPriceHistoryItem>>> History([FromQuery] int? productId, [FromQuery] int take = 100) =>
        await AllowedAsync(false) ? Ok(await _pricing.HistoryAsync(productId, take)) : StatusCode(403, new { message = "مجوز مشاهده قیمت‌گذاری لازم است." });
}
