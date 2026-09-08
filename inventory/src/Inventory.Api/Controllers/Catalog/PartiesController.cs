using Inventory.Api.Services;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>مدیریت طرف حساب‌ها (مشتری/تأمین‌کننده).</summary>
[Route("api/parties")]
public class PartiesController : ApiControllerBase
{
    private readonly IInventoryService _service;

    public PartiesController(IInventoryService service) => _service = service;

    /// <summary>فهرست طرف حساب‌ها بر اساس نوع.</summary>
    [HttpGet]
    public async Task<ActionResult<List<Party>>> GetAll([FromQuery] PartyType type)
        => Ok(await _service.GetPartiesAsync(type));

    /// <summary>ایجاد یا ویرایش طرف حساب.</summary>
    [HttpPost]
    public async Task<ActionResult<Party>> Save([FromBody] Party party)
        => Ok(await _service.SavePartyAsync(party));

    /// <summary>حذف طرف حساب.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _service.DeletePartyAsync(id);
        return Ok(new { ok = true });
    }
}
