using Inventory.Api.Services;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>مدیریت گروه‌های کالا.</summary>
[Route("api/categories")]
public class CategoriesController : ApiControllerBase
{
    private readonly IInventoryService _service;

    public CategoriesController(IInventoryService service) => _service = service;

    /// <summary>فهرست گروه‌های کالا (با تعداد کالای هر گروه).</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<ProductCategory>>> GetAll([FromQuery] bool activeOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await _service.GetCategoriesPagedAsync(activeOnly, page, pageSize));

    /// <summary>ایجاد یا ویرایش گروه کالا.</summary>
    [HttpPost]
    public async Task<ActionResult<ProductCategory>> Save([FromBody] ProductCategory category)
        => Ok(await _service.SaveCategoryAsync(category));

    /// <summary>حذف گروه کالا.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _service.DeleteCategoryAsync(id);
        return Ok(new { ok = true });
    }
}
