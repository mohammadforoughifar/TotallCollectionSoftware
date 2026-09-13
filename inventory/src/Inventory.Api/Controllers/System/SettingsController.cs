using Inventory.Api.Services;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>تنظیمات برنامه (روش قیمت‌گذاری و موجودی منفی).</summary>
[Route("api/settings")]
public class SettingsController : ApiControllerBase
{
    private readonly IInventoryService _service;

    public SettingsController(IInventoryService service) => _service = service;

    /// <summary>دریافت تنظیمات فعلی.</summary>
    [HttpGet]
    public async Task<ActionResult<AppSettings>> Get()
        => Ok(await _service.GetSettingsAsync());

    /// <summary>ذخیره تنظیمات — فقط مدیر.</summary>
    [HttpPost]
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin")]
    public async Task<ActionResult<AppSettings>> Save([FromBody] AppSettings settings)
        => Ok(await _service.SaveSettingsAsync(settings));
}

/// <summary>مدیریت معرف‌ها (بازاریاب‌ها).</summary>
[Route("api/referrers")]
public class ReferrersController : ApiControllerBase
{
    private readonly IInventoryService _service;

    public ReferrersController(IInventoryService service) => _service = service;

    /// <summary>فهرست معرف‌ها.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<Referrer>>> GetAll([FromQuery] bool activeOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await _service.GetReferrersPagedAsync(activeOnly, page, pageSize));

    /// <summary>ایجاد یا ویرایش معرف.</summary>
    [HttpPost]
    public async Task<ActionResult<Referrer>> Save([FromBody] Referrer referrer)
        => Ok(await _service.SaveReferrerAsync(referrer));

    /// <summary>حذف معرف.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _service.DeleteReferrerAsync(id);
        return Ok(new { ok = true });
    }

    /// <summary>کیف پول معرف‌ها با فیلتر و مرتب‌سازی (sortBy: name|commission|paid|balance) — صفحه‌بندی‌شده.</summary>
    [HttpGet("wallets")]
    public async Task<ActionResult<PagedResult<Referrer>>> GetWallets(
        [FromQuery] string? search, [FromQuery] string? sortBy = "name", [FromQuery] bool desc = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await _service.GetReferrerWalletsPagedAsync(search, sortBy, desc, page, pageSize));

    /// <summary>فهرست اسناد پرداخت (اختیاری: فقط یک معرف) — صفحه‌بندی‌شده.</summary>
    [HttpGet("payments")]
    public async Task<ActionResult<PagedResult<ReferrerPayment>>> GetPayments(
        [FromQuery] int? referrerId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await _service.GetReferrerPaymentsPagedAsync(referrerId, page, pageSize));

    /// <summary>ثبت سند پرداخت پورسانت به معرف.</summary>
    [HttpPost("payments")]
    public async Task<ActionResult<ReferrerPayment>> AddPayment([FromBody] ReferrerPayment payment)
        => Ok(await _service.AddReferrerPaymentAsync(payment));

    /// <summary>حذف سند پرداخت.</summary>
    [HttpDelete("payments/{id:int}")]
    public async Task<IActionResult> DeletePayment(int id)
    {
        await _service.DeleteReferrerPaymentAsync(id);
        return Ok(new { ok = true });
    }
}
