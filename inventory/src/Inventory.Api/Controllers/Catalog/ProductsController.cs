using Inventory.Api.Services;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>مدیریت کالاها.</summary>
[Route("api/products")]
public class ProductsController : ApiControllerBase
{
    private readonly IInventoryService _service;

    public ProductsController(IInventoryService service) => _service = service;

    /// <summary>فهرست کالاها با جستجو، فیلتر انبار و صفحه‌بندی.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<Product>>> GetAll(
        [FromQuery] string? search, [FromQuery] bool? below, [FromQuery] int? warehouseId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await _service.GetProductsAsync(search, below == true, page, pageSize, warehouseId));

    /// <summary>فهرست خلاصه کالاها برای لیست‌های انتخاب.</summary>
    [HttpGet("lookups")]
    public async Task<ActionResult<List<LookupItem>>> GetLookups()
    {
        var res = await _service.GetProductsAsync(null, false, 1, 100000);
        return Ok(res.Items.OrderBy(p => p.Name)
            .Select(p => new LookupItem { Id = p.Id, Name = $"{p.Code} — {p.Name}" }).ToList());
    }

    /// <summary>دریافت یک کالا.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<Product>> GetById(int id)
    {
        var product = await _service.GetProductAsync(id);
        return product is null ? NotFound(new { message = "کالا یافت نشد." }) : Ok(product);
    }

    /// <summary>ایجاد یا ویرایش کالا.</summary>
    [HttpPost]
    public async Task<ActionResult<Product>> Save([FromBody] Product product)
        => Ok(await _service.SaveProductAsync(product));

    /// <summary>حذف کالا.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _service.DeleteProductAsync(id);
        return Ok(new { ok = true });
    }

    /// <summary>ورود گروهی کالا از فایل اکسل (xlsx).</summary>
    [HttpPost("import")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<ExcelImportResult>> Import(IFormFile? file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "فایل اکسل انتخاب نشده است." });
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "فقط فایل با فرمت xlsx پشتیبانی می‌شود." });

        await using var stream = file.OpenReadStream();
        return Ok(await _service.ImportProductsAsync(stream));
    }

    /// <summary>دانلود فایل اکسل نمونه برای ورود گروهی کالا.</summary>
    [HttpGet("import/template")]
    public IActionResult DownloadTemplate()
        => File(_service.BuildProductTemplate(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "Products-Template.xlsx");
}
