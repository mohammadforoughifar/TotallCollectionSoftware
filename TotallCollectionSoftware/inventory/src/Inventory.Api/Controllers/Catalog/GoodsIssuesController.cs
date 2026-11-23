using Inventory.Api.Services.Catalog;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>
/// حواله تحویل کالا — سند «فقط مقدار» (بدون قیمت/ریال).
/// <para>
/// یک مشتری + چند سطر کالا با مقدار. هیچ اثری روی موجودی و حسابداری ندارد.
/// </para>
/// </summary>
[Route("api/goods-issues")]
[Inventory.Api.Infrastructure.ModulePermission("GoodsIssues")]
public class GoodsIssuesController : ApiControllerBase
{
    private readonly IGoodsIssueService _service;

    public GoodsIssuesController(IGoodsIssueService service) => _service = service;

    /// <summary>فهرست حواله‌ها با فیلتر مشتری/بازه تاریخ/جستجو.</summary>
    [HttpGet]
    public async Task<ActionResult<List<GoodsIssueDto>>> GetAll(
        [FromQuery] int? partyId = null, [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null, [FromQuery] string? search = null)
        => Ok(await _service.GetAsync(partyId, from, to, search));

    /// <summary>
    /// کالاهای انباری برای انتخاب در سطرهای بیجک (با موجودی).
    /// سطر دستی (نام آزاد بدون شناسهٔ کالا) هم مجاز است؛ این فهرست فقط انتخاب را ساده می‌کند.
    /// </summary>
    [HttpGet("product-options")]
    public async Task<ActionResult<List<GoodsIssueProductOptionDto>>> ProductOptions()
        => Ok(await _service.GetProductOptionsAsync());

    [HttpGet("warehouse-options")]
    public async Task<ActionResult<List<LookupItem>>> WarehouseOptions()
        => Ok(await _service.GetWarehouseOptionsAsync());

    /// <summary>یک حواله با سطرهایش.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<GoodsIssueDto>> Get(int id)
        => await _service.GetAsync(id) is { } dto ? Ok(dto) : NotFound();

    /// <summary>ثبت یا ویرایش حواله.</summary>
    [HttpPost]
    public async Task<ActionResult<GoodsIssueDto>> Save([FromBody] GoodsIssueCommand cmd)
        => Ok(await _service.SaveAsync(cmd));

    /// <summary>حذف حواله (سطرهایش هم حذف می‌شوند).</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _service.DeleteAsync(id);
        return Ok(new { ok = true });
    }
}
