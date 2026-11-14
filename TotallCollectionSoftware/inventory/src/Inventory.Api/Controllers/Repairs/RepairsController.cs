using Inventory.Api.Services;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>پذیرش و مدیریت تعمیرات.</summary>
[Route("api/repairs")]
[Inventory.Api.Infrastructure.ModulePermission("Repairs")]
public class RepairsController : ApiControllerBase
{
    private readonly IRepairService _svc;

    public RepairsController(IRepairService svc) => _svc = svc;
    private string? Actor => User.Identity?.Name ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>فهرست پذیرش‌ها با جستجو/فیلتر وضعیت/تعمیرکار + صفحه‌بندی.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<RepairOrderDto>>> GetAll(
        [FromQuery] string? search, [FromQuery] RepairStatus? status,
        [FromQuery] int? technicianId, [FromQuery] int page = 1, [FromQuery] int pageSize = 15, [FromQuery] int? skip = null, [FromQuery] int? take = null)
        => Ok(await _svc.GetRepairsAsync(search, status, technicianId, Paging.ToPage(skip, take, page), Paging.ToPageSize(take, pageSize)));

    [HttpGet("products")]
    public async Task<ActionResult<PagedResult<Product>>> RepairProducts([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await _svc.RepairProductsAsync(search, page, pageSize));

    [HttpGet("products/{id:int}")]
    public async Task<ActionResult<Product>> RepairProduct(int id)
    {
        var product = await _svc.RepairProductAsync(id);
        return product is null ? NotFound() : Ok(product);
    }

    [HttpGet("warehouses")]
    public async Task<ActionResult<List<Warehouse>>> RepairWarehouses() => Ok(await _svc.RepairWarehousesAsync());

    [HttpGet("accounts")]
    public async Task<ActionResult<List<TrsAccount>>> RepairAccounts() => Ok(await _svc.RepairAccountsAsync());

    /// <summary>جزئیات یک پذیرش.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<RepairOrderDto>> Get(int id)
    {
        var r = await _svc.GetRepairAsync(id);
        return r is null ? NotFound(new { message = "پذیرش یافت نشد." }) : Ok(r);
    }

    /// <summary>ایجاد / ویرایش پذیرش.</summary>
    [HttpPost]
    public async Task<ActionResult<RepairOrderDto>> Save([FromBody] RepairOrderDto dto)
        => Ok(await _svc.SaveRepairAsync(dto));

    /// <summary>حذف پذیرش (فقط بدون فاکتور).</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _svc.DeleteRepairAsync(id);
        return Ok(new { ok = true });
    }

    /// <summary>تغییر وضعیت پذیرش.</summary>
    [HttpPost("{id:int}/status/{status}")]
    public async Task<ActionResult<RepairOrderDto>> SetStatus(int id, RepairStatus status)
        => Ok(await _svc.SetStatusAsync(id, status));

    /// <summary>صدور فاکتور فروش + تحویل دستگاه.</summary>
    [HttpPost("{id:int}/invoice")]
    public async Task<ActionResult<RepairOrderDto>> Invoice(int id, [FromBody] RepairInvoiceRequest request)
        => Ok(await _svc.InvoiceAsync(id, request, Actor));

    [HttpGet("{id:int}/invoice-detail")]
    public async Task<ActionResult<Order>> LinkedInvoice(int id) => Ok(await _svc.LinkedInvoiceAsync(id));

    [HttpGet("{id:int}/warranty-credit")]
    public async Task<ActionResult<ShopOrderCreditState>> WarrantyCreditState(int id) => Ok(await _svc.WarrantyCreditStateAsync(id));

    [HttpPost("{id:int}/warranty-credit-receipts")]
    public async Task<IActionResult> ReceiveWarrantyCredit(int id, [FromBody] ShopOrderCreditReceiptCommand cmd)
    {
        await _svc.ReceiveWarrantyCreditAsync(id, cmd, Actor);
        return Ok(new { ok = true });
    }
    [HttpPost("{id:int}/warranty-credit-receipts/{receiptId:int}/reverse")]
    public async Task<IActionResult> ReverseWarrantyCredit(int id, int receiptId, [FromBody] ShopOrderCreditReversalCommand cmd)
    {
        await _svc.ReverseWarrantyCreditAsync(id, receiptId, cmd, Actor);
        return Ok(new { ok = true });
    }
}

/// <summary>مدیریت تعمیرکارها.</summary>
[Route("api/technicians")]
[Inventory.Api.Infrastructure.ModulePermission("Repairs")]
public class TechniciansController : ApiControllerBase
{
    private readonly IRepairService _svc;

    public TechniciansController(IRepairService svc) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<List<Technician>>> GetAll([FromQuery] bool activeOnly = false, [FromQuery] int skip = 0, [FromQuery] int? take = null)
        => Ok(await Paging.ResultAsync(async pagination => await _svc.GetTechniciansAsync(activeOnly, pagination: pagination), skip, take));

    [HttpPost]
    public async Task<ActionResult<Technician>> Save([FromBody] Technician technician)
        => Ok(await _svc.SaveTechnicianAsync(technician));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _svc.DeleteTechnicianAsync(id);
        return Ok(new { ok = true });
    }
}
